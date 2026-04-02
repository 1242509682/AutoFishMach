using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
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

    #region 钓鱼核心逻辑
    public void Execute()
    {
        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0)
            return;

        // 如果液体已死，不再检查液体，直接返回
        if (data.LiquidDead)
            return;

        // 1. 液体实时更新(内部自带快速检查)
        EnvManager.SyncLiquid(data);

        // 2. 液体更新后仍不满足液体条件，直接返回，并标记液体已死
        if (data.MaxLiq == 0 || data.MaxLiq < Config.NeedLiqStack)
        {
            if (!data.LiquidBroadcast)
            {
                data.LiquidBroadcast = true;
                data.LiquidDead = true; // 标记液体已死，停止后续检测
                var text = data.MaxLiq == 0
                    ? $"钓鱼机 [c/ED756F:{data.ChestIndex}] 鱼池[c/FF716D:液体异常]\n传送 /tppos {data.Pos.X} {data.Pos.Y}\n"
                    : $"钓鱼机 [c/ED756F:{data.ChestIndex}] 液体不足 [c/FF716D:{Config.NeedLiqStack}] 格\n传送 /tppos {data.Pos.X} {data.Pos.Y}\n";
                TSPlayer.All.SendMessage(TextGradient(text + "修好鱼池后[c/F1FA51:打开箱子]即可"), color);
            }
            return;
        }
        data.LiquidBroadcast = false;
        data.LiquidDead = false; // 液体充足时清除死亡标记

        // 3. 鱼竿
        if (!FindRod(out Item rodItem, out int rodSlot))
        {
            // 仅在未播报过时播报一次
            if (!data.RodBroadcast)
            {
                data.RodBroadcast = true;
                var text = $"钓鱼机 [c/ED756F:{data.ChestIndex}] 未找到[c/FF716D:鱼竿]\n" +
                           $"传送 /tppos {data.Pos.X} {data.Pos.Y}\n";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return;
        }
        data.RodBroadcast = false;

        // 4. 鱼饵
        if (!FindBait(out Item baitItem, out int baitSlot))
        {
            if (!data.BaitBroadcast)
            {
                data.BaitBroadcast = true;
                var text = $"钓鱼机 [c/ED756F:{data.ChestIndex}] 未找到[c/FF716D:鱼饵]\n" +
                           $"传送 /tppos {data.Pos.X} {data.Pos.Y}\n";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return;
        }
        data.BaitBroadcast = false;

        // 5. 使用消耗物品并返回额外临时鱼力
        int UsedBonus = UsedItem();

        // 6. 渔力计算
        int power = rodItem.fishingPole + baitItem.bait;
        power += data.ExtraPower + UsedBonus;

        var now = DateTime.UtcNow;

        // 钓鱼药水临时加成（+20）
        if (now < data.FishingPotionTime)
            power += Config.FishingPotionPower;

        // 鱼饵桶临时加成（+10）
        if (now < data.ChumBucketTime)
            power += Config.ChumBucketPower;

        // 7. 消耗鱼饵
        if (!ConsumeBait(baitSlot, baitItem, power))
            return;

        // 8. 自定义渔获
        bool allow = false;
        if (Config.CustomFishes.Any())
            CustomFishes(rodItem, ref allow);
        if (allow) return;

        // 9. 原版渔获
        var context = BuildFishingContext(power, rodItem, baitItem);
        int itemType = RuleList.TryGetItemDropType(context);
        if (itemType == 0) return;

        var fish = new Item();
        fish.SetDefaults(itemType);
        fish.stack = 1;

        if (data.Exclude.Contains(fish.type)) return;

        // 10. 放入箱子
        PutToChest(fish);
    }
    #endregion

    #region 查找鱼竿和鱼饵
    public bool FindRod(out Item rodItem, out int slot)
    {
        int rodSlot = data.RodSlot;
        bool found = FindItem(i => i.fishingPole > 0, ref rodSlot, "鱼竿", out rodItem, out slot);
        data.RodSlot = rodSlot;
        return found;
    }

    public bool FindBait(out Item baitItem, out int slot)
    {
        int baitSlot = data.BaitSlot;
        bool found = FindItem(i => i.bait > 0, ref baitSlot, "鱼饵", out baitItem, out slot);
        data.BaitSlot = baitSlot;
        return found;
    }
    #endregion

    #region 查找物品 返回物品实例
    private bool FindItem(Func<Item, bool> pred, ref int Cache, string ItemName, out Item item, out int slot)
    {
        item = new();
        slot = -1;

        // 罢工就不找了
        if (Cache == -2)
            return false;

        var chest = Main.chest[data.ChestIndex];
        if (chest == null) return false;

        // 尝试使用缓存槽位
        if (Cache >= 0)
        {
            var cacheItem = chest.item[Cache];
            if (cacheItem != null && !cacheItem.IsAir && pred(cacheItem))
            {
                item = cacheItem;
                slot = Cache;
                return true;
            }
            // 缓存失效，标记为需要重新查找
            Cache = -1;
        }

        // 重新查找
        var items = chest.item.AsSpan();
        for (int i = 0; i < items.Length; i++)
        {
            ref var foundItem = ref items[i];
            if (foundItem != null && !foundItem.IsAir && pred(foundItem))
            {
                Cache = i;
                item = foundItem;
                slot = i;
                return true;
            }
        }

        // 找不到，标记为永久缺失等待玩家放入
        Cache = -2;
        return false;
    }
    #endregion

    #region 使用消耗类型物品
    private int UsedItem()
    {
        // 检查并消耗宝匣药水
        var CratePotionSlot = data.CratePotionSlot;
        var CratePotionTime = data.CratePotionTime;
        ConsumeItem(ItemID.CratePotion, ref CratePotionSlot, ref CratePotionTime, 4, $"+{Config.CratePotionBonus}%宝匣率");
        data.CratePotionSlot = CratePotionSlot;
        data.CratePotionTime = CratePotionTime;

        // 检查并消耗钓鱼药水
        var FishingPotionSlot = data.FishingPotionSlot;
        var FishingPotionTime = data.FishingPotionTime;
        ConsumeItem(ItemID.FishingPotion, ref FishingPotionSlot, ref FishingPotionTime, 8, $"+{Config.FishingPotionPower}渔力");
        data.FishingPotionSlot = FishingPotionSlot;
        data.FishingPotionTime = FishingPotionTime;

        // 检查并消耗鱼饵桶
        var ChumBucketSlot = data.ChumBucketSlot;
        var ChumBucketTime = data.ChumBucketTime;
        ConsumeItem(ItemID.ChumBucket, ref ChumBucketSlot, ref ChumBucketTime, 10, $"+{Config.ChumBucketPower}渔力");
        data.ChumBucketSlot = ChumBucketSlot;
        data.ChumBucketTime = ChumBucketTime;

        // 在渔力计算之前，处理自定义消耗物品
        int UsedBonus = 0;
        var now = DateTime.UtcNow;
        if (Config.RegionBuffEnabled && Config.CustomUsedItem.Count > 0)
            foreach (var used in Config.CustomUsedItem)
            {
                // 先检查是否在有效期内
                if (data.Custom.TryGetValue(used.ItemType, out var state) && state.Expiry > now)
                {
                    // 有效期内，直接累加加成
                    UsedBonus += state.Bonus;
                    continue;
                }

                int slot = -1;
                DateTime expiry = DateTime.MinValue;
                if (data.Custom.TryGetValue(used.ItemType, out state))
                {
                    slot = state.Slot;
                    expiry = state.Expiry;
                    // 过期则移除缓存，避免重复检查
                    if (expiry <= now)
                    {
                        data.Custom.Remove(used.ItemType);
                    }
                }

                int bonus = 0;
                UsedCustomItem(used, ref slot, ref expiry, ref bonus);
                if (bonus > 0)
                    UsedBonus += bonus;

                // 更新缓存
                if (slot != -2)
                    data.Custom[used.ItemType] = new CustomState(slot, expiry, bonus, Lang.GetItemNameValue(used.ItemType));
                else if (data.Custom.ContainsKey(used.ItemType))
                    data.Custom.Remove(used.ItemType);
            }

        return UsedBonus;
    }
    #endregion

    #region 查找消耗物品（只找一次,找不到就罢工）
    private void ConsumeItem(int type, ref int slot, ref DateTime Time, int Min, string? info = null)
    {
        // 效果仍在有效期内，无需消耗新药水,罢工就不找了
        if (DateTime.UtcNow < Time || slot == -2)
            return;

        var chest = Main.chest[data.ChestIndex];
        if (chest == null) return;

        // 尝试使用缓存槽位（仅当有有效缓存时）
        if (slot >= 0)
        {
            var item = chest.item[slot];
            if (item != null && !item.IsAir && item.type == type)
            {
                // 消耗一瓶
                item.stack--;
                if (item.stack <= 0)
                {
                    item.TurnToAir();
                    slot = -1;  // 清除缓存
                }
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
                Time = DateTime.UtcNow.AddMinutes(Min);

                // 有人就播报
                if (data.RegionPlayers.Count > 0)
                    foreach (var plr in data.RegionPlayers)
                        plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{ItemIcon(type)}" +
                                                    TextGradient($"获得{Min}分钟{info}"), color2);
                return;
            }

            // 缓存失效，清除
            slot = -1;
        }

        if (slot == -1)
        {
            // 重新查找
            var items = chest.item.AsSpan();
            for (int i = 0; i < items.Length; i++)
            {
                ref var item = ref items[i];
                if (item != null && !item.IsAir && item.type == type)
                {
                    item.stack--;
                    if (item.stack <= 0)
                        item.TurnToAir();
                    else
                        slot = i;  // 记录槽位
                    NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
                    Time = DateTime.UtcNow.AddMinutes(Min);

                    // 有人就播报
                    if (data.RegionPlayers.Count > 0)
                        foreach (var plr in data.RegionPlayers)
                            plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{ItemIcon(type)}" +
                                                TextGradient($"获得{Min}分钟{info}"), color2);
                    return;
                }
            }
        }

        // 没找到就不找了
        slot = -2;
    }
    #endregion

    #region 消耗自定义物品方法(用于区域buff)
    private void UsedCustomItem(CustomUsedItems UsedItem, ref int slot, ref DateTime expiry, ref int bonus)
    {
        // 如果效果仍在有效期内，无需消耗新药水
        if (DateTime.UtcNow < expiry || slot == -2)
            return;

        var chest = Main.chest[data.ChestIndex];
        if (chest == null) return;

        // 尝试使用缓存槽位
        if (slot >= 0)
        {
            var item = chest.item[slot];
            if (item != null && !item.IsAir && item.type == UsedItem.ItemType)
            {
                // 消耗一瓶
                item.stack--;
                if (item.stack <= 0)
                {
                    item.TurnToAir();
                    slot = -1;
                }
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
                expiry = DateTime.UtcNow.AddMinutes(UsedItem.Minutes);
                bonus = UsedItem.Power;

                // 激活区域BUFF
                if (UsedItem.BuffID > 0)
                {
                    data.ActiveZoneBuffs[UsedItem.BuffID] = expiry;

                    // 立即为区域内所有玩家刷新BUFF
                    if (data.RegionPlayers.Count > 0)
                        foreach (var plr in data.RegionPlayers)
                        {
                            plr.SetBuff(UsedItem.BuffID, 300);

                            plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{ItemIcon(UsedItem.ItemType)}" +
                            TextGradient($"持续{UsedItem.Minutes}分钟:\n" +
                            TextGradient($"[c/5F9DB8:-] {UsedItem.BuffDesc}")), color);
                        }
                }

                return;
            }
            // 缓存失效，清除
            slot = -1;
        }

        // 重新查找
        var items = chest.item.AsSpan();
        for (int i = 0; i < items.Length; i++)
        {
            ref var item = ref items[i];
            if (item != null && !item.IsAir && item.type == UsedItem.ItemType)
            {
                item.stack--;
                if (item.stack <= 0)
                    item.TurnToAir();
                else
                    slot = i;
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
                expiry = DateTime.UtcNow.AddMinutes(UsedItem.Minutes);
                bonus = UsedItem.Power;

                if (UsedItem.BuffID > 0)
                {
                    data.ActiveZoneBuffs[UsedItem.BuffID] = expiry;

                    // 立即为区域内所有玩家刷新BUFF
                    if (data.RegionPlayers.Count > 0)
                        foreach (var plr in data.RegionPlayers)
                        {
                            plr.SetBuff(UsedItem.BuffID, 300);

                            plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{ItemIcon(UsedItem.ItemType)}" +
                                TextGradient($"持续{UsedItem.Minutes}分钟:\n" +
                                TextGradient($"[c/5F9DB8:-] {UsedItem.BuffDesc}")), color);
                        }
                }
                return;
            }
        }

        // 没找到，标记为永久缺失
        slot = -2;
    }
    #endregion

    #region 消耗鱼饵
    private bool ConsumeBait(int slot, Item baitItem, int power)
    {
        if (baitItem == null || baitItem.IsAir) return false;

        // 原版概率：消耗概率 = 1 / (1 + power/6)
        float chance = 1f / (1f + power / 6f);
        if (data.HasTackle) chance *= 0.8f; // 钓具箱减少20%消耗概率

        if (Main.rand.NextFloat() < chance)
        {
            baitItem.stack--;
            if (baitItem.stack <= 0)
            {
                baitItem.TurnToAir();
                data.BaitSlot = -1;
            }
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, data.ChestIndex, slot);
            return true;
        }
        return true; // 原版未消耗也返回 true，表示钓鱼成功
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
                chance = (int)MathF.Max(1, chance / 2);

            if (Main.rand.Next(chance) != 0)
                continue;

            if (rule.NPCType > 0)
            {
                if (!Config.EnableCustomNPC) continue;

                if (data.RegionPlayers.Count == 0) continue;

                bool inLava = data.LavaCount >= data.MaxLiq, inHoney = data.HoneyCount >= data.MaxLiq;
                if (inLava || inHoney) continue;

                Vector2 spawnPos = new Vector2(data.LiqPos.X * 16 + 8, data.LiqPos.Y * 16 + 8);

                if (IsMonsterSolo(spawnPos, rule.NPCType)) continue;

                int npcIndex = NPC.NewNPC(null, (int)spawnPos.X, (int)spawnPos.Y, rule.NPCType);
                if (npcIndex >= 0)
                {
                    var npc = Main.npc[npcIndex];
                    npc.active = true;
                    npc.netUpdate = true;
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npcIndex);

                    if (data.RegionPlayers.Count > 0)
                        foreach (var plr in data.RegionPlayers)
                            plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 钓到了 " +
                                                         $"{Lang.GetNPCNameValue(rule.NPCType)}"), color2);
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

        int maxLiq = data.MaxLiq, water = data.WaterCount, lava = data.LavaCount, honey = data.HoneyCount;
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0) honey = 0;

        // 蜂蜜水体 1.5 倍修正（仅用于水体质量计算）
        int effectiveWater = maxLiq;
        if (data.LiqName == "蜂蜜")
            effectiveWater = (int)(effectiveWater * 1.5);

        float atmo = data.atmo;
        int waterNeeded = (int)(300f * atmo);
        float waterQuality = MathF.Min(1f, (float)effectiveWater / waterNeeded);
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

        float luck = TempPlayer.luck;
        if (luck < 0f && Main.rand.NextFloat() < -luck)
        {
            double factor = 0.9 - Main.rand.NextDouble() * 0.3; // 0.6 ~ 0.9
            fishingPower = (int)(fishingPower * factor);
        }
        else if (luck > 0f && Main.rand.NextFloat() < luck)
        {
            double factor = 1.1 + Main.rand.NextDouble() * 0.3; // 1.1 ~ 1.4
            fishingPower = (int)(fishingPower * factor);
        }

        bool junk = Main.rand.Next(50) > fishingPower && Main.rand.Next(50) > fishingPower && maxLiq < waterNeeded;
        bool hasCratePotion = DateTime.UtcNow < data.CratePotionTime;
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

    #region 原版渔获概率计算
    private void FishingCheck_RollDropLevels(int fishingLevel, bool hasCratePotion, out bool common, out bool uncommon, out bool rare, out bool veryrare, out bool legendary, out bool crate)
    {
        // 修复：防止除零，渔力至少为 1
        if (fishingLevel <= 0) fishingLevel = 1;

        // 直接使用整数除法得到概率分母，然后判断随机数
        // 原版公式：分母 = 150 / fishingLevel, 150*2/level, 150*7/level ...
        int commonDiv = 150 / fishingLevel;
        int uncommonDiv = 150 * 2 / fishingLevel;
        int rareDiv = 150 * 7 / fishingLevel;
        int veryrareDiv = 150 * 15 / fishingLevel;
        int legendaryDiv = 150 * 30 / fishingLevel;

        // 保底值（原版逻辑）
        if (commonDiv < 2) commonDiv = 2;
        if (uncommonDiv < 3) uncommonDiv = 3;
        if (rareDiv < 4) rareDiv = 4;
        if (veryrareDiv < 5) veryrareDiv = 5;
        if (legendaryDiv < 6) legendaryDiv = 6;

        // 宝匣概率基础10%，药水加成后增加
        int crateDiv = 100; // 分母100，即1%概率
        int crateRate = 10;
        if (hasCratePotion) crateRate += Config.CratePotionBonus;

        // 直接判断随机
        common = Main.rand.Next(commonDiv) == 0;
        uncommon = Main.rand.Next(uncommonDiv) == 0;
        rare = Main.rand.Next(rareDiv) == 0;
        veryrare = Main.rand.Next(veryrareDiv) == 0;
        legendary = Main.rand.Next(legendaryDiv) == 0;
        crate = Main.rand.Next(crateDiv) < crateRate;
    }
    #endregion

    #region 物品存入箱子
    private void PutToChest(Item item)
    {
        Vector2 from = new Vector2(data.LiqPos.X * 16 + 8, data.LiqPos.Y * 16 + 8);
        Point pos = data.Pos;
        int idx = data.ChestIndex;
        var chest = Main.chest[idx];
        if (chest == null) return;

        // 尝试存入当前箱子
        if (TryPutIntoChest(chest, item))
        {
            SendEffects(item, from, new Point(chest.x, chest.y));
            return;
        }

        // 当前箱子满，尝试区域内的其他箱子
        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region != null)
        {
            var chests = Main.chest.AsSpan();
            for (int i = 0; i < chests.Length; i++)
            {
                var other = chests[i];
                if (other == null || other == chest) continue;

                if (!region.Area.Contains(other.x, other.y) ||
                    !TryPutIntoChest(other, item))
                    continue;

                SendEffects(item, from, new Point(other.x, other.y));
                return;
            }
        }

        // 都失败，掉落在地上
        int dropX = data.Pos.X * 16 + 8;
        int dropY = data.Pos.Y * 16 + 8;
        int index = Item.NewItem(null, new Vector2(dropX, dropY), Vector2.Zero, item.type);
        if (data.RegionPlayers.Count > 0)
            foreach (var plr in data.RegionPlayers)
                plr.SendData(PacketTypes.UpdateItemDrop, null, index);
    }

    private bool TryPutIntoChest(Chest chest, Item item)
    {
        var items = chest.item.AsSpan();
        int slot = -1;
        for (int s = 0; s < items.Length; s++)
        {
            ref var slotItem = ref items[s];
            if (slotItem != null && !slotItem.IsAir)
            {
                if (slotItem.type == item.type && slotItem.stack < slotItem.maxStack)
                {
                    slotItem.stack++;
                    NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, s);
                    return true;
                }
            }
            else if (slot == -1)
            {
                slot = s;
            }
        }

        if (slot != -1)
        {
            items[slot] = item.Clone();
            NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
            return true;
        }
        return false;
    }
    #endregion

    #region 生成动画方法
    private void SendEffects(Item item, Vector2 from, Point toPos)
    {
        if (data.RegionPlayers.Count == 0) return;

        Vector2 to = new Vector2(toPos.X * 16 + 8, toPos.Y * 16 + 8);

        if (Config.FakeFish)
            SpawnFakeFish(item, from);

        if (Config.ChestTransfer)
            Chest.VisualizeChestTransfer(from, to, item.type, Chest.ItemTransferVisualizationSettings.Hopper);

        if (Config.Sparkle)
        {
            var setting = new ParticleOrchestraSettings { PositionInWorld = to, MovementVector = Vector2.Zero };
            ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.RainbowBoulder4, setting);
        }
    }
    #endregion

    #region 生成假鱼方法
    private void SpawnFakeFish(Item fish, Vector2 from)
    {
        int FakeFishCount = Main.rand.Next(2, 5);
        for (int i = 0; i < FakeFishCount; i++)
        {
            Vector2 offset = new Vector2(Main.rand.Next(-12, 13), Main.rand.Next(-6, 7)); // 独立偏移
            // 向位
            bool isJump = Main.rand.Next(3) == 0; // 1/3 概率跳跃
            float horiz = Main.rand.Next(-6, 7) * 0.5f; // 垂直
            float vert = Main.rand.Next(-7, -4); // 水平
            Vector2 vel = new Vector2(horiz, vert);

            // 水波
            var GlowSettings = new ParticleOrchestraSettings { PositionInWorld = from + offset, MovementVector = new Vector2(0f, -1f), UniqueInfoPiece = fish.type };
            ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.LakeSparkle, GlowSettings);

            // 假鱼
            ParticleOrchestraType FishType = isJump ? ParticleOrchestraType.FakeFishJump : ParticleOrchestraType.FakeFish;
            var fishSttring = new ParticleOrchestraSettings { PositionInWorld = from + offset, MovementVector = vel, UniqueInfoPiece = fish.type };
            ParticleOrchestrator.BroadcastOrRequestParticleSpawn(FishType, fishSttring);
        }
    }
    #endregion

    #region 禁钓怪物模式
    private bool IsMonsterSolo(Vector2 spawnPos, int npcType)
    {
        if (!Config.SoloCustomMonster) return false;

        var data = this.data;
        if (Config.SoloMode == 0)
        {
            // 模式0：不同类各一个，检查是否有任何自定义怪物存在
            if (data.Monsters.TryGetValue(npcType, out int cnt) && cnt > 0)
                return true;
        }
        else if (Config.SoloMode == 1)
        {
            // 模式1：仅单挑，检查所有怪物数量
            if (data.Monsters.Count > 0)
                return true;
        }
        return false;
    }
    #endregion

}