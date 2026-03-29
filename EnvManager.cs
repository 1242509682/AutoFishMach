using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

public static class EnvManager
{
    #region 刷新机器的环境缓存（无需玩家对象）
    public static void RefreshEnv(MachData data)
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

            // 大气因子
            data.atmo = GetAtmo(yPos);

            // 颠倒海洋
            data.RolledRemixOcean = Main.remixWorld && data.HeightLevel == 1 && yPos >= Main.rockLayer && Main.rand.Next(3) == 0;
            data.IntMach = false;
        }

        // 水体统计
        data.LiquidPos = NewGetLiquid(data, out int water, out int lava, out int honey);
        data.WatCnt = water;
        data.LavCnt = lava;
        data.HonCnt = honey;

        // 记录数量最多的水体
        data.MaxLiq = Math.Max(water, Math.Max(lava, honey));
        data.LiqName = water >= data.MaxLiq ? "水" : lava >= data.MaxLiq ? "岩浆" : honey >= data.MaxLiq ? "蜂蜜" : string.Empty;

        // 一次性刷新所有物品相关缓存
        UpdateMachineCache(data);
    }
    #endregion

    #region 更新钓鱼机的所有物品相关缓存（渔力加成、鱼竿、鱼饵、特殊道具）仅从主箱子读取
    private static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    public static void UpdateMachineCache(MachData data)
    {
        // 重置所有缓存
        data.ExtraPower = 0;
        data.CanFishInLava = false;
        data.HasTackle = false;

        // 统一：-1 = 未缓存/需查找，-2 = 确认无物品
        // 先设为 -1，扫描后会根据结果改为实际槽位或 -2
        data.RodSlot = -1;
        data.BaitSlot = -1;
        data.CratePotionSlot = -1;
        data.FishingPotionSlot = -1;
        data.ChumBucketSlot = -1;

        var chest = Main.chest[data.ChestIndex];
        for (int s = 0; s < chest.item.Length; s++)
        {
            var item = chest.item[s];
            if (item == null || item.IsAir) continue;

            // 鱼竿缓存
            if (data.RodSlot == -1 && item.fishingPole > 0)
                data.RodSlot = s;

            // 鱼饵缓存
            if (data.BaitSlot == -1 && item.bait > 0)
                data.BaitSlot = s;

            // 宝匣药水缓存
            if (data.CratePotionSlot == -1 && item.type == ItemID.CratePotion)
                data.CratePotionSlot = s;

            // 钓鱼药水缓存
            if (data.FishingPotionSlot == -1 && item.type == ItemID.FishingPotion)
                data.FishingPotionSlot = s;

            // 鱼饵桶缓存
            if (data.ChumBucketSlot == -1 && item.type == ItemID.ChumBucket)
                data.ChumBucketSlot = s;

            // 渔力加成道具（排除消耗型药水）
            if (item.type != ItemID.FishingPotion &&
                item.type != ItemID.CratePotion &&
                Config.CustomPowerItems.TryGetValue(item.type, out int power))
                data.ExtraPower += power;

            // 熔岩钓鱼道具
            if (lavaItems.Contains(item.type))
                data.CanFishInLava = true;

            // 钓具箱/渔夫渔具袋
            if (item.type == ItemID.TackleBox ||
                item.type == ItemID.AnglerTackleBag ||
                item.type == ItemID.LavaproofTackleBag)
                data.HasTackle = true;
        }
    }
    #endregion

    #region 模拟玩家并设置位置，用于环境计算
    public static void SetupTempPlayer(MachData data, Player? target = null)
    {
        var plr = target ?? TempPlayer;
        plr.position = new Vector2(data.Pos.X * 16, data.Pos.Y * 16);
        plr.UpdateBiomes();
        plr.ZoneCorrupt = data.ZoneCorrupt;
        plr.ZoneCrimson = data.ZoneCrimson;
        plr.ZoneJungle = data.ZoneJungle;
        plr.ZoneSnow = data.ZoneSnow;
        plr.ZoneHallow = data.ZoneHallow;
        plr.ZoneDesert = data.ZoneDesert;
        plr.ZoneBeach = data.ZoneBeach;
        plr.ZoneRain = data.ZoneRain;

        int hl = data.HeightLevel;
        plr.ZoneSkyHeight = hl == 0;
        plr.ZoneOverworldHeight = hl == 1;
        plr.ZoneDirtLayerHeight = hl == 2;
        plr.ZoneRockLayerHeight = hl == 3;
        plr.ZoneUnderworldHeight = hl == 4;
    }
    #endregion

    #region 统计半径内的水体，并返回最大连通水体的最近点（用于生成物品与NPC等）
    public static Point NewGetLiquid(MachData data, out int water, out int lava, out int honey)
    {
        // 获取区域边界（若区域不存在则回退到旧方法）
        var region = TShock.Regions.GetRegionByName(data.RegName);

        int minX = region.Area.X;
        int maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y;
        int maxY = region.Area.Y + region.Area.Height - 1;

        water = 0;
        lava = 0;
        honey = 0;
        Point bestWater = Point.Zero;
        Point bestLava = Point.Zero;
        Point bestHoney = Point.Zero;
        int distWaterSq = int.MaxValue;
        int distLavaSq = int.MaxValue;
        int distHoneySq = int.MaxValue;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];
                if (tile?.liquid > 0)
                {
                    if (tile.liquidType() == LiquidID.Water)
                    {
                        water++;
                        int distSq = (x - data.Pos.X) * (x - data.Pos.X) + (y - data.Pos.Y) * (y - data.Pos.Y);
                        if (distSq < distWaterSq)
                        {
                            distWaterSq = distSq;
                            bestWater = new Point(x, y + 1);
                        }
                    }
                    else if (tile.liquidType() == LiquidID.Lava)
                    {
                        lava++;
                        int distSq = (x - data.Pos.X) * (x - data.Pos.X) + (y - data.Pos.Y) * (y - data.Pos.Y);
                        if (distSq < distLavaSq)
                        {
                            distLavaSq = distSq;
                            bestLava = new Point(x, y + 1);
                        }
                    }
                    else if (tile.liquidType() == LiquidID.Honey)
                    {
                        honey++;
                        int distSq = (x - data.Pos.X) * (x - data.Pos.X) + (y - data.Pos.Y) * (y - data.Pos.Y);
                        if (distSq < distHoneySq)
                        {
                            distHoneySq = distSq;
                            bestHoney = new Point(x, y + 1);
                        }
                    }
                }
            }
        }

        // 按数量最多的液体返回坐标
        int max = Math.Max(water, Math.Max(lava, honey));
        if (max == lava) return bestLava;
        if (max == honey) return bestHoney;
        if (max == water) return bestWater;
        return Point.Zero;
    }
    #endregion

    #region 快速检查区域内是否有任意液体达到指定阈值，并返回达标液体的数量（创建钓鱼机时使用）
    public static int QuickLiquidCheck(Point center, ref string name)
    {
        name = "无";
        int radius = Config.Range;
        int total = Config.NeedLiqStack;
        int minX = Math.Max(center.X - radius, 0);
        int maxX = Math.Min(center.X + radius, Main.maxTilesX - 1);
        int minY = Math.Max(center.Y - radius, 0);
        int maxY = Math.Min(center.Y + radius, Main.maxTilesY - 1);

        int water = 0, lava = 0, honey = 0;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];
                if (tile?.liquid > 0)
                {
                    if (tile.liquidType() == LiquidID.Water)
                    {
                        water++;
                        if (water >= total)
                        {
                            name = "水";
                            return water;
                        }
                    }
                    else if (tile.liquidType() == LiquidID.Lava)
                    {
                        lava++;
                        if (lava >= total)
                        {
                            name = "岩浆";
                            return lava;
                        }
                    }
                    else if (tile.liquidType() == LiquidID.Honey)
                    {
                        honey++;
                        if (honey >= total)
                        {
                            name = "蜂蜜";
                            return honey;
                        }
                    }
                }
            }
        }

        // 没有达标，返回0
        return 0;
    }
    #endregion

    #region 计算大气因子
    private static float GetAtmo(int yPos)
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
}