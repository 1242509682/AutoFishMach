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
            UpdateItemCache(data); // 物品缓存刷新只在玩家改变箱子物品时触发
            data.IntMach = false;
        }

        // 水体统计
        data.LiquidPos = NewGetLiquid(data, out int water, out int lava, out int honey);
        data.WatCnt = water;
        data.LavCnt = lava;
        data.HonCnt = honey;

        // 记录数量最多的水体
        data.MaxLiq = (int)MathF.Max(water, (int)MathF.Max(lava, honey));
        data.LiqName = water >= data.MaxLiq ? "水" : lava >= data.MaxLiq ? "岩浆" : honey >= data.MaxLiq ? "蜂蜜" : string.Empty;
    }
    #endregion

    #region 更新钓鱼机的所有物品相关缓存（渔力加成、鱼竿、鱼饵、特殊道具）仅从主箱子读取
    private static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    public static void UpdateItemCache(MachData data)
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
        plr.luck = data.luck;

        int hl = data.HeightLevel;
        plr.ZoneSkyHeight = hl == 0;
        plr.ZoneOverworldHeight = hl == 1;
        plr.ZoneDirtLayerHeight = hl == 2;
        plr.ZoneRockLayerHeight = hl == 3;
        plr.ZoneUnderworldHeight = hl == 4;
    }
    #endregion

    #region 统计半径内液体获取液体最多的最近坐标点（用于生成物品与NPC）- 优化版
    public static Point NewGetLiquid(MachData data, out int water, out int lava, out int honey)
    {
        // 获取区域边界
        var region = TShock.Regions.GetRegionByName(data.RegName);

        int minX = region.Area.X;
        int maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y;
        int maxY = region.Area.Y + region.Area.Height - 1;

        water = 0;
        lava = 0;
        honey = 0;

        Point waterPos = Point.Zero, lavaPos = Point.Zero, honeyPos = Point.Zero;
        int waterDist = int.MaxValue, lavaDist = int.MaxValue, honeyDist = int.MaxValue;

        // 预计算中心点坐标
        int centerX = data.Pos.X;
        int centerY = data.Pos.Y;

        for (int x = minX; x <= maxX; x++)
        {
            int dx = x - centerX;
            int dxSq = dx * dx; // 预计算dx平方

            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];

                if (tile?.liquid > 0)
                {
                    int liquidType = tile.liquidType();

                    // 预计算距离平方
                    int dy = y - centerY;
                    int distSq = dxSq + dy * dy;

                    switch (liquidType)
                    {
                        case LiquidID.Water:
                            water++;
                            if (distSq < waterDist)
                            {
                                waterDist = distSq;
                                waterPos = new Point(x, y + 1);
                            }
                            break;

                        case LiquidID.Lava:
                            lava++;
                            if (distSq < lavaDist)
                            {
                                lavaDist = distSq;
                                lavaPos = new Point(x, y + 1);
                            }
                            break;

                        case LiquidID.Honey:
                            honey++;
                            if (distSq < honeyDist)
                            {
                                honeyDist = distSq;
                                honeyPos = new Point(x, y + 1);
                            }
                            break;
                    }
                }
            }
        }

        // 按数量最多的液体返回坐标
        int max = (int)MathF.Max(water, (int)MathF.Max(lava, honey));
        if (max == lava) return lavaPos;
        if (max == honey) return honeyPos;
        if (max == water) return waterPos;
        return Point.Zero;
    }
    #endregion

    #region 快速检查区域内是否有任意液体达到指定阈值，并返回达标液体的数量（创建钓鱼机时使用）
    public static int QuickLiquidCheck(Point center)
    {
        int radius = Config.Range; // 62
        int need = Config.NeedLiqStack; // 75
        int centerX = center.X;
        int centerY = center.Y;

        int minX = (int)MathF.Max(centerX - radius, 0);
        int maxX = (int)MathF.Min(centerX + radius, Main.maxTilesX - 1);
        int minY = (int)MathF.Max(centerY - radius, 0);
        int maxY = (int)MathF.Min(centerY + radius, Main.maxTilesY - 1);

        // 分别追踪三种液体，一旦有任何一种达到阈值就立即返回
        int water = 0, lava = 0, honey = 0;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];

                if (tile?.liquid > 0)
                {
                    int liquidType = tile.liquidType();

                    switch (liquidType)
                    {
                        case LiquidID.Water:
                            water++;
                            if (water >= need) return water; // 达标立即返回
                            break;

                        case LiquidID.Lava:
                            lava++;
                            if (lava >= need) return lava; // 达标立即返回
                            break;

                        case LiquidID.Honey:
                            honey++;
                            if (honey >= need) return honey; // 达标立即返回
                            break;
                    }
                }
            }
        }

        // 没有达标，返回0
        return 0;
    }
    #endregion

    #region 计算大气因子
    private static float atmoFactor;
    private static float atmoConst;
    public static void InitAtmo()
    {
        float num = (float)Main.maxTilesX / 4200f;
        num *= num;
        atmoConst = 60f + 10f * num;
        atmoFactor = (float)(6f / Main.worldSurface); // 将除法转为乘法
    }
    private static float GetAtmo(int yPos)
    {
        float atmo = (yPos - atmoConst) * atmoFactor;
        return (int)Math.Clamp(atmo, 0.25f, 1f);
    }
    #endregion
}