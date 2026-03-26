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
    public override Version Version => new(1, 0, 3);
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
    private static Player sharedPlayer = new();
    #endregion

    #region 注册与释放
    public override void Initialize()
    {
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
    }
    #endregion

    #region 游戏更新事件
    private static long nextFrame = 0; // 下一次执行的目标帧数
    private static long frameCounter = 0; // 帧计数器（每次游戏更新+1）
    private static long saveFrame = 0;
    private void OnGameUpdate(EventArgs args)
    {
        if (!Config.Enabled) return;

        frameCounter++;
        if (frameCounter >= nextFrame)
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

        // 定期保存（脏数据）
        if (frameCounter >= saveFrame)
        {
            if (IsDirty) Save();
            saveFrame = frameCounter + Config.SaveInterval;
        }
    }
    #endregion

    #region 放入物品到自钓箱时自动刷新缓存
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
                // 获取当前玩家的真实环境（使用 TSPlayer 的 TPlayer）
                var plr = e.Player;
                data.ZoneCorrupt = plr.TPlayer.ZoneCorrupt;
                data.ZoneCrimson = plr.TPlayer.ZoneCrimson;
                data.ZoneJungle = plr.TPlayer.ZoneJungle;
                data.ZoneSnow = plr.TPlayer.ZoneSnow;
                data.ZoneHallow = plr.TPlayer.ZoneHallow;
                data.ZoneDesert = plr.TPlayer.ZoneDesert;
                data.ZoneBeach = plr.TPlayer.ZoneBeach;
                data.ZoneDungeon = plr.TPlayer.ZoneDungeon;

                int lava = 0, honey = 0;
                int water = GetWaterTiles(data.Pos, ref lava, ref honey);
                data.WatCnt = water;
                data.LavCnt = lava;
                data.HonCnt = honey;
                data.WaterPos = FindWaterInRadius(data.Pos, Config.Range); // 刷新水体坐标
                data.BonusTotal = RefreshCaches(data); // 直接赋值，返回值用于同步
                UpdateRodAndBaitCache(data);
                data.CacheTime = DateTime.Now;
                SetDirty();
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
                    (DateTime.Now - data.lastRodWarning).TotalSeconds > Config.BC_CoolDown)
                {
                    data.lastRodWarning = DateTime.Now;
                    TShock.Utils.Broadcast($"\n{data.Owner}的钓鱼机缺少鱼竿，请放入鱼竿", color2);
                    TShock.Utils.Broadcast($"传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}", color2);
                }
                return;
            }

            // 2. 查找鱼饵
            if (!FindBait(data, out Item baitItem, out Chest baitChest, out int baitSlot))
            {
                if (Config.Broadcast &&
                    (DateTime.Now - data.lastBaitWarning).TotalSeconds > Config.BC_CoolDown)
                {
                    data.lastBaitWarning = DateTime.Now;
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
            if (Config.PowerChanceBonus > 0) Power += Config.PowerChanceBonus;
            Power += data.BonusTotal; // 直接使用缓存

            // 消耗鱼饵（概率）
            if (ConsumeBait(baitChest, data, baitSlot, baitItem, Power))
            {
                var noSpawn = false;

                // 添加自定义渔获判定
                if (Config.CustomFishes.Any())
                    CustomFishes(data, rodItem, ref noSpawn);

                // 如果自定义渔获成功则跳过原版渔获
                if (noSpawn) return;

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
                if ((DateTime.Now - data.CacheTime).TotalSeconds > Config.GetInterval)
                {
                    int lava = 0, honey = 0;
                    int water = GetWaterTiles(data.Pos, ref lava, ref honey);
                    data.WatCnt = water;
                    data.LavCnt = lava;
                    data.HonCnt = honey;
                    data.WaterPos = FindWaterInRadius(data.Pos, Config.Range); // 刷新水体坐标
                    data.BonusTotal = RefreshCaches(data); // 直接赋值，返回值用于同步
                    data.CacheTime = DateTime.Now;
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

    #region 更新鱼竿/鱼饵缓存（通常在创建机器或箱子变化时调用）
    public static void UpdateRodAndBaitCache(MachData data)
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

    public static bool HasItem(MachData data, Func<Item, bool> predicate) => FindItem(data, predicate, out _, out _, out _);

    // 在钓鱼机附近查找符合条件的物品（鱼竿或鱼饵、加成物品）
    private static bool FindItem(MachData data, Func<Item, bool> predicate, out Item foundItem, out Chest chest, out int slot)
    {
        foundItem = new();
        chest = new();
        slot = -1;

        // 优先使用缓存的箱子索引
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

        // 一次扫描整个范围（不再渐进）
        return TryFindItem(data.Pos, Config.Range, predicate, out foundItem, out chest, out slot);
    }

    // 在指定半径内查找符合条件的物品
    private static bool TryFindItem(Point center, int radius, Func<Item, bool> predicate, out Item foundItem, out Chest chest, out int slot)
    {
        foundItem = new();
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

        // 原版消耗概率：1 / (1 + PowerChanceBonus/6)
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

    #region 设置玩家环境（复用）
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
        SetupPlayer(sharedPlayer, data);
        int heightLevel = data.HeightLevel;
        if (Main.remixWorld && heightLevel == 2 && Main.rand.Next(2) == 0)
            heightLevel = 1;

        // 环境冲突处理（每次钓鱼时随机决定）
        bool corruption = sharedPlayer.ZoneCorrupt;
        bool crimson = sharedPlayer.ZoneCrimson;
        bool jungle = sharedPlayer.ZoneJungle;
        bool snow = sharedPlayer.ZoneSnow;
        bool hallow = sharedPlayer.ZoneHallow;
        bool desert = sharedPlayer.ZoneDesert;
        bool Beach = sharedPlayer.ZoneBeach;
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
        int waterTiles = data.WatCnt, lavaTiles = data.LavCnt, honeyTiles = data.HonCnt;
        // 特殊世界蜂蜜修正
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0) honeyTiles = 0;

        // 大气因子
        float atmo = data.atmo;

        // 需要水体
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
            Player = sharedPlayer,
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
        int total = 0;
        data.HasCratePotion = false;
        data.CanFishInLava = false;

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
                }
            }
        }

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

    #region 获取水体坐标
    public static Point FindWaterInRadius(Point center, int radius)
    {
        int minX, maxX, minY, maxY;
        GetCenter(center, radius, out minX, out maxX, out minY, out maxY);

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
    private static void CustomFishes(MachData data, Item rodItem, ref bool noSapwn)
    {
        // 创建临时玩家，用于条件判断中的环境检查（如生物群落）
        SetupPlayer(sharedPlayer, data);

        foreach (var rule in Config.CustomFishes)
        {
            // 检查条件（如果配置了条件）
            if (rule.Cond.Count > 0 && !Utils.CheckConds(rule.Cond, sharedPlayer))
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

                // 统计水体（用于敌怪生成判断）
                bool inLava = data.LavCnt > 0;
                bool inHoney = data.HonCnt > 0;

                // 检查附近是否有玩家（否则NPC会消失）
                if (!IsAnyPlayerNearby(data.Pos, Config.Range))
                    continue;

                // 添加液体检查：岩浆或蜂蜜中不能生成敌怪
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

                noSapwn = true;
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

                noSapwn = true;
            }
        }
    }

    // 检查指定位置附近是否有在线玩家
    private static bool IsAnyPlayerNearby(Point pos, int range)
    {
        Vector2 center = new Vector2(pos.X * 16, pos.Y * 16);
        float Squared = range * range * 256f; // 距离平方，避免开方
        foreach (var plr in TShock.Players)
        {
            if (plr?.Active == true)
            {
                Vector2 tpos = plr.TPlayer.position;
                if ((tpos - center).LengthSquared() <= Squared)
                    return true;
            }
        }
        return false;
    }
    #endregion

}