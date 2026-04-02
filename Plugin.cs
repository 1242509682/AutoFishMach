using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.FishDropRules;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
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
    public override Version Version => new(1, 1, 0);
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
    // 使用队列来管理待执行的机器，减少遍历次数
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
        ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
        ServerApi.Hooks.NpcKilled.Register(this, OnNPCKilled);
        GetDataHandlers.ChestItemChange += OnChestItemChange!;
        GetDataHandlers.ChestOpen += OnChestOpen!;
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
            ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Deregister(this, OnNPCKilled);
            GetDataHandlers.ChestItemChange -= OnChestItemChange!;
            GetDataHandlers.ChestOpen -= OnChestOpen!;
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
        var all = Machines;
        var span = CollectionsMarshal.AsSpan(all);
        for (int i = 0; i < span.Length; i++)
        {
            Save(span[i]);
        }

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
            TShock.Log.ConsoleError($"[{PluginName}] 配置文件加载失败：\n{ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args)
    {
        var all = Machines;
        var span = CollectionsMarshal.AsSpan(all);
        for (int i = 0; i < span.Length; i++)
        {
            Save(span[i]);
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
        var all = Machines;
        var span = CollectionsMarshal.AsSpan(all);
        for (int i = 0; i < span.Length; i++)
        {
            var data = span[i];
            int min = Config.MinFrames;
            int max = Config.MaxFrames;
            int delay = Main.rand.Next(min, max + 1);

            if (data.nextFrame == 0)
                data.nextFrame = Timer + delay;
        }
    }
    #endregion

    #region 游戏更新事件（主要触发器）
    public static long Timer = 0; // 帧计数器（每次游戏更新+1）
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        Timer++;

        // 无电路模式：使用游戏更新事件自动定时执行
        if (!Config.NeedWiring)
        {
            // 优化：使用索引访问而不是foreach，减少迭代开销
            var all = Machines;
            var span = CollectionsMarshal.AsSpan(all);
            for (int i = 0; i < span.Length; i++)
            {
                ref var data = ref span[i];

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

            // 限制每帧执行数量，防止卡顿
            int maxCount = 0;
            while (Queue.Count > 0 && maxCount < 10)
            {
                var data = Queue.Dequeue();
                (data.Engine ?? (data.Engine = new AutoFishing(data))).Execute();
                maxCount++;
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

        // 如果没开启电路限频,则根据游戏内的计时器频率触发
        if (!Config.LimitFrames)
        {
            (data.Engine ?? (data.Engine = new AutoFishing(data))).Execute();
            return;
        }

        // 限频检查：如果距离上次执行还没到最小间隔，则跳过
        int minFrames = Config.MinFrames;
        if (Timer < data.nextFrame)
            return;

        (data.Engine ?? (data.Engine = new AutoFishing(data))).Execute();

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
            EnvManager.SyncForChestOpen(plr, data, pos);
            plr.RemoveData("sync");
            return;
        }

        // 平时打开箱子 检查电线、检查区域存在、同步环境/物品/液体缓存
        if (data != null)
        {
            // 如果箱子没有连接电线，则提示
            if (Config.NeedWiring && !HasWiring(data.Pos))
            {
                plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] [c/75D1FF:未连接]电线," +
                                             "\n连接电路与计时器后将自动启动"), color2);
                return;
            }

            // 如果液体不足导致机器暂停，玩家打开箱子时恢复检测
            if (data.LiquidDead)
            {
                data.LiquidDead = false;
                plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 尝试修正锚点坐标.."), color);
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
                if (IsOverlap(pos, worldId, "重建", out left, out top, out w, out h,data.RegName))
                    return;

                // 创建区域
                if (TShock.Regions.AddRegion(left, top, w, h, RegionName, owner, worldId, 0))
                {
                    TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);
                    TShock.Utils.Broadcast(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域已重建"), color);
                }
            }

            // 更新环境
            EnvManager.SyncZone(plr, data);
            // 更新物品缓存
            EnvManager.SyncItem(data);
            // 更新液体
            EnvManager.SyncLiquid(data);
            Save(data); // 保存一次
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

        // 如果箱子没有连接电线，则提示
        if (Config.NeedWiring && !HasWiring(data.Pos))
        {
            plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] [c/75D1FF:未连接]电线," +
                                         "\n连接电路与计时器后将自动启动"), color2);
            return;
        }

        // 如果液体不足导致机器暂停，玩家打开箱子时恢复检测
        if (data.LiquidDead)
        {
            data.LiquidDead = false;
            plr.SendMessage(TextGradient($"鱼池液体不足[c/FF6352:已暂停]工作\n" +
                                         $"正在尝试修复ing..."), color);
        }

        // 更新环境
        EnvManager.SyncZone(plr, data);
        // 更新物品缓存
        EnvManager.SyncItem(data);
        // 更新液体
        EnvManager.SyncLiquid(data);
    }
    #endregion

    #region 区域进出事件
    private void OnRegionEnter(RegionHooks.RegionEnteredEventArgs args)
    {
        if (!Config.Enabled) return;

        if (!IsAfmRegion(args.Region.Name)) return;

        var data = FindRegion(args.Region.Name);
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

        Save(data);
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

        Save(data);
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

        var data = FindRegion(plr.CurrentRegion.Name);
        if (data == null || plr.CurrentRegion.Name != data.RegName) return;
        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);

        Save(data);
    }
    #endregion

    #region 玩家Buff更新事件（刷新区域Buff用）
    private void OnPlayerBuffUpdate(object sender, PlayerBuffUpdateEventArgs args)
    {
        if (!Config.Enabled || !Config.RegionBuffEnabled) return;

        var plr = args.Player;
        if (plr == null || !plr.Active || plr.CurrentRegion == null) return;

        var data = FindRegion(plr.CurrentRegion.Name);
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

    #region 怪物生成与死亡事件 (更新npc缓存,检测钓出单独怪物)
    private void OnNpcSpawn(NpcSpawnEventArgs args)
    {
        if (!Config.Enabled ||
            !Config.EnableCustomNPC ||
            !Config.SoloCustomMonster) return;

        var npc = Main.npc[args.NpcId];
        if (npc == null || !npc.active || npc.townNPC ||
            npc.SpawnedFromStatue || npc.catchItem != 0 ||
            npc.type == NPCID.WallofFlesh ||
            npc.type == NPCID.TargetDummy) return;

        // 更新钓鱼机的NPC缓存
        UpdateNpcCache(npc, true);
    }

    private void OnNPCKilled(NpcKilledEventArgs args)
    {
        if (!Config.Enabled ||
            !Config.EnableCustomNPC ||
            !Config.SoloCustomMonster) return;

        var npc = args.npc;
        if (npc == null || !npc.active || npc.townNPC ||
            npc.SpawnedFromStatue || npc.catchItem != 0 ||
            npc.type == NPCID.WallofFlesh ||
            npc.type == NPCID.TargetDummy) return;

        // 更新钓鱼机的NPC缓存
        UpdateNpcCache(npc, false);
    }
    #endregion

    #region 更新钓鱼机的NPC缓存
    private static void UpdateNpcCache(NPC npc, bool add)
    {
        // 只处理自定义怪物
        bool isCustom = Config.CustomFishes.Any(r => r.NPCType == npc.type);
        if (!isCustom) return;

        // 获取 NPC 所在的钓鱼机区域
        int tileX = (int)(npc.position.X / 16);
        int tileY = (int)(npc.position.Y / 16);
        var region = TShock.Regions.InAreaRegion(tileX, tileY).
                     FirstOrDefault(r => IsAfmRegion(r.Name));

        if (region == null) return;

        var data = FindRegion(region.Name);
        if (data == null) return;

        if (add)
        {
            data.Monsters.TryGetValue(npc.type, out int cnt);
            data.Monsters[npc.type] = cnt + 1;
        }
        else
        {
            if (data.Monsters.TryGetValue(npc.type, out int cnt) && cnt > 0)
            {
                if (cnt == 1) data.Monsters.Remove(npc.type);
                else data.Monsters[npc.type] = cnt - 1;
            }
        }
    }
    #endregion

    #region 创建数据
    public static void CreateData(TSPlayer plr, int index, Point pos)
    {
        var sw = Stopwatch.StartNew();

        // 检查箱子是否已被占用
        var existing = DataManager.FindTile(pos);
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

        UpdateData(data, plr);
        sw.Stop();
        TSPlayer.All.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 创建用时 {sw.ElapsedMilliseconds} ms"), color);
    }
    #endregion

    #region 更新所有缓存数据
    public static void UpdateData(MachData data, TSPlayer plr)
    {
        // 初始化设置一次,其他时候不设置
        if (data.IntMach)
        {
            // 高度等级
            int yPos = data.Pos.Y;
            if (Main.remixWorld)
                data.HeightLevel = yPos < Main.worldSurface * 0.5 ? 0 : yPos < Main.worldSurface ? 1 : yPos < Main.rockLayer ? 3 : yPos < Main.maxTilesY - 300 ? 2 : 4;
            else
                data.HeightLevel = yPos < Main.worldSurface * 0.5 ? 0 : yPos < Main.worldSurface ? 1 : yPos < Main.rockLayer ? 2 : yPos < Main.maxTilesY - 300 ? 3 : 4;

            // 初始化大气因子
            EnvManager.InitAtmo();
            data.atmo = EnvManager.GetAtmo(yPos);

            // 颠倒海洋
            data.RolledRemixOcean = Main.remixWorld && data.HeightLevel == 1 && yPos >= Main.rockLayer && Main.rand.Next(3) == 0;
            data.IntMach = false;
        }

        // 刷新钓鱼环境
        EnvManager.SyncZone(plr, data);
        // 刷新物品缓存
        EnvManager.SyncItem(data);
        // 计算一次液体
        EnvManager.SyncLiquid(data);
        // 添加数据并保存
        AddOrSave(data);
    }
    #endregion

    #region 更新区域大小与建筑保护
    private static void UpdateRegions()
    {
        var all = Machines;
        var span = CollectionsMarshal.AsSpan(all);
        for (int i = 0; i < span.Length; i++)
        {
            ref var data = ref span[i];
            if (string.IsNullOrEmpty(data.RegName)) continue;

            Point pos = data.Pos;
            int left, top, w, h;
            string RegionName = data.RegName;
            string worldId = Main.worldID.ToString();

            // 更新建筑保护
            TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);

            // 重叠检查
            if (IsOverlap(pos, worldId, "更新", out left, out top, out w, out h, RegionName))
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
    public static bool IsOverlap(Point pos, string worldId, string state, out int left, out int top, out int w, out int h, string? self = null)
    {
        int r = Config.Range;
        left = (int)MathF.Max(0, pos.X - r);
        top = (int)MathF.Max(0, pos.Y - r);
        int right = (int)MathF.Min(Main.maxTilesX - 1, pos.X + r);
        int bot = (int)MathF.Min(Main.maxTilesY - 1, pos.Y + r);
        w = right - left + 1;
        h = bot - top + 1;
        Rectangle newRect = new Rectangle(left, top, w, h);

        // 检查重叠时排除自身
        var overlap = TShock.Regions.Regions.Any(rgn =>
            rgn.WorldID == worldId &&
            rgn.Area.Intersects(newRect) &&
            rgn.Name != self);

        if (overlap)
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

    #region 检查箱子位置是否有电线（2x2区域）
    public static bool HasWiring(Point pos)
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
}