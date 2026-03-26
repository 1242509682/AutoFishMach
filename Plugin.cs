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
    public override Version Version => new(1, 0, 4);
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
    // 复用玩家对象（避免频繁创建）
    private static Player TempPlayer = new();
    // 玩家与机器映射
    private static Dictionary<string, List<MachData>> PlayerMachines = new();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
        GeneralHooks.ReloadEvent += ReloadConfig;
        On.Terraria.WorldGen.KillTile += OnKillTile;
        ServerApi.Hooks.GamePostInitialize.Register(this, GamePost);
        ServerApi.Hooks.WorldSave.Register(this, OnWorldSave);
        ServerApi.Hooks.GameUpdate.Register(this, OnGameUpdate);
        ServerApi.Hooks.ServerLeave.Register(this, OnPlayerLeave);
        GetDataHandlers.ChestItemChange += OnChestItemChange!;
        GetDataHandlers.ChestOpen += OnChestOpen!;
        GetDataHandlers.PlayerUpdate += OnPlayerUpdate!;
        On.Terraria.Wiring.Hopper += OnHopper; // 新增
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
            ServerApi.Hooks.ServerLeave.Deregister(this, OnPlayerLeave);
            GetDataHandlers.ChestItemChange -= OnChestItemChange!;
            GetDataHandlers.ChestOpen -= OnChestOpen!;
            GetDataHandlers.PlayerUpdate -= OnPlayerUpdate!;
            On.Terraria.Wiring.Hopper -= OnHopper; // 新增
            Commands.ChatCommands.RemoveAll(x => x.CommandDelegate == MyCommand.CmdAfm);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new(); // 配置文件实例
    private static void ReloadConfig(ReloadEventArgs args)
    {
        Save();  // 先保存机器数据（如果有脏数据）
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
        // 初始化保存定时器
        saveFrame = frameCounter + Config.SaveInterval;
    }
    #endregion

    #region 游戏更新事件
    private static long nextFrame = 0; // 下一次执行的目标帧数
    private static long frameCounter = 0; // 帧计数器（每次游戏更新+1）
    private static long saveFrame = 0; // 保存缓存的帧数
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        frameCounter++;

        // 定期保存（脏数据）
        if (frameCounter >= saveFrame)
        {
            if (IsDirty) Save();
            saveFrame = frameCounter + Config.SaveInterval;
        }

        if (!Config.NeedWiring && frameCounter >= nextFrame)
        {
            // 执行所有钓鱼机的钓鱼逻辑
            var all = Machines;
            foreach (var m in all)
                FishOnce(m);

            // 计算下一次执行的帧数间隔（支持随机范围）
            int min = Config.MinFrames;
            int max = Config.MaxFrames;
            int delay = min == max ? min : Main.rand.Next(min, max + 1);
            nextFrame = frameCounter + delay;
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
        var data = FindTile(pos);
        if (data == null) return;

        FishOnce(data);
    }
    #endregion

    #region 挖掉箱子自动移除钓鱼机
    private static void OnKillTile(On.Terraria.WorldGen.orig_KillTile orig, int x, int y, bool fail, bool effectOnly, bool noItem)
    {
        if (Config.Enabled)
        {
            var pos = new Point(x, y);
            if (FindTile(pos) != null)
                Remove(pos);
        }

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
                if (Config.Broadcast &&
                    (DateTime.UtcNow - data.lastRodWarning).TotalSeconds > Config.BC_CoolDown)
                {
                    data.lastRodWarning = DateTime.UtcNow;
                    var text = $"\n{data.Owner}的钓鱼机缺少鱼竿，请放入鱼竿" +
                               $"\n传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}";
                    TSPlayer.All.SendMessage(TextGradient(text), color);
                }
                return;
            }

            // 2. 查找鱼饵
            if (!FindBait(data, out Item baitItem, out Chest baitChest, out int baitSlot))
            {
                if (Config.Broadcast &&
                    (DateTime.UtcNow - data.lastBaitWarning).TotalSeconds > Config.BC_CoolDown)
                {
                    data.lastBaitWarning = DateTime.UtcNow;
                    var text = $"\n{data.Owner}的钓鱼机缺少鱼饵，请放入鱼饵" +
                               $"\n传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}";
                    TSPlayer.All.SendMessage(TextGradient(text), color);
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
            if (Config.PowerChanceBonus > 0) Power += Config.PowerChanceBonus;
            Power += data.BonusTotal; // 直接使用缓存

            // 消耗鱼饵（概率）
            if (ConsumeBait(baitChest, data, baitSlot, baitItem, Power))
            {
                var allow = false;

                // 添加自定义渔获判定
                if (Config.CustomFishes.Any())
                    CustomFishes(data, rodItem, ref allow);

                // 如果自定义渔获成功则跳过原版渔获
                if (allow) return;

                // 构建钓鱼上下文（包含环境、高度、稀有度等）
                var context = BuildFishingContext(data, Power, rodItem, baitItem);
                // 根据上下文从规则列表中获取掉落的物品类型
                int itemType = ruleList.TryGetItemDropType(context);
                if (itemType == 0) return;

                // 初始化掉落物物品
                var fish = new Item();
                fish.SetDefaults(itemType);
                fish.stack = 1;

                if (data.Exclude.Contains(fish.type)) return;

                // 尝试放入钓鱼箱
                Vector2 from = new Vector2(data.WaterPos.X * 16 + 8, data.WaterPos.Y * 16 + 8);
                Point pos = data.Pos;
                int idx = data.ChestIndex;
                if (idx != -1)
                {
                    var chest = Main.chest[idx];
                    for (int s = 0; s < chest.item.Length; s++)
                    {
                        if (chest.item[s].IsAir)
                        {
                            chest.item[s] = fish.Clone();
                            Transfer(from, pos.X, pos.Y, idx, s, fish.type);
                            break; // 放入后立即退出
                        }
                        else if (chest.item[s].type == fish.type && chest.item[s].stack < chest.item[s].maxStack)
                        {
                            chest.item[s].stack++;
                            Transfer(from, pos.X, pos.Y, idx, s, fish.type);
                            break; // 放入后立即退出
                        }
                    }
                }

                // 刷新缓存（30秒）
                if ((DateTime.UtcNow - data.CacheTime).TotalSeconds > Config.GetInterval)
                {
                    int lava = 0, honey = 0;
                    int water = GetWaterTiles(data.Pos, ref lava, ref honey);
                    data.WatCnt = water;
                    data.LavCnt = lava;
                    data.HonCnt = honey;
                    data.WaterPos = FindWaterInRadius(data.Pos, Config.Range);
                    data.BonusTotal = RefreshCaches(data);
                    data.CacheTime = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"{ex}");
        }
    }
    #endregion

    #region 从箱子获取鱼饵与鱼竿方法（带缓存）
    private static bool FindBait(MachData data, out Item baitItem, out Chest chest, out int slot)
    {
        // 优先使用缓存
        if (data.BaitChest != -1 && data.BaitSlot != -1)
        {
            var cachedChest = Main.chest[data.BaitChest];
            if (cachedChest != null)
            {
                var item = cachedChest.item[data.BaitSlot];
                if (item != null && !item.IsAir && item.bait > 0)
                {
                    baitItem = item;
                    chest = cachedChest;
                    slot = data.BaitSlot;
                    return true;
                }
            }
            // 缓存无效，清除
            data.BaitChest = -1;
            data.BaitSlot = -1;
        }

        // 回退到扫描
        if (FindItem(data, item => item.bait > 0, out baitItem, out chest, out slot))
        {
            // 更新缓存
            data.BaitChest = chest.index;
            data.BaitSlot = slot;
            return true;
        }

        return false;
    }
    public static bool FindRod(MachData data, out Item rodItem, out Chest chest, out int slot)
    {
        // 优先使用缓存
        if (data.RodChest != -1 && data.RodSlot != -1)
        {
            var cachedChest = Main.chest[data.RodChest];
            if (cachedChest != null)
            {
                var item = cachedChest.item[data.RodSlot];
                if (item != null && !item.IsAir && item.fishingPole > 0)
                {
                    rodItem = item;
                    chest = cachedChest;
                    slot = data.RodSlot;
                    return true;
                }
            }
            // 缓存无效，清除
            data.RodChest = -1;
            data.RodSlot = -1;
        }

        // 回退到扫描
        if (FindItem(data, item => item.fishingPole > 0, out rodItem, out chest, out slot))
        {
            // 更新缓存
            data.RodChest = chest.index;
            data.RodSlot = slot;
            return true;
        }
        return false;
    }
    private static bool FindItem(MachData data, Func<Item, bool> pred, out Item found, out Chest chest, out int slot)
    {
        found = new();
        chest = new();
        slot = -1;

        // 优先使用缓存的箱子索引
        if (data.ChestIndex != -1)
        {
            var myChest = Main.chest[data.ChestIndex];
            if (myChest != null && myChest.x == data.Pos.X && myChest.y == data.Pos.Y)
            {
                for (int s = 0; s < myChest.item.Length; s++)
                {
                    var item = myChest.item[s];
                    if (item != null && !item.IsAir && pred(item))
                    {
                        found = item;
                        chest = myChest;
                        slot = s;
                        return true;
                    }
                }
            }
        }

        // 一次扫描整个范围（不再渐进）
        return TryFindItem(data.Pos, Config.Range, pred, out found, out chest, out slot);
    }

    // 在指定半径内查找符合条件的物品
    private static bool TryFindItem(Point center, int radius, Func<Item, bool> pred, out Item found, out Chest chest, out int slot)
    {
        found = new();
        chest = new();
        slot = -1;

        int minX, maxX, minY, maxY;
        GetCenter(center, radius, out minX, out maxX, out minY, out maxY);

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
                    if (item != null && !item.IsAir && pred(item))
                    {
                        found = item;
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

        // 原版消耗概率：1 / (1 + PowerChanceBonus/6)
        float chance = 1f / (1f + Power / 6f);

        // 检查是否有减少消耗的饰品
        if (data.HasTackle)
            chance *= 0.8f; // 消耗概率降低20%

        if (Main.rand.NextFloat() < chance)
        {
            baitItem.stack--;
            if (baitItem.stack <= 0)
            {
                baitItem.TurnToAir();
                data.BaitChest = -1;
                data.BaitSlot = -1;
            }
            // 同步箱子更新到客户端
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
            return true;
        }
        return true; // 未消耗也继续（原版即使不消耗也能钓鱼）
    }
    #endregion

    #region 设置假玩家环境（复用）
    private static void SetupPlayer(Player plr, MachData data)
    {
        plr.position = new Vector2(data.Pos.X * 16, data.Pos.Y * 16);
        plr.UpdateBiomes();
        plr.ZoneCorrupt = data.ZoneCorrupt;
        plr.ZoneCrimson = data.ZoneCrimson;
        plr.ZoneJungle = data.ZoneJungle;
        plr.ZoneSnow = data.ZoneSnow;
        plr.ZoneHallow = data.ZoneHallow;
        plr.ZoneDesert = data.ZoneDesert;
        plr.ZoneBeach = data.ZoneBeach;
        plr.ZoneRain = true;

        // 高度等级
        plr.ZoneSkyHeight = data.HeightLevel == 0;
        plr.ZoneOverworldHeight = data.HeightLevel == 1;
        plr.ZoneDirtLayerHeight = data.HeightLevel == 2;
        plr.ZoneRockLayerHeight = data.HeightLevel == 3;
        plr.ZoneUnderworldHeight = data.HeightLevel == 4;
    }
    #endregion

    #region 创建钓鱼对象信息
    public static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    private static FishingContext BuildFishingContext(MachData data, int fishingPower, Item rodItem, Item baitItem)
    {
        // 临时玩家并设置位置，用于原版规则中的 Zone 判断(解决环境匣掉落问题)
        SetupPlayer(TempPlayer, data);
        int heightLevel = data.HeightLevel;
        if (Main.remixWorld && heightLevel == 2 && Main.rand.Next(2) == 0)
            heightLevel = 1;

        // 获取缓存环境
        bool corruption = TempPlayer.ZoneCorrupt;
        bool crimson = TempPlayer.ZoneCrimson;
        bool jungle = TempPlayer.ZoneJungle;
        bool snow = TempPlayer.ZoneSnow;
        bool hallow = TempPlayer.ZoneHallow;
        bool desert = TempPlayer.ZoneDesert;
        bool Beach = TempPlayer.ZoneBeach;
        bool rolledRemixOcean = data.RolledRemixOcean;

        // 环境冲突处理（每次钓鱼时随机决定）
        if (corruption && crimson)
        {
            if (Main.rand.Next(2) == 0)
                crimson = false;
            else
                corruption = false;
        }

        if (jungle && snow && Main.rand.Next(2) == 0) jungle = false;

        // 感染沙漠：沙漠 + 腐化/猩红/神圣 之一
        bool infectedDesert = desert && (corruption || crimson || hallow);

        // 水体统计
        int waterTiles = data.WatCnt, lavaTiles = data.LavCnt, honeyTiles = data.HonCnt;
        // 特殊世界蜂蜜修正
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0) honeyTiles = 0;

        // 大气因子
        float atmo = data.atmo;

        // 水体需求
        int waterNeeded = (int)(300f * atmo);

        // 水体质量
        float waterQuality = Math.Min(1f, (float)waterTiles / waterNeeded);

        // 根据水体质量降低鱼力
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

        // 稀有度标志
        bool junk = Main.rand.Next(50) > fishingPower && Main.rand.Next(50) > fishingPower && waterTiles < waterNeeded;
        bool hasCratePotion = data.HasCratePotion;
        bool common, uncommon, rare, veryrare, legendary, crate;
        FishingCheck_RollDropLevels(fishingPower, hasCratePotion, out common, out uncommon, out rare, out veryrare, out legendary, out crate);

        // 任务鱼
        int questFish = -1;
        if (Config.QuestFish && NPC.AnyNPCs(NPCID.Angler) && !Main.anglerQuestFinished)
            questFish = Main.anglerQuestItemNetIDs[Main.anglerQuest];

        // 熔岩钓鱼完整判定（鱼竿、鱼饵、饰品）
        bool canFishInLava = data.CanFishInLava ||
                             ItemID.Sets.CanFishInLava[rodItem.type] ||
                             ItemID.Sets.IsLavaBait[baitItem.type];

        // 构建上下文
        var fc = new FishingContext
        {
            Random = new Terraria.Utilities.UnifiedRandom(Main.rand.Next()),
            Fisher = new FishingAttempt(),
            Player = TempPlayer,
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
    #endregion

    #region 原版稀有度计算（参考 FishingCheck_RollDropLevels）
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

    #region 刷新鱼力加成、宝匣药水、岩浆钓鱼缓存
    public static int RefreshCaches(MachData data)
    {
        // 先设置 TempPlayer 的环境为钓鱼机位置
        SetupPlayer(TempPlayer, data);

        int total = 0;
        data.HasCratePotion = false;
        data.CanFishInLava = false;
        data.HasTackle = false;

        if (data.ChestIndex != -1)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null && chest.x == data.Pos.X && chest.y == data.Pos.Y)
            {
                foreach (var item in chest.item)
                {
                    if (item == null || item.IsAir) continue;

                    if (Config.CustomPowerItems.TryGetValue(item.type, out int power))
                        total += power;

                    if (item.type == ItemID.CratePotion)
                        data.HasCratePotion = true;

                    if (lavaItems.Contains(item.type))
                        data.CanFishInLava = true;

                    if (item.type == ItemID.TackleBox || item.type == ItemID.AnglerTackleBag)
                        data.HasTackle = true;
                }
            }
        }

        data.HasMonsterRule = Config.CustomFishes.Any(r => r.NPCType > 0 && (r.Cond.Count == 0 || CheckConds(r.Cond, TempPlayer)));
        data.BonusTotal = total;
        return total;
    }
    #endregion

    #region 物品进箱动画
    private static void Transfer(Vector2 from, int x, int y, int ci, int slot, int itemType)
    {
        NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, ci, slot);
        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        Chest.VisualizeChestTransfer(from, to, itemType, Chest.ItemTransferVisualizationSettings.Hopper);
    }
    #endregion

    #region 获取水体数量（统计指定坐标周围一定半径内的液体数量（水、岩浆、蜂蜜））
    public static int GetWaterTiles(Point pos, ref int lava, ref int honey)
    {
        // 缓存当前水体数量 避免重复计算
        int water = 0;
        int radius = Config.Range; // 直接使用图格半径
        int minX, maxX, minY, maxY;
        GetCenter(pos, radius, out minX, out maxX, out minY, out maxY);

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

    #region 获取连通水体坐标（返回最近且连通的水体）
    public static Point FindWaterInRadius(Point center, int radius)
    {
        // 如果箱子本身就在液体中，直接返回
        var tile = Main.tile[center.X, center.Y];
        if (tile?.liquid > 0)
            return center;

        // BFS 搜索连通液体
        int minX, maxX, minY, maxY;
        GetCenter(center, radius, out minX, out maxX, out minY, out maxY);
        var visited = new bool[maxX - minX + 1, maxY - minY + 1];
        var queue = new Queue<Point>();
        queue.Enqueue(center);
        visited[center.X - minX, center.Y - minY] = true;

        Point nearest = Point.Zero;
        int minDistSq = int.MaxValue;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var ctile = Main.tile[current.X, current.Y];
            if (ctile?.liquid > 0)
            {
                int dx = current.X - center.X;
                int dy = current.Y - center.Y;
                int distSq = dx * dx + dy * dy;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearest = current;
                }
                continue;
            }

            // 四个方向扩展
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0) continue;
                    int nx = current.X + i;
                    int ny = current.Y + j;
                    if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                    if (visited[nx - minX, ny - minY]) continue;
                    visited[nx - minX, ny - minY] = true;
                    queue.Enqueue(new Point(nx, ny));
                }
            }
        }

        // 如果找到了连通水体，返回最近的那个
        if (nearest != Point.Zero)
            return nearest;

        // 回退：在范围内按距离查找最近的水体（不考虑连通性）
        return OldFindWater(center, radius);
    }
    #endregion

    #region 获取水体坐标（返回最近的水体）
    public static Point OldFindWater(Point center, int radius)
    {
        int minX, maxX, minY, maxY;
        GetCenter(center, radius, out minX, out maxX, out minY, out maxY);
        Point nearest = Point.Zero;
        int minDistSq = int.MaxValue;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var t = Main.tile[x, y];
                if (t?.liquid > 0)
                {
                    int dx = x - center.X;
                    int dy = y - center.Y;
                    int distSq = dx * dx + dy * dy;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        nearest = new Point(x, y);
                    }
                }
            }
        }
        return nearest;
    }
    #endregion

    #region 计算大气因子
    public static float GetAtmo(int yPos)
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

    #region 获取矩形坐标
    public static void GetCenter(Point center, int radius, out int minX, out int maxX, out int minY, out int maxY)
    {
        minX = Math.Max(center.X - radius, 0);
        maxX = Math.Min(center.X + radius, Main.maxTilesX - 1);
        minY = Math.Max(center.Y - radius, 0);
        maxY = Math.Min(center.Y + radius, Main.maxTilesY - 1);
    }
    #endregion

    #region 自定义额外渔获
    private static void CustomFishes(MachData data, Item rodItem, ref bool allow)
    {
        // 创建临时玩家，用于条件判断中的环境检查（如生物群落）
        SetupPlayer(TempPlayer, data);

        foreach (var rule in Config.CustomFishes)
        {
            // 检查条件（如果配置了条件）
            if (rule.Cond.Count > 0 && !CheckConds(rule.Cond, TempPlayer))
                continue;

            // 鱼饵投掷者概率加成：概率分母减半
            int Chance = rule.Chance;
            if (rodItem.type == ItemID.BloodFishingRod)
                Chance = Math.Max(1, Chance / 2);

            if (Main.rand.Next(Chance) != 0)
                continue;

            // 生成NPC
            if (rule.NPCType > 0)
            {
                // 检查是否允许生成自定义NPC
                if (!Config.EnableCustomNPC)
                    continue; // 跳过此规则，继续处理下一条

                // 使用玩家邻近标识，不再遍历所有玩家
                if (data.NearbyPlayers.Count == 0) continue;

                // 统计水体（用于敌怪生成判断.岩浆或蜂蜜中不能生成敌怪）
                bool inLava = data.LavCnt > 0;
                bool inHoney = data.HonCnt > 0;
                if (inLava || inHoney)
                    continue;

                Vector2 spawnPos = new Vector2(data.WaterPos.X * 16 + 8, data.WaterPos.Y * 16 + 8);

                // 独立怪物检查
                if (Config.SoloCustomMonster)
                {
                    // 获取所有自定义渔获的怪物类型集合
                    var Monsters = Config.CustomFishes
                        .Where(r => r.NPCType > 0)
                        .Select(r => r.NPCType)
                        .ToHashSet();

                    // 在 Config.Range 范围内检查是否存在任何自定义怪物
                    bool duplicate = false;
                    for (int i = 0; i < Main.maxNPCs; i++)
                    {
                        var npc = Main.npc[i];
                        if (npc.active && Monsters.Contains(npc.type))
                        {
                            // 图格距离转换为像素距离平方
                            float distSq = (npc.position - spawnPos).LengthSquared();
                            float maxDistSq = Config.Range * Config.Range * 256f;
                            if (distSq <= maxDistSq)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                    }

                    if (duplicate)
                        continue; // 已有一个怪物，跳过本次生成
                }

                int npcIndex = NPC.NewNPC(null, (int)spawnPos.X, (int)spawnPos.Y, rule.NPCType);
                if (npcIndex >= 0)
                {
                    var npc = Main.npc[npcIndex];
                    npc.active = true;
                    npc.netUpdate = true;
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npcIndex);

                    if (Config.Broadcast)
                        TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 钓到了 {Lang.GetNPCNameValue(rule.NPCType)}", color2);
                }

                allow = true;
                return;    // 生成敌怪后，本次钓鱼不再处理其他掉落（包括原版）
            }
            else if (rule.ItemType > 0)
            {
                // 检查排除列表
                if (data.Exclude.Contains(rule.ItemType))
                    continue;

                // 创建自定义物品
                var custom = new Item();
                custom.SetDefaults(rule.ItemType);
                custom.stack = 1;

                // 尝试放入钓鱼箱
                Vector2 from = new Vector2(data.WaterPos.X * 16 + 8, data.WaterPos.Y * 16 + 8);
                Point pos = data.Pos;
                int idx = data.ChestIndex;
                if (idx != -1)
                {
                    var chest = Main.chest[idx];
                    for (int s = 0; s < chest.item.Length; s++)
                    {
                        if (chest.item[s].IsAir)
                        {
                            chest.item[s] = custom.Clone();
                            Transfer(from, pos.X, pos.Y, idx, s, custom.type);
                            break; // 放入后立即退出
                        }
                        else if (chest.item[s].type == custom.type && chest.item[s].stack < chest.item[s].maxStack)
                        {
                            chest.item[s].stack++;
                            Transfer(from, pos.X, pos.Y, idx, s, custom.type);
                            break; // 放入后立即退出
                        }
                    }
                }

                if (Config.Broadcast)
                    TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 额外钓到了 {ItemIcon(custom.type, custom.stack)} 坐标:{data.Pos.X} {data.Pos.Y}", color2);

                allow = true;
            }
        }
    }
    #endregion

    #region 箱子打开事件（创建、更新、获取数据）
    private void OnChestOpen(object sender, GetDataHandlers.ChestOpenEventArgs e)
    {
        if (!Config.Enabled) return;

        var plr = e.Player;
        if (plr == null) return;

        int c = Chest.FindChest(e.X, e.Y);
        var data = FindChest(c);

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
            if (data == null)
            {
                plr.SendErrorMessage("该位置没有钓鱼机");
                return;
            }

            MyCommand.ShowMachineInfo(plr, data);
            plr.RemoveData("info");
            return;
        }

        // 处理 exclude 指令排除物品模式
        if (plr.GetData<bool>("exc"))
        {
            if (data == null)
            {
                plr.SendErrorMessage("该位置没有钓鱼机");
                return;
            }

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
                TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 排除了 {ItemIcon(type, 1)} {name}", color2);
            }
            else
            {
                data.Exclude.Add(type);
                TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 添加了排除物品 {ItemIcon(type, 1)} {name}", color2);
            }

            SetDirty();
            plr.RemoveData("exc");
            return;
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

            if (Config.EnableCustomNPC && data.HasMonsterRule)
            {
                AddPlayerToMachine(plr.Name, data);
            }

            UpdateData(data, plr);
            return;
        }

        // 非钓鱼箱：检查是否在某个钓鱼机的范围内，记录玩家
        if (Machines != null)
        {
            float rangeSq = Config.Range * Config.Range;
            foreach (var data2 in Machines)
            {
                int dx = Math.Abs(data2.Pos.X - e.X);
                int dy = Math.Abs(data2.Pos.Y - e.Y);
                if (dx * dx + dy * dy <= rangeSq)
                {
                    if (Config.EnableCustomNPC && data2.HasMonsterRule)
                    {
                        AddPlayerToMachine(plr.Name, data2);
                    }
                }
            }
        }
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

    #region 添加玩家到机器映射
    private void AddPlayerToMachine(string name, MachData data)
    {
        if (!PlayerMachines.TryGetValue(name, out var list))
        {
            list = new List<MachData>();
            PlayerMachines[name] = list;
        }

        if (!list.Contains(data))
        {
            list.Add(data);
            data.NearbyPlayers.Add(name);
        }
    }
    #endregion

    #region 创建数据
    private void CreateData(TSPlayer plr, int index, Point pos)
    {
        // 检查箱子是否已被占用
        var existing = FindTile(pos);
        if (existing != null)
        {
            plr.SendErrorMessage($"该箱子已被 {existing.Owner} 的钓鱼机占用");
            return;
        }

        // 检查与其他钓鱼机距离
        foreach (var other in Machines)
        {
            int dx = Math.Abs(other.Pos.X - pos.X);
            int dy = Math.Abs(other.Pos.Y - pos.Y);
            if (dx * dx + dy * dy <= Config.Range * Config.Range)
            {
                plr.SendErrorMessage($"与其他钓鱼机距离过近（{Config.Range}格）");
                return;
            }
        }

        // 检查水体是否充足
        int lava = 0, honey = 0;
        int water = GetWaterTiles(pos, ref lava, ref honey);
        if (water < 75)
        {
            plr.SendMessage($"箱子附近{Config.Range}格内液体不足75格", color2);
            return;
        }

        // 创建新机器
        var NewData = new MachData
        {
            Owner = plr.Name,
            Pos = pos,
            ChestIndex = index
        };

        UpdateData(NewData, plr);
        var NeedWiring = Config.NeedWiring ? $"并[c/FF716D:连上电线]与计时器" : "\n";
        plr.SendMessage(TextGradient($"\n{plr.Name}的自动钓鱼机 ({pos.X},{pos.Y}) 创建成功! "), color);
        plr.SendMessage(TextGradient($"附近{Config.Range}格内液体数量:{water}"), color);
        plr.SendMessage(TextGradient($"请给箱子放入鱼竿和鱼饵 {NeedWiring}"), color);
    }
    #endregion

    #region 更新机器基础数据（环境、高度、水体）
    private static void UpdateData(MachData data, TSPlayer plr)
    {
        // 环境
        data.ZoneCorrupt = plr.TPlayer.ZoneCorrupt;
        data.ZoneCrimson = plr.TPlayer.ZoneCrimson;
        data.ZoneJungle = plr.TPlayer.ZoneJungle;
        data.ZoneSnow = plr.TPlayer.ZoneSnow;
        data.ZoneHallow = plr.TPlayer.ZoneHallow;
        data.ZoneDesert = plr.TPlayer.ZoneDesert;
        data.ZoneBeach = plr.TPlayer.ZoneBeach;
        data.ZoneDungeon = plr.TPlayer.ZoneDungeon;

        // 高度等级
        int yPos = data.Pos.Y;
        if (Main.remixWorld)
        {
            if (yPos < Main.worldSurface * 0.5) data.HeightLevel = 0;
            else if (yPos < Main.worldSurface) data.HeightLevel = 1;
            else if (yPos < Main.rockLayer) data.HeightLevel = 3;
            else if (yPos < Main.maxTilesY - 300) data.HeightLevel = 2;
            else data.HeightLevel = 4;
        }
        else
        {
            if (yPos < Main.worldSurface * 0.5) data.HeightLevel = 0;
            else if (yPos < Main.worldSurface) data.HeightLevel = 1;
            else if (yPos < Main.rockLayer) data.HeightLevel = 2;
            else if (yPos < Main.maxTilesY - 300) data.HeightLevel = 3;
            else data.HeightLevel = 4;
        }

        data.atmo = GetAtmo(yPos);
        data.RolledRemixOcean = Main.remixWorld && data.HeightLevel == 1 && yPos >= Main.rockLayer && Main.rand.Next(3) == 0;

        // 水体统计
        int lava = 0, honey = 0;
        int water = GetWaterTiles(data.Pos, ref lava, ref honey);
        data.WatCnt = water;
        data.LavCnt = lava;
        data.HonCnt = honey;
        data.WaterPos = FindWaterInRadius(data.Pos, Config.Range); // 可替换为 FindConnectedWater
        data.BonusTotal = RefreshCaches(data); // 刷新加成和缓存
        UpdateItem(data);
        AddOrUpdate(data);
    }
    #endregion

    #region 更新鱼竿/鱼饵缓存（通常在创建机器或箱子变化时调用）
    public static void UpdateItem(MachData data)
    {
        // 尝试重新查找鱼竿并缓存
        if (FindItem(data, item => item.fishingPole > 0, out _, out Chest rodChest, out int rodSlot))
        {
            data.RodChest = rodChest.index;
            data.RodSlot = rodSlot;
        }
        else
        {
            data.RodChest = -1;
            data.RodSlot = -1;
        }

        // 尝试重新查找鱼饵并缓存
        if (FindItem(data, item => item.bait > 0, out _, out Chest baitChest, out int baitSlot))
        {
            data.BaitChest = baitChest.index;
            data.BaitSlot = baitSlot;
        }
        else
        {
            data.BaitChest = -1;
            data.BaitSlot = -1;
        }
    }
    #endregion

    #region 更改箱子物品事件(刷新缓存)
    private void OnChestItemChange(object sender, GetDataHandlers.ChestItemEventArgs e)
    {
        if (!Config.Enabled) return;

        // 验证箱子索引有效性
        if (e.ID < 0 || e.ID >= Main.chest.Length) return;

        var data = FindChest(e.ID);
        if (data != null)
        {
            if (e.ID == data.ChestIndex)
            {
                if (Config.EnableCustomNPC && data.HasMonsterRule)
                {
                    // 记录下附近的玩家
                    AddPlayerToMachine(e.Player.Name, data);
                }

                UpdateData(data, e.Player);
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

    #region 玩家更新事件（检测离开范围）
    private void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs e)
    {
        if (!Config.Enabled || !Config.EnableCustomNPC || Machines is null)
            return;

        var plr = e.Player;
        if (plr == null || !plr.Active) return;

        if (!PlayerMachines.TryGetValue(plr.Name, out var datas)) return;

        Vector2 pos = plr.TPlayer.position;
        float rangeSq = Config.Range * Config.Range * 256f;

        for (int i = datas.Count - 1; i >= 0; i--)
        {
            var data = datas[i];
            Vector2 center = new Vector2(data.Pos.X * 16 + 8, data.Pos.Y * 16 + 8);
            if ((pos - center).LengthSquared() > rangeSq)
            {
                datas.RemoveAt(i);
                data.NearbyPlayers.Remove(plr.Name);

                if (!Config.Broadcast ||
                    !data.HasMonsterRule ||
                     data.NearbyPlayers.Count != 0)
                    continue;

                var text = TextGradient($"由于{plr.Name}超出{Config.Range}格范围\n" +
                                        $"{data.Owner}钓鱼机附近没有玩家,已关闭钓怪功能\n" +
                                        $"如需钓出怪物:请打开一次自钓箱");

                TShock.Utils.Broadcast(text, color2);
            }
        }

        if (datas.Count == 0)
            PlayerMachines.Remove(plr.Name);
    }
    #endregion

    #region 玩家离开事件（清理检测记录）
    private void OnPlayerLeave(LeaveEventArgs args)
    {
        if (!Config.Enabled || !Config.EnableCustomNPC || Machines is null)
            return;

        var plr = TShock.Players[args.Who];
        if (plr == null || !plr.Active) return;

        if (PlayerMachines.TryGetValue(plr.Name, out var datas))
        {
            foreach (var data in datas)
            {
                data.NearbyPlayers.Remove(plr.Name);

                if (!Config.Broadcast ||
                    !data.HasMonsterRule ||
                     data.NearbyPlayers.Count != 0)
                    continue;

                var text = TextGradient($"由于{plr.Name}离开服务器\n" +
                                        $"{data.Owner}钓鱼机附近没有玩家,已关闭钓怪功能\n" +
                                        $"如需钓出怪物:请打开一次自钓箱");

                TShock.Utils.Broadcast(text, color2);
            }

            PlayerMachines.Remove(plr.Name);
        }
    }
    #endregion
}