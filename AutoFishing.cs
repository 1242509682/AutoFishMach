using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Drawing;
using Terraria.GameContent.FishDropRules;
using Terraria.ID;
using TShockAPI;
using ZstdSharp.Unsafe;
using static FishMach.MachData;
using static FishMach.Plugin;
using static FishMach.Utils;

namespace FishMach;

public class AutoFishing
{
    private readonly MachData data;

    private static readonly Item[] coin = new Item[4];

    static AutoFishing()
    {
        coin[0] = ContentSamples.ItemsByType[ItemID.CopperCoin];
        coin[1] = ContentSamples.ItemsByType[ItemID.SilverCoin];
        coin[2] = ContentSamples.ItemsByType[ItemID.GoldCoin];
        coin[3] = ContentSamples.ItemsByType[ItemID.PlatinumCoin];
    }

    public AutoFishing(MachData data)
    {
        this.data = data;
    }

    #region 钓鱼核心逻辑
    public void Execute()
    {
        // 快速跳过：无人/永久缺鱼竿鱼饵/液体已死
        if ((Config.WhenEmpty && data.Players.Count == 0) ||
            data.RodSlot == -2 || data.BaitSlot == -2 || data.LiqDead)
            return;

        // 1. 液体检测（仅液体未死时执行，内部有快速检查避免全量遍历)
        EnvManager.SyncLiquid(data);

        // 2. 液体更新后仍不满足液体条件，直接返回，并标记液体已死
        if (data.MaxLiq == 0 || data.MaxLiq < Config.NeedLiqStack)
        {
            if (!data.LiquidText)
            {
                data.LiquidText = true;
                data.LiqDead = true; // 标记液体已死，停止后续检测
                var text = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 鱼池[c/FF716D:液体异常]\n";
                TSPlayer.All.SendMessage(Grad(text), color);

                // 清空动画队列，避免播放过时动画
                data.ClearAnim();
            }
            return;
        }
        data.LiquidText = false;
        data.LiqDead = false; // 液体充足时清除死亡标记

        // 岩浆特殊处理
        if (data.LiqType == LiquidID.Lava && !data.CanFishInLava)
        {
            if (!data.LavaText)
            {
                data.LavaText = true;
                TSPlayer.All.SendMessage(Grad($"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 鱼池为岩浆，缺少岩浆用品\n"), color);
                data.ClearAnim();  // 清空动画队列，避免播放过时动画
            }
            return;
        }
        data.LavaText = false;

        // 3. 鱼竿
        if (!FindRod(out Item rodItem, out int rodSlot))
        {
            // 仅在未播报过时播报一次
            if (!data.RodText)
            {
                data.RodText = true;
                var text = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 未找到[c/FF716D:鱼竿]\n";
                TSPlayer.All.SendMessage(Grad(text), color);
                data.ClearAnim();  // 清空动画队列，避免播放过时动画
            }
            return;
        }
        data.RodText = false;

        // 4. 鱼饵
        if (!FindBait(out Item baitItem, out int baitSlot))
        {
            if (!data.BaitText)
            {
                data.BaitText = true;
                var text = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 未找到[c/FF716D:鱼饵]\n";
                TSPlayer.All.SendMessage(Grad(text), color);
                data.ClearAnim();  // 清空动画队列，避免播放过时动画
            }
            return;
        }
        data.BaitText = false;

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
        ConsumeBait(baitSlot, baitItem, power);

        // 8. 自定义渔获
        bool allow = false;
        if (Config.CustomFishes.Any())
            CustomFishes(rodItem, ref allow);
        if (allow) return;

        // 9. 原版渔获
        var context = BuildFishingContext(power, rodItem, baitItem);
        int itemType = RuleList.TryGetItemDropType(context);
        if (itemType == 0 || data.Exclude.Contains(itemType)) return;

        Item fish = ContentSamples.ItemsByType[itemType].Clone();
        fish.stack = 1;

        // 10. 放入箱子
        FishPutToAnim(fish);
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
        if (DateTime.UtcNow < Time || slot == -2) return;

        if (TakeOne(type, ref slot))
        {
            Time = DateTime.UtcNow.AddMinutes(Min);
            if (data.Players.Count > 0 && !string.IsNullOrEmpty(info))
            {
                foreach (var plr in data.Players)
                    plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{Icon(type)}" +
                                    Grad($"获得{Min}分钟{info}"), color2);
            }
        }
    }
    #endregion

    #region 消耗自定义物品方法(用于区域buff)
    private void UsedCustomItem(CustomUsedItems UsedItem, ref int slot, ref DateTime expiry, ref int bonus)
    {
        if (DateTime.UtcNow < expiry || slot == -2) return;

        if (TakeOne(UsedItem.ItemType, ref slot))
        {
            expiry = DateTime.UtcNow.AddMinutes(UsedItem.Minutes);
            bonus = UsedItem.Power;

            if (UsedItem.BuffID > 0)
            {
                data.ActiveZoneBuffs[UsedItem.BuffID] = expiry;
                if (data.Players.Count > 0)
                {
                    foreach (var plr in data.Players)
                    {
                        plr.SetBuff(UsedItem.BuffID, 300);
                        plr.SendMessage($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已使用{Icon(UsedItem.ItemType)}" +
                                        Grad($"持续{UsedItem.Minutes}分钟:\n" +
                                        Grad($"[c/5F9DB8:-] {UsedItem.BuffDesc}")), color);
                    }
                }
            }
        }
    }
    #endregion

    #region 从箱子中消耗一个指定类型的物品，返回是否成功
    private bool TakeOne(int itemType, ref int slot)
    {
        if (slot == -2) return false;

        var chest = Main.chest[data.ChestIndex];
        if (chest == null) return false;

        // 缓存槽位有效
        if (slot >= 0)
        {
            var item = chest.item[slot];
            if (item != null && !item.IsAir && item.type == itemType)
            {
                int idx = slot;
                item.stack--;
                if (item.stack <= 0)
                {
                    item.TurnToAir();
                    slot = -1;
                }
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, idx);
                return true;
            }
            slot = -1;
        }

        // 重新查找
        var items = chest.item.AsSpan();
        for (int i = 0; i < items.Length; i++)
        {
            ref var it = ref items[i];
            if (it != null && !it.IsAir && it.type == itemType)
            {
                it.stack--;
                if (it.stack <= 0)
                {
                    it.TurnToAir();
                    slot = -1;
                }
                else
                {
                    slot = i;
                }
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
                return true;
            }
        }

        slot = -2;
        return false;
    }
    #endregion

    #region 消耗鱼饵
    private void ConsumeBait(int slot, Item baitItem, int power)
    {
        if (baitItem == null || baitItem.IsAir) return;

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
        }
    }
    #endregion

    #region 自定义渔获
    private void CustomFishes(Item rodItem, ref bool allow)
    {
        var plr = EnvManager.SetPlayer(data, true);

        foreach (var rule in Config.CustomFishes)
        {
            if (rule.Cond.Count > 0 && !CheckConds(rule.Cond, plr))
                continue;

            int chance = rule.Chance;
            if (rodItem.type == ItemID.BloodFishingRod)
                chance = (int)MathF.Max(1, chance / 2);

            if (Main.rand.Next(chance) != 0)
                continue;

            if (rule.NPCType > 0)
            {
                if (!data.CustomNPC || data.Players.Count == 0) continue;

                if (Config.RegionSafe && Config.AutoOffSafe && data.Safe)
                {
                    data.Safe = false;
                    DataManager.Save(data);
                    foreach (var tsplr in data.Players)
                        tsplr.SendMessage(Grad($"怪物防护已自动[c/FF716D:关闭]"), color2);

                    continue;
                }

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

                    if (data.Players.Count > 0)
                        foreach (var tsplr in data.Players)
                            tsplr.SendMessage(Grad($"钓到了:" +
                                                          $"{Lang.GetNPCNameValue(rule.NPCType)}"), color2);

                    data.Monsters[rule.NPCType] = data.Monsters.GetValueOrDefault(rule.NPCType) + 1;
                }
                allow = true;
                return;
            }
            else if (rule.ItemType > 0)
            {
                if (data.Exclude.Contains(rule.ItemType)) continue;
                var custom = ContentSamples.ItemsByType[rule.ItemType].Clone();
                custom.stack = 1;
                FishPutToAnim(custom);
                allow = true;
            }
        }
    }
    #endregion

    #region 构建钓鱼信息上下文
    private FishingContext BuildFishingContext(int fishingPower, Item rodItem, Item baitItem)
    {
        var plr = EnvManager.SetPlayer(data);

        int heightLevel = data.HeightLevel;
        if (Main.remixWorld && heightLevel == 2 && Main.rand.Next(2) == 0)
            heightLevel = 1;

        bool corruption = plr.ZoneCorrupt;
        bool crimson = plr.ZoneCrimson;
        bool jungle = plr.ZoneJungle;
        bool snow = plr.ZoneSnow;
        bool hallow = plr.ZoneHallow;
        bool desert = plr.ZoneDesert;
        bool beach = plr.ZoneBeach;
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

        float luck = plr.luck;
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
        if (data.QuestFish && NPC.AnyNPCs(NPCID.Angler) && !Main.anglerQuestFinished)
            questFish = Main.anglerQuestItemNetIDs[Main.anglerQuest];

        bool canFishInLava = data.CanFishInLava ||
                             ItemID.Sets.CanFishInLava[rodItem.type] ||
                             ItemID.Sets.IsLavaBait[baitItem.type];

        var fc = new FishingContext
        {
            Random = Main.rand,
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

    #region 动画执行队列方法
    private void AddMove(Item item, Vector2 from, Point toPos, bool skipFake)
    {
        bool wasEmpty = data.AnimQueue.Count == 0 && !Plugin.ActiveAnim.Contains(data);
        data.AnimQueue.Enqueue(new AnimReq
        {
            Type = AnimType.Move,
            item = item,
            from = from,
            toPos = toPos,
            skipFake = skipFake,
            data = this.data
        });
        if (wasEmpty) Plugin.ActiveAnim.Add(data);
        if (data.AnimFrame == 0)
            data.AnimFrame = Plugin.Timer + 30;
    }

    private void AddSparkle(Point pos)
    {
        bool wasEmpty = data.AnimQueue.Count == 0 && !Plugin.ActiveAnim.Contains(data);
        data.AnimQueue.Enqueue(new AnimReq
        {
            Type = AnimType.Sparkle,
            toPos = pos,
            data = this.data
        });
        if (wasEmpty) Plugin.ActiveAnim.Add(data);
        if (data.AnimFrame == 0)
            data.AnimFrame = Plugin.Timer + 30;
    }

    private void AddTransfer(Item item, Vector2 from, Point toPos, int chestIdx, bool skipFake)
    {
        bool wasEmpty = data.AnimQueue.Count == 0 && !Plugin.ActiveAnim.Contains(data);
        data.AnimQueue.Enqueue(new AnimReq
        {
            Type = AnimType.Transfer,
            item = item,
            from = from,
            toPos = toPos,
            chestIdx = chestIdx,
            skipFake = skipFake,
            data = this.data
        });
        if (wasEmpty) Plugin.ActiveAnim.Add(data);
        if (data.AnimFrame == 0)
            data.AnimFrame = Plugin.Timer + 30;
    }

    public void PlayMove(Item item, Vector2 from, Point toPos, bool skipFake)
    {
        Vector2 to = new Vector2(toPos.X * 16 + 8, toPos.Y * 16 + 8);
        if (!skipFake && Config.FakeFish)
            SpawnFakeFish(item, from);
        if (Config.ChestTransfer)
            Chest.VisualizeChestTransfer(from, to, item.type, Chest.ItemTransferVisualizationSettings.Hopper);
    }

    public void PlaySparkle(Point pos)
    {
        if (!Config.Sparkle) return;
        Vector2 worldPos = new Vector2(pos.X * 16 + 8, pos.Y * 16 + 8);
        var setting = new ParticleOrchestraSettings { PositionInWorld = worldPos, MovementVector = Vector2.Zero };
        ParticleOrchestrator.BroadcastOrRequestParticleSpawn(ParticleOrchestraType.RainbowBoulder4, setting);
    }
    #endregion

    #region 生成假鱼方法
    private void SpawnFakeFish(Item fish, Vector2 from)
    {
        int FakeFishCount = Main.rand.Next(2, 5);
        for (int i = 0; i < FakeFishCount; i++)
        {
            Vector2 offset = RandomOffset(12, 6);
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
        if (!data.CustomNPC ||
            !data.SoloMonster) return false;

        if (!data.SoloMode)
        {
            // 不同类各一个，检查是否有任何自定义怪物存在
            if (data.Monsters.TryGetValue(npcType, out int cnt) && cnt > 0)
                return true;
        }
        else
        {
            // 仅单挑，检查所有怪物数量
            if (data.Monsters.Count > 0)
                return true;
        }
        return false;
    }
    #endregion

    #region 非转移物品判断
    public static bool SafeItem(Item item, MachData data)
    {
        // 空物品，安全跳过
        if (item == null || item.IsAir) return true;
        // 如果是排除表内物品,则不转移
        if (data.Exclude.Contains(item.type)) return true;
        if (data.SafeTypes.Contains(item.type)) return true;
        // 如果配置禁止转移钱币，则钱币视为安全物品（留在主箱）
        if (!Config.TransferCoins && item.IsACoin) return true;
        if (item.fishingPole > 0) return true;
        if (item.bait > 0) return true;
        return false;
    }
    #endregion

    #region 重建非转移物品缓存(在SyncItem方法调用,由玩家放入箱子物品时更新)
    public static void UpdateSafeItem(MachData data)
    {
        data.SafeTypes.Clear();
        foreach (var kv in Config.CustomPowerItems)
            data.SafeTypes.Add(kv.Key);
        foreach (var used in Config.CustomUsedItem)
            data.SafeTypes.Add(used.ItemType);
        data.SafeTypes.Add(ItemID.CratePotion);
        data.SafeTypes.Add(ItemID.FishingPotion);
        data.SafeTypes.Add(ItemID.ChumBucket);
        data.SafeTypes.Add(ItemID.LavaFishingHook);
        data.SafeTypes.Add(ItemID.LavaproofTackleBag);
        data.SafeTypes.Add(ItemID.HotlineFishingHook);
        data.SafeTypes.Add(ItemID.TackleBox);
        data.SafeTypes.Add(ItemID.AnglerTackleBag);
    }
    #endregion

    #region 钓鱼产出物品存入动画排序
    private void FishPutToAnim(Item fish)
    {
        int index = -1;

        // 1. 传输模式（多输出箱）
        if (data.HasOut)
        {
            foreach (int outIdx in data.OutChests)
            {
                var outChest = Main.chest[outIdx];
                if (outChest != null && CanPut(outChest, fish))
                {
                    index = outIdx;
                    break;
                }
            }
        }

        // 2. 主箱子
        if (index == -1)
        {
            var mainChest = Main.chest[data.ChestIndex];
            if (mainChest != null && CanPut(mainChest, fish))
                index = data.ChestIndex;
        }

        // 3. 区域内其他箱子（仅从缓存读取，缓存不存在则重建后重试）
        if (index == -1)
        {
            if (!DataManager.RegionChests.TryGetValue(data.RegName, out var chests))
            {
                DataManager.UpdateRegionChests(data);
                DataManager.RegionChests.TryGetValue(data.RegName, out chests);
            }

            if (chests != null)
            {
                foreach (int i in chests)
                {
                    if (i == data.ChestIndex) continue;
                    if (data.OutChests.Contains(i)) continue;
                    var other = Main.chest[i];
                    if (other != null && CanPut(other, fish))
                    {
                        index = i;
                        break;
                    }
                }
            }
        }

        if (index >= 0)
        {
            var chest = Main.chest[index];
            if (chest != null)
            {
                // 添加飞行动画（假鱼 + 物品移动）
                Vector2 from = new Vector2(data.LiqPos.X * 16 + 8, data.LiqPos.Y * 16 + 8);
                AddTransfer(fish, from, new Point(chest.x, chest.y), chest.index, skipFake: false);
                AddSparkle(new Point(chest.x, chest.y));
            }
        }
        else
        {
            // 没有箱子能放，直接掉落地面（无动画）
            int dropX = data.Pos.X * 16 + 8;
            int dropY = data.Pos.Y * 16 + 8;
            int idx = Item.NewItem(null, new Vector2(dropX, dropY), Vector2.Zero, fish.type);
            NetMessage.SendData((int)PacketTypes.UpdateItemDrop, -1, -1, null, idx);
            Pick.Add(new PickItem() { idx = idx, Type = fish.type }); // 添加到拾取表
        }

        // 触发批量转移
        var TransferInterval = Config.TransferInterval > 0 ? Config.TransferInterval : 5;
        if (data.HasOut && !data.NeedPut && Plugin.Timer - data.LastPutFrame > TransferInterval * 60)
        {
            data.NeedPut = true;
            PutQueue.Enqueue(data);
            data.LastPutFrame = Plugin.Timer;
        }
    }
    #endregion

    #region 判断是否可以存入 以便于产生动画
    private static bool CanPut(Chest chest, Item item)
    {
        if (chest == null) return false;

        // 钱币特殊处理（总是允许，因为会自动兑换）
        if (Config.TransferCoins && item.IsACoin) return true;

        var items = chest.item.AsSpan();
        int need = item.stack;

        for (int i = 0; i < items.Length; i++)
        {
            ref var slot = ref items[i];
            if (!slot.IsAir && slot.type == item.type && slot.stack < slot.maxStack)
            {
                int canTake = slot.maxStack - slot.stack;
                if (canTake >= need) return true;
                need -= canTake;
                if (need <= 0) return true;
            }
        }

        // 检查空位
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].IsAir)
            {
                need -= item.maxStack;
                if (need <= 0) return true;
            }
        }

        return false;
    }
    #endregion

    #region 处理主箱物品方法（预估可转移后再清理）
    public static void TransferItem(MachData data)
    {
        if (!data.HasOut) return;
        if (data.ChestIndex == -1) return;

        var mainChest = Main.chest[data.ChestIndex];
        if (mainChest == null) return;

        var movedMap = new Dictionary<int, int>();

        var items = mainChest.item.AsSpan();
        for (int i = 0; i < items.Length; i++)
        {
            ref var it = ref items[i];
            if (it == null || it.IsAir) continue;
            if (SafeItem(it, data)) continue;

            int oldType = it.type;
            int oldStack = it.stack;
            bool isCoin = it.IsACoin;  // 提前保存

            // 调用通用放置逻辑，不允许回退到主箱
            PutWithFallback(it, -1, data, false);

            int moved = oldStack - it.stack;
            if (moved > 0)
            {
                // 更新主箱槽位（因为物品已从主箱移除或减少）
                if (it.stack <= 0)
                    it.TurnToAir();
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, data.ChestIndex, i);

                if (!isCoin) // 排除钱币
                {
                    movedMap.TryGetValue(oldType, out int cnt);
                    movedMap[oldType] = cnt + moved;
                }
            }
        }

        // 批量播报
        if (Config.ShowTransferMsg && data.Players.Count > 0 && movedMap.Count > 0)
        {
            string msg = Grad("已转移:") + string.Join(" ", movedMap.Select(kv => Icon(kv.Key, kv.Value)));
            foreach (var plr in data.Players)
                plr.SendMessage(msg, color);
        }
    }
    #endregion

    #region 将物品存入箱子（钓鱼产出）
    /// <summary>
    /// 通用物品放置方法，按优先级尝试：指定箱 → 输出箱 → 主箱（可选） → 区域其他箱 → 地面。
    /// 会尽可能将物品全部分散到多个箱子，直到全部转移或无处可放。
    /// </summary>
    /// <param name="item">物品（会被修改，转移后 stack 会减少）</param>
    /// <param name="preferIdx">优先尝试的箱子索引，-1 表示不优先</param>
    /// <param name="data">钓鱼机数据</param>
    /// <param name="allowMain">是否允许回退到主箱</param>
    /// <returns>最后一次成功放入的箱子索引，-2 表示掉落地面，-3 表示未做任何操作（物品为空）</returns>
    public static int PutWithFallback(Item item, int preferIdx, MachData data, bool allowMain)
    {
        if (item == null || item.IsAir) return -3;

        int lastTarget = -2;

        // 1. 优先箱
        if (preferIdx != -1)
        {
            var prefer = Main.chest[preferIdx];
            if (prefer != null && TryPutIntoChest(prefer, item))
            {
                lastTarget = preferIdx;
                if (item.stack == 0) return lastTarget;
            }
        }

        // 2. 输出箱（循环所有输出箱，直到物品全部分配或没有更多输出箱）
        if (data.HasOut)
        {
            foreach (int outIdx in data.OutChests)
            {
                if (outIdx == preferIdx) continue;
                var outChest = Main.chest[outIdx];
                if (outChest == null) continue;
                if (TryPutIntoChest(outChest, item))
                {
                    lastTarget = outIdx;
                    if (item.stack == 0) return lastTarget;
                }
            }
        }

        // 3. 主箱（若允许）
        if (allowMain)
        {
            var mainChest = Main.chest[data.ChestIndex];
            if (mainChest != null && TryPutIntoChest(mainChest, item))
            {
                lastTarget = data.ChestIndex;
                if (item.stack == 0) return lastTarget;
            }
        }

        // 4. 如果区域内箱子缓存不存在 重建后尝试
        if (!DataManager.RegionChests.TryGetValue(data.RegName, out var chestSet))
        {
            DataManager.UpdateRegionChests(data);
            DataManager.RegionChests.TryGetValue(data.RegName, out chestSet);
        }

        // 区域内其他箱子（排除主箱和输出箱）
        if (chestSet != null)
        {
            foreach (int i in chestSet)
            {
                if (i == data.ChestIndex) continue;
                if (data.OutChests.Contains(i)) continue;
                var other = Main.chest[i];
                if (other == null) continue;
                if (TryPutIntoChest(other, item))
                {
                    lastTarget = i;
                    if (item.stack == 0) return lastTarget;
                }
            }
        }

        // 5. 最终掉落地面（仅当物品还有剩余时）
        if (item.stack > 0)
        {
            int dropX = data.Pos.X * 16 + 8;
            int dropY = data.Pos.Y * 16 + 8;
            var idx = Item.NewItem(null, new Vector2(dropX, dropY), Vector2.Zero, item.type, item.stack);
            NetMessage.SendData((int)PacketTypes.UpdateItemDrop, -1, -1, null, idx);
            Pick.Add(new PickItem() { idx = idx, Type = item.type }); // 添加到拾取表
            item.TurnToAir();
            lastTarget = -2;
        }

        return lastTarget;
    }
    #endregion

    #region 物品放入箱子方法
    public static bool TryPutIntoChest(Chest chest, Item item)
    {
        if (item == null || item.IsAir) return true;

        // 钱币特殊处理（自动兑换）
        if (Config.TransferCoins && item.IsACoin)
            return TryPutCoin(chest, item);

        var items = chest.item.AsSpan();
        int orig = item.stack; // 原始数量
        int remain = orig; // 剩余待转移数量
        Span<int> free = stackalloc int[items.Length];
        int freeCnt = 0;

        for (int i = 0; i < items.Length && remain > 0; i++)
        {
            ref var slot = ref items[i];
            if (!slot.IsAir && slot.type == item.type && slot.stack < slot.maxStack)
            {
                int canTake = slot.maxStack - slot.stack;
                int take = Math.Min(remain, canTake);
                slot.stack += take;
                remain -= take;
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
            }
            else if (slot.IsAir)
            {
                free[freeCnt++] = i;
            }
        }

        if (remain > 0 && freeCnt > 0)
        {
            int slotIdx = 0;
            while (remain > 0 && slotIdx < freeCnt)
            {
                int take = Math.Min(remain, item.maxStack);
                int slot = free[slotIdx++];
                items[slot] = item.Clone();
                items[slot].stack = take;
                items[slot].prefix = 0;   // 重置前缀
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
                remain -= take;
            }
        }

        // 更新原物品
        bool success = remain < orig;
        item.stack = remain;
        if (item.stack == 0) item.TurnToAir();
        return success;
    }
    #endregion

    #region 转移钱币方法
    private static bool TryPutCoin(Chest chest, Item coin)
    {
        if (!coin.IsACoin) return false;

        Span<Item> items = chest.item.AsSpan();
        long total = 0;
        int free = 0;          // 空位 + 钱币格子（因为之后会清空）

        // 一次遍历：统计总值 + 计数可用格子
        for (int i = 0; i < items.Length; i++)
        {
            ref Item it = ref items[i];
            if (it.IsAir)
            {
                free++;
                continue;
            }
            if (it.IsACoin)
            {
                free++; // 钱币格子后续会被清空，视为可用
                total += it.type switch
                {
                    ItemID.CopperCoin => it.stack,
                    ItemID.SilverCoin => (long)it.stack * 100,
                    ItemID.GoldCoin => (long)it.stack * 10000,
                    ItemID.PlatinumCoin => (long)it.stack * 1000000,
                    _ => 0
                };
            }
        }

        // 加上新钱币
        total += coin.type switch
        {
            ItemID.CopperCoin => coin.stack,
            ItemID.SilverCoin => (long)coin.stack * 100,
            ItemID.GoldCoin => (long)coin.stack * 10000,
            ItemID.PlatinumCoin => (long)coin.stack * 1000000,
            _ => 0
        };

        // 拆分为各面额数量
        int[] cnts = Terraria.Utils.CoinsSplit(total);
        int need = 0;
        for (int i = 0; i < 4; i++)
            if (cnts[i] > 0)
                need += (cnts[i] + 9999 - 1) / 9999;

        if (free < need) return false;

        // 清空所有钱币格子（发送网络包）
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].IsACoin)
            {
                items[i].TurnToAir();
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, i);
            }
        }

        // 按目标分布填充
        int[] ids = { ItemID.CopperCoin, ItemID.SilverCoin, ItemID.GoldCoin, ItemID.PlatinumCoin };
        int slot = 0;
        for (int t = 0; t < 4; t++)
        {
            int rem = cnts[t];
            while (rem > 0)
            {
                while (slot < items.Length && !items[slot].IsAir) slot++;
                if (slot >= items.Length) return false;
                int take = Math.Min(rem, 9999);
                items[slot] = AutoFishing.coin[t].Clone(); // 替换原来的 SetDefaults
                items[slot].stack = take;
                NetMessage.SendData((int)PacketTypes.ChestItem, -1, -1, null, chest.index, slot);
                rem -= take;
                slot++;
            }
        }

        coin.TurnToAir();
        return true;
    }
    #endregion
}