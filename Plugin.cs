using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.FishDropRules;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using static FishMach.Utils;
using static TShockAPI.GetDataHandlers;

namespace FishMach;

[ApiVersion(2, 1)]
public class Plugin(Main game) : TerrariaPlugin(game)
{
    #region 插件信息
    public static string PluginName => "自动钓鱼机";
    public override string Name => PluginName;
    public override string Author => "羽学";
    public override Version Version => new(1, 0, 8);
    public override string Description => "使用/afm 指令指定一个箱子作为自动钓鱼机";
    #endregion

    #region 文件路径
    public static readonly string MainPath = Path.Combine(TShock.SavePath, PluginName); // 主文件夹路径
    public static readonly string Paths = Path.Combine(MainPath, $"配置文件.json"); // 配置文件路径
    public static string CachePath(int worldID) => Path.Combine(MainPath, $"数据缓存_{worldID}.json"); // 缓存文件路径
    #endregion

    #region 静态成员
    // 主指令名称
    public static string afm = "afm";
    // 权限节点
    public static string perm = "afm.use";
    // 检查玩家是否有管理员权限
    public static bool IsAdmin(TSPlayer plr) => plr.HasPermission("afm.admin");
    // 钓鱼掉落规则列表，存储原版所有掉落规则
    internal static FishDropRuleList RuleList = new();
    // 复用玩家对象（避免频繁创建）
    internal static Player TempPlayer = new();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        GeneralHooks.ReloadEvent += ReloadConfig;
        On.Terraria.WorldGen.KillTile += OnKillTile;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        GetDataHandlers.ChestItemChange += OnChestItemChange!;
        GetDataHandlers.ChestOpen += OnChestOpen!;
        GetDataHandlers.PlayerZone += OnPlayerZone;
        GetDataHandlers.PlayerBuffUpdate += OnPlayerBuffUpdate;
        On.Terraria.Wiring.Hopper += OnHopper;
        RegionHooks.RegionEntered += OnRegionEnter;
        RegionHooks.RegionLeft += OnRegionLeave;
        Commands.ChatCommands.Add(new Command(perm, MyCommand.CmdAfm, afm));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= ReloadConfig;
            On.Terraria.WorldGen.KillTile -= OnKillTile;
            ServerApi.Hooks.GamePostInitialize.Deregister(this, GamePost);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            GetDataHandlers.ChestItemChange -= OnChestItemChange!;
            GetDataHandlers.ChestOpen -= OnChestOpen!;
            GetDataHandlers.PlayerZone -= OnPlayerZone;
            GetDataHandlers.PlayerBuffUpdate -= OnPlayerBuffUpdate;
            On.Terraria.Wiring.Hopper -= OnHopper;
            RegionHooks.RegionEntered -= OnRegionEnter;
            RegionHooks.RegionLeft -= OnRegionLeave;
            Commands.ChatCommands.RemoveAll(x => x.CommandDelegate == MyCommand.CmdAfm);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new(); // 配置文件实例
    private static void ReloadConfig(ReloadEventArgs args)
    {
        // 先保存钓鱼机数据（如果有脏数据）
        DataManager.CanSave = true;
        LoadConfig();
        args.Player.SendMessage($"[{PluginName}]重新加载配置完毕。", color);
    }

    private static void LoadConfig()
    {
        try
        {
            if (!Directory.Exists(MainPath))
                Directory.CreateDirectory(MainPath);

            Config = Configuration.Read(); // 读取配置（内部会创建默认配置）
            Config.ParseFrames(); // 解析钓鱼间隔字符串（如 "60" 或 "60,180"）
            Config.AutoDesc(); // 自动描述
            Config.AutoFillNames(); // 自动写物品名
            Config.Write(); // 写回（确保配置存在）

            RuleList = new FishDropRuleList();
            var populator = new GameContentFishDropPopulator(RuleList);
            populator.Populate();

            // 移除无效的区域或没缓存的钓鱼机
            RemoveRegion();
            // 更新区域范围与建筑保护属性
            UpdateRegions();

            SaveFrame = Timer + (Config.SaveInterval * 60);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 加载配置文件失败: {ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args) => DataManager.CanSave = true;
    private void GamePost(EventArgs args) // 加载完世界后事件
    {
        // 加载钓鱼机缓存
        DataManager.Load();
        // 加载配置文件
        LoadConfig();

        // 初始化所有机器的下次执行时间（如果为0）
        foreach (var data in DataManager.Machines)
        {
            int min = Config.MinFrames;
            int max = Config.MaxFrames;
            int delay = Main.rand.Next(min, max + 1);

            if (data.nextFrame == 0)
                data.nextFrame = Timer + delay;
        }
    }
    #endregion

    #region 游戏更新事件（触发器）
    private static long Timer = 0; // 帧计数器（每次游戏更新+1）
    private static long SaveFrame = 0; // 保存缓存的帧数
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        Timer++;

        // 定期保存（内存数据）
        if (Timer >= SaveFrame)
        {
            if (DataManager.CanSave)
                DataManager.Save();

            SaveFrame = Timer + (Config.SaveInterval * 60);
        }

        // 无电路模式：使用游戏更新事件自动定时执行
        if (!Config.NeedWiring)
        {
            var all = DataManager.Machines;
            foreach (var data in all)
            {
                // 检查是否到达执行时间
                if (Timer >= data.nextFrame)
                {
                    var engine = new AutoFishing(data);
                    engine.Execute();

                    // 计算下次执行时间（支持随机范围）
                    int min = Config.MinFrames;
                    int max = Config.MaxFrames;
                    int delay = min == max ? min : Main.rand.Next(min, max + 1);
                    data.nextFrame = Timer + delay;
                }
            }
        }
    }

    // 漏斗事件
    private void OnHopper(On.Terraria.Wiring.orig_Hopper orig, int sourceX, int sourceY)
    {
        orig(sourceX, sourceY);

        if (!Config.Enabled || !Config.NeedWiring) return;

        int x = sourceX;
        int y = sourceY;
        var tile = Main.tile[x, y];
        if (tile.frameX % 36 != 0) x--;
        if (tile.frameY % 36 != 0) y--;

        var pos = new Point(x, y);
        var data = DataManager.FindTile(pos);
        if (data == null) return;

        // 执行钓鱼
        var engine = new AutoFishing(data);

        // 电路限频
        if (!Config.LimitFrames)
        {
            engine.Execute();
            return;
        }

        // 限频检查：如果距离上次执行还没到最小间隔，则跳过
        int minFrames = Config.MinFrames;
        if (Timer < data.nextFrame)
            return;

        engine.Execute();

        // 更新下次执行时间（使用配置的间隔）
        int maxFrames = Config.MaxFrames;
        int delay = minFrames == maxFrames ? minFrames : Main.rand.Next(minFrames, maxFrames + 1);
        data.nextFrame = Timer + delay;
    }
    #endregion

    #region 挖掉箱子自动移除钓鱼机与对应区域
    private static void OnKillTile(On.Terraria.WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
    {
        if (Config.Enabled)
        {
            var pos = new Point(x, y);
            var data = DataManager.FindTile(pos);
            if (data != null)
            {
                if (!string.IsNullOrEmpty(data.RegName))
                    TShock.Regions.DeleteRegion(data.RegName);
                DataManager.Remove(pos);
            }
        }

        orig(x, y, fail, effectOnly, noItem);
    }
    #endregion

    #region 箱子打开事件（创建、查看数据）
    private void OnChestOpen(object sender, GetDataHandlers.ChestOpenEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        int c = Chest.FindChest(e.X, e.Y);
        var data = DataManager.FindChest(c);

        var chest = Main.chest[c];
        if (chest == null) return;

        var pos = new Point(e.X, e.Y);

        // 处理 set 指令创建模式
        if (data == null && plr.GetData<bool>("set"))
        {
            CreateData(plr, c, pos);
            plr.RemoveData("set");
            return;
        }

        // 处理 info 指令查看模式
        if (plr.GetData<bool>("info"))
        {
            if (data == null) plr.SendErrorMessage("该位置没有钓鱼机");
            else MyCommand.ShowMachineInfo(plr, data);
            plr.RemoveData("info");
            return;
        }

        if (data != null)
        {
            // 如果箱子没有连接电线，则提示
            if (Config.NeedWiring && !HasWiring(data.Pos))
            {
                plr.SendMessage(TextGradient($"{data.Owner}的自动钓鱼机[c/FF716D:未连接]电线," +
                                             "\n连接电路与计时器后将自动启动"), color2);
                return;
            }
        }
    }
    #endregion

    #region 箱子物品更改事件（刷新物品缓存）
    public static Dictionary<string, HashSet<int>> pend = new(); // 玩家名 -> 待处理物品
    private void OnChestItemChange(object sender, GetDataHandlers.ChestItemEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        var data = DataManager.FindChest(e.ID);
        if (data == null) return;

        // 如果箱子没有连接电线，则提示
        if (Config.NeedWiring && !HasWiring(data.Pos))
        {
            e.Player.SendMessage(TextGradient($"{data.Owner}的自动钓鱼机[c/FF716D:未连接]电线," +
                                              "\n连接电路与计时器后将自动启动"), color2);
            return;
        }

        // 批量排除模式：记录物品
        if (pend.TryGetValue(plr.Name, out var set))
        {
            if (e.Type != 0 && set.Add(e.Type))
                plr.SendMessage($"[c/FFA500:已记录]:{ItemIcon(e.Type)}", color2);

            return;
        }

        // 更新物品缓存
        EnvManager.UpdateMachineCache(data);
        DataManager.CanSave = true;
    }
    #endregion

    #region 区域进出事件
    private void OnRegionEnter(RegionHooks.RegionEnteredEventArgs args)
    {
        if (!Config.Enabled) return;

        if (!IsAfmRegion(args.Region.Name)) return;
        var data = DataManager.FindRegion(args.Region.Name);
        if (data == null)
        {
            // 移除无效的区域或没缓存的钓鱼机
            RemoveRegion();
            return;
        }

        // 添加到区域玩家集合
        if (!data.RegionPlayers.Contains(args.Player))
            data.RegionPlayers.Add(args.Player);

        // 立即刷新一次BUFF
        RefreshBuffs(args.Player, data);

        // 玩家进入时整理一次箱子
        if (Config.AutoPut)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                var af = new AutoFishing(data);
                af.SortChest(chest);
            }
        }

        if (Config.RegionBroadcast)
        {
            var env = new List<string>();
            var env2 = MyCommand.GetHeightName(data.HeightLevel);
            if (data.ZoneHallow) env.Add("神圣");
            if (data.ZoneCorrupt) env.Add("腐化");
            if (data.ZoneCrimson) env.Add("猩红");
            if (data.ZoneJungle) env.Add("丛林");
            if (data.ZoneSnow) env.Add("雪原");
            if (data.ZoneDesert) env.Add("沙漠");
            if (data.ZoneBeach) env.Add("海洋");
            if (data.ZoneDungeon) env.Add("地牢");
            if (data.RolledRemixOcean) env.Add("颠倒海洋");

            // 计算当前渔力
            int rodPower = AutoFishing.GetRodPower(data);
            int baitPower = AutoFishing.GetBaitPower(data);
            int extraPower = data.ExtraPower;
            int tempPower = 0;
            if (DateTime.UtcNow < data.FishingPotionTime) tempPower += Config.FishingPotionPower;
            if (DateTime.UtcNow < data.ChumBucketTime) tempPower += Config.ChumBucketPower;
            if (Config.CustomUsedItem.Count > 0)
                foreach (var UsedItem in Config.CustomUsedItem)
                    if (data.CustomConsumables.TryGetValue(UsedItem.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                        tempPower += state.Bonus;
            int Power = rodPower + baitPower + extraPower + tempPower;

            var PowerText = string.Empty;
            if (Power > 0)
                PowerText = TextGradient($"渔力:{Power}");

            args.Player.SendMessage(TextGradient($"欢迎来到[c/E8EB6E:{data.Owner}]的自动钓鱼机 当前 [c/FF716D:{data.RegionPlayers.Count}] 人"), color);
            args.Player.SendMessage(TextGradient($"环境:{env2},{string.Join(",", env)}"), color);
            args.Player.SendMessage(TextGradient($"在钓 {data.LiqName} [c/61BFE2:{data.MaxLiq}] 格 {PowerText}"), color);

            if (data.Exclude.Count > 0)
                args.Player.SendMessage($"排除物品: {string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id)}"))}", color2);

            var mess = new StringBuilder();
            if (Config.CustomUsedItem.Count > 0)
            {
                int idx = 1;
                mess.AppendLine($"区域buff:");
                foreach (var UsedItem in Config.CustomUsedItem)
                    if (data.CustomConsumables.TryGetValue(UsedItem.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                    {
                        double min = (state.Expiry - DateTime.UtcNow).TotalMinutes;
                        if (UsedItem.BuffID > 0)
                        {
                            string buffName = $"{UsedItem.BuffName}";
                            string buffDesc = $"[c/5F9DB8:-] {UsedItem.BuffDesc}";
                            mess.AppendLine($"{idx}.{buffName} 剩余[c/61BBE2:{FormatRemaining(min)}] \n{buffDesc}");
                            idx++;
                        }
                    }
            }
            args.Player.SendMessage(TextGradient(mess.ToString()), color);
        }

        DataManager.CanSave = true;
    }

    private void OnRegionLeave(RegionHooks.RegionLeftEventArgs args)
    {
        if (!Config.Enabled) return;

        if (!IsAfmRegion(args.Region.Name)) return;
        var plr = args.Player;
        if (plr == null || !plr.Active) return;

        var data = DataManager.FindRegion(args.Region.Name);
        if (data == null)
        {
            // 移除无效的区域或没缓存的钓鱼机
            RemoveRegion();
            return;
        }

        if (pend.ContainsKey(plr.Name))
            pend.Remove(plr.Name);

        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);

        // 玩家离开时整理一次箱子
        if (Config.AutoPut)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                var af = new AutoFishing(data);
                af.SortChest(chest);
            }
        }

        if (Config.RegionBroadcast)
            plr.SendMessage(TextGradient($"你离开了{data.Owner}的自动钓鱼机区域\n"), color);

        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0)
            plr.SendMessage(TextGradient($"【{data.Owner}自钓机】检测到附近没有玩家自动关闭"), color);

        DataManager.CanSave = true;
    }

    private void OnServerLeave(LeaveEventArgs args)
    {
        if (!Config.Enabled) return;

        var plr = TShock.Players[args.Who];
        if (plr == null || !IsAfmRegion(plr.CurrentRegion.Name)) return;

        if (plr.GetData<bool>("set"))
            plr.RemoveData("set");
        if (plr.GetData<bool>("info"))
            plr.RemoveData("info");

        if (pend.ContainsKey(plr.Name))
            pend.Remove(plr.Name);

        var data = DataManager.FindRegion(plr.CurrentRegion.Name);
        if (data == null || plr.CurrentRegion.Name != data.RegName) return;
        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);
        UpdateData(data, plr.TPlayer);
        DataManager.CanSave = true;
    }
    #endregion

    #region 玩家Buff更新事件（刷新区域Buff用）
    private void OnPlayerBuffUpdate(object sender, PlayerBuffUpdateEventArgs args)
    {
        if (!Config.Enabled || !Config.RegionBuffEnabled) return;

        args.Handled = true;

        var plr = args.Player;
        if (plr == null || !plr.Active) return;

        var data = GetDataByXY(plr.TileX, plr.TileY);
        if (data == null) return;
        RefreshBuffs(plr, data);
    }
    #endregion

    #region 刷新buff方法（由进入事件和Buff更新事件来驱动）
    public static void RefreshBuffs(TSPlayer plr, MachData data)
    {
        if (!Config.RegionBuffEnabled) return;
        if (!plr.Active || !data.RegionPlayers.Contains(plr)) return; // 玩家不在区域内，不刷新

        // 清理过期的BUFF
        var expired = data.ActiveZoneBuffs
            .Where(kvp => kvp.Value <= DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var buffId in expired)
            data.ActiveZoneBuffs.Remove(buffId);

        // 刷新有效BUFF
        foreach (var kvp in data.ActiveZoneBuffs)
        {
            if (kvp.Value > DateTime.UtcNow)
                plr.SetBuff(kvp.Key, 300); // 5秒刷新一次，实际会持续到过期
        }
    }
    #endregion

    #region 环境更新事件 定时更新钓鱼机缓存
    private void OnPlayerZone(object? sender, PlayerZoneEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        var data = GetDataByXY(plr.TileX, plr.TileY);
        if (data == null) return;

        // 玩家与钓鱼机距离超过20格则跳过
        // (避免从区域边界进来就更新，导致环境不一致)
        int dx = plr.TileX - data.Pos.X;
        int dy = plr.TileY - data.Pos.Y;
        int dist = (int)Math.Sqrt(dx * dx + dy * dy); // 欧几里得距离
        if (dist > Config.UpdateTileRange) return;

        var sec = TimeSpan.FromSeconds(Config.UpdateInterval);
        if (DateTime.UtcNow - data.LastEnvUpdate <= sec) return;

        UpdateData(data, plr.TPlayer);
    }

    // 根据位置获取机器的空间索引(区域)
    private static bool IsAfmRegion(string name) => name.StartsWith("afm_");
    private static MachData? GetDataByXY(int x, int y)
    {
        var rgns = TShock.Regions.InAreaRegion(x, y);
        foreach (var r in rgns)
            if (IsAfmRegion(r.Name))
                return DataManager.FindRegion(r.Name);
        return null;
    }
    #endregion

    #region 创建数据
    public static void CreateData(TSPlayer plr, int index, Point pos)
    {
        // 检查箱子是否已被占用
        var existing = DataManager.FindTile(pos);
        if (existing != null)
        {
            plr.SendErrorMessage($"该箱子已被 {existing.Owner} 的钓鱼机占用");
            return;
        }

        // 计算区域边界
        int r = Config.Range;
        int left = Math.Max(0, pos.X - r);
        int top = Math.Max(0, pos.Y - r);
        int right = Math.Min(Main.maxTilesX - 1, pos.X + r);
        int bot = Math.Min(Main.maxTilesY - 1, pos.Y + r);
        int w = right - left + 1;
        int h = bot - top + 1;
        var newRect = new Rectangle(left, top, w, h);

        // 重叠检查
        bool overlap = TShock.Regions.Regions.Any(rgn =>
            rgn.WorldID == Main.worldID.ToString() &&
            rgn.Area.Intersects(newRect) && IsAfmRegion(rgn.Name));

        if (overlap)
        {
            plr.SendErrorMessage($"与其他钓鱼机距离过近（{Config.Range}格）");
            return;
        }

        // 检查水体是否充足(不考虑连通性,UpdateData方法内部会缓存已连通的水域)
        var Liq = EnvManager.QuickLiquidCheck(pos);
        if (Liq < Config.NeedLiqStack)
        {
            plr.SendMessage($"箱子附近{Config.Range}格内液体不足{Config.NeedLiqStack}格", color2);
            return;
        }

        // 创建区域
        string rName = $"afm_{plr.Name}_{DateTime.Now.Ticks}";
        bool ok = TShock.Regions.AddRegion(left, top, w, h, rName, plr.Name, Main.worldID.ToString(), 0);
        if (!ok)
        {
            plr.SendErrorMessage("创建钓鱼区域失败！");
            return;
        }
        TShock.Regions.SetRegionState(rName, Config.DisabledBuild);

        // 创建新机器
        var NewData = new MachData
        {
            Owner = plr.Name,
            Pos = pos,
            RegName = rName,
            ChestIndex = index,
            IntMach = true,
            // 初始化下次执行时间为当前帧数 + 随机延迟，避免所有新机器同时执行
            nextFrame = Timer + Main.rand.Next(Config.MinFrames, Config.MaxFrames + 1)
        };

        UpdateData(NewData, plr.TPlayer);
    }
    #endregion

    #region 检查箱子位置是否有电线（2x2区域）
    private static bool HasWiring(Point pos)
    {
        for (int x = pos.X; x < pos.X + 2; x++)
        {
            for (int y = pos.Y; y < pos.Y + 2; y++)
            {
                var tile = Main.tile[x, y];
                if (tile != null && (tile.wire() || tile.wire2() || tile.wire3() || tile.wire4()))
                    return true;
            }
        }
        return false;
    }
    #endregion

    #region 更新所有缓存数据
    public static void UpdateData(MachData data, Player plr)
    {
        // 刷新钓鱼环境
        data.ZoneCorrupt = plr.ZoneCorrupt;
        data.ZoneCrimson = plr.ZoneCrimson;
        data.ZoneJungle = plr.ZoneJungle;
        data.ZoneSnow = plr.ZoneSnow;
        data.ZoneHallow = plr.ZoneHallow;
        data.ZoneDesert = plr.ZoneDesert;
        data.ZoneBeach = plr.ZoneBeach;
        data.ZoneDungeon = plr.ZoneDungeon;
        data.ZoneRain = plr.ZoneRain;

        // 刷新环境（无需玩家）
        EnvManager.RefreshEnv(data);
        DataManager.AddOrUpdate(data);

        // 标记环境已更新
        data.LastEnvUpdate = DateTime.UtcNow;
    }
    #endregion

    #region 同步移除区域与钓鱼机
    private static void RemoveRegion()
    {
        string worldId = Main.worldID.ToString();
        // 获取所有插件区域（名称以 "afm_" 开头）
        var Regions = TShock.Regions.Regions
            .Where(r => r.WorldID == worldId && r.Name.StartsWith("afm_"))
            .ToList();

        // 1. 删除孤立区域（没有对应机器的区域）
        foreach (var r in Regions)
        {
            if (DataManager.FindRegion(r.Name) == null)
            {
                TShock.Log.ConsoleInfo($"[{PluginName}] 删除无效区域: {r.Name}");
                TShock.Regions.DeleteRegion(r.Name);
            }
        }

        // 2. 删除孤立机器（区域不存在的机器）
        var Has = new List<MachData>();
        foreach (var data in DataManager.Machines)
        {
            if (!string.IsNullOrEmpty(data.RegName))
            {
                var region = TShock.Regions.GetRegionByName(data.RegName);
                if (region == null)
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 删除无效机器: {data.Owner}的钓鱼机 (区域 {data.RegName})");
                    Has.Add(data);
                }
            }
        }

        foreach (var machine in Has)
            DataManager.Remove(machine.Pos);

        // 如果有变更，保存数据
        if (Has.Count > 0)
            DataManager.CanSave = true;
    }
    #endregion

    #region 更新区域大小与建筑保护
    private static void UpdateRegions()
    {
        int updated = 0;
        int failed = 0;
        foreach (var data in DataManager.Machines)
        {
            if (string.IsNullOrEmpty(data.RegName)) continue;

            // 更新建筑保护
            TShock.Regions.SetRegionState(data.RegName, Config.DisabledBuild);

            int r = Config.Range;
            int left = Math.Max(0, data.Pos.X - r);
            int top = Math.Max(0, data.Pos.Y - r);
            int right = Math.Min(Main.maxTilesX - 1, data.Pos.X + r);
            int bot = Math.Min(Main.maxTilesY - 1, data.Pos.Y + r);
            int w = right - left + 1;
            int h = bot - top + 1;

            var newRect = new Rectangle(left, top, w, h);

            // 检查新矩形是否与其它插件区域重叠
            bool overlap = TShock.Regions.Regions.Any(rgn =>
                rgn.WorldID == Main.worldID.ToString() &&
                rgn.Name != data.RegName &&
                IsAfmRegion(rgn.Name) &&
                rgn.Area.Intersects(newRect));

            if (overlap)
            {
                TShock.Log.ConsoleWarn($"[{PluginName}] 无法更新区域 {data.RegName}：新区域与其它钓鱼机区域重叠");
                failed++;
                continue;
            }

            // 更新区域位置和大小
            if (TShock.Regions.PositionRegion(data.RegName, left, top, w, h))
            {
                updated++;
            }
            else
            {
                TShock.Log.ConsoleWarn($"[{PluginName}] 更新区域 {data.RegName} 失败");
                failed++;
            }
        }
        if (updated > 0 || failed > 0)
        {
            TShock.Log.ConsoleInfo($"[{PluginName}] 更新区域完成: 成功 {updated} 个, 失败 {failed} 个");
        }
    }
    #endregion

}