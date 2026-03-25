using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using static Plugin.DataStorage;
using static Plugin.Plugin;
using static Plugin.Utils;

namespace Plugin;

internal class MyCommand
{
    #region 主指令
    public static void CmdAfm(CommandArgs args)
    {
        if (args.Parameters.Count == 0)
        {
            Help(args);
            return;
        }

        var plr = args.Player;

        switch (args.Parameters[0].ToLower())
        {
            case "s":
            case "set":
            case "选择":
                {
                    plr.AwaitingTempPoint = 1;
                    plr.SendInfoMessage("请点击任意箱子作为选择...");
                }
                break;

            case "sv":
            case "save":
            case "保存":
                {
                    var point = plr.TempPoints[0];
                    if (point == Point.Zero)
                    {
                        plr.SendInfoMessage($"您还没有选择箱子，请先使用 /{afm} s 敲击图格");
                        return;
                    }

                    var tile = Main.tile[point.X, point.Y];
                    if (tile?.active() == true && !TileID.Sets.BasicChest[tile.type])
                    {
                        plr.SendInfoMessage($"您点击的图格不是箱子，请先使用 /{afm} s 重新敲击");
                        plr.AwaitingTempPoint = 0;
                        return;
                    }

                    var existing = Find(point);
                    if (existing == null)
                    {
                        // 创建新机器，并缓存当前玩家的环境信息
                        var data = new MachData { Owner = plr.Name, Pos = point };

                        // 获取当前玩家的真实环境（使用 TSPlayer 的 TPlayer）
                        var player = plr.TPlayer;
                        data.ZoneCorrupt = player.ZoneCorrupt;
                        data.ZoneCrimson = player.ZoneCrimson;
                        data.ZoneJungle = player.ZoneJungle;
                        data.ZoneSnow = player.ZoneSnow;
                        data.ZoneHallow = player.ZoneHallow;
                        data.ZoneDesert = player.ZoneDesert;
                        data.ZoneBeach = player.ZoneBeach;
                        data.ZoneDungeon = player.ZoneDungeon;

                        // 计算高度等级
                        int yPos = point.Y;
                        if (Main.remixWorld)
                        {
                            if (yPos < Main.worldSurface * 0.5)
                                data.HeightLevel = 0;
                            else if (yPos < Main.worldSurface)
                                data.HeightLevel = 1;
                            else if (yPos < Main.rockLayer)
                                data.HeightLevel = 3;
                            else if (yPos < Main.maxTilesY - 300)
                                data.HeightLevel = 2;
                            else
                                data.HeightLevel = 4;
                        }
                        else
                        {
                            if (yPos < Main.worldSurface * 0.5)
                                data.HeightLevel = 0;
                            else if (yPos < Main.worldSurface)
                                data.HeightLevel = 1;
                            else if (yPos < Main.rockLayer)
                                data.HeightLevel = 2;
                            else if (yPos < Main.maxTilesY - 300)
                                data.HeightLevel = 3;
                            else
                                data.HeightLevel = 4;
                        }

                        // remix 海洋判定（仅在保存时确定一次，世界固定后不再变化）
                        data.RolledRemixOcean = Main.remixWorld && data.HeightLevel == 1 && yPos >= Main.rockLayer && Main.rand.Next(3) == 0;

                        // 检测水体是否充足
                        int lavaTiles = 0, honeyTiles = 0;
                        int waterTiles = GetWaterTiles(data.Pos, ref lavaTiles, ref honeyTiles);
                        if (waterTiles < 75)
                        {
                            plr.SendMessage($"当前箱子附近{Config.Range}格内液体不足75格,请前往其他地点放置箱子", color2);
                            plr.AwaitingTempPoint = 0;
                            return;
                        }

                        AddOrUpdate(data);
                        plr.SendMessage(TextGradient($"\n{plr.Name}的自动钓鱼机 ({point.X},{point.Y}) 创建成功! "), color);
                        plr.SendMessage(TextGradient($"附近{Config.Range}格内液体数量:{waterTiles}"), color);
                        plr.SendMessage(TextGradient($"请给本箱子放入鱼竿和鱼饵\n"), color);
                    }
                    else
                    {
                        Remove(point);
                        plr.SendMessage($"已移除 ({point.X},{point.Y}) 的自动钓鱼机", color);
                    }
                    plr.AwaitingTempPoint = 0;
                }
                break;

            case "ls":
            case "list":
            case "列表":
                {
                    var all = GetAll();
                    if (all.Count == 0)
                    {
                        plr.SendInfoMessage("没有自动钓鱼机");
                        return;
                    }

                    int page = 1;
                    if (args.Parameters.Count > 1 && !int.TryParse(args.Parameters[1], out page)) page = 1;

                    int index = 1;
                    List<string> lines = [];
                    foreach (var data in all)
                    {
                        int baitCount = GetBaitCount(data.Pos);
                        string baitInfo = baitCount > 0 ? $"鱼饵:{baitCount}" : "鱼饵:无";
                        string rodName = data.FishRod == -1 ? "无" : TShock.Utils.GetItemById(data.FishRod).Name;

                        lines.Add(
                            $"{index}.{data.Owner}的钓鱼机 " +
                            $"鱼竿:{rodName} {baitInfo} " +
                            $"坐标:{data.Pos.X},{data.Pos.Y}");

                        index++;
                    }

                    PaginationTools.SendPage(plr, page, lines, new PaginationTools.Settings
                    {
                        HeaderFormat = "自动钓鱼机列表 ({0}/{1})",
                        FooterFormat = $"输入 /{afm} list {0} 查看更多"
                    });
                }
                break;

            case "if":
            case "info":
                {
                    if (!GetPos(args, out Point pos)) return;
                    var data = Find(pos);
                    if (data == null) { plr.SendErrorMessage("未找到钓鱼机"); return; }

                    // 实时统计水体（水、岩浆、蜂蜜）
                    int lavaTiles = 0, honeyTiles = 0;
                    int waterTiles = GetWaterTiles(pos, ref lavaTiles, ref honeyTiles);

                    // 大气因子（根据机器 Y 坐标计算）
                    int yPos = data.Pos.Y;
                    float atmo;
                    if (yPos < Main.worldSurface * 0.5)
                        atmo = 0.25f;
                    else if (yPos < Main.worldSurface)
                        atmo = 0.5f;
                    else
                        atmo = 1f;

                    int waterNeeded = (int)(300f * atmo);
                    float waterQuality = Math.Min(1f, (float)waterTiles / waterNeeded);

                    // 基础渔力（不含鱼饵，因为鱼饵动态消耗）
                    int basePower = Config.Power;
                    if (data.FishRod != -1)
                    {
                        var rod = new Item();
                        rod.SetDefaults(data.FishRod);
                        basePower += rod.fishingPole;
                    }
                    basePower += GetBonus(data.Acc); // 饰品加成

                    int finalPower = (int)(basePower * waterQuality); // 水体修正后的渔力

                    // 输出环境信息
                    plr.SendInfoMessage($"{data.Owner}钓鱼机信息:");

                    // 原始环境标志
                    plr.SendMessage($"沙漠:{(data.ZoneDesert?"是":"否")} 雪原:{(data.ZoneSnow?"是":"否")} 丛林:{(data.ZoneJungle?"是":"否")}", color);
                    plr.SendMessage($"腐化:{(data.ZoneCorrupt?"是":"否")} 猩红:{(data.ZoneCrimson?"是":"否")} 神圣:{(data.ZoneHallow?"是":"否")}", color);
                    plr.SendMessage($"海洋:{(data.ZoneBeach?"是":"否")} 地牢:{(data.ZoneDungeon?"是":"否")}", color);

                    if (data.ZoneCorrupt && data.ZoneCrimson)
                        plr.SendMessage("冲突:同时存在腐化和猩红，实际钓鱼时随机选择其一", color2);
                    if (data.ZoneJungle && data.ZoneSnow)
                        plr.SendMessage("冲突:同时存在丛林和雪地，实际钓鱼时随机选择其一（雪地优先）", color2);

                    // 其他环境参数
                    plr.SendMessage($"高度等级:{GetHeightLevel(data.HeightLevel)}", color);
                    plr.SendMessage($"大气因子:{atmo:F2}", color);
                    plr.SendMessage($"水体统计: 水{waterTiles} 岩浆:{lavaTiles} 蜂蜜:{honeyTiles}", color);
                    plr.SendMessage($"需水体量:{waterNeeded}", color);
                    plr.SendMessage($"水体质量:{waterQuality:P0}", color);
                    plr.SendMessage($"无饵渔力:{basePower}", color);
                    plr.SendMessage($"含水渔力:{finalPower}", color);
                    plr.SendMessage($"熔岩钓鱼:{(data.Acc.Any(id => lavaItems.Contains(id)) ? "是" : "否")}", color);
                    plr.SendMessage($"颠倒海洋:{(data.RolledRemixOcean ? "是":"否")}", color);
                }
                break;


            case "rs":
            case "reset":
            case "清空":
                HandleReset(args);
                break;

            default:
                Help(args);
                break;
        }
    }
    #endregion

    #region 清空所有数据
    private static void HandleReset(CommandArgs args)
    {
        if (!IsAdmin(args.Player)) return;
        Clear();
        args.Player.SendMessage("所有数据已重置", Color.OrangeRed);
    }
    #endregion

    #region 菜单指令
    private static void Help(CommandArgs args)
    {
        var plr = args.Player;
        if (!plr.RealPlayer)
        {
            plr.SendMessage($"《自动钓鱼机》", color);
            plr.SendMessage($"/{afm} ls - 列出所有机器", color);
            plr.SendMessage($"/{afm} reset - 重置插件数据", color);
            plr.SendMessage($"部分指令需进入游戏后查看", color);
        }
        else
        {
            plr.SendMessage("\n[i:3455][c/AD89D5:自动][c/D68ACA:钓][c/DF909A:鱼][c/E5A894:机][i:3454] " +
                            "[i:3456][C/F2F2C7:开发] [C/BFDFEA:by] [c/00FFFF:羽学] [i:3459]", color);

            var mess = new StringBuilder();
            mess.AppendLine($"/{afm} set - 选择箱子设为钓鱼机");
            mess.AppendLine($"/{afm} save - 添加/移除选择的钓鱼机");
            mess.AppendLine($"/{afm} list - 列出所有机器");
            mess.AppendLine($"/{afm} info - 获取钓鱼机信息");
            if (IsAdmin(plr))
                mess.AppendLine($"/{afm} reset - 重置插件数据");
            GradMess(mess, plr);
        }
    }
    #endregion

    #region 获取高度等级说明
    private static string GetHeightLevel(int HeightLevel)
    {
        switch (HeightLevel)
        {
            case 0: return "太空(0)";
            case 1: return "地表(1)";
            case 2: return "地下(2)";
            case 3: return "洞穴(3)";
            case 4: return "地狱(4)";
            default: return "未知";
        }
    } 
    #endregion

    #region 获取鱼饵数量
    private static int GetBaitCount(Point pos)
    {
        int range = Config.Range;
        int count = 0;
        int minX = Math.Max(pos.X - range, 0), maxX = Math.Min(pos.X + range, Main.maxTilesX - 1);
        int minY = Math.Max(pos.Y - range, 0), maxY = Math.Min(pos.Y + range, Main.maxTilesY - 1);

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                int ci = Chest.FindChest(x, y);
                if (ci == -1) continue;
                var chest = Main.chest[ci];
                for (int s = 0; s < chest.item.Length; s++)
                {
                    var item = chest.item[s];
                    if (item != null && !item.IsAir && item.bait > 0)
                    {
                        count += item.stack;
                    }
                }
            }

        return count;
    }
    #endregion

    #region 获取坐标
    private static bool GetPos(CommandArgs args, out Point point)
    {
        var plr = args.Player;
        point = Point.Zero;
        point = plr.TempPoints[0];
        if (point != Point.Zero) return true;

        plr.SendInfoMessage($"请先使用 /{afm} s 选择图格");
        return false;
    }
    #endregion
}