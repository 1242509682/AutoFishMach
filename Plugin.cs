using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.FishDropRules;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using static Plugin.DataStorage;
using static Plugin.Utils;

namespace Plugin;

[ApiVersion(2, 1)]
public class Plugin(Main game) : TerrariaPlugin(game)
{
    #region 插件信息
    public static string PluginName => "自动钓鱼机";
    public override string Name => PluginName;
    public override string Author => "羽学";
    public override Version Version => new(1, 0, 0);
    public override string Description => "使用/afm 指令指定一个箱子作为自动钓鱼机";
    #endregion

    #region 文件路径
    public static readonly string MainPath = Path.Combine(TShock.SavePath, PluginName); // 主文件夹路径
    public static readonly string Paths = Path.Combine(MainPath, $"配置文件.json"); // 配置文件路径
    public static string CachePath(int worldID) => Path.Combine(MainPath, $"数据缓存_{worldID}.json"); // 缓存文件路径
    #endregion

    #region 静态成员
    public static string afm = "afm";
    public static string perm = "afm.use";
    public static bool IsAdmin(TSPlayer plr) => plr.HasPermission("afm.admin");
    private static FishDropRuleList ruleList = new();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        LoadConfig();
        GeneralHooks.ReloadEvent += ReloadConfig;
        On.Terraria.WorldGen.KillTile += OnKillTile;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        GetDataHandlers.ChestItemChange.Register(OnChestItemChange!);
        Commands.ChatCommands.Add(new Command(perm, MyCommand.CmdAfm, afm));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= ReloadConfig;
            On.Terraria.WorldGen.KillTile -= OnKillTile;
            ServerApi.Hooks.GamePostInitialize.Deregister(this, GamePost);
            ServerApi.Hooks.WorldSave.Deregister(this, OnWorldSave);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnGameUpdate);
            GetDataHandlers.ChestItemChange.UnRegister(OnChestItemChange!);
            Commands.ChatCommands.RemoveAll(x => x.CommandDelegate == MyCommand.CmdAfm);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new(); // 配置文件实例
    private static void ReloadConfig(ReloadEventArgs args)
    {
        LoadConfig();
        args.Player.SendMessage($"[{PluginName}]重新加载配置完毕。", color);
    }
    private static void LoadConfig()
    {
        try
        {
            if (!Directory.Exists(MainPath))
                Directory.CreateDirectory(MainPath);

            Config = Configuration.Read();
            Config.ParseFrames();
            Config.Write();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 加载配置文件失败: {ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args) => Save();
    private void GamePost(EventArgs args)
    {
        Load();
        ruleList = new FishDropRuleList();
        var populator = new GameContentFishDropPopulator(ruleList);
        populator.Populate();
    }
    #endregion

    #region 游戏更新事件
    private static long nextFrame = 0;          // 下一次执行的目标帧数
    private static long frameCounter = 0;       // 帧计数器（每次游戏更新+1）
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        frameCounter++;
        if (frameCounter < nextFrame) return;

        // 执行所有钓鱼机
        var all = GetAll();
        foreach (var m in all)
            FishOnce(m);

        // 计算下一次执行的帧数间隔（支持随机范围）
        int min = Config.MinFrames;
        int max = Config.MaxFrames;
        int delay = min == max ? min : Main.rand.Next(min, max + 1);
        nextFrame = frameCounter + delay;
    }
    #endregion

    #region 挖掉箱子自动移除钓鱼机
    private static void OnKillTile(On.Terraria.WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
    {
        if (!Config.Enabled)
        {
            orig(x, y, fail, effectOnly, noItem);
            return;
        }

        Remove(new Point(x, y));
        orig(x, y, fail, effectOnly, noItem);
    } 
    #endregion

    #region 放入物品到箱子时自动配置钓鱼机
    private void OnChestItemChange(object sender, GetDataHandlers.ChestItemEventArgs e)
    {
        // 检查功能是否启用
        if (!Config.Enabled) return;

        // 获取箱子实例
        var chest = Main.chest[e.ID];
        if (chest == null) return;

        // 箱子坐标（图格单位）
        var chestPos = new Point(chest.x, chest.y);

        // 查找附近的机器（范围 10 格）
        var nearby = GetAll()
            .Where(m => Math.Abs(m.Pos.X - chestPos.X) <= Config.Range &&
                        Math.Abs(m.Pos.Y - chestPos.Y) <= Config.Range)
            .ToList();

        if (nearby.Count == 0) return;
        // 取第一个钓鱼机
        var machine = nearby.First();

        // 调用更新方法
        machine.UpdateSlot(e.Type, e.Stacks);

        Save(); // 保存机器数据

        // 发送通知
        var operation = e.Stacks > 0 ?
            $"添加了{e.Stacks}个{Lang.GetItemNameValue(e.Type)}" :
            "已移除物品\n注:重启钓鱼机需要重新放入鱼竿";
        var text = $"\n[{machine.Pos.X},{machine.Pos.Y}] {machine.Owner}的钓鱼机 {operation}\n";
        e.Player.SendMessage(TextGradient(text), color);
    }
    #endregion

    #region 钓鱼核心
    private static void FishOnce(MachData data)
    {
        try
        {
            // 检查鱼竿
            if (data.FishRod == -1)
            {
                if (Config.Broadcast && (DateTime.Now - data.lastMissingWarning).TotalSeconds > Config.Warning)
                {
                    data.lastMissingWarning = DateTime.Now;
                    TShock.Utils.Broadcast($"\n{data.Owner}的钓鱼机已暂停工作,请打开箱子重放鱼竿", color2);
                    TShock.Utils.Broadcast($"传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}", color2);
                }
                return;
            }

            var fishRod = new Item();
            fishRod.SetDefaults(data.FishRod);
            if (fishRod.fishingPole <= 0) return;

            // 查找鱼饵
            if (!FindBait(data, out Item baitItem, out Chest chest, out int slot))
            {
                if (Config.Broadcast && (DateTime.Now - data.lastMissingWarning).TotalSeconds > Config.Warning)
                {
                    data.lastMissingWarning = DateTime.Now;
                    TShock.Utils.Broadcast($"\n{data.Owner}的钓鱼机 附近 {Config.Range} 格内没有鱼饵", color2);
                    TShock.Utils.Broadcast($"传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}", color2);
                }
                return;
            }

            // 确认箱子有效性（防止查找后箱子被破坏）
            if (chest == null ||
                chest.x < 0 || chest.x >= Main.maxTilesX ||
                chest.y < 0 || chest.y >= Main.maxTilesY)
                return;

            // 计算渔力
            int Power = fishRod.fishingPole + baitItem.bait;
            if (Config.Power > 0) Power += Config.Power;
            Power += GetBonus(data.Acc);

            // 消耗鱼饵（概率）
            if (ConsumeBait(chest, slot, baitItem, Power))
            {
                var context = BuildFishingContext(data, Power, baitItem);
                int itemType = ruleList.TryGetItemDropType(context);
                if (itemType == 0) return;

                var fish = new Item();
                fish.SetDefaults(itemType);
                fish.stack = 1;

                if (data.Exclude.Contains(fish.type)) return;

                // 直接尝试放入附近箱子
                TryDeposit(data, fish);

                if (Config.Broadcast)
                    TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 钓到了 {ItemIcon(fish.type, fish.stack)} 坐标:{data.Pos.X} {data.Pos.Y}", color2);

                Save(); // 保存机器数据
            }
        }
        catch (Exception ex) 
        {
            TShock.Log.ConsoleError($"{ex}");
        }
    }
    #endregion

    #region 创建钓鱼对象信息
    public static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    private static FishingContext BuildFishingContext(MachData data, int fishingPower, Item baitItem)
    {
        // 环境冲突处理（每次钓鱼时随机决定）
        bool corruption = data.ZoneCorrupt;
        bool crimson = data.ZoneCrimson;
        bool jungle = data.ZoneJungle;
        bool snow = data.ZoneSnow;
        bool hallow = data.ZoneHallow;
        bool desert = data.ZoneDesert;

        if (corruption && crimson)
        {
            if (Main.rand.Next(2) == 0)
                crimson = false;
            else
                corruption = false;
        }
        if (jungle && snow && Main.rand.Next(2) == 0)
            jungle = false;

        // 感染沙漠：沙漠 + 腐化/猩红/神圣 之一
        bool infectedDesert = desert && (corruption || crimson || hallow);

        // 水体统计
        int lavaTiles = 0, honeyTiles = 0;
        int waterTiles = GetWaterTiles(data.Pos,ref lavaTiles,ref honeyTiles);

        // 高度等级（使用缓存）
        int heightLevel = data.HeightLevel;

        // 大气因子（根据 y 坐标实时计算，因为世界可能被动态改变）
        int yPos = data.Pos.Y;
        float atmo;
        if (yPos < Main.worldSurface * 0.5)
            atmo = 0.25f;
        else if (yPos < Main.worldSurface)
            atmo = 0.5f;
        else
            atmo = 1f;

        int waterNeeded = (int)(300f * atmo);
        float waterQuality = Math.Min(1f, (float)waterTiles / waterNeeded);
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

        // 稀有度标志
        bool common, uncommon, rare, veryrare, legendary, crate;
        FishingCheck_RollDropLevels(fishingPower, out common, out uncommon, out rare, out veryrare, out legendary, out crate);

        // 任务鱼
        int questFish = Main.anglerQuestItemNetIDs[Main.anglerQuest];
        if (Main.player[Main.myPlayer].HasItem(questFish) || !NPC.AnyNPCs(369) || Main.anglerQuestFinished)
            questFish = -1;

        // 可熔岩钓鱼
        bool canFishInLava = data.Acc.Any(id => lavaItems.Contains(id));

        // remix 海洋（使用缓存的标志，因为世界固定后不再变化）
        bool rolledRemixOcean = data.RolledRemixOcean;

        // 构建上下文
        var fc = new FishingContext
        {
            Random = new Terraria.Utilities.UnifiedRandom(Main.rand.Next()),
            Fisher = new FishingAttempt(),
            Player = new Player(), // 不再需要 Player 对象，因为环境已缓存
            RolledCorruption = corruption,
            RolledCrimson = crimson,
            RolledJungle = jungle,
            RolledSnow = snow,
            RolledDesert = desert,
            RolledInfectedDesert = infectedDesert && Main.rand.Next(2) == 0,
            RolledRemixOcean = rolledRemixOcean
        };

        fc.Fisher.common = common;
        fc.Fisher.uncommon = uncommon;
        fc.Fisher.rare = rare;
        fc.Fisher.veryrare = veryrare;
        fc.Fisher.legendary = legendary;
        fc.Fisher.crate = crate;
        fc.Fisher.heightLevel = heightLevel;
        fc.Fisher.atmo = atmo;
        fc.Fisher.waterTilesCount = waterTiles;
        fc.Fisher.waterQuality = waterQuality;
        fc.Fisher.fishingLevel = fishingPower;
        fc.Fisher.inLava = lavaTiles > 0;
        fc.Fisher.inHoney = honeyTiles > 0;
        fc.Fisher.rolledEnemySpawn = Main.rand.Next(100) < (fishingPower / 200f) ? 1 : 0;
        fc.Fisher.questFish = questFish;
        fc.Fisher.CanFishInLava = canFishInLava;

        return fc;
    }

    // 原版稀有度计算（参考 FishingCheck_RollDropLevels）
    private static void FishingCheck_RollDropLevels(int fishingLevel, out bool common, out bool uncommon, out bool rare, out bool veryrare, out bool legendary, out bool crate)
    {
        int num = 150 / fishingLevel;
        int num2 = 150 * 2 / fishingLevel;
        int num3 = 150 * 7 / fishingLevel;
        int num4 = 150 * 15 / fishingLevel;
        int num5 = 150 * 30 / fishingLevel;
        int num6 = 10;

        if (num < 2) num = 2;
        if (num2 < 3) num2 = 3;
        if (num3 < 4) num3 = 4;
        if (num4 < 5) num4 = 5;
        if (num5 < 6) num5 = 6;

        common = Main.rand.Next(num) == 0;
        uncommon = Main.rand.Next(num2) == 0;
        rare = Main.rand.Next(num3) == 0;
        veryrare = Main.rand.Next(num4) == 0;
        legendary = Main.rand.Next(num5) == 0;
        crate = Main.rand.Next(100) < num6;
    }
    #endregion

    #region 获取水体数量
    public static int GetWaterTiles(Point pos, ref int lava, ref int honey)
    {
        // 缓存当前水体数量 避免重复计算
        int water = 0;
        int radius = Config.Range; // 直接使用图格半径
        int minX = Math.Max(pos.X - radius, 0);
        int maxX = Math.Min(pos.X + radius, Main.maxTilesX - 1);
        int minY = Math.Max(pos.Y - radius, 0);
        int maxY = Math.Min(pos.Y + radius, Main.maxTilesY - 1);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var t = Main.tile[x, y];
                if (t?.liquid > 0)
                {
                    if (t.liquidType() == LiquidID.Water) water++;
                    else if (t.liquidType() == LiquidID.Lava) lava++;
                    else if (t.liquidType() == LiquidID.Honey) honey++;
                }
            }

        return water + lava + honey;
    }
    #endregion

    #region 从附近的箱子获取鱼饵方法
    private static bool FindBait(MachData data, out Item baitItem, out Chest chest, out int slot)
    {
        baitItem = new();
        chest = new();
        slot = -1;

        int range = Config.Range;
        int minX = Math.Max(data.Pos.X - range, 0), maxX = Math.Min(data.Pos.X + range, Main.maxTilesX - 1);
        int minY = Math.Max(data.Pos.Y - range, 0), maxY = Math.Min(data.Pos.Y + range, Main.maxTilesY - 1);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                int ci = Chest.FindChest(x, y);
                if (ci == -1) continue;
                var c = Main.chest[ci];
                for (int s = 0; s < c.item.Length; s++)
                {
                    var item = c.item[s];
                    if (item != null && !item.IsAir && item.bait > 0)
                    {
                        baitItem = item;
                        chest = c;
                        slot = s;
                        return true;
                    }
                }
            }
        }
        return false;
    }
    #endregion

    #region 获取额外鱼力
    public static int GetBonus(List<int> acc)
    {
        if (!acc.Any()) return 0;

        int total = 0;
        foreach (var type in acc)
        {
            if (Config.CustomPowerItems.TryGetValue(type, out int power))
                total += power;
        }

        return total;
    }
    #endregion

    #region 物品进箱动画
    private static void TryDeposit(MachData data, Item i)
    {
        int range = Config.Range;
        int minX = Math.Max(data.Pos.X - range, 0), maxX = Math.Min(data.Pos.X + range, Main.maxTilesX - 1);
        int minY = Math.Max(data.Pos.Y - range, 0), maxY = Math.Min(data.Pos.Y + range, Main.maxTilesY - 1);
        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int ci = Chest.FindChest(x, y);
                if (ci == -1) continue;
                var chest = Main.chest[ci];
                for (int s = 0; s < chest.item.Length; s++)
                {
                    if (chest.item[s].IsAir)
                    {
                        chest.item[s] = i.Clone();
                        Transfer(data, i, x, y, ci, s);
                        return;
                    }
                    else if (chest.item[s].type == i.type && chest.item[s].stack < chest.item[s].maxStack)
                    {
                        chest.item[s].stack++;
                        Transfer(data, i, x, y, ci, s);
                        return;
                    }
                }
            }
    }

    // 转移动画
    private static void Transfer(MachData data, Item i, int x, int y, int ci, int s)
    {
        NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, ci, s);
        Vector2 from = new(data.Pos.X * 16 + 8, data.Pos.Y * 16 + 8);
        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        Chest.VisualizeChestTransfer(from, to, i.type, Chest.ItemTransferVisualizationSettings.Hopper);
    }
    #endregion

    #region 消耗鱼饵
    private static bool ConsumeBait(Chest chest, int slot, Item baitItem, int Power)
    {
        // 安全检查
        if (chest == null || baitItem == null || baitItem.IsAir)
            return false;

        // 验证箱子坐标是否有效
        if (chest.x < 0 || chest.x >= Main.maxTilesX || chest.y < 0 || chest.y >= Main.maxTilesY)
            return false;

        // 原版消耗概率：1 / (1 + Power/6)
        float chance = 1f / (1f + Power / 6f);
        if (Main.rand.NextFloat() < chance)
        {
            baitItem.stack--;
            if (baitItem.stack <= 0)
            {
                baitItem.TurnToAir();
            }
            // 同步箱子更新到客户端
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
            return true;
        }
        return true; // 未消耗也继续
    }
    #endregion
}