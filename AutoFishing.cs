using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.FishDropRules;
using Terraria.ID;
using TShockAPI;
using static FishMach.Plugin;
using static FishMach.Utils;

namespace FishMach;

public class AutoFishing
{
    private readonly MachData data;

    public AutoFishing(MachData data)
    {
        this.data = data;
    }

    // 检查箱子有效性
    private bool IsValidChest(Chest chest) => chest != null && chest.x >= 0 && chest.x < Main.maxTilesX && chest.y >= 0 && chest.y < Main.maxTilesY;

    #region 钓鱼核心逻辑
    public bool Execute()
    {
        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.PlrCnt == 0)
            return false;

        // 1. 鱼竿
        if (!FindRod(out Item rodItem, out Chest rodChest, out int rodSlot))
        {
            if (Config.Broadcast && (DateTime.UtcNow - data.lastRodWarning).TotalSeconds > Config.BC_CoolDown)
            {
                data.lastRodWarning = DateTime.UtcNow;
                var text = $"\n{data.Owner}的钓鱼机缺少鱼竿，请放入鱼竿\n传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return false;
        }

        // 2. 鱼饵
        if (!FindBait(out Item baitItem, out Chest baitChest, out int baitSlot))
        {
            if (Config.Broadcast && (DateTime.UtcNow - data.lastBaitWarning).TotalSeconds > Config.BC_CoolDown)
            {
                data.lastBaitWarning = DateTime.UtcNow;
                var text = $"\n{data.Owner}的钓鱼机缺少鱼饵，请放入鱼饵\n传送到钓鱼机:/tppos {data.Pos.X} {data.Pos.Y}";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return false;
        }

        // 3. 渔力
        int power = rodItem.fishingPole + baitItem.bait;
        if (Config.PowerChanceBonus > 0) power += Config.PowerChanceBonus;
        power += data.BonusTotal;

        // 4. 消耗鱼饵
        if (!ConsumeBait(baitChest, baitSlot, baitItem, power))
            return false;

        // 确保环境最新
        if (data.EnvDirty || (DateTime.UtcNow - data.LastEnvUpd).TotalSeconds > 5)
            EnvManager.RefreshEnv(data);

        // 5. 自定义渔获
        bool allow = false;
        if (Config.CustomFishes.Any())
            CustomFishes(rodItem, ref allow);
        if (allow) return true;

        // 6. 原版渔获
        var context = BuildFishingContext(power, rodItem, baitItem);
        int itemType = RuleList.TryGetItemDropType(context);
        if (itemType == 0) return false;

        var fish = new Item();
        fish.SetDefaults(itemType);
        fish.stack = 1;

        if (data.Exclude.Contains(fish.type)) return false;

        PutToChest(fish);
        return true;
    }
    #endregion

    #region 获取鱼竿名称与鱼力
    public Item? GetRodItem(out Chest chest, out int slot)
    {
        if (FindRod(out Item rod, out chest, out slot))
            return rod;
        return null;
    }

    public Item? GetBaitItem(out Chest chest, out int slot)
    {
        if (FindBait(out Item bait, out chest, out slot))
            return bait;
        return null;
    }

    public static string GetRodName(MachData data)
    {
        var engine = new AutoFishing(data);
        var rod = engine.GetRodItem(out _, out _);
        return rod?.Name ?? "无";
    }

    public static int GetRodPower(MachData data)
    {
        var engine = new AutoFishing(data);
        var rod = engine.GetRodItem(out _, out _);
        return rod?.fishingPole ?? 0;
    }
    #endregion

    #region 查找物品
    // 公共方法：查找鱼竿
    public bool FindRod(out Item rodItem, out Chest chest, out int slot)
    {
        int rodChest = data.RodChest;
        int rodSlot = data.RodSlot;
        if (FindItemAndCache(item => item.fishingPole > 0, ref rodChest, ref rodSlot, out rodItem, out chest, out slot))
        {
            data.RodChest = rodChest;
            data.RodSlot = rodSlot;
            return true;
        }
        return false;
    }

    // 公共方法：查找鱼饵
    public bool FindBait(out Item baitItem, out Chest chest, out int slot)
    {
        int baitChest = data.BaitChest;
        int baitSlot = data.BaitSlot;
        if (FindItemAndCache(item => item.bait > 0, ref baitChest, ref baitSlot, out baitItem, out chest, out slot))
        {
            data.BaitChest = baitChest;
            data.BaitSlot = baitSlot;
            return true;
        }
        return false;
    }

    // 私有方法：查找物品并更新缓存（使用 ref 局部变量）
    private bool FindItemAndCache(Func<Item, bool> predicate, ref int IndexCache, ref int slotCache, out Item item, out Chest chest, out int slot)
    {
        // 1. 检查缓存
        if (IndexCache != -1 && slotCache != -1)
        {
            var myChest = Main.chest[IndexCache];
            if (myChest != null && IsValidChest(myChest))
            {
                var cacheItem = myChest.item[slotCache];
                if (cacheItem != null && !cacheItem.IsAir && predicate(cacheItem))
                {
                    item = cacheItem;
                    chest = myChest;
                    slot = slotCache;
                    return true;
                }
            }

            // 缓存失效，清除
            IndexCache = -1;
            slotCache = -1;
        }

        // 2. 查找（仅主箱子）
        if (FindItem(predicate, out item, out chest, out slot))
        {
            IndexCache = chest.index;
            slotCache = slot;
            return true;
        }

        return false;
    }

    // 从主箱子找物品
    public bool FindItem(Func<Item, bool> pred, out Item found, out Chest chest, out int slot)
    {
        found = new();
        chest = new();
        slot = -1;

        // 只检查主箱子
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

        return false;
    }
    #endregion

    #region 获取鱼饵数量
    public static int GetBaitCount(int c)
    {
        int count = 0;
        if (c != -1)
        {
            var chest = Main.chest[c];
            if (chest != null)
            {
                for (int s = 0; s < chest.item.Length; s++)
                {
                    var item = chest.item[s];
                    if (item != null && !item.IsAir && item.bait > 0)
                        count += item.stack;
                }
            }
        }
        return count;
    }
    #endregion

    #region 消耗鱼饵
    private bool ConsumeBait(Chest chest, int slot, Item baitItem, int power)
    {
        if (chest == null || baitItem == null || baitItem.IsAir) return false;
        if (!IsValidChest(chest)) return false;

        float chance = 1f / (1f + power / 6f);
        if (data.HasTackle) chance *= 0.8f;

        if (Main.rand.NextFloat() < chance)
        {
            baitItem.stack--;
            if (baitItem.stack <= 0)
            {
                baitItem.TurnToAir();
                data.BaitChest = -1;
                data.BaitSlot = -1;
            }
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
            return true;
        }
        return true;
    }
    #endregion

    #region 自定义渔获
    private void CustomFishes(Item rodItem, ref bool allow)
    {
        EnvManager.SetupTempPlayer(data);

        foreach (var rule in Config.CustomFishes)
        {
            if (rule.Cond.Count > 0 && !CheckConds(rule.Cond, TempPlayer))
                continue;

            int chance = rule.Chance;
            if (rodItem.type == ItemID.BloodFishingRod)
                chance = Math.Max(1, chance / 2);

            if (Main.rand.Next(chance) != 0)
                continue;

            if (rule.NPCType > 0)
            {
                if (!Config.EnableCustomNPC) continue;

                bool inLava = data.LavCnt >= data.MaxLiq;
                bool inHoney = data.HonCnt >= data.MaxLiq;
                if (inLava || inHoney) continue;

                Vector2 spawnPos = new Vector2(data.LiquidPos.X * 16 + 8, data.LiquidPos.Y * 16 + 8);
                if (IsMonsterSolo(spawnPos, rule.NPCType, out bool dup)) continue;

                if (data.PlrCnt == 0) continue;

                int npcIndex = NPC.NewNPC(null, (int)spawnPos.X, (int)spawnPos.Y, rule.NPCType);
                if (npcIndex >= 0)
                {
                    var npc = Main.npc[npcIndex];
                    npc.active = true;
                    npc.netUpdate = true;
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npcIndex);
                    if (Config.Broadcast)
                        TSPlayer.All.SendMessage($"{data.Owner}的钓鱼机 钓到了 {Lang.GetNPCNameValue(rule.NPCType)}", color2);
                }
                allow = true;
                return;
            }
            else if (rule.ItemType > 0)
            {
                if (data.Exclude.Contains(rule.ItemType)) continue;
                var custom = new Item();
                custom.SetDefaults(rule.ItemType);
                custom.stack = 1;
                PutToChest(custom);
                if (Config.Broadcast)
                    TSPlayer.All.SendMessage($"{data.Owner}的钓鱼机 额外钓到了 {ItemIcon(custom.type, custom.stack)} 坐标:{data.Pos.X} {data.Pos.Y}", color2);
                allow = true;
            }
        }
    }
    #endregion

    #region 构建钓鱼信息上下文
    private FishingContext BuildFishingContext(int fishingPower, Item rodItem, Item baitItem)
    {
        EnvManager.SetupTempPlayer(data);

        int heightLevel = data.HeightLevel;
        if (Main.remixWorld && heightLevel == 2 && Main.rand.Next(2) == 0)
            heightLevel = 1;

        bool corruption = TempPlayer.ZoneCorrupt;
        bool crimson = TempPlayer.ZoneCrimson;
        bool jungle = TempPlayer.ZoneJungle;
        bool snow = TempPlayer.ZoneSnow;
        bool hallow = TempPlayer.ZoneHallow;
        bool desert = TempPlayer.ZoneDesert;
        bool beach = TempPlayer.ZoneBeach;
        bool rolledRemixOcean = data.RolledRemixOcean;

        if (corruption && crimson)
        {
            if (Main.rand.Next(2) == 0) crimson = false;
            else corruption = false;
        }
        if (jungle && snow && Main.rand.Next(2) == 0) jungle = false;

        bool infectedDesert = desert && (corruption || crimson || hallow);

        int maxLiq = data.MaxLiq , water = data.WatCnt, lava = data.LavCnt, honey = data.HonCnt;
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0) honey = 0;

        float atmo = data.atmo;
        int waterNeeded = (int)(300f * atmo);
        float waterQuality = Math.Min(1f, (float)maxLiq / waterNeeded);
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

        bool junk = Main.rand.Next(50) > fishingPower && Main.rand.Next(50) > fishingPower && maxLiq < waterNeeded;
        bool hasCratePotion = data.HasCratePotion;
        bool common, uncommon, rare, veryrare, legendary, crate;
        FishingCheck_RollDropLevels(fishingPower, hasCratePotion, out common, out uncommon, out rare, out veryrare, out legendary, out crate);

        int questFish = -1;
        if (Config.QuestFish && NPC.AnyNPCs(NPCID.Angler) && !Main.anglerQuestFinished)
            questFish = Main.anglerQuestItemNetIDs[Main.anglerQuest];

        bool canFishInLava = data.CanFishInLava ||
                             ItemID.Sets.CanFishInLava[rodItem.type] ||
                             ItemID.Sets.IsLavaBait[baitItem.type];

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
        fc.Fisher.waterTilesCount = maxLiq;
        fc.Fisher.waterQuality = waterQuality;
        fc.Fisher.fishingLevel = fishingPower;
        fc.Fisher.inLava = lava >= maxLiq;
        fc.Fisher.inHoney = honey >= maxLiq;
        fc.Fisher.rolledEnemySpawn = Main.rand.Next(100) < (fishingPower / 200f) ? 1 : 0;
        fc.Fisher.questFish = questFish;
        fc.Fisher.CanFishInLava = canFishInLava && lava >= maxLiq;

        return fc;
    }
    #endregion

    #region 物品存入与转移动画(箱满掉地上)
    private void PutToChest(Item item)
    {
        Vector2 from = new Vector2(data.LiquidPos.X * 16 + 8, data.LiquidPos.Y * 16 + 8);
        Point pos = data.Pos;
        int idx = data.ChestIndex;
        if (idx != -1)
        {
            var chest = Main.chest[idx];
            int firstEmpty = -1;

            for (int s = 0; s < chest.item.Length; s++)
            {
                var slotItem = chest.item[s];
                if (slotItem != null && !slotItem.IsAir)
                {
                    // 可堆叠的同类物品
                    if (slotItem.type == item.type && slotItem.stack < slotItem.maxStack)
                    {
                        slotItem.stack++;
                        Transfer(from, pos.X, pos.Y, idx, s, item.type);

                        // 每10次整理一次箱子
                        data.PutCounter++;
                        if (data.PutCounter >= Config.PutCounter)
                        {

                            SortChest(chest);
                            data.PutCounter = 0;
                        }
                        return;
                    }
                }
                else if (firstEmpty == -1)
                {
                    // 记录第一个空位
                    firstEmpty = s;
                }
            }

            // 没有可堆叠的槽位，尝试放入第一个空位
            if (firstEmpty != -1)
            {
                chest.item[firstEmpty] = item.Clone();
                Transfer(from, pos.X, pos.Y, idx, firstEmpty, item.type);
                // 每10次整理一次箱子
                data.PutCounter++;
                if (data.PutCounter >= Config.PutCounter)
                {
                    SortChest(chest);
                    data.PutCounter = 0;
                }
                return;
            }

            // 箱子已满，掉落在地上
            int dropX = data.Pos.X * 16 + 8;
            int dropY = data.Pos.Y * 16 + 8;
            int index = Item.NewItem(null, new Vector2(dropX, dropY), Vector2.Zero, item.type, item.stack);
        }
    }

    // 转移动画
    private void Transfer(Vector2 from, int x, int y, int ci, int slot, int itemType)
    {
        NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, ci, slot);
        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        Chest.VisualizeChestTransfer(from, to, itemType, Chest.ItemTransferVisualizationSettings.Hopper);
    }

    /// <summary>
    /// 整理箱子：合并相同物品，压缩槽位，并按物品ID排序
    /// </summary>
    public void SortChest(Chest chest)
    {
        if (chest == null) return;

        // 统计所有物品类型及总数量
        Dictionary<int, int> itemCounts = new();
        for (int i = 0; i < chest.item.Length; i++)
        {
            Item item = chest.item[i];
            if (item != null && !item.IsAir)
            {
                int type = item.type;
                int maxStack = item.maxStack;
                int newStack = item.stack;

                if (itemCounts.ContainsKey(type))
                    newStack += itemCounts[type];

                itemCounts[type] = newStack;

                // 清空当前槽位
                item.TurnToAir();
            }
        }

        // 重新填充箱子（按类型顺序）
        int slot = 0;
        foreach (var kvp in itemCounts)
        {
            int type = kvp.Key;
            int totalStack = kvp.Value;

            while (totalStack > 0)
            {
                int add = Math.Min(totalStack, 9999);
                chest.item[slot].SetDefaults(type);
                chest.item[slot].stack = add;
                totalStack -= add;
                slot++;

                if (slot >= chest.item.Length) break; // 箱子容量足够，一般不会溢出
            }
            if (slot >= chest.item.Length) break;
        }

        // 发送网络更新，同步所有槽位
        for (int i = 0; i < chest.item.Length; i++)
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
    }
    #endregion

    #region 禁钓怪物模式
    private bool IsMonsterSolo(Vector2 spawnPos, int npcType, out bool duplicate)
    {
        duplicate = false;
        if (!Config.SoloCustomMonster) return false;

        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null) return false;  // 无区域则无法判定，视为允许

        float maxDistSq = Config.Range * Config.Range * 256f;

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var npc = Main.npc[i];
            if (!npc.active) continue;

            // 快速剪枝：只检查区域内的 NPC
            int tileX = (int)(npc.position.X / 16);
            int tileY = (int)(npc.position.Y / 16);
            if (!region.InArea(tileX, tileY)) continue;

            bool shouldCheck = Config.SoloMode == 1
                ? Config.CustomFishes.Any(r => r.NPCType == npc.type)
                : npc.type == npcType;

            if (shouldCheck)
            {
                float distSq = (npc.position - spawnPos).LengthSquared();
                if (distSq <= maxDistSq)
                {
                    duplicate = true;
                    return true;
                }
            }
        }
        return false;
    }
    #endregion

    #region 原版渔获概率计算
    private void FishingCheck_RollDropLevels(int fishingLevel, bool hasCratePotion, out bool common, out bool uncommon, out bool rare, out bool veryrare, out bool legendary, out bool crate)
    {
        int commonRate = 150 / fishingLevel;
        int uncommonRate = 150 * 2 / fishingLevel;
        int rareRate = 150 * 7 / fishingLevel;
        int veryrareRate = 150 * 15 / fishingLevel;
        int legendaryRate = 150 * 30 / fishingLevel;

        int crateRate = 10 + Config.CrateChanceBonus;
        if (hasCratePotion) crateRate += 15;

        if (commonRate < 2) commonRate = 2;
        if (uncommonRate < 3) uncommonRate = 3;
        if (rareRate < 4) rareRate = 4;
        if (veryrareRate < 5) veryrareRate = 5;
        if (legendaryRate < 6) legendaryRate = 6;

        common = Main.rand.Next(commonRate) == 0;
        uncommon = Main.rand.Next(uncommonRate) == 0;
        rare = Main.rand.Next(rareRate) == 0;
        veryrare = Main.rand.Next(veryrareRate) == 0;
        legendary = Main.rand.Next(legendaryRate) == 0;
        crate = Main.rand.Next(100) < crateRate;
    }
    #endregion
}