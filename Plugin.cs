using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.FishDropRules;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using static FishMach.DataManager;
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
    public override Version Version => new(1, 0, 9);
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
    // 优化：使用队列来管理待执行的机器，减少遍历次数
    private static readonly Queue<MachData> Queue = new Queue<MachData>();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        GeneralHooks.ReloadEvent += ReloadConfig;
        On.Terraria.WorldGen.KillTile += OnKillTile;
        On.Terraria.Wiring.Hopper += OnHopper;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        GetDataHandlers.ChestItemChange += OnChestItemChange!;
        GetDataHandlers.ChestOpen += OnChestOpen!;
        GetDataHandlers.PlayerZone += OnPlayerZone;
        GetDataHandlers.PlayerBuffUpdate += OnPlayerBuffUpdate!;
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
            On.Terraria.Wiring.Hopper -= OnHopper;
            ServerApi.Hooks.GamePostInitialize.Deregister(this, GamePost);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            GetDataHandlers.ChestItemChange -= OnChestItemChange!;
            GetDataHandlers.ChestOpen -= OnChestOpen!;
            GetDataHandlers.PlayerZone -= OnPlayerZone;
            GetDataHandlers.PlayerBuffUpdate -= OnPlayerBuffUpdate!;
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
        // 先保存钓鱼机数据
        foreach (var data in DataManager.Machines)
            DataManager.Save(data);

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

            // 更新区域范围与建筑保护属性
            UpdateRegions();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 加载配置文件失败: {ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args)
    {
        for (int i = 0; i < Machines.Count; i++)
        {
            DataManager.Save(DataManager.Machines[i]);
        }
    }

    private void GamePost(EventArgs args) // 加载完世界后事件
    {
        // 初始化掉落规则列表
        RuleList = new FishDropRuleList();
        new GameContentFishDropPopulator(RuleList).Populate();

        // 初始化大气因子
        EnvManager.InitAtmo();

        // 加载钓鱼机缓存
        DataManager.LoadAll();

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
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        Timer++;

        // 无电路模式：使用游戏更新事件自动定时执行
        if (!Config.NeedWiring)
        {
            // 优化：使用索引访问而不是foreach，减少迭代开销
            var all = DataManager.Machines;
            for (int i = 0; i < all.Count; i++)
            {
                MachData? data = all[i];

                // 检查是否到达执行时间
                if (Timer >= data.nextFrame)
                {
                    // 将机器加入执行队列
                    Queue.Enqueue(data);

                    // 计算下次执行时间（支持随机范围）
                    int min = Config.MinFrames;
                    int max = Config.MaxFrames;
                    int delay = min == max ? min : Main.rand.Next(min, max + 1);
                    data.nextFrame = Timer + delay;
                }
            }

            // 执行队列中的机器
            while (Queue.Count > 0)
            {
                var data = Queue.Dequeue();
                var engine = new AutoFishing(data);
                engine.Execute();
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

        // 如果没开启电路限频,则根据游戏内的计时器频率触发
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
                DataManager.Remove(pos);
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

        // 处理 sync 指令同步模式
        if (plr.GetData<bool>("sync"))
        {
            plr.RemoveData("sync");
            if (data == null)
            {
                // 没有钓鱼机数据，检查当前玩家所在区域是否为钓鱼机区域且数据丢失
                var region = plr.CurrentRegion;
                if (region != null && IsAfmRegion(region.Name) && FindRegion(region.Name) == null)
                {
                    TShock.Regions.DeleteRegion(region.Name);
                    plr.SendMessage(TextGradient($"数据丢失,已删除无效区域 {plr.CurrentRegion.Name}"), color);
                    return;
                }

                plr.SendErrorMessage("该位置没有钓鱼机");
                return;
            }

            // 检查箱子是否仍然存在且位置正确
            var chest2 = Main.chest[data.ChestIndex];
            if (chest2 == null || chest2.x != data.Pos.X || chest2.y != data.Pos.Y)
            {
                // 箱子已被移除或移动，区域无效，删除区域和机器
                TShock.Regions.DeleteRegion(plr.CurrentRegion.Name);
                DataManager.Remove(data.Pos);
                TShock.Utils.Broadcast($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已不存在\n" +
                                       $"删除无效区域: {plr.CurrentRegion.Name}", color);
                return;
            }

            // 检查区域是否存在，若不存在则重建
            var region2 = TShock.Regions.GetRegionByName(data.RegName);
            if (region2 == null)
            {
                int left, top, w, h;
                string RegionName = data.RegName;
                string owner = data.Owner;
                string worldId = Main.worldID.ToString();

                // 重叠检查
                if (IsOverlap(pos, worldId, "重建", out left, out top, out w, out h))
                    return;

                // 创建区域
                if (TShock.Regions.AddRegion(left, top, w, h, RegionName, owner, worldId, 0))
                {
                    TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);
                    TShock.Utils.Broadcast(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域已重建"), color);
                }
            }

            // 有数据，执行距离检查
            int dx = plr.TileX - data.Pos.X;
            int dy = plr.TileY - data.Pos.Y;
            int dist = (int)MathF.Sqrt(dx * dx + dy * dy);
            if (dist > Config.UpdateTileRange)
            {
                plr.SendErrorMessage($"距离钓鱼机过远（需在{Config.UpdateTileRange}格内），请靠近后再同步");
                return;
            }

            UpdateData(data, plr.TPlayer);
            EnvManager.UpdateItemCache(data);
            plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
            return;
        }

        if (data != null)
        {
            // 如果箱子没有连接电线，则提示
            if (Config.NeedWiring && !HasWiring(data.Pos))
            {
                plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] [c/75D1FF:未连接]电线," +
                                             "\n连接电路与计时器后将自动启动"), color2);
                return;
            }

            // 检查区域是否存在，若不存在则重建
            var region = TShock.Regions.GetRegionByName(data.RegName);
            if (region == null)
            {
                int left, top, w, h;
                string RegionName = data.RegName;
                string owner = data.Owner;
                string worldId = Main.worldID.ToString();

                // 重叠检查
                if (IsOverlap(pos, worldId, "重建", out left, out top, out w, out h))
                    return;

                // 创建区域
                if (TShock.Regions.AddRegion(left, top, w, h, RegionName, owner, worldId, 0))
                {
                    TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);
                    TShock.Utils.Broadcast(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域已重建"), color);
                }
            }

            DataManager.Save(data);
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

        // 批量排除模式：记录物品
        if (pend.TryGetValue(plr.Name, out var set))
        {
            if (e.Type != 0 && set.Add(e.Type))
                plr.SendMessage($"[c/FFA500:已记录]:{ItemIcon(e.Type)}", color2);

            return;
        }

        // 更新物品缓存
        EnvManager.UpdateItemCache(data);
        DataManager.Save(data);
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
            // 没有钓鱼机数据，检查当前玩家所在区域是否为钓鱼机区域且数据丢失
            var region = args.Region;
            if (region != null)
            {
                TShock.Regions.DeleteRegion(region.Name);
                args.Player.SendMessage(TextGradient($"数据丢失,已删除无效区域 {region.Name}"), color);
            }

            return;
        }

        // 检查箱子是否仍然存在且位置正确
        var chest = Main.chest[data.ChestIndex];
        if (chest == null || chest.x != data.Pos.X || chest.y != data.Pos.Y)
        {
            // 箱子已被移除或移动，区域无效，删除区域和机器
            TShock.Regions.DeleteRegion(args.Region.Name);
            DataManager.Remove(data.Pos);
            TShock.Utils.Broadcast($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已不存在\n" +
                                   $"删除无效区域: {args.Region.Name}", color);
            return;
        }

        // 添加到区域玩家集合
        if (!data.RegionPlayers.Contains(args.Player))
            data.RegionPlayers.Add(args.Player);

        // 立即刷新一次BUFF
        RefreshBuffs(args.Player, data);

        // 区域信息
        if (Config.RegionBroadcast)
            MyCommand.RegionInfo(args, data);

        // 玩家进入时整理一次箱子
        if (Config.AutoPut)
        {
            var Using = false;
            foreach (var plr in data.RegionPlayers)
                if (plr.ActiveChest == data.ChestIndex)
                    Using = true;

            var mess = string.Empty;

            new AutoFishing(data).SortChest(chest, Using, ref mess);

            if (!string.IsNullOrEmpty(mess) && Config.RegionBroadcast && data.RegionPlayers.Count > 0)
                foreach (var plr in data.RegionPlayers)
                    plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 触发自动整理:\n") + mess, color);
        }

        DataManager.Save(data);
    }

    private void OnRegionLeave(RegionHooks.RegionLeftEventArgs args)
    {
        if (!Config.Enabled) return;

        if (!IsAfmRegion(args.Region.Name)) return;
        var plr = args.Player;
        if (plr == null || !plr.Active) return;

        var data = DataManager.FindRegion(args.Region.Name);
        if (data == null) return;

        if (pend.ContainsKey(plr.Name))
            pend.Remove(plr.Name);

        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);

        if (Config.RegionBroadcast)
            plr.SendMessage(TextGradient($"\n你离开了钓鱼机 [c/ED756F:{data.ChestIndex}] 区域\n"), color);

        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0)
            plr.SendMessage(TextGradient($"检测到附近没有玩家自动关闭"), color);

        DataManager.Save(data);
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
        if (plr.GetData<bool>("sync"))
            plr.RemoveData("sync");

        if (pend.ContainsKey(plr.Name))
            pend.Remove(plr.Name);

        var data = DataManager.FindRegion(plr.CurrentRegion.Name);
        if (data == null || plr.CurrentRegion.Name != data.RegName) return;
        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);

        UpdateData(data, plr.TPlayer);
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
    private static void RefreshBuffs(TSPlayer plr, MachData data)
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

        // 玩家与钓鱼机距离超过10格则跳过
        // (避免从区域边界进来就更新，导致环境不一致)
        int dx = plr.TileX - data.Pos.X;
        int dy = plr.TileY - data.Pos.Y;
        int dist = (int)MathF.Sqrt(dx * dx + dy * dy); // 欧几里得距离
        if (dist > Config.UpdateTileRange) return;

        // 冷却检查 用于限频 避免频繁计算液体数量
        var sec = TimeSpan.FromSeconds(Config.UpdateInterval);
        if (DateTime.UtcNow - data.LastEnvUpdate <= sec) return;

        UpdateData(data, plr.TPlayer);
    }
    #endregion

    #region 检查箱子位置是否有电线（2x2区域）
    private static bool HasWiring(Point pos)
    {
        for (int x = pos.X; x < pos.X + 2; x++)
            for (int y = pos.Y; y < pos.Y + 2; y++)
            {
                var tile = Main.tile[x, y];
                if (tile != null && (tile.wire() || tile.wire2() || tile.wire3() || tile.wire4()))
                    return true;
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
        data.luck = plr.luck;

        // 刷新环境（无需玩家）
        EnvManager.RefreshEnv(data);
        AddOrUpdate(data);

        // 标记环境已更新
        data.LastEnvUpdate = DateTime.UtcNow;
    }
    #endregion

    #region 创建数据
    public static void CreateData(TSPlayer plr, int index, Point pos)
    {
        var sw = Stopwatch.StartNew();

        // 检查箱子是否已被占用
        var existing = DataManager.GetDataByXY(pos.X, pos.Y);
        if (existing != null)
        {
            plr.SendMessage($"该位置已被 {existing.Owner}的钓鱼机 [c/ED756F:{existing.ChestIndex}] 占用", color2);
            sw.Stop();
            return;
        }

        // 检查水体是否充足
        if (EnvManager.QuickLiquidCheck(pos) < Config.NeedLiqStack)
        {
            plr.SendMessage($"箱子附近 {Config.Range}格 液体不足:{Config.NeedLiqStack}", color2);
            sw.Stop();
            return;
        }

        // 计算区域边界
        int left, top, w, h;
        string RegionName = $"afm_{plr.Name}_{index}";
        string owner = plr.Name;
        string worldId = Main.worldID.ToString();

        // 重叠检查
        if (IsOverlap(pos, worldId, string.Empty, out left, out top, out w, out h))
        {
            sw.Stop();
            return;
        }

        // 创建区域
        if (TShock.Regions.AddRegion(left, top, w, h, RegionName, owner, worldId, 0))
            TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);

        // 创建新机器
        var data = new MachData
        {
            Owner = plr.Name,
            Pos = pos,
            RegName = RegionName,
            ChestIndex = index,
            IntMach = true,
            WorldId = worldId,
        };

        UpdateData(data, plr.TPlayer);

        sw.Stop();
        TSPlayer.All.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 创建用时 {sw.ElapsedMilliseconds} ms"), color);
    }
    #endregion

    #region 更新区域大小与建筑保护
    private static void UpdateRegions()
    {
        foreach (var data in DataManager.Machines)
        {
            if (string.IsNullOrEmpty(data.RegName)) continue;

            Point pos = data.Pos;
            int left, top, w, h;
            string RegionName = data.RegName;
            string owner = data.Owner;
            string worldId = Main.worldID.ToString();

            // 更新建筑保护
            TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);

            // 重叠检查
            if (IsOverlap(pos, worldId, "更新", out left, out top, out w, out h))
            {
                continue;
            }

            // 更新区域范围大小
            if (TShock.Regions.PositionRegion(RegionName, left, top, w, h))
            {
                TSPlayer.All.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域已更新"), color);
            }
        }
    }
    #endregion

    #region 检查区域重叠
    private static bool IsOverlap(Point pos, string worldId, string state, out int left, out int top, out int w, out int h)
    {
        int r = Config.Range;
        left = (int)MathF.Max(0, pos.X - r);
        top = (int)MathF.Max(0, pos.Y - r);
        int right = (int)MathF.Min(Main.maxTilesX - 1, pos.X + r);
        int bot = (int)MathF.Min(Main.maxTilesY - 1, pos.Y + r);
        w = right - left + 1;
        h = bot - top + 1;
        Rectangle newRect = new Rectangle(left, top, w, h);

        if (TShock.Regions.Regions.Any(rgn => rgn.WorldID == worldId && rgn.Area.Intersects(newRect)))
        {
            if (!string.IsNullOrEmpty(state))
            {
                string mess = $"钓鱼区 {pos.X},{pos.Y} {state}失败：\n" +
                              $"1.钓鱼机与其它区域重叠\n" +
                              $"2.与其他钓鱼机距离过近（{Config.Range}格）";

                TShock.Utils.Broadcast(TextGradient(mess), color);
            }

            return true;
        }

        return false;
    }
    #endregion

}