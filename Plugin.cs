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
    public override Version Version => new(1, 0, 6);
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
        On.Terraria.Wiring.Hopper += OnHopper;
        GetDataHandlers.LiquidSet += OnLiquidSet!;
        GetDataHandlers.TileEdit += OnTileEdit!;
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
            On.Terraria.Wiring.Hopper -= OnHopper;
            GetDataHandlers.LiquidSet -= OnLiquidSet!;
            GetDataHandlers.TileEdit -= OnTileEdit!;
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
        DataManager.NeedSave();
        LoadConfig();
        // 移除无效的区域或没缓存的钓鱼机
        SyncRemoveRegion();
        // 更新区域范围与建筑保护属性
        UpdateAllRegions();
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
            Config.Write(); // 写回（确保配置存在）

            RuleList = new FishDropRuleList();
            var populator = new GameContentFishDropPopulator(RuleList);
            populator.Populate();

        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 加载配置文件失败: {ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args) => DataManager.NeedSave();
    private void GamePost(EventArgs args) // 加载完世界后事件
    {
        // 加载配置文件
        LoadConfig();
        // 加载钓鱼机缓存
        DataManager.Load();
        // 不管是无效机器还是无效区域都会同步移除(避免没正常关服导致箱子丢失 区域残留)
        SyncRemoveRegion();
        // 更新区域范围
        UpdateAllRegions();
        // 初始化保存定时器
        SaveFrame = Timer + Config.SaveInterval;
    }
    #endregion

    #region 游戏更新事件（触发器）
    private static long nextFrame = 0; // 下一次执行的目标帧数
    private static long Timer = 0; // 帧计数器（每次游戏更新+1）
    private static long SaveFrame = 0; // 保存缓存的帧数
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        Timer++;

        // 定期保存（脏数据）
        if (Timer >= SaveFrame)
        {
            if (DataManager.IsCanSave) DataManager.Save();
            SaveFrame = Timer + Config.SaveInterval;
        }

        if (!Config.NeedWiring && Timer >= nextFrame)
        {
            // 执行所有钓鱼机的钓鱼逻辑
            var all = DataManager.Machines;
            foreach (var m in all)
            {
                var engine = new AutoFishing(m);
                engine.Execute();
            }

            // 计算下一次执行的帧数间隔（支持随机范围）
            int min = Config.MinFrames;
            int max = Config.MaxFrames;
            int delay = min == max ? min : Main.rand.Next(min, max + 1);
            nextFrame = Timer + delay;
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

        var engine = new AutoFishing(data);
        engine.Execute();
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

    #region 箱子打开事件（创建、更新、获取数据）
    private void OnChestOpen(object sender, GetDataHandlers.ChestOpenEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null) return;

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

        // 处理 exclude 指令排除物品模式
        if (plr.GetData<bool>("exc"))
        {
            if (data == null) plr.SendErrorMessage("该位置没有钓鱼机");
            else
            {
                // 权限检查
                if (!IsAdmin(plr) && data.Owner != plr.Name)
                {
                    plr.SendErrorMessage("你没有权限修改别人的钓鱼机排除列表");
                    return;
                }

                int type = plr.SelectedItem.type;
                string name = plr.SelectedItem.Name;
                bool isRemove = data.Exclude.Contains(type);

                if (isRemove)
                {
                    data.Exclude.Remove(type);
                    TSPlayer.All.SendMessage($"{data.Owner}的钓鱼机 排除了 {ItemIcon(type, 1)} {name}", color2);
                }
                else
                {
                    data.Exclude.Add(type);
                    TSPlayer.All.SendMessage($"{data.Owner}的钓鱼机 添加了排除物品 {ItemIcon(type, 1)} {name}", color2);
                }

                plr.RemoveData("exc");
            }
        }

        // 处理钓鱼机箱子打开（更新环境、刷新缓存）
        if (data != null)
        {
            // 如果箱子没有连接电线，则提示
            if (Config.NeedWiring && !HasWiring(data.Pos))
            {
                plr.SendMessage(TextGradient($"{data.Owner}的自动钓鱼机[c/FF716D:未连接]电线," +
                                             "\n连接电路与计时器后将自动启动"), color2);
                return;
            }

            var now = DateTime.UtcNow;
            var envUpdInt = TimeSpan.FromSeconds(Config.UpdateInterval);
            if (now - data.LastEnvUpd > envUpdInt)
            {
                UpdateData(data, plr);
                data.LastEnvUpd = now;
            }
            return;
        }
    }
    #endregion

    #region 更改箱子物品事件(刷新缓存)
    private void OnChestItemChange(object sender, GetDataHandlers.ChestItemEventArgs e)
    {
        if (!Config.Enabled) return;

        // 验证箱子索引有效性
        if (e.ID < 0 || e.ID >= Main.chest.Length) return;

        var data = DataManager.FindChest(e.ID);
        if (data != null)
        {
            if (e.ID == data.ChestIndex)
            {
                // 如果箱子没有连接电线，则提示
                if (Config.NeedWiring && !HasWiring(data.Pos))
                {
                    e.Player.SendMessage(TextGradient($"{data.Owner}的自动钓鱼机[c/FF716D:未连接]电线," +
                                                      "\n连接电路与计时器后将自动启动"), color2);
                    return;
                }

                var now = DateTime.UtcNow;
                var envUpdInt = TimeSpan.FromSeconds(Config.UpdateInterval);
                if (now - data.LastEnvUpd > envUpdInt)
                {
                    UpdateData(data, e.Player);
                    data.LastEnvUpd = now;
                }
            }
            else
            {
                // 非主箱子变化：如果修改的箱子是鱼竿或鱼饵所在箱子，清除对应缓存
                if (e.ID == data.RodChest)
                {
                    data.RodChest = -1;
                    data.RodSlot = -1;
                }
                if (e.ID == data.BaitChest)
                {
                    data.BaitChest = -1;
                    data.BaitSlot = -1;
                }
            }
        }
    }
    #endregion

    #region 区域进出事件
    private static bool IsAfmRgn(string name) => name.StartsWith("afm_");
    private void OnRegionEnter(RegionHooks.RegionEnteredEventArgs args)
    {
        if (!IsAfmRgn(args.Region.Name)) return;
        var data = DataManager.FindByRgn(args.Region.Name);
        if (data == null) return;

        data.PlrCnt++;

        // 玩家进入时整理一次箱子
        if (data.ChestIndex != -1)
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
            args.Player.SendMessage(TextGradient($"\n欢迎来到 {data.Owner}的自钓机区域"), color);

            if (data.PlrCnt > 0)
                args.Player.SendMessage(TextGradient($"当前区域有{data.PlrCnt}个玩家在钓{data.LiqName}"), color);
        }

        DataManager.NeedSave();
    }

    private void OnRegionLeave(RegionHooks.RegionLeftEventArgs args)
    {
        if (!IsAfmRgn(args.Region.Name)) return;
        var data = DataManager.FindByRgn(args.Region.Name);
        if (data == null) return;
        data.PlrCnt--;

        if (Config.RegionBroadcast)
        {
            args.Player.SendMessage(TextGradient($"\n你离开了 {data.Owner}的自钓机区域"), color);

            // 无人自动关闭检查
            if (Config.AutoStopWhenEmpty && data.PlrCnt == 0)
                args.Player.SendMessage(TextGradient($"检测到附近没有玩家自动关闭"), color);
        }

        DataManager.NeedSave();
    }

    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;

        if (!IsAfmRgn(plr.CurrentRegion.Name)) return;

        var data = DataManager.FindByRgn(plr.CurrentRegion.Name);

        data.PlrCnt--;

        DataManager.NeedSave();
    }
    #endregion

    #region 图格液体编辑事件（每5秒更新一次,优先判断玩家是否在区域内）
    private void OnLiquidSet(object sender, GetDataHandlers.LiquidSetEventArgs e)
    {
        if (!Config.Enabled || e.Amount <= 0) return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        var data = GetDataByXY(e.TileX, e.TileY);
        if (data == null || plr.CurrentRegion.Name != data.RegName) return;

        if ((DateTime.UtcNow - data.LastEnvUpd).TotalSeconds > Config.UpdateInterval)
        {
            UpdateData(data, plr);
            data.LastEnvUpd = DateTime.UtcNow;
        }
    }

    private void OnTileEdit(object? sender, TileEditEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        switch (e.Action)
        {
            case EditAction.PlaceTile:
            case EditAction.KillTile:
            case EditAction.ReplaceTile:
            case EditAction.TryKillTile:
            case EditAction.Acutate:
            case EditAction.PlaceWall:
            case EditAction.KillWall:
            case EditAction.PlaceWire:
            case EditAction.PlaceWire2:
            case EditAction.PlaceWire3:
            case EditAction.PlaceWire4:
            case EditAction.KillWire:
            case EditAction.KillWire2:
            case EditAction.KillWire3:
            case EditAction.KillWire4:
                {
                    if (e.EditData == 0)
                    {
                        var data = GetDataByXY(e.X, e.Y);
                        if (data == null || plr.CurrentRegion.Name != data.RegName) return;
                        if ((DateTime.UtcNow - data.LastEnvUpd).TotalSeconds > Config.UpdateInterval)
                        {
                            UpdateData(data, plr);
                            data.LastEnvUpd = DateTime.UtcNow;
                        }
                    }
                }
                break;
        }
    }

    // 根据位置获取机器（通过区域）
    private static MachData? GetDataByXY(int x, int y)
    {
        var rgns = TShock.Regions.InAreaRegion(x, y);
        foreach (var r in rgns)
            if (IsAfmRgn(r.Name))
                return DataManager.FindByRgn(r.Name);
        return null;
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

    #region 创建数据
    private void CreateData(TSPlayer plr, int index, Point pos)
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
            rgn.Area.Intersects(newRect) && IsAfmRgn(rgn.Name));

        if (overlap)
        {
            plr.SendErrorMessage($"与其他钓鱼机距离过近（{Config.Range}格）");
            return;
        }

        // 检查水体是否充足(不考虑连通性,UpdateData方法内部会缓存已连通的水域)
        var LiqName = string.Empty;
        var Liq = EnvManager.QuickLiquidCheck(pos, ref LiqName);
        if (Liq < 75)
        {
            plr.SendMessage($"箱子附近{Config.Range}格内液体不足75格", color2);
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
            ChestIndex = index
        };

        UpdateData(NewData, plr);
        var NeedWiring = Config.NeedWiring ? $"并[c/FF716D:连上电线]与计时器" : "\n";
        plr.SendMessage(TextGradient($"\n{plr.Name}的自动钓鱼机 ({pos.X},{pos.Y}) 创建成功! "), color);
        plr.SendMessage(TextGradient($"附近 {Config.Range}格 [c/FF716D:最多的液体]为:{LiqName}"), color);
        plr.SendMessage(TextGradient($"请给箱子放入鱼竿和鱼饵 {NeedWiring}"), color);
    }
    #endregion

    #region 更新所有缓存数据
    public static void UpdateData(MachData data, TSPlayer? plr = null)
    {
        // 刷新钓鱼环境
        if (plr != null)
        {
            data.ZoneCorrupt = plr.TPlayer.ZoneCorrupt;
            data.ZoneCrimson = plr.TPlayer.ZoneCrimson;
            data.ZoneJungle = plr.TPlayer.ZoneJungle;
            data.ZoneSnow = plr.TPlayer.ZoneSnow;
            data.ZoneHallow = plr.TPlayer.ZoneHallow;
            data.ZoneDesert = plr.TPlayer.ZoneDesert;
            data.ZoneBeach = plr.TPlayer.ZoneBeach;
            data.ZoneDungeon = plr.TPlayer.ZoneDungeon;
        }

        // 刷新环境（无需玩家）
        EnvManager.RefreshEnv(data);
        DataManager.AddOrUpdate(data);
    }
    #endregion

    #region 同步移除区域与钓鱼机
    private static void SyncRemoveRegion()
    {
        string worldId = Main.worldID.ToString();
        // 获取所有插件区域（名称以 "afm_" 开头）
        var Regions = TShock.Regions.Regions
            .Where(r => r.WorldID == worldId && r.Name.StartsWith("afm_"))
            .ToList();

        // 1. 删除孤立区域（没有对应机器的区域）
        foreach (var r in Regions)
        {
            if (DataManager.FindByRgn(r.Name) == null)
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
            DataManager.NeedSave();
    }
    #endregion

    #region 更新区域大小与建筑保护
    private static void UpdateAllRegions()
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
                IsAfmRgn(rgn.Name) &&
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