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
    public override Version Version => new(1, 1, 7);
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
        On.Terraria.Wiring.HitWireSingle += OnHitWireSingle;
        On.Terraria.WorldGen.PlaceChest += OnPlaceChest;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        ServerApi.Hooks.NpcSpawn.Register(this, OnNpcSpawn);
        ServerApi.Hooks.NpcKilled.Register(this, OnNPCKilled);
        GetDataHandlers.ChestItemChange += OnChestItemChange!;
        GetDataHandlers.ChestOpen += OnChestOpen!;
        GetDataHandlers.PlayerZone += OnPlayerZone;
        GetDataHandlers.PlayerBuffUpdate += OnPlayerBuffUpdate!;
        GetDataHandlers.LiquidSet += OnLiquidSet!;
        GetDataHandlers.NewProjectile += OnNewProjectile;
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
            On.Terraria.Wiring.HitWireSingle -= OnHitWireSingle;
            On.Terraria.WorldGen.PlaceChest -= OnPlaceChest;
            ServerApi.Hooks.GamePostInitialize.Deregister(this, GamePost);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            ServerApi.Hooks.NpcSpawn.Deregister(this, OnNpcSpawn);
            ServerApi.Hooks.NpcKilled.Deregister(this, OnNPCKilled);
            GetDataHandlers.ChestItemChange -= OnChestItemChange!;
            GetDataHandlers.ChestOpen -= OnChestOpen!;
            GetDataHandlers.PlayerZone -= OnPlayerZone;
            GetDataHandlers.PlayerBuffUpdate -= OnPlayerBuffUpdate!;
            GetDataHandlers.LiquidSet -= OnLiquidSet!;
            GetDataHandlers.NewProjectile -= OnNewProjectile;
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
            span[i].ClearAnim();
        }

        LoadConfig();
        FishSched.Init();   // 重建队列
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

            // 修剪钓鱼机数量（新增）
            TrimMachines();

            // 如果配置限制了最大传输箱数，则修剪超出的机器
            TrimOutChest();

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

        FishSched.Init();  // 无电路模式下初始化调度器
    }
    #endregion

    #region 游戏更新事件（主要触发器）
    public static long Timer = 0; // 帧计数器（每次游戏更新+1）
    public static readonly HashSet<MachData> ActiveAnim = new(); // 存储当前有待处理动画的机器
    public static readonly Queue<MachData> PutQueue = new();  // 存储当前有待处理物品传输的机器
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        Timer++;

        // 处理无电路模式 自动钓鱼
        if (!Config.NeedWiring)
            FishSched.Update(Timer);

        // 处理动画：每台机器独立
        var toRemove = new List<MachData>();
        foreach (var data in ActiveAnim.ToList())
        {
            // 如果开启无人关闭且区域内无玩家，则清空动画队列并跳过处理
            if ((Config.NeedWiring && !data.Wiring) || (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0))
            {
                toRemove.Add(data); // 标记，不立即删除
                continue;
            }

            if (data.AnimQueue.Count > 0 && Timer >= data.AnimFrame)
            {
                var req = data.AnimQueue.Dequeue();
                var engine = data.Engine ?? (data.Engine = new AutoFishing(data));
                switch (req.Type)
                {
                    case AnimType.Move:
                        engine.PlayMove(req.item, req.from, req.toPos, req.skipFake);
                        break;
                    case AnimType.Sparkle:
                        engine.PlaySparkle(req.toPos);
                        break;
                    case AnimType.Transfer:
                        engine.PlayMove(req.item, req.from, req.toPos, req.skipFake);
                        AutoFishing.PutWithFallback(req.item, req.chestIdx, req.data,true);
                        break;
                }
                data.AnimFrame = Timer + 60; // 每台机器独立计时
            }

            // 如果队列为空，从活跃集合中移除
            if (data.AnimQueue.Count == 0)
                toRemove.Add(data);
        }

        // 遍历结束后统一清理动画
        foreach (var data in toRemove)
        {
            data.ClearAnim(); // 清空队列
        }

        // 处理转移队列（每帧最多处理 5 台，避免卡顿）
        int count = 0;
        while (PutQueue.Count > 0 && count < Config.TransferStack)
        {
            var data = PutQueue.Dequeue();
            if (!data.HasOut) continue; // 传输模式已关闭
            if (!data.NeedPut) continue; // 已被其他逻辑处理
            AutoFishing.TransferItem(data);
            data.NeedPut = false;
            count++;
        }
    }
    #endregion

    #region 电路模式
    private void OnHitWireSingle(On.Terraria.Wiring.orig_HitWireSingle orig, int x, int y)
    {
        // 执行原方法
        orig(x, y);

        if (!Config.Enabled || !Config.NeedWiring) return;

        int x2 = x;
        int y2 = y;
        var tile = Main.tile[x, y];
        if (tile.frameX % 36 != 0) x2--;
        if (tile.frameY % 36 != 0) y2--;

        var pos = new Point(x2, y2);
        var data = FindTile(pos);
        if (data == null) return;

        // 记录触发帧(用于辅助判断是否开启电路)
        data.WiringFrame = Timer;

        // 限频检查：如果距离上次执行还没到最小间隔，则跳过
        if (Timer < data.nextFrame) return;

        var engine = data.Engine ?? (data.Engine = new AutoFishing(data));
        engine.Execute();

        // 更新下次执行时间（使用配置的间隔）
        int minFrames = Config.MinFrames;
        int maxFrames = Config.MaxFrames;
        int delay = minFrames == maxFrames ? minFrames : Main.rand.Next(minFrames, maxFrames + 1);
        data.nextFrame = Timer + delay;
    }

    //  检查箱子位置是否有电线（2x2区域）
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

    #region 环境同步事件（环境变化、液体修改、弹幕生成）
    private void OnPlayerZone(object? sender, PlayerZoneEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null || !plr.Active ||
            plr.CurrentRegion == null ||
            !IsAfmRegion(plr.CurrentRegion.Name)) return;

        var data = FindRegion(plr.CurrentRegion.Name);
        if (data == null) return;

        // 恢复液体自动检测
        if (data.LiqDead)
        {
            data.LiqDead = false;
            data.AnimFrame = 0; // 重置动画计时
        }

        // 同步环境（生物群落、幸运值等）
        if ((DateTime.UtcNow - data.LastZoneUpdate).TotalSeconds >= 10)
        {
            EnvManager.SyncZone(plr, data);
            data.LastZoneUpdate = DateTime.UtcNow;
        }
    }

    private void OnLiquidSet(object? sender, LiquidSetEventArgs e)
    {
        if (!Config.Enabled || e.Amount <= 0) return;

        var plr = e.Player;
        if (plr == null || !plr.Active ||
            plr.CurrentRegion == null ||
            !IsAfmRegion(plr.CurrentRegion.Name)) return;

        var data = FindRegion(plr.CurrentRegion.Name);
        if (data == null) return;

        // 恢复液体自动检测
        if (data.LiqDead)
        {
            data.LiqDead = false;
            data.AnimFrame = 0; // 重置动画计时
        }
    }

    private void OnNewProjectile(object? sender, NewProjectileEventArgs e)
    {
        if (!Config.Enabled) return;

        // 仅处理配置中指定的弹幕类型（如液体炸弹、环境改造弹等）
        if (!Config.LiqProj.Contains(e.Type)) return;

        var plr = e.Player;
        if (plr == null || !plr.Active ||
            plr.CurrentRegion == null ||
            !IsAfmRegion(plr.CurrentRegion.Name)) return;

        var data = FindRegion(plr.CurrentRegion.Name);
        if (data == null) return;

        // 恢复液体自动检测
        if (data.LiqDead)
        {
            data.LiqDead = false;
            data.AnimFrame = 0; // 重置动画计时
        }
    }
    #endregion

    #region 放置箱子事件，缓存区域附近的箱子
    private int OnPlaceChest(On.Terraria.WorldGen.orig_PlaceChest orig, int x, int y, ushort type, bool notNearOtherChests, int style)
    {
        var c = orig(x, y, type, notNearOtherChests, style);

        if (c != -1)
        {
            var region = TShock.Regions.InAreaRegion(x, y).FirstOrDefault(r => IsAfmRegion(r.Name));
            if (region != null)
            {
                var data = DataManager.FindRegion(region.Name);
                if (data != null)
                {
                    if (!DataManager.RegionChests.TryGetValue(region.Name, out var set))
                    {
                        set = new HashSet<int>();
                        DataManager.RegionChests[region.Name] = set;
                    }
                    set.Add(c);
                }
            }
        }

        return c;
    }
    #endregion

    #region 挖掉箱子自动移除钓鱼机与对应区域
    private static void OnKillTile(On.Terraria.WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
    {
        if (Config.Enabled)
        {
            var tile = Main.tile[x, y];
            if (tile == null || !TileID.Sets.BasicChest[tile.type])
            {
                orig(x, y, fail, effectOnly, noItem);
                return;
            }

            int x2 = x;
            int y2 = y;
            if (tile.frameX % 36 != 0) x2--;
            if (tile.frameY % 36 != 0) y2--;

            int idx = Chest.FindChest(x2, y2);

            if (idx != -1)
            {
                // 处理主箱
                var data = FindChest(idx);
                if (data != null && idx == data.ChestIndex)
                {
                    // 从区域箱子缓存中移除
                    if (DataManager.RegionChests.TryGetValue(data.RegName, out var chestSet))
                        chestSet.Remove(idx);

                    Remove(new Point(x2, y2));
                    TSPlayer.All.SendMessage(TextGradient($"钓鱼机已被摧毁: [c/ED756F:{data.ChestIndex}]"), color);
                }

                // 处理传输箱
                if (OutChestMap.TryGetValue(idx, out var outDataSet))
                {
                    var del = new List<string>();
                    foreach (var outData in outDataSet.ToList())
                    {
                        if (outData.OutChests.Contains(idx))
                        {
                            DataManager.RemoveOutChest(outData, idx);
                            outData.ClearAnim();
                            del.Add($"[c/ED756F:{outData.ChestIndex}]");

                            // 从区域箱子缓存中移除
                            if (DataManager.RegionChests.TryGetValue(outData.RegName, out var chestSet2))
                                chestSet2.Remove(idx);
                        }
                    }
                    if (del.Count > 0)
                        TSPlayer.All.SendMessage(TextGradient($"被摧毁传输箱的钓鱼机: {string.Join(",", del)}"), color);
                }
            }
        }

        // 最后执行原方法
        orig(x, y, fail, effectOnly, noItem);
    }
    #endregion

    #region 箱子打开事件（创建、查看数据）
    public static Dictionary<string, HashSet<int>> outPend = new(); // 玩家名 -> 待处理的传输箱索引集合
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

        // 批量传输箱记录模式
        if (outPend.TryGetValue(plr.Name, out var outSet))
        {
            if (c != -1 && !outSet.Contains(c))
            {
                outSet.Add(c);
                plr.SendMessage(TextGradient($"已记录传输箱: [c/FF6857:{c}]"), color);
            }
            return; // 记录模式下不执行后续钓鱼机相关逻辑
        }

        // 平时打开箱子 检查电线、检查区域存在、同步环境/物品/液体缓存
        if (data != null)
        {
            var text = string.Empty;
            if (data.MaxLiq < Config.NeedLiqStack)
                text += " 液体[c/FFB374:不足]";
            if (data.RodSlot < 0)
                text += " [c/FFB374:没有]鱼竿";
            if (data.BaitSlot < 0)
                text += " [c/FFB374:没有]鱼饵";

            if (Config.NeedWiring)
            {
                if (!HasWiring(data.Pos))
                    text += " [c/FFB374:未接]电线";

                if (!data.Wiring)
                    text += " [c/FFB374:未开]电路";
            }

            if (!string.IsNullOrEmpty(text))
                plr.SendMessage(TextGradient($"异常检测:{text}"), color);


            // 检查区域是否存在，若不存在则重建
            var region = TShock.Regions.GetRegionByName(data.RegName);
            if (region == null)
            {
                int left, top, w, h;
                string RegionName = data.RegName;
                string owner = data.Owner;
                string worldId = Main.worldID.ToString();

                // 重叠检查
                if (IsOverlap(pos, worldId, "重建", out left, out top, out w, out h, data.RegName))
                    return;

                // 创建区域
                if (TShock.Regions.AddRegion(left, top, w, h, RegionName, owner, worldId, 0))
                {
                    TShock.Regions.SetRegionState(RegionName, Config.RegionBuild);
                    plr.SendMessage(TextGradient($"\n钓鱼机区域已重建"), color);
                }
            }

            // 更新环境
            EnvManager.SyncZone(plr, data);
            // 更新物品缓存
            EnvManager.SyncItem(data);

            // 恢复液体自动检测
            if (data.LiqDead)
            {
                data.LiqDead = false;
                data.AnimFrame = 0; // 重置动画计时
            }

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

        // 恢复液体自动检测
        if (data.LiqDead)
        {
            data.LiqDead = false;
            data.AnimFrame = 0; // 重置动画计时
        }

        // 更新环境
        EnvManager.SyncZone(plr, data);
        // 更新物品缓存
        EnvManager.SyncItem(data);
        // 如果开启了传输模式且尚未入队，则加入转移队列
        if (data.HasOut && !data.NeedPut)
        {
            data.NeedPut = true;
            PutQueue.Enqueue(data);
        }
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
        {
            plr.SendMessage(TextGradient($"检测到附近没有玩家自动关闭"), color);
            // 清空动画队列
            data.ClearAnim();
        }

        Save(data);
    }

    private void OnServerLeave(LeaveEventArgs args)
    {
        if (!Config.Enabled) return;

        var plr = TShock.Players[args.Who];
        if (plr == null || !IsAfmRegion(plr.CurrentRegion.Name)) return;

        if (plr.ContainsData("set")) plr.RemoveData("set");
        if (plr.ContainsData("info")) plr.RemoveData("info");
        if (plr.ContainsData("sync")) plr.RemoveData("sync");

        if (plr.ContainsData("out"))
            plr.RemoveData("out");

        if (pend.ContainsKey(plr.Name))
            pend.Remove(plr.Name);

        if (outPend.ContainsKey(plr.Name))
            outPend.Remove(plr.Name);

        var data = FindRegion(plr.CurrentRegion.Name);
        if (data == null || plr.CurrentRegion.Name != data.RegName) return;
        if (data.RegionPlayers.Contains(plr))
            data.RegionPlayers.Remove(plr);

        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0)
        {
            // 清空动画队列
            data.ClearAnim();
        }

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
        // 检查钓鱼机数量上限
        if (Config.MaxMachines > 0 && Machines.Count >= Config.MaxMachines)
        {
            plr.SendMessage(TextGradient($"\n已达到最大钓鱼机数量限制:{Config.MaxMachines}"), color);
            return;
        }

        var sw = Stopwatch.StartNew();

        // 检查箱子是否已被占用
        var existing = DataManager.FindChest(index);
        if (existing != null)
        {
            plr.SendMessage($"\n该箱子已被占用: [c/ED756F:{existing.ChestIndex}] ", color2);
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
            plr.SendMessage(TextGradient($"\n附近存在区域重叠,无法创建钓鱼机"), color);
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
        UpdateRegions(data);
        if (!Config.NeedWiring)
            FishSched.Init();   // 重建队列，包含新机器
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

        data.LastPutFrame = Timer;
        // 添加数据并保存
        AddOrSave(data);
    }
    #endregion

    #region 更新区域大小与建筑保护
    public static void UpdateRegions(MachData? mach = null)
    {
        if (mach != null)
        {
            // 只更新指定的一台
            if (string.IsNullOrEmpty(mach.RegName)) return;

            Point pos = mach.Pos;
            int left, top, w, h;
            string regionName = mach.RegName;
            string worldId = Main.worldID.ToString();

            // 更新建筑保护
            TShock.Regions.SetRegionState(regionName, Config.RegionBuild);

            // 重叠检查
            if (IsOverlap(pos, worldId, "更新", out left, out top, out w, out h, regionName))
                return;

            // 更新区域范围
            if (TShock.Regions.PositionRegion(regionName, left, top, w, h))
            {
                TSPlayer.All.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{mach.ChestIndex}] 区域已更新"), color);
            }

            UpdateRegionChests(mach);
            return;
        }

        var all = Machines;
        Span<MachData> span = CollectionsMarshal.AsSpan(all);
        var list = new List<string>();
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
                continue;

            // 更新区域范围大小
            if (TShock.Regions.PositionRegion(RegionName, left, top, w, h))
                list.Add($"[c/ED756F:{data.ChestIndex}]");
        }

        // 一次性更新所有区域的箱子缓存
        UpdateAllRegionChests();

        if (list.Count > 0)
            TSPlayer.All.SendMessage(TextGradient($"钓鱼机区域已更新:{string.Join(",", list)}"), color);
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

    #region 修剪钓鱼机数量（超出限制时删除最后创建的）
    private static void TrimMachines()
    {
        if (Config.MaxMachines <= 0) return;

        int toRemove = Machines.Count - Config.MaxMachines;
        if (toRemove <= 0) return;

        // 从末尾开始删除（最后创建的）
        for (int i = 0; i < toRemove; i++)
        {
            var data = Machines[^1]; // 最后一个
            string info = $"{data.Owner}的钓鱼机 [c/ED756F:{data.ChestIndex}] - {data.Pos.X},{data.Pos.Y}";
            DataManager.Remove(data.Pos); // Remove 内部会清理映射、区域、文件
            TSPlayer.All.SendMessage(TextGradient($"钓鱼机已达上限 {Config.MaxMachines},已自动删除\n {info}"), color);
        }
    }
    #endregion

    #region 更新传输箱最大数量
    private static void TrimOutChest()
    {
        if (Config.MaxOutChest > 0)
        {
            int trimmed = 0;
            foreach (var data in Machines)
            {
                int max = Config.MaxOutChest;
                if (data.OutChests.Count > max)
                {
                    int removeCount = data.OutChests.Count - max;
                    for (int i = 0; i < removeCount; i++)
                    {
                        int lastIdx = data.OutChests[data.OutChests.Count - 1];
                        DataManager.RemoveOutChest(data, lastIdx);
                    }
                    trimmed++;
                }
            }

            if (trimmed > 0)
            {
                TSPlayer.All.SendMessage(TextGradient($"因配置最大传输箱数调整为 {Config.MaxOutChest}\n已自动移除超出的传输箱。"), color);
            }
        }
    }
    #endregion

}