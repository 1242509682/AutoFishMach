using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.FishDropRules;
using Terraria.ID;
using Terraria.Localization;
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
    public bool Execute()
    {
        // 无人自动关闭检查
        if (Config.AutoStopWhenEmpty && data.RegionPlayers.Count == 0)
            return false;

        // 1. 鱼竿
        if (!FindRod(out Item rodItem, out int rodSlot))
        {
            // 仅在未播报过时播报一次
            if (Config.Broadcast && !data.RodBroadcast)
            {
                data.RodBroadcast = true;
                var text = $"{data.Owner}的钓鱼机 [c/FF716D:缺少鱼竿]\n前往放入[c/FF716D:鱼竿]:/tppos {data.Pos.X} {data.Pos.Y}\n";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return false;
        }
        data.RodBroadcast = false;

        // 2. 鱼饵
        if (!FindBait(out Item baitItem, out int baitSlot))
        {
            if (Config.Broadcast && !data.BaitBroadcast)
            {
                data.BaitBroadcast = true;
                var text = $"{data.Owner}的钓鱼机 [c/FF716D:缺少鱼饵]\n前往放入[c/FF716D:鱼饵]:/tppos {data.Pos.X} {data.Pos.Y}\n";
                TSPlayer.All.SendMessage(TextGradient(text), color);
            }
            return false;
        }
        data.BaitBroadcast = false;

        // 3. 使用消耗物品并返回额外临时鱼力
        int UsedBonus = UesdItem();

        // 4. 渔力计算
        int power = rodItem.fishingPole + baitItem.bait;
        power += data.ExtraPower + UsedBonus;

        // 钓鱼药水临时加成（+20）
        if (DateTime.UtcNow < data.FishingPotionTime)
            power += Config.FishingPotionPower;

        // 鱼饵桶临时加成（+10）
        if (DateTime.UtcNow < data.ChumBucketTime)
            power += Config.ChumBucketPower;

        // 5. 消耗鱼饵
        if (!ConsumeBait(baitSlot, baitItem, power))
            return false;

        // 6. 自定义渔获
        bool allow = false;
        if (Config.CustomFishes.Any())
            CustomFishes(rodItem, ref allow);
        if (allow) return true;

        // 7. 原版渔获
        var context = BuildFishingContext(power, rodItem, baitItem);
        int itemType = RuleList.TryGetItemDropType(context);
        if (itemType == 0) return false;

        var fish = new Item();
        fish.SetDefaults(itemType);
        fish.stack = 1;

        if (data.Exclude.Contains(fish.type)) return false;

        // 8. 放入箱子
        PutToChest(fish);
        return true;
    }
    #endregion

    #region 获取鱼竿名称与鱼力
    public Item? GetRodItem(out int slot)
    {
        if (FindRod(out Item rod, out slot))
            return rod;
        return null;
    }

    public Item? GetBaitItem(out int slot)
    {
        if (FindBait(out Item bait, out slot))
            return bait;
        return null;
    }

    public static string GetRodName(MachData data)
    {
        var engine = new AutoFishing(data);
        var rod = engine.GetRodItem(out _);
        return rod?.Name ?? "无";
    }

    public static int GetRodPower(MachData data)
    {
        var engine = new AutoFishing(data);
        var rod = engine.GetRodItem(out _);
        return rod?.fishingPole ?? 0;
    }

    public static int GetBaitPower(MachData data)
    {
        var engine = new AutoFishing(data);
        var rod = engine.GetBaitItem(out _);
        return rod?.bait ?? 0;
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
        for (int i = 0; i < chest.item.Length; i++)
        {
            var foundItem = chest.item[i];
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
    private int UesdItem()
    {

        // 检查并消耗宝匣药水
        var CratePotionSlot = data.CratePotionSlot;
        var CratePotionTime = data.CratePotionTime;
        ConsumeItem(ItemID.CratePotion, ref CratePotionSlot, ref CratePotionTime, 4, Lang.GetItemName(ItemID.CratePotion));
        data.CratePotionSlot = CratePotionSlot;
        data.CratePotionTime = CratePotionTime;

        // 检查并消耗钓鱼药水
        var FishingPotionSlot = data.FishingPotionSlot;
        var FishingPotionTime = data.FishingPotionTime;
        ConsumeItem(ItemID.FishingPotion, ref FishingPotionSlot, ref FishingPotionTime, 8, Lang.GetItemName(ItemID.FishingPotion));
        data.FishingPotionSlot = FishingPotionSlot;
        data.FishingPotionTime = FishingPotionTime;

        // 检查并消耗鱼饵桶
        var ChumBucketSlot = data.ChumBucketSlot;
        var ChumBucketTime = data.ChumBucketTime;
        ConsumeItem(ItemID.ChumBucket, ref ChumBucketSlot, ref ChumBucketTime, 10, Lang.GetItemName(ItemID.ChumBucket));
        data.ChumBucketSlot = ChumBucketSlot;
        data.ChumBucketTime = ChumBucketTime;

        // 在渔力计算之前，处理自定义消耗物品
        int UsedBonus = 0;
        if (Config.RegionBuffEnabled && Config.CustomUsedItem.Count > 0)
            foreach (var used in Config.CustomUsedItem)
            {
                int slot = data.CustomConsumables.TryGetValue(used.ItemType, out var state) ? state.Slot : -1;
                DateTime expiry = data.CustomConsumables.TryGetValue(used.ItemType, out state) ? state.Expiry : DateTime.MinValue;
                int bonus = 0;
                UsedCustomItem(used, ref slot, ref expiry, ref bonus);
                if (bonus > 0)
                    UsedBonus += bonus;

                // 更新缓存
                if (slot != -2)
                    data.CustomConsumables[used.ItemType] = (slot, expiry, bonus);
                else if (data.CustomConsumables.ContainsKey(used.ItemType))
                    data.CustomConsumables.Remove(used.ItemType);
            }

        return UsedBonus;
    }
    #endregion

    #region 查找消耗物品（只找一次,找不到就罢工）
    private void ConsumeItem(int type, ref int slot, ref DateTime Time, int Min, LocalizedText name)
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
                return;
            }

            // 缓存失效，清除
            slot = -1;
        }

        if (slot == -1)
        {
            // 重新查找
            for (int i = 0; i < chest.item.Length; i++)
            {
                var item = chest.item[i];
                if (item != null && !item.IsAir && item.type == type)
                {
                    item.stack--;
                    if (item.stack <= 0)
                        item.TurnToAir();
                    else
                        slot = i;  // 记录槽位
                    NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
                    Time = DateTime.UtcNow.AddMinutes(Min);
                    foreach (var plr in data.RegionPlayers)
                        plr.SendMessage(TextGradient($"已经使用{name.ToString()},钓鱼机获得{Min}分钟加成"), color2);
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
        if (DateTime.UtcNow < expiry || slot == 2)
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
                            plr.SendMessage($"钓鱼机已使用{ItemIcon(UsedItem.ItemType)}," +
                                            TextGradient($"区域获得{UsedItem.Minutes}分钟:\n" +
                                            $"[c/5F9DB8:-] {UsedItem.BuffDesc}"), color);
                        }
                }

                return;
            }
            // 缓存失效，清除
            slot = -1;
        }

        // 重新查找
        for (int i = 0; i < chest.item.Length; i++)
        {
            var item = chest.item[i];
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
                            plr.SendMessage($"钓鱼机已使用{ItemIcon(UsedItem.ItemType)}," +
                                            TextGradient($"区域获得{UsedItem.Minutes}分钟:\n" +
                                            $"[c/5F9DB8:-] {UsedItem.BuffDesc}"), color);
                        }
                }
                return;
            }
        }

        // 没找到，标记为永久缺失
        slot = -2;
    }
    #endregion

    #region 获取鱼饵数量
    public static int GetBaitCount(int c)
    {
        int count = 0;
        if (c != -1)
        {
            var chest = Main.chest[c];
            if (chest == null) return count;

            for (int s = 0; s < chest.item.Length; s++)
            {
                var item = chest.item[s];
                if (item != null && !item.IsAir && item.bait > 0)
                    count += item.stack;
            }
            return count;
        }
        return count;
    }
    #endregion

    #region 消耗鱼饵
    private bool ConsumeBait(int slot, Item baitItem, int power)
    {
        if (baitItem == null || baitItem.IsAir) return false;

        float chance = 1f / (1f + power / 6f);
        if (data.HasTackle) chance *= 0.8f;

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

                if (data.RegionPlayers.Count == 0) continue;

                bool inLava = data.LavCnt >= data.MaxLiq, inHoney = data.HonCnt >= data.MaxLiq;
                if (inLava || inHoney) continue;

                Vector2 spawnPos = new Vector2(data.LiquidPos.X * 16 + 8, data.LiquidPos.Y * 16 + 8);
                if (IsMonsterSolo(spawnPos, rule.NPCType, out bool dup)) continue;

                int npcIndex = NPC.NewNPC(null, (int)spawnPos.X, (int)spawnPos.Y, rule.NPCType);
                if (npcIndex >= 0)
                {
                    var npc = Main.npc[npcIndex];
                    npc.active = true;
                    npc.netUpdate = true;
                    NetMessage.SendData((int)PacketTypes.NpcUpdate, -1, -1, null, npcIndex);
                    foreach (var plr in data.RegionPlayers)
                        plr.SendMessage($"钓鱼机钓到了 {Lang.GetNPCNameValue(rule.NPCType)}", color2);
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
                foreach (var plr in data.RegionPlayers)
                    plr.SendMessage($"钓鱼机钓到了 {ItemIcon(custom.type)}", color2);
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

        int maxLiq = data.MaxLiq, water = data.WatCnt, lava = data.LavCnt, honey = data.HonCnt;
        if (Main.notTheBeesWorld && Main.rand.Next(2) == 0) honey = 0;

        float atmo = data.atmo;
        int waterNeeded = (int)(300f * atmo);
        float waterQuality = Math.Min(1f, (float)maxLiq / waterNeeded);
        if (waterQuality < 1f)
            fishingPower = (int)(fishingPower * waterQuality);

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

    #region 物品存入箱子
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
                        if (data.RegionPlayers.Count > 0)
                        {
                            SpawnFakeFish(item);
                            Transfer(from, pos.X, pos.Y, idx, s, item);
                            ChestGlow(item, pos.X, pos.Y);
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
                if (data.RegionPlayers.Count > 0)
                {
                    SpawnFakeFish(item);
                    Transfer(from, pos.X, pos.Y, idx, firstEmpty, item);
                    ChestGlow(item, pos.X, pos.Y);
                }
                return;
            }

            // 箱子已满，掉落在地上
            int dropX = data.Pos.X * 16 + 8;
            int dropY = data.Pos.Y * 16 + 8;
            int index = Item.NewItem(null, new Vector2(dropX, dropY), Vector2.Zero, item.type, item.stack);
            TSPlayer.All.SendData(PacketTypes.UpdateItemDrop, null, index);
        }
    }
    #endregion

    #region 粒子动画方法
    // 水波与假鱼动画
    private void SpawnFakeFish(Item fish)
    {
        Vector2 from = new Vector2(data.LiquidPos.X * 16 + 8, data.LiquidPos.Y * 16 + 8);

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
            var GlowSettings = new ParticleOrchestraSettings{ PositionInWorld = from + offset, MovementVector = new Vector2(0f, -1f), UniqueInfoPiece = fish.type};
            ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.LakeSparkle, GlowSettings);

            // 假鱼
            ParticleOrchestraType FishType = isJump ? ParticleOrchestraType.FakeFishJump : ParticleOrchestraType.FakeFish;
            var fishSttring = new ParticleOrchestraSettings{ PositionInWorld = from + offset,MovementVector = vel,UniqueInfoPiece = fish.type };
            ParticleOrchestrator.BroadcastOrRequestParticleSpawn(FishType, fishSttring);
        }
    }

    // 物品转移箱子动画
    private void Transfer(Vector2 from, int x, int y, int index, int slot, Item item)
    {
        NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, index, slot);

        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        Chest.VisualizeChestTransfer(from, to, item.type, Chest.ItemTransferVisualizationSettings.Hopper);
    }

    // 箱子闪光动画 （粒子自带音效）
    private void ChestGlow(Item item, int x, int y)
    {
        Vector2 from = new Vector2(data.LiquidPos.X * 16 + 8, data.LiquidPos.Y * 16 + 8);
        Vector2 to = new(x * 16 + 8, y * 16 + 8);
        var setting = new ParticleOrchestraSettings { PositionInWorld = to, MovementVector = Vector2.Zero, UniqueInfoPiece = item.type };
        ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.RainbowBoulder4, setting);
    }
    #endregion

    #region 整理箱子
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
                int newStack = item.stack;
                if (itemCounts.ContainsKey(type))
                    newStack += itemCounts[type];
                itemCounts[type] = newStack;
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

                if (slot >= chest.item.Length) break;
            }
            if (slot >= chest.item.Length) break;
        }

        for (int i = chest.item.Length - 1; i > chest.item.Length; i--)
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

        int crateRate = 10;
        if (hasCratePotion) crateRate += Config.CratePotionBonus;

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