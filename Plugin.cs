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
    public override Version Version => new(1, 0, 1);
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
    private static FishDropRuleList ruleList = new();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        GeneralHooks.ReloadEvent += ReloadConfig;
        On.Terraria.WorldGen.KillTile += OnKillTile;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
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

            Config = Configuration.Read(); // 读取配置（内部会创建默认配置）
            Config.ParseFrames(); // 解析钓鱼间隔字符串（如 "60" 或 "60,180"）
            Config.Write(); // 写回（确保配置存在）

            ruleList = new FishDropRuleList();
            var populator = new GameContentFishDropPopulator(ruleList);
            populator.Populate();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{PluginName}] 加载配置文件失败: {ex.Message}");
        }
    }
    #endregion

    #region 加载与保存世界事件
    private void OnWorldSave(WorldSaveEventArgs args) => Save();
    private void GamePost(EventArgs args) // 加载完世界后事件
    {
        // 加载配置文件
        LoadConfig();
        // 加载钓鱼机缓存
        Load();
    }
    #endregion

    #region 游戏更新事件
    private static long nextFrame = 0; // 下一次执行的目标帧数
    private static long frameCounter = 0; // 帧计数器（每次游戏更新+1）
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        frameCounter++;
        if (frameCounter < nextFrame) return; // 未到执行时间则跳过

        // 执行所有钓鱼机的钓鱼逻辑
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

        Remove(new Point(x, y)); // 尝试移除该坐标的钓鱼机
        orig(x, y, fail, effectOnly, noItem);
    }
    #endregion

    #region 钓鱼核心（单次钓鱼流程）
    private static void FishOnce(MachData data)
    {
        try
        {
            // 1. 查找鱼竿
            if (!FindRod(data, out Item rodItem, out Chest rodChest, out int rodSlot))
            {
                if ((DateTime.Now - data.lastMissingWarning).TotalSeconds > Config.Warning)
                {
                    data.lastMissingWarning = DateTime.Now;
                    TShock.Utils.Broadcast($"\n{data.Owner}的钓鱼机缺少鱼竿，请放入鱼竿", color2);
                    TShock.Utils.Broadcast($"传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}", color2);
                }
                return;
            }

            // 2. 查找鱼饵
            if (!FindBait(data, out Item baitItem, out Chest baitChest, out int baitSlot))
            {
                if ((DateTime.Now - data.lastMissingWarning).TotalSeconds > Config.Warning)
                {
                    data.lastMissingWarning = DateTime.Now;
                    TShock.Utils.Broadcast($"\n{data.Owner}的钓鱼机缺少鱼饵，请放入鱼饵", color2);
                    TShock.Utils.Broadcast($"传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}", color2);
                }
                return;
            }

            // 验证箱子有效性（防止查找后箱子被破坏）
            if (rodChest == null || baitChest == null ||
                rodChest.x < 0 || rodChest.x >= Main.maxTilesX ||
                rodChest.y < 0 || rodChest.y >= Main.maxTilesY ||
                baitChest.x < 0 || baitChest.x >= Main.maxTilesX ||
                baitChest.y < 0 || baitChest.y >= Main.maxTilesY)
                return;

            // 计算渔力 = 鱼竿渔力 + 鱼饵渔力 + 额外加成 + 饰品加成
            int Power = rodItem.fishingPole + baitItem.bait;
            if (Config.Power > 0) Power += Config.Power;
            Power += GetBonus(data);

            // 消耗鱼饵（概率）
            if (ConsumeBait(baitChest, data, baitSlot, baitItem, Power))
            {
                // 构建钓鱼上下文（包含环境、高度、稀有度等）
                var context = BuildFishingContext(data, Power, rodItem, baitItem);
                // 根据上下文从规则列表中获取掉落的物品类型
                int itemType = ruleList.TryGetItemDropType(context);
                if (itemType != 0)
                {
                    // 初始化掉落物物品
                    var fish = new Item();
                    fish.SetDefaults(itemType);
                    fish.stack = 1;

                    if (data.Exclude.Contains(fish.type)) return;

                    // 直接尝试放入附近箱子
                    TryDeposit(data, fish);

                    if (Config.Broadcast)
                        TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 钓到了 {ItemIcon(fish.type, fish.stack)} 坐标:{data.Pos.X} {data.Pos.Y}", color2);
                }

                // 添加自定义渔获判定
                if (Config.CustomFishes.Any())
                {
                    CustomFishes(data);
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"{ex}");
        }
    }
    #endregion

    #region 从箱子获取鱼饵与鱼竿方法
    private static bool FindBait(MachData data, out Item baitItem, out Chest chest, out int slot) =>
        FindItem(data, item => item.bait > 0, out baitItem, out chest, out slot);
    public static bool FindRod(MachData data, out Item rodItem, out Chest chest, out int slot) =>
        FindItem(data, item => item.fishingPole > 0, out rodItem, out chest, out slot);
    public static bool HasItem(MachData data, Func<Item, bool> predicate) =>
        FindItem(data, predicate, out _, out _, out _);

    // 在钓鱼机附近查找符合条件的物品（鱼竿或鱼饵、加成物品）
    private static bool FindItem(MachData data, Func<Item, bool> predicate, out Item foundItem, out Chest chest, out int slot)
    {
        foundItem = new();
        chest = new();
        slot = -1;

        // 1. 优先使用缓存的箱子索引
        if (data.ChestIndex != -1)
        {
            var cachedChest = Main.chest[data.ChestIndex];
            if (cachedChest != null && cachedChest.x == data.Pos.X && cachedChest.y == data.Pos.Y)
            {
                for (int s = 0; s < cachedChest.item.Length; s++)
                {
                    var item = cachedChest.item[s];
                    if (item != null && !item.IsAir && predicate(item))
                    {
                        foundItem = item;
                        chest = cachedChest;
                        slot = s;
                        return true;
                    }
                }
            }
        }

        // 2. 回退到渐进半径搜索
        int radius = 2;
        int maxRadius = Config.Range;
        while (radius <= maxRadius)
        {
            if (TryFindItem(data.Pos, radius, predicate, out foundItem, out chest, out slot))
                return true;
            radius += 2;
        }

        return false;
    }

    // 在指定半径内查找符合条件的物品
    private static bool TryFindItem(Point center, int radius, Func<Item, bool> predicate, out Item foundItem, out Chest chest, out int slot)
    {
        foundItem = new();
        chest = new();
        slot = -1;

        int minX = Math.Max(center.X - radius, 0);
        int maxX = Math.Min(center.X + radius, Main.maxTilesX - 1);
        int minY = Math.Max(center.Y - radius, 0);
        int maxY = Math.Min(center.Y + radius, Main.maxTilesY - 1);

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
                    if (item != null && !item.IsAir && predicate(item))
                    {
                        foundItem = item;
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

    #region 根据渔力概率消耗鱼饵（原版公式）
    private static bool ConsumeBait(Chest chest, MachData data, int slot, Item baitItem, int Power)
    {
        // 安全检查
        if (chest == null || baitItem == null || baitItem.IsAir)
            return false;

        // 验证箱子坐标是否有效
        if (chest.x < 0 || chest.x >= Main.maxTilesX || chest.y < 0 || chest.y >= Main.maxTilesY)
            return false;

        // 原版消耗概率：1 / (1 + Power/6)
        float chance = 1f / (1f + Power / 6f);

        // 检查是否有减少消耗的饰品
        if (HasItem(data, item => item.type == ItemID.TackleBox || item.type == ItemID.AnglerTackleBag))
            chance *= 0.8f; // 消耗概率降低20%

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
        return true; // 未消耗也继续（原版即使不消耗也能钓鱼）
    }
    #endregion

    #region 创建钓鱼对象信息
    public static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    private static FishingContext BuildFishingContext(MachData data, int fishingPower, Item rodItem, Item baitItem)
    {
        // 创建临时玩家并设置位置，用于原版规则中的 Zone 判断(解决环境匣掉落问题)
        var plr = new Player();
        plr.position = new Vector2(data.Pos.X * 16, data.Pos.Y * 16);
        plr.UpdateBiomes();
        plr.ZoneCorrupt = data.ZoneCorrupt;
        plr.ZoneCrimson = data.ZoneCrimson;
        plr.ZoneJungle = data.ZoneJungle;
        plr.ZoneSnow = data.ZoneSnow;
        plr.ZoneHallow = data.ZoneHallow;
        plr.ZoneDesert = data.ZoneDesert;
        plr.ZoneBeach = data.ZoneBeach;
        plr.ZoneRain = true; // 下雨加成 默认打开

        // 高度等级
        plr.ZoneSkyHeight = data.HeightLevel == 0;
        plr.ZoneOverworldHeight = data.HeightLevel == 1;
        plr.ZoneDirtLayerHeight = data.HeightLevel == 2;
        plr.ZoneRockLayerHeight = data.HeightLevel == 3;
        plr.ZoneUnderworldHeight = data.HeightLevel == 4;
        int heightLevel = GetHeightLevel(plr);
        if (Main.remixWorld && heightLevel == 2 && Main.rand.Next(2) == 0)
            heightLevel = 1;

        // 环境冲突处理（每次钓鱼时随机决定）
        bool corruption = plr.ZoneCorrupt;
        bool crimson = plr.ZoneCrimson;
        bool jungle = plr.ZoneJungle;
        bool snow = plr.ZoneSnow;
        bool hallow = plr.ZoneHallow;
        bool desert = plr.ZoneDesert;
        bool Beach = plr.ZoneBeach;
        bool rolledRemixOcean = data.RolledRemixOcean;

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
        int waterTiles = GetWaterTiles(data.Pos, ref lavaTiles, ref honeyTiles);
        // 特殊世界蜂蜜修正
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0)
            honeyTiles = 0;

        // 大气因子
        int yPos = data.Pos.Y;
        float atmo = CalculateAtmo(yPos);

        int waterNeeded = (int)(300f * atmo);
        float waterQuality = Math.Min(1f, (float)waterTiles / waterNeeded);
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

        // 稀有度标志
        bool junk = Main.rand.Next(50) > fishingPower && Main.rand.Next(50) > fishingPower && waterTiles < waterNeeded;
        bool hasCratePotion = HasItem(data, item => item.type == ItemID.CratePotion);
        bool common, uncommon, rare, veryrare, legendary, crate;
        FishingCheck_RollDropLevels(fishingPower, hasCratePotion, out common, out uncommon, out rare, out veryrare, out legendary, out crate);

        // 任务鱼
        int questFish = -1;
        if (NPC.AnyNPCs(NPCID.Angler) && !Main.anglerQuestFinished)
            questFish = Main.anglerQuestItemNetIDs[Main.anglerQuest];

        // 熔岩钓鱼完整判定（鱼竿、鱼饵、饰品）
        bool canFishInLava = ItemID.Sets.CanFishInLava[rodItem.type] ||
                             ItemID.Sets.IsLavaBait[baitItem.type] ||
                             HasItem(data, item => lavaItems.Contains(item.type));

        // 构建上下文
        var fc = new FishingContext
        {
            Random = new Terraria.Utilities.UnifiedRandom(Main.rand.Next()),
            Fisher = new FishingAttempt(),
            Player = plr,
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
        fc.Fisher.junk = junk;
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
    private static void FishingCheck_RollDropLevels(int fishingLevel, bool hasCratePotion, out bool common, out bool uncommon, out bool rare, out bool veryrare, out bool legendary, out bool crate)
    {

        // 计算各稀有度的概率分母（值越小，概率越高）
        int commonRate = 150 / fishingLevel;
        int uncommonRate = 150 * 2 / fishingLevel;
        int rareRate = 150 * 7 / fishingLevel;
        int veryrareRate = 150 * 15 / fishingLevel;
        int legendaryRate = 150 * 30 / fishingLevel;

        // 宝箱概率基数（%），默认10，配置加成，宝箱药水再加15
        int crateRate = 10 + Config.CrateChanceBonus;
        if (hasCratePotion)
            crateRate += 15;

        // 确保最小概率，避免分母过小导致概率过高
        if (commonRate < 2) commonRate = 2;
        if (uncommonRate < 3) uncommonRate = 3;
        if (rareRate < 4) rareRate = 4;
        if (veryrareRate < 5) veryrareRate = 5;
        if (legendaryRate < 6) legendaryRate = 6;

        // 随机判断各稀有度
        common = Main.rand.Next(commonRate) == 0;
        uncommon = Main.rand.Next(uncommonRate) == 0;
        rare = Main.rand.Next(rareRate) == 0;
        veryrare = Main.rand.Next(veryrareRate) == 0;
        legendary = Main.rand.Next(legendaryRate) == 0;
        crate = Main.rand.Next(100) < crateRate;
    }
    #endregion

    #region 计算大气因子
    public static float CalculateAtmo(int yPos)
    {
        // 原版精确公式
        float num = (float)Main.maxTilesX / 4200f;
        num *= num;
        float atmo = (float)((yPos - (60f + 10f * num)) / (Main.worldSurface / 6.0));
        if (atmo < 0.25f) atmo = 0.25f;
        if (atmo > 1f) atmo = 1f;
        return atmo;
    } 
    #endregion

    #region 获取水体数量
    // 统计指定坐标周围一定半径内的液体数量（水、岩浆、蜂蜜）
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

        return water + lava + honey; // 返回总液体数
    }
    #endregion

    #region 获取高度等级
    public static int GetHeightLevel(Player plr)
    {
        if (plr.ZoneSkyHeight)
            return 0;
        if (plr.ZoneOverworldHeight)
            return 1;
        if (plr.ZoneDirtLayerHeight)
            return 2;
        if (plr.ZoneRockLayerHeight)
            return 3;
        if (plr.ZoneUnderworldHeight)
            return 4;

        return 0; // 默认太空
    }

    // 将高度等级转换为可读字符串
    public static string GetHeightLevelString(int level) => level switch
    {
        0 => "太空(0)",
        1 => "地表(1)",
        2 => "地下(2)",
        3 => "洞穴(3)",
        4 => "地狱(4)",
        _ => "未知"
    };
    #endregion

    #region 获取额外鱼力
    public static int GetBonus(MachData data)
    {
        int total = 0;
        if (data.ChestIndex != -1)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null && chest.x == data.Pos.X && chest.y == data.Pos.Y)
            {
                foreach (var item in chest.item)
                {
                    if (item != null && !item.IsAir && Config.CustomPowerItems.TryGetValue(item.type, out int power))
                    {
                        total += power;
                    }
                }
            }
        }

        return total;
    }
    #endregion

    #region 物品进箱动画
    private static void TryDeposit(MachData data, Item i)
    {
        Vector2 from = FindFishingSpot(data.Pos);
        Point pos = data.Pos;

        // 1. 先尝试放入钓鱼箱
        int idx = data.ChestIndex;
        if (idx != -1 && TryPutInChest(Main.chest[idx], pos.X, pos.Y, idx, from, i))
            return;

        // 2. 渐进式搜索附近箱子（半径从2开始，每次增加2，直到Config.Range）
        int radius = 2;
        int step = 2;
        int maxRadius = Config.Range;

        while (radius <= maxRadius)
        {
            if (TryFindAndPutInNearbyChest(pos, radius, from, i))
                return;
            radius += step;
        }
    }

    /// <summary>
    /// 尝试将物品放入指定箱子，成功返回true
    /// </summary>
    private static bool TryPutInChest(Chest chest, int chestX, int chestY, int chestIdx, Vector2 from, Item item)
    {
        for (int s = 0; s < chest.item.Length; s++)
        {
            if (chest.item[s].IsAir)
            {
                chest.item[s] = item.Clone();
                Transfer(from, chestX, chestY, chestIdx, s, item.type);
                return true;
            }
            else if (chest.item[s].type == item.type && chest.item[s].stack < chest.item[s].maxStack)
            {
                chest.item[s].stack++;
                Transfer(from, chestX, chestY, chestIdx, s, item.type);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 在指定半径内搜索可用的箱子，并尝试放入物品
    /// </summary>
    private static bool TryFindAndPutInNearbyChest(Point center, int radius, Vector2 from, Item item)
    {
        int minX = Math.Max(center.X - radius, 0);
        int maxX = Math.Min(center.X + radius, Main.maxTilesX - 1);
        int minY = Math.Max(center.Y - radius, 0);
        int maxY = Math.Min(center.Y + radius, Main.maxTilesY - 1);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                // 跳过钓鱼箱本身（已在第一步处理）
                if (x == center.X && y == center.Y) continue;

                int ci = Chest.FindChest(x, y);
                if (ci == -1) continue;

                var chest = Main.chest[ci];
                if (TryPutInChest(chest, chest.x, chest.y, ci, from, item))
                    return true;
            }
        }
        return false;
    }

    // 播放物品转移动画并同步箱子内容
    private static void Transfer(Vector2 from, int x, int y, int ci, int slot, int itemType)
    {
        NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, ci, slot);
        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        Chest.VisualizeChestTransfer(from, to, itemType, Chest.ItemTransferVisualizationSettings.Hopper);
    }

    // 寻找钓鱼点（返回世界坐标）- 渐进式搜索
    private static Vector2 FindFishingSpot(Point pos)
    {
        // 从 2 格开始，每次增加 2 格，直到 Config.Range
        int radius = 2;
        int step = 2;
        int maxRadius = Config.Range;

        while (radius <= maxRadius)
        {
            Point best = FindWaterInRadius(pos, radius);
            if (best != Point.Zero)
            {
                // 返回水体图格的中心坐标（水下）
                return new Vector2(best.X * 16 + 8, best.Y * 16 + 8);
            }
            radius += step;
        }

        // 没找到水，返回机器坐标
        return new Vector2(pos.X * 16 + 8, pos.Y * 16 + 8);
    }

    /// <summary>
    /// 在指定半径内查找最近的水体图格（返回第一个找到的）
    /// </summary>
    private static Point FindWaterInRadius(Point center, int radius)
    {
        int minX = Math.Max(center.X - radius, 0);
        int maxX = Math.Min(center.X + radius, Main.maxTilesX - 1);
        int minY = Math.Max(center.Y - radius, 0);
        int maxY = Math.Min(center.Y + radius, Main.maxTilesY - 1);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var t = Main.tile[x, y];
                if (t?.liquid > 0)
                    return new Point(x, y);
            }
        }
        return Point.Zero;
    }
    #endregion

    #region 自定义额外渔获
    private static void CustomFishes(MachData data)
    {
        // 创建临时玩家，用于条件判断中的环境检查（如生物群落）
        var plr = new Player();
        plr.position = new Vector2(data.Pos.X * 16, data.Pos.Y * 16);
        plr.UpdateBiomes();
        plr.ZoneCorrupt = data.ZoneCorrupt;
        plr.ZoneCrimson = data.ZoneCrimson;
        plr.ZoneJungle = data.ZoneJungle;
        plr.ZoneSnow = data.ZoneSnow;
        plr.ZoneHallow = data.ZoneHallow;
        plr.ZoneDesert = data.ZoneDesert;
        plr.ZoneBeach = data.ZoneBeach;
        plr.ZoneRain = true; // 下雨加成 默认打开

        // 高度等级
        plr.ZoneSkyHeight = data.HeightLevel == 0;
        plr.ZoneOverworldHeight = data.HeightLevel == 1;
        plr.ZoneDirtLayerHeight = data.HeightLevel == 2;
        plr.ZoneRockLayerHeight = data.HeightLevel == 3;
        plr.ZoneUnderworldHeight = data.HeightLevel == 4;

        foreach (var rule in Config.CustomFishes)
        {
            // 检查条件（如果配置了条件）
            if (rule.Cond.Any())
            {
                // 使用 Utils.CheckConds 判断条件是否满足，传入 plr
                if (!CheckConds(rule.Cond, plr))
                    continue;
            }

            // 概率判定
            if (Main.rand.Next(rule.ChanceDenominator) == 0)
            {
                // 检查排除列表
                if (data.Exclude.Contains(rule.ItemType))
                    continue;

                // 创建自定义物品
                var custom = new Item();
                custom.SetDefaults(rule.ItemType);
                custom.stack = 1;

                // 尝试放入附近箱子
                TryDeposit(data, custom);

                if (Config.Broadcast)
                {
                    TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 额外钓到了 {ItemIcon(custom.type, custom.stack)} 坐标:{data.Pos.X} {data.Pos.Y}", color2);
                }
            }
        }
    } 
    #endregion
}