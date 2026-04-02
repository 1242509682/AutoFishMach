using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using static FishMach.Utils;
using static FishMach.Plugin;
using static FishMach.DataManager;

namespace FishMach;

public static class EnvManager
{
    #region 更新钓鱼机的所有物品相关缓存（渔力加成、鱼竿、鱼饵、特殊道具）仅从主箱子读取
    private static int[] lavaItems = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
    public static void SyncItem(MachData data)
    {
        data.ExtraPower = 0;
        data.CanFishInLava = false;
        data.HasTackle = false;

        data.RodSlot = -1;
        data.BaitSlot = -1;
        data.CratePotionSlot = -1;
        data.FishingPotionSlot = -1;
        data.ChumBucketSlot = -1;

        var chest = Main.chest[data.ChestIndex];
        if (chest == null) return;
        var items = chest.item.AsSpan();
        for (int i = 0; i < items.Length; i++)
        {
            // 只检查不修改 不需要用 ref
            Item item = items[i];
            if (item == null || item.IsAir) continue;

            if (data.RodSlot == -1 && item.fishingPole > 0)
                data.RodSlot = i;

            if (data.BaitSlot == -1 && item.bait > 0)
                data.BaitSlot = i;

            if (data.CratePotionSlot == -1 && item.type == ItemID.CratePotion)
                data.CratePotionSlot = i;

            if (data.FishingPotionSlot == -1 && item.type == ItemID.FishingPotion)
                data.FishingPotionSlot = i;

            if (data.ChumBucketSlot == -1 && item.type == ItemID.ChumBucket)
                data.ChumBucketSlot = i;

            if (item.type != ItemID.FishingPotion &&
                item.type != ItemID.CratePotion &&
                Config.CustomPowerItems.TryGetValue(item.type, out int power))
                data.ExtraPower += power;

            bool isLava = false;
            for (int j = 0; j < lavaItems.Length; j++)
            {
                if (lavaItems[j] == item.type)
                {
                    isLava = true;
                    break;
                }
            }
            if (isLava) data.CanFishInLava = true;

            if (item.type == ItemID.TackleBox ||
                item.type == ItemID.AnglerTackleBag ||
                item.type == ItemID.LavaproofTackleBag)
                data.HasTackle = true;
        }
    }
    #endregion

    #region 快速检查液体坐标（含快速水体大小检测）
    /// <summary>
    /// 快速检查当前锚点所在水体是否仍满足液体需求（轻量级，避免每次全量统计）
    /// 返回 true 表示液体不足，需要全量统计；false 表示液体充足，无需更新。
    /// </summary>
    public static bool FindLiq(MachData data)
    {
        int x = data.LiqPos.X, y = data.LiqPos.Y;

        // 1. 边界与基础有效性
        if (x <= 0 || x >= Main.maxTilesX || y <= 0 || y >= Main.maxTilesY) return true;

        var tile = Main.tile[x, y];
        int curr = tile.liquidType();
        int target = data.LiqType;

        // 锚点位置没有液体 或者 有实体块（防止从固体中钓鱼）
        if (tile.liquid == 0 || WorldGen.SolidTile(tile)) return true;

        // 液体类型不对
        if (target != -1 && curr != target) return true;

        // 锚点液体少于1格（液体值 < 16 代表不足一格）
        if (tile.liquid < 16) return true;

        // 2. 获取区域边界
        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null) return true;

        int minX = region.Area.X, maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y, maxY = region.Area.Y + region.Area.Height - 1;

        // 检查锚点是否在水面下1格（若深度异常则重新统计）
        int top = y;
        while (top > minY)
        {
            var upTile = Main.tile[x, top - 1];
            if (upTile.liquid == 0 || WorldGen.SolidTile(x, top - 1))
                break;
            top--;
        }
        if (Math.Abs(y - top) != 1) return true;

        int need = Config.NeedLiqStack;
        if (need <= 0)
            return false;

        // 水平扩展宽度
        int left = x, right = x;
        while (left > minX && Main.tile[left - 1, y].liquid > 0 && !WorldGen.SolidTile(left - 1, y))
            left--;
        while (right < maxX && Main.tile[right + 1, y].liquid > 0 && !WorldGen.SolidTile(right + 1, y))
            right++;
        if (right - left + 1 < 3)
            return true;

        // 向下统计液体，达到 need 即停止
        int count = 0;
        int maxDepth = Math.Min(maxY - y + 1, need);
        for (int xi = left; xi <= right && count < need; xi++)
        {
            int yi = y;
            for (int scanned = 0; scanned < maxDepth && yi <= maxY; scanned++, yi++)
            {
                var bt = Main.tile[xi, yi];
                if (bt.liquid == 0 || WorldGen.SolidTile(bt))
                    break;
                if (++count >= need)
                    return false; // 已足够
            }
        }

        return true; // 统计不足，需要更新
    }
    #endregion

    #region 统计液体
    public static void SyncLiquid(MachData data)
    {
        // 快速检查：若当前液体仍然充足，则直接返回
        if (!FindLiq(data))
            return;

        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null)
        {
            data.MaxLiq = 0;
            return;
        }

        int minX = region.Area.X, maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y, maxY = region.Area.Y + region.Area.Height - 1;

        // 统计变量
        int water = 0, lava = 0, honey = 0;
        // 为每种液体分别记录边界
        int waterMinY = int.MaxValue, waterMinX = int.MaxValue, waterMaxX = int.MinValue;
        int lavaMinY = int.MaxValue, lavaMinX = int.MaxValue, lavaMaxX = int.MinValue;
        int honeyMinY = int.MaxValue, honeyMinX = int.MaxValue, honeyMaxX = int.MinValue;

        // 一次遍历统计所有液体
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var tile = Main.tile[x, y];
                if (tile.liquid == 0 || WorldGen.SolidTile(tile)) continue;

                int type = tile.liquidType();
                switch (type)
                {
                    case LiquidID.Water:
                        water++;
                        if (y < waterMinY) waterMinY = y;
                        if (x < waterMinX) waterMinX = x;
                        if (x > waterMaxX) waterMaxX = x;
                        break;
                    case LiquidID.Lava:
                        lava++;
                        if (y < lavaMinY) lavaMinY = y;
                        if (x < lavaMinX) lavaMinX = x;
                        if (x > lavaMaxX) lavaMaxX = x;
                        break;
                    case LiquidID.Honey:
                        honey++;
                        if (y < honeyMinY) honeyMinY = y;
                        if (x < honeyMinX) honeyMinX = x;
                        if (x > honeyMaxX) honeyMaxX = x;
                        break;
                }
            }
        }

        // 确定主要液体类型
        int maxLiq = Math.Max(water, Math.Max(lava, honey));
        if (maxLiq == 0)
        {
            data.MaxLiq = 0;
            data.LiqType = -1;
            data.LiqName = "无";
            return;
        }

        // 选择主要液体对应的边界
        int liqType;
        int count;
        int minYval, minXval, maxXval;
        if (maxLiq == honey)
        {
            liqType = LiquidID.Honey;
            count = honey;
            minYval = honeyMinY;
            minXval = honeyMinX;
            maxXval = honeyMaxX;
        }
        else if (maxLiq == lava)
        {
            liqType = LiquidID.Lava;
            count = lava;
            minYval = lavaMinY;
            minXval = lavaMinX;
            maxXval = lavaMaxX;
        }
        else
        {
            liqType = LiquidID.Water;
            count = water;
            minYval = waterMinY;
            minXval = waterMinX;
            maxXval = waterMaxX;
        }

        // 计算锚点（水面中间位置，下移一格）
        int midX = (minXval + maxXval) / 2;
        Point anchor = new Point(midX, minYval + 1);

        // 检查锚点是否可用（深度正确且上方无固体）
        bool depthOk = (anchor.Y - minYval == 1) || (count == 1 && anchor.Y == minYval);
        bool topSolid = anchor.Y > 0 && WorldGen.SolidTile(Main.tile[anchor.X, anchor.Y - 1]);

        if (!depthOk || topSolid)
        {
            // 如果锚点不可用，则回退到箱子位置（并标记液体不足）
            data.MaxLiq = 0;
            data.LiqType = -1;
            data.LiqName = "无";
            data.LiqPos = data.Pos;
            data.LiquidDead = true;
            if(data.RegionPlayers.Count > 0)
                foreach (var plr in data.RegionPlayers)
                    plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 修正锚点[c/FF6552:失败]"), color);
            return;
        }

        // 更新机器数据
        data.WaterCount = water;
        data.LavaCount = lava;
        data.HoneyCount = honey;
        data.MaxLiq = maxLiq;
        data.LiqType = liqType;
        data.LiqName = liqType == LiquidID.Honey ? "蜂蜜" :
                       liqType == LiquidID.Lava ? "岩浆" : "水";
        data.LiqPos = anchor;
    }
    #endregion

    #region 验证同步方法（验证钓鱼机数据是否有效，检查玩家距离）
    private static bool ValidateSync(TSPlayer plr, MachData data, out string Msg)
    {
        Msg = string.Empty;

        // 1. 检查箱子是否存在且位置正确
        var chest = Main.chest[data.ChestIndex];
        if (chest == null || chest.x != data.Pos.X || chest.y != data.Pos.Y)
        {
            // 箱子已不存在或移动，删除区域和机器数据
            TShock.Regions.DeleteRegion(data.RegName);
            DataManager.Remove(data.Pos);
            Msg = $"钓鱼机 [c/ED756F:{data.ChestIndex}] 已不存在，已删除无效区域。";
            return false;
        }

        // 2. 检查区域是否存在，若不存在则尝试重建
        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null)
        {
            int left, top, w, h;
            if (IsOverlap(data.Pos, data.WorldId, "重建", out left, out top, out w, out h, data.RegName))
            {
                Msg = $"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域重建失败，区域重叠。";
                return false;
            }

            if (!TShock.Regions.AddRegion(left, top, w, h, data.RegName, data.Owner, data.WorldId, 0))
            {
                Msg = $"钓鱼机 [c/ED756F:{data.ChestIndex}] 区域重建失败。";
                return false;
            }
            TShock.Regions.SetRegionState(data.RegName, Config.RegionBuild);
        }

        // 3. 距离检查（使用平方距离避免 sqrt）
        int dx = plr.TileX - data.Pos.X;
        int dy = plr.TileY - data.Pos.Y;
        int rangeSq = Config.ZoneRange * Config.ZoneRange;
        if (dx * dx + dy * dy > rangeSq)
        {
            Msg = $"距离钓鱼机过远，需在 {Config.ZoneRange} 格内，请靠近后再同步。";
            return false;
        }

        return true;
    }
    #endregion

    #region 使用Sync指令立即同步（需在区域且距离足够近）
    public static bool SyncForCmd(TSPlayer plr)
    {
        // 检查玩家是否在钓鱼机区域内
        if (plr.CurrentRegion == null || !IsAfmRegion(plr.CurrentRegion.Name))
        {
            return false;
        }

        var data = DataManager.FindRegion(plr.CurrentRegion.Name);
        if (data == null)
        {
            TShock.Regions.DeleteRegion(plr.CurrentRegion.Name);
            plr.SendMessage(TextGradient($"数据丢失，已删除无效区域 {plr.CurrentRegion.Name}"), color);
            return false;
        }

        if (ValidateSync(plr, data, out string error))
        {
            UpdateData(data, plr);
            plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
            return true;
        }
        else
        {
            // 距离不足时设置同步标记，等待箱子打开
            if (error.Contains("距离"))
            {
                plr.SetData("sync", true);
                plr.SendMessage(TextGradient("请打开要同步数据的钓鱼机箱子..."), color);
            }
            else
            {
                plr.SendMessage(TextGradient(error), color);
            }
            return false;
        }
    }
    #endregion

    #region 需要打开箱子来同步，同时检查区域、箱子、数据完整性
    public static void SyncForChestOpen(TSPlayer plr, MachData? data, Point pos)
    {
        if (data == null)
        {
            // 没有钓鱼机数据，检查当前玩家所在区域是否为钓鱼机区域且数据丢失
            var region = plr.CurrentRegion;
            if (region != null && IsAfmRegion(region.Name) && FindRegion(region.Name) == null)
            {
                TShock.Regions.DeleteRegion(region.Name);
                plr.SendMessage(TextGradient($"数据丢失，已删除无效区域 {region.Name}"), color);
            }
            else
            {
                plr.SendErrorMessage("该位置没有钓鱼机");
            }
            return;
        }

        if (ValidateSync(plr, data, out string error))
        {
            UpdateData(data, plr);
            plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
            plr.SendMessage(TextGradient($"如果环境错误,请[c/75D1FF:打开一次]钓鱼箱"), color);
        }
        else
        {
            plr.SendMessage(TextGradient(error), color);
        }
    }
    #endregion

    #region 更新缓存环境（需距离检查）
    public static void SyncZone(TSPlayer plr, MachData data)
    {
        // 距离检查
        int dx = plr.TileX - data.Pos.X;
        int dy = plr.TileY - data.Pos.Y;
        int rangeSq = Config.ZoneRange * Config.ZoneRange;
        if (dx * dx + dy * dy > rangeSq) return;

        if (data.ZoneCorrupt != plr.TPlayer.ZoneCorrupt)
            data.ZoneCorrupt = plr.TPlayer.ZoneCorrupt;

        if (data.ZoneCrimson != plr.TPlayer.ZoneCrimson)
            data.ZoneCrimson = plr.TPlayer.ZoneCrimson;

        if (data.ZoneJungle != plr.TPlayer.ZoneJungle)
            data.ZoneJungle = plr.TPlayer.ZoneJungle;

        if (data.ZoneSnow != plr.TPlayer.ZoneSnow)
            data.ZoneSnow = plr.TPlayer.ZoneSnow;

        if (data.ZoneHallow != plr.TPlayer.ZoneHallow)
            data.ZoneHallow = plr.TPlayer.ZoneHallow;

        if (data.ZoneDesert != plr.TPlayer.ZoneDesert)
            data.ZoneDesert = plr.TPlayer.ZoneDesert;

        if (data.ZoneBeach != plr.TPlayer.ZoneBeach)
            data.ZoneBeach = plr.TPlayer.ZoneBeach;

        if (data.ZoneDungeon != plr.TPlayer.ZoneDungeon)
            data.ZoneDungeon = plr.TPlayer.ZoneDungeon;

        if (data.ZoneRain != plr.TPlayer.ZoneRain)
            data.ZoneRain = plr.TPlayer.ZoneRain;

        if (data.luck != plr.TPlayer.luck)
            data.luck = plr.TPlayer.luck;
    }
    #endregion

    #region 将缓存的环境赋值给假玩家
    public static void SetupTempPlayer(MachData data)
    {
        var plr = TempPlayer;
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

    #region 快速检查区域内是否有任意液体达到指定阈值，并返回达标液体的数量（创建钓鱼机时使用）
    public static int QuickLiquidCheck(Point center)
    {
        int radius = Config.Range; // 62
        int need = Config.NeedLiqStack; // 75
        int cx = center.X, cy = center.Y;

        int minX = (int)MathF.Max(cx - radius, 0);
        int maxX = (int)MathF.Min(cx + radius, Main.maxTilesX - 1);
        int minY = (int)MathF.Max(cy - radius, 0);
        int maxY = (int)MathF.Min(cy + radius, Main.maxTilesY - 1);

        // 分别追踪三种液体，一旦有任何一种达到阈值就立即返回
        int water = 0, lava = 0, honey = 0;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                ITile tile = Main.tile[x, y];
                byte liq = tile.liquid;

                if (liq > 0)
                {
                    int type = tile.liquidType();
                    switch (type)
                    {
                        case LiquidID.Water:
                            if (++water >= need) return water;
                            break;
                        case LiquidID.Lava:
                            if (++lava >= need) return lava;
                            break;
                        case LiquidID.Honey:
                            if (++honey >= need) return honey;
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
    public static float GetAtmo(int yPos)
    {
        float atmo = (yPos - atmoConst) * atmoFactor;
        return Math.Clamp(atmo, 0.25f, 1f);
    }
    #endregion

}