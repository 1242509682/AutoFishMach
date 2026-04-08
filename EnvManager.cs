using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using static FishMach.DataManager;
using static FishMach.Plugin;
using static FishMach.Utils;

namespace FishMach;

public static class EnvManager
{
    #region 更新钓鱼机的所有物品相关缓存（渔力加成、鱼竿、鱼饵、特殊道具）仅从主箱子读取
    private static HashSet<int> lavaItemsSet = [ItemID.LavaFishingHook, ItemID.LavaproofTackleBag, ItemID.HotlineFishingHook];
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

            if (lavaItemsSet.Contains(item.type))
                data.CanFishInLava = true;

            if (item.type == ItemID.TackleBox ||
                item.type == ItemID.AnglerTackleBag ||
                item.type == ItemID.LavaproofTackleBag)
                data.HasTackle = true;
        }


        // 重建非转移物品缓存
        AutoFishing.UpdateSafeItem(data);
    }
    #endregion

    #region 快速检查液体坐标（含快速水体大小检测）
    /// <summary>
    /// 快速检查当前锚点所在水体是否仍满足液体需求（轻量级，避免每次全量统计）
    /// 返回 false 表示液体不足，需要全量统计；true 表示液体充足，无需更新。
    /// </summary>
    public static bool CheckLiq(MachData data)
    {
        int x = data.LiqPos.X, y = data.LiqPos.Y;
        if (x <= 0 || x >= Main.maxTilesX || y <= 0 || y >= Main.maxTilesY) return false;

        var tile = Main.tile[x, y];
        int curr = tile.liquidType();
        int target = data.LiqType;

        if (tile.liquid == 0 || WorldGen.SolidTile(tile)) return false;
        if (target != -1 && curr != target) return false;
        if (tile.liquid < 16) return false;

        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null) return false;

        int minX = region.Area.X, maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y, maxY = region.Area.Y + region.Area.Height - 1;

        // 深度检查（保留）
        int top = y;
        while (top > minY)
        {
            var upTile = Main.tile[x, top - 1];
            if (upTile.liquid == 0 || WorldGen.SolidTile(x, top - 1))
                break;
            top--;
        }
        if (Math.Abs(y - top) != 1 && !(y == top)) return false;

        int need = Config.NeedLiqStack;
        if (need <= 0) return true;

        int left = x, right = x;
        while (left > minX && Main.tile[left - 1, y].liquid > 0 && !WorldGen.SolidTile(left - 1, y))
            left--;
        while (right < maxX && Main.tile[right + 1, y].liquid > 0 && !WorldGen.SolidTile(right + 1, y))
            right++;
        if (right - left + 1 < 3) return false;

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
                    return true;
            }
        }
        return false;
    }
    #endregion

    #region 统计液体（连通区域优先）
    public static void SyncLiquid(MachData data)
    {
        // 快速检查：如果液体严重不足，直接标记并返回
        if (CheckLiq(data)) return;

        // 清空动画队列，避免播放过时动画
        data.ClearAnim();

        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null)
        {
            data.MaxLiq = 0;
            return;
        }

        int minX = region.Area.X, maxX = region.Area.X + region.Area.Width - 1;
        int minY = region.Area.Y, maxY = region.Area.Y + region.Area.Height - 1;
        int w = maxX - minX + 1, h = maxY - minY + 1;

        int size = w * h;
        if (data.Visited == null || data.Visited.Length < size)
            data.Visited = new bool[size];
        else
            Array.Clear(data.Visited, 0, size);

        var queue = data.LiqQueue;
        queue.Clear();

        int bestTotal = 0;
        int bestWater = 0, bestLava = 0, bestHoney = 0;
        Point bestLiqPos = data.Pos;

        int[] dx = { 0, 0, -1, 1 };
        int[] dy = { -1, 1, 0, 0 };

        for (int x = 0; x < w; x++)
        {
            int wx = minX + x;
            for (int y = 0; y < h; y++)
            {
                int idx = x * h + y;
                if (data.Visited[idx]) continue;

                int wy = minY + y;
                var tile = Main.tile[wx, wy];
                if (tile.liquid == 0 || WorldGen.SolidTile(tile)) continue;

                queue.Enqueue((wx, wy));
                data.Visited[idx] = true;

                int total = 0, water = 0, lava = 0, honey = 0;
                int areaMinX = wx, areaMaxX = wx, areaMinY = wy, areaMaxY = wy;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    var ctile = Main.tile[cx, cy];
                    total++;
                    if (ctile.lava()) lava++;
                    else if (ctile.honey()) honey++;
                    else water++;

                    if (cx < areaMinX) areaMinX = cx;
                    if (cx > areaMaxX) areaMaxX = cx;
                    if (cy < areaMinY) areaMinY = cy;
                    if (cy > areaMaxY) areaMaxY = cy;

                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d];
                        int ny = cy + dy[d];
                        if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                        int ix = nx - minX, iy = ny - minY;
                        int nidx = ix * h + iy;
                        if (data.Visited[nidx]) continue;
                        var ntile = Main.tile[nx, ny];
                        if (ntile.liquid > 0 && !WorldGen.SolidTile(ntile))
                        {
                            data.Visited[nidx] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                int surfaceY = areaMinY;
                int midX = (areaMinX + areaMaxX) / 2;
                Point LiqPos = new Point(midX, surfaceY + 1);
                bool depthOk = (LiqPos.Y - surfaceY == 1) || (total == 1 && LiqPos.Y == surfaceY);
                if (!depthOk) continue;

                if (total > bestTotal)
                {
                    bestTotal = total;
                    bestWater = water;
                    bestLava = lava;
                    bestHoney = honey;
                    bestLiqPos = LiqPos;
                }
            }
        }

        if (bestTotal == 0)
        {
            data.MaxLiq = 0;
            data.LiqType = -1;
            data.LiqName = "无";
            return;
        }

        data.WaterCount = bestWater;
        data.LavaCount = bestLava;
        data.HoneyCount = bestHoney;
        data.MaxLiq = bestTotal;

        if (bestHoney == bestTotal)
        {
            data.LiqName = "蜂蜜";
            data.LiqType = LiquidID.Honey;
        }
        else if (bestLava == bestTotal)
        {
            data.LiqName = "岩浆";
            data.LiqType = LiquidID.Lava;
        }
        else
        {
            data.LiqName = "水";
            data.LiqType = LiquidID.Water;
        }

        data.LiqPos = bestLiqPos != Point.Zero ? bestLiqPos : data.Pos;
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
            Msg = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 已不存在，已删除无效区域。";
            return false;
        }

        // 2. 检查区域是否存在，若不存在则尝试重建
        var region = TShock.Regions.GetRegionByName(data.RegName);
        if (region == null)
        {
            int left, top, w, h;
            if (IsOverlap(data.Pos, data.WorldId, "重建", out left, out top, out w, out h, data.RegName))
            {
                Msg = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 区域重建失败，区域重叠。";
                return false;
            }

            if (!TShock.Regions.AddRegion(left, top, w, h, data.RegName, data.Owner, data.WorldId, 0))
            {
                Msg = $"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 区域重建失败。";
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
            Msg = $"\n距离钓鱼机过远,需在 {Config.ZoneRange} 格内，请靠近后再同步。";
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
            plr.SendMessage(TextGradient($"\n数据丢失，已删除无效区域 {plr.CurrentRegion.Name}"), color);
            return false;
        }

        if (ValidateSync(plr, data, out string error))
        {
            UpdateData(data, plr);
            UpdateRegions(data);
            plr.SendMessage(TextGradient($"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
            return true;
        }
        else
        {
            // 距离不足时设置同步标记，等待箱子打开
            if (error.Contains("距离"))
            {
                AfmPlrMag.GetPlyData(plr.Name).SyncFlag = true;
                plr.SendMessage(TextGradient("\n请打开要同步数据的钓鱼机箱子..."), color);
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
                plr.SendMessage(TextGradient($"\n数据丢失，已删除无效区域 {region.Name}"), color);
            }
            else
            {
                plr.SendErrorMessage("\n该位置没有钓鱼机");
            }
            return;
        }

        if (ValidateSync(plr, data, out string error))
        {
            UpdateData(data, plr);
            UpdateRegions(data);
            plr.SendMessage(TextGradient($"\n钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
            plr.SendMessage(TextGradient($"如果环境错误,请[c/75D1FF:打开一次]钓鱼箱\n"), color);
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

        data.ZoneCorrupt = plr.TPlayer.ZoneCorrupt;
        data.ZoneCrimson = plr.TPlayer.ZoneCrimson;
        data.ZoneJungle = plr.TPlayer.ZoneJungle;
        data.ZoneSnow = plr.TPlayer.ZoneSnow;
        data.ZoneHallow = plr.TPlayer.ZoneHallow;
        data.ZoneDesert = plr.TPlayer.ZoneDesert;
        data.ZoneBeach = plr.TPlayer.ZoneBeach;
        data.ZoneDungeon = plr.TPlayer.ZoneDungeon;
        data.ZoneRain = plr.TPlayer.ZoneRain;
        data.ZoneShimmer = plr.TPlayer.ZoneShimmer;
        data.ZoneSandstorm = plr.TPlayer.ZoneSandstorm;
        data.ZoneShadowCandle = plr.TPlayer.ZoneShadowCandle;
        data.ZoneWaterCandle = plr.TPlayer.ZoneWaterCandle;
        data.ZonePeaceCandle = plr.TPlayer.ZonePeaceCandle;
        data.ZoneGraveyard = plr.TPlayer.ZoneGraveyard;
        data.ZoneGranite = plr.TPlayer.ZoneGranite;
        data.ZoneMarble = plr.TPlayer.ZoneMarble;
        data.ZoneMeteor = plr.TPlayer.ZoneMeteor;
        data.ZoneGlowshroom = plr.TPlayer.ZoneGlowshroom;
        data.ZoneGemCave = plr.TPlayer.ZoneGemCave;
        data.ZoneHive = plr.TPlayer.ZoneHive;
        data.ZoneLihzhardTemple = plr.TPlayer.ZoneLihzhardTemple;
        data.ZoneOldOneArmy = plr.TPlayer.ZoneOldOneArmy;
        data.ZoneTowerNebula = plr.TPlayer.ZoneTowerNebula;
        data.ZoneTowerSolar = plr.TPlayer.ZoneTowerSolar;
        data.ZoneTowerStardust = plr.TPlayer.ZoneTowerStardust;
        data.ZoneTowerVortex = plr.TPlayer.ZoneTowerVortex;
        data.ZoneUndergroundDesert = plr.TPlayer.ZoneUndergroundDesert;
        data.luck = plr.TPlayer.luck;
    }
    #endregion

    #region 将缓存环境赋值给假玩家
    public static Player SetPlayer(MachData data, bool Custom = false)
    {
        var plr = new Player();
        plr.position = new Vector2(data.Pos.X * 16, data.Pos.Y * 16);
        // plr.UpdateBiomes();
        plr.ZoneHallow = data.ZoneHallow; // 神圣
        plr.ZoneCorrupt = data.ZoneCorrupt; //腐化
        plr.ZoneCrimson = data.ZoneCrimson; // 猩红
        plr.ZoneJungle = data.ZoneJungle; // 丛林
        plr.ZoneSnow = data.ZoneSnow; // 雪原
        plr.ZoneDesert = data.ZoneDesert; // 沙漠
        plr.ZoneBeach = data.ZoneBeach; // 海洋
        plr.ZoneDungeon = data.ZoneDungeon; // 地牢

        plr.luck = data.luck; // 幸运值

        int hl = data.HeightLevel;
        plr.ZoneSkyHeight = hl == 0; // 天空
        plr.ZoneOverworldHeight = hl == 1; // 地表
        plr.ZoneDirtLayerHeight = hl == 2; // 地下
        plr.ZoneRockLayerHeight = hl == 3; // 洞穴
        plr.ZoneUnderworldHeight = hl == 4; // 地狱

        // 给自定义渔获用的 常规钓鱼 用上面就够
        if (Custom)
        {
            plr.ZoneShimmer = data.ZoneShimmer; // 微光
            plr.ZoneRain = data.ZoneRain; // 下雨
            plr.ZoneSandstorm = data.ZoneSandstorm; // 沙尘暴
            plr.ZoneShadowCandle = data.ZoneShadowCandle; // 影烛 
            plr.ZoneWaterCandle = data.ZoneWaterCandle; // 水蜡烛 
            plr.ZonePeaceCandle = data.ZonePeaceCandle; // 和平蜡烛 
            plr.ZoneGraveyard = data.ZoneGraveyard; // 墓地 
            plr.ZoneGranite = data.ZoneGranite; // 花岗岩 
            plr.ZoneMarble = data.ZoneMarble; // 大理石 
            plr.ZoneMeteor = data.ZoneMeteor; // 陨石坑 
            plr.ZoneGlowshroom = data.ZoneGlowshroom; // 蘑菇地 
            plr.ZoneGemCave = data.ZoneGemCave; // 宝石洞 
            plr.ZoneHive = data.ZoneHive; // 蜂巢 
            plr.ZoneLihzhardTemple = data.ZoneLihzhardTemple; // 神庙 
            plr.ZoneOldOneArmy = data.ZoneOldOneArmy; // 旧日军团 
            plr.ZoneTowerNebula = data.ZoneTowerNebula; // 星云天塔柱 
            plr.ZoneTowerSolar = data.ZoneTowerSolar; // 日耀天塔柱 
            plr.ZoneTowerStardust = data.ZoneTowerStardust; // 星尘天塔柱 
            plr.ZoneTowerVortex = data.ZoneTowerVortex; // 星漩天塔柱 
            plr.ZoneUndergroundDesert = data.ZoneUndergroundDesert; // 地下沙漠 
        }

        return plr;
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