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

                        // 获取箱子索引并缓存
                        int chestIdx = Chest.FindChest(point.X, point.Y);
                        if (chestIdx != -1)
                            data.ChestIndex = chestIdx;

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
                        string rodName = GetRodName(data); // 动态获取鱼竿名称

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

                    // 大气因子
                    int yPos = data.Pos.Y;
                    float atmo = CalculateAtmo(yPos);

                    int waterNeeded = (int)(300f * atmo);
                    float waterQuality = Math.Min(1f, (float)waterTiles / waterNeeded);

                    // 基础渔力（不含鱼饵，因为鱼饵动态消耗）
                    int basePower = Config.Power;
                    basePower += GetRodPower(data); // 动态获取鱼竿渔力
                    basePower += GetBonus(data); // 饰品加成

                    int finalPower = (int)(basePower * waterQuality); // 水体修正后的渔力

                    // 输出环境信息
                    plr.SendInfoMessage($"{data.Owner}钓鱼机信息:");

                    // 原始环境标志
                    plr.SendMessage($"沙漠:{(data.ZoneDesert ? "是" : "否")} 雪原:{(data.ZoneSnow ? "是" : "否")} 丛林:{(data.ZoneJungle ? "是" : "否")}", color);
                    plr.SendMessage($"腐化:{(data.ZoneCorrupt ? "是" : "否")} 猩红:{(data.ZoneCrimson ? "是" : "否")} 神圣:{(data.ZoneHallow ? "是" : "否")}", color);
                    plr.SendMessage($"海洋:{(data.ZoneBeach ? "是" : "否")} 地牢:{(data.ZoneDungeon ? "是" : "否")}", color);

                    if (data.ZoneCorrupt && data.ZoneCrimson)
                        plr.SendMessage("冲突:同时存在腐化和猩红，实际钓鱼时随机选择其一", color2);
                    if (data.ZoneJungle && data.ZoneSnow)
                        plr.SendMessage("冲突:同时存在丛林和雪地，实际钓鱼时随机选择其一（雪地优先）", color2);

                    // 其他环境参数
                    plr.SendMessage($"高度等级:{GetHeightLevelString(data.HeightLevel)}", color);
                    plr.SendMessage($"大气因子:{atmo:F2}", color);
                    plr.SendMessage($"水体统计: 水{waterTiles} 岩浆:{lavaTiles} 蜂蜜:{honeyTiles}", color);
                    plr.SendMessage($"需水体量:{waterNeeded}", color);
                    plr.SendMessage($"水体质量:{waterQuality:P0}", color);
                    plr.SendMessage($"无饵渔力:{basePower}", color);
                    plr.SendMessage($"含水渔力:{finalPower}", color);
                    plr.SendMessage($"熔岩钓鱼:{(HasItem(data, item => lavaItems.Contains(item.type)) ? "是" : "否")}", color);
                    plr.SendMessage($"颠倒海洋:{(data.RolledRemixOcean ? "是" : "否")}", color);
                    // 显示排除物品列表
                    string excludeText = data.Exclude.Count > 0
                        ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id, 1)}"))
                        : "无";
                    plr.SendMessage($"排除物品: {excludeText}", color);
                }
                break;

            case "i":
            case "item":
            case "渔获":
                HandleCustomFish(args);
                break;

            case "exc":
            case "exclude":
            case "排除":
                HandleExcludeItem(args);
                break;

            case "cd":
            case "cond":
            case "条件":
                ShowConditions(args);
                break;

            case "lt":
            case "loot":
            case "自定义":
                ListCustomFishes(args);
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

    #region 菜单指令
    private static void Help(CommandArgs args)
    {
        var plr = args.Player;
        if (!plr.RealPlayer)
        {
            plr.SendMessage($"《自动钓鱼机》", color);
            plr.SendMessage($"/{afm} list - 列出所有自钓机", color);
            plr.SendMessage($"/{afm} loot - 查看自定渔获", color);
            plr.SendMessage($"/{afm} cond - 查看自定渔获条件", color);
            plr.SendMessage($"/{afm} reset - 重置插件数据", color);
            plr.SendMessage($"部分指令需进入游戏后查看", color);
        }
        else
        {
            plr.SendMessage("\n[i:3455][c/AD89D5:自动][c/D68ACA:钓][c/DF909A:鱼][c/E5A894:机][i:3454] " +
                            "[i:3456][C/F2F2C7:开发] [C/BFDFEA:by] [c/00FFFF:羽学] [i:3459]", color);

            var mess = new StringBuilder();
            mess.AppendLine($"/{afm} set - 选择箱子设为自钓机");
            mess.AppendLine($"/{afm} save - 添加/移除选择的自钓机");
            mess.AppendLine($"/{afm} list - 列出所有自钓机");
            mess.AppendLine($"/{afm} info - 获取自钓机信息");
            mess.AppendLine($"/{afm} loot - 查看自定渔获");
            mess.AppendLine($"/{afm} exc - 修改排除物品");
            if (IsAdmin(plr))
            {
                mess.AppendLine($"/{afm} cond - 查看自定渔获条件");
                mess.AppendLine($"/{afm} item - 修改自定义渔获");
                mess.AppendLine($"/{afm} reset - 重置插件数据");
            }
            GradMess(mess, plr);
        }
    }
    #endregion

    #region 获取鱼竿名称与鱼力
    public static string GetRodName(MachData data)
    {
        if (FindRod(data, out Item rodItem, out _, out _))
            return rodItem.Name;
        return "无";
    }

    public static int GetRodPower(MachData data)
    {
        if (FindRod(data, out Item rodItem, out _, out _))
            return rodItem.fishingPole;
        return 0;
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

    #region 管理排除物品列表（存在则移除，不存在则添加）
    private static void HandleExcludeItem(CommandArgs args)
    {
        var plr = args.Player;
        var pos = plr.TempPoints[0];
        if (pos == Point.Zero)
        {
            plr.SendInfoMessage($"请先使用 /{afm} s 选择钓鱼机箱子");
            return;
        }

        var data = Find(pos);
        if (data == null)
        {
            plr.SendErrorMessage("该位置没有钓鱼机");
            return;
        }

        // 权限检查：只有机器所有者或管理员可以修改
        if (!IsAdmin(plr) && data.Owner != plr.Name)
        {
            plr.SendErrorMessage("你没有权限修改别人的钓鱼机排除列表");
            return;
        }

        if (!plr.RealPlayer)
        {
            plr.SendErrorMessage("请进入游戏后手持物品再使用指令");
            return;
        }

        var heldItem = plr.SelectedItem;
        if (heldItem == null || heldItem.type == 0)
        {
            plr.SendErrorMessage("请手持一个物品");
            return;
        }

        int type = heldItem.type;
        string name = heldItem.Name;
        bool isRemove = data.Exclude.Contains(type);

        if (isRemove)
        {
            data.Exclude.Remove(type);
            TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 排除了 {ItemIcon(type, 1)} {name}", color2);
        }
        else
        {
            data.Exclude.Add(type);
            TShock.Utils.Broadcast($"{data.Owner}的钓鱼机 添加了排除物品 {ItemIcon(type, 1)} {name}", color2);
        }

        Save(); // 保存机器数据
    }
    #endregion

    #region 根据手持物品管理自定义渔获（管理员）
    private static void HandleCustomFish(CommandArgs args)
    {
        var plr = args.Player;
        if (!IsAdmin(plr))
        {
            plr.SendErrorMessage("你没有权限使用此命令");
            return;
        }

        if (!plr.RealPlayer)
        {
            plr.SendErrorMessage("请进入游戏后手持物品再使用指令");
            ItemHelp(plr);
            return;
        }

        var heldItem = plr.SelectedItem;
        if (heldItem == null || heldItem.type == 0)
        {
            plr.SendErrorMessage("请手持一个物品");
            ItemHelp(plr);
            return;
        }

        int type = heldItem.type;
        var existing = Config.CustomFishes.FirstOrDefault(r => r.ItemType == type);
        string name = heldItem.Name;

        // 如果已存在，则移除
        if (existing != null)
        {
            Config.CustomFishes.Remove(existing);
            Config.Write();
            TShock.Utils.Broadcast($"管理员 [c/47D3C3:{plr.Name}] 移除了自定义渔获 [i/s1:{type}] {name}", color);
            return;
        }

        // 添加新规则
        int Denom = 100; // 默认分母 100
        var conds = new List<string>();

        // 解析参数
        if (args.Parameters.Count >= 2)
        {
            string param = args.Parameters[1];
            // 尝试解析为整数（概率分母）
            if (int.TryParse(param, out int denom))
            {
                Denom = denom;
                // 后续参数为条件
                for (int i = 2; i < args.Parameters.Count; i++)
                    conds.Add(args.Parameters[i]);
            }
            else
            {
                // 第一个非数字参数作为条件的一部分
                conds.Add(param);
                for (int i = 2; i < args.Parameters.Count; i++)
                    conds.Add(args.Parameters[i]);
            }
        }

        // 去除空条件，合并条件字符串（支持逗号分隔）
        var final = new List<string>();
        foreach (var cond in conds)
        {
            var parts = cond.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                    final.Add(part.Trim());
            }
        }

        var newRule = new CustomFishRule
        {
            ItemType = type,
            ChanceDenominator = Denom,
            Cond = final
        };
        Config.CustomFishes.Add(newRule);
        Config.Write();

        string condText = final.Count > 0 ? $" 条件: {string.Join(", ", final)}" : "";
        TShock.Utils.Broadcast($"管理员 [c/47D3C3:{plr.Name}] 添加了自定义渔获 [i/s1:{type}] {name} 概率:1/{Denom}{condText}", color);

        ItemHelp(plr);
    }

    private static void ItemHelp(TSPlayer plr)
    {
        plr.SendMessage($"\n手持物品输入:/{afm} i 20 克眼,困难模式..\n", color);
        plr.SendMessage($"查看可用进度条件:/{afm} cd", color2);
        plr.SendMessage($"数字设置概率分母,文字设置进度条件", color2);
        plr.SendMessage($"无第2参数存在则移除,不在则添加,默认1%概率", color2);
    }
    #endregion

    #region 显示所有可用条件（无需权限）
    private static void ShowConditions(CommandArgs args)
    {
        var plr = args.Player;
        var conds = AllConditions;

        if (conds.Count == 0)
        {
            plr.SendInfoMessage("暂无可用条件");
            return;
        }

        // 分页显示（每页 10 个）
        int page = 1;
        if (args.Parameters.Count > 1 && !int.TryParse(args.Parameters[1], out page))
            page = 1;
        if (page < 1) page = 1;

        int perPage = 10;
        int total = (int)Math.Ceiling(conds.Count / (double)perPage);
        if (page > total) page = total;

        int start = (page - 1) * perPage;
        int end = Math.Min(start + perPage, conds.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"[c/47D3C3:可用条件列表 (第 {page}/{total} 页)]");
        for (int i = start; i < end; i++)
        {
            sb.AppendLine($"[c/55CDFF:•] {conds[i]}");
        }
        if (total > 1)
        {
            sb.AppendLine($"[c/FFFF00:输入 /{afm} cond {page + 1} 查看下一页]");
        }
        plr.SendMessage(sb.ToString(), color);
    }
    #endregion

    #region 列出当前自定义渔获（无需权限，支持分页）
    private static void ListCustomFishes(CommandArgs args)
    {
        var plr = args.Player;
        if (Config.CustomFishes.Count == 0)
        {
            plr.SendInfoMessage("暂无自定义渔获");
            return;
        }

        int page = 1;
        if (args.Parameters.Count > 1 && !int.TryParse(args.Parameters[1], out page))
            page = 1;
        if (page < 1) page = 1;

        int perPage = 8;
        int total = (int)Math.Ceiling(Config.CustomFishes.Count / (double)perPage);
        if (page > total) page = total;

        int start = (page - 1) * perPage;
        int end = Math.Min(start + perPage, Config.CustomFishes.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"[c/47D3C3:══ 自定义渔获列表 (第{page}/{total}页) ══]");

        for (int i = start; i < end; i++)
        {
            var rule = Config.CustomFishes[i];
            // 计算百分比： (1 / 分母) * 100
            double percent = 100.0 / rule.ChanceDenominator;
            string Display;
            // 如果百分比是整数，显示整数；否则保留两位小数
            if (Math.Abs(percent - Math.Round(percent)) < 0.001)
                Display = Math.Round(percent).ToString();
            else
                Display = percent.ToString("F2");

            string condText = rule.Cond.Count > 0 ? $" 条件: {string.Join(", ", rule.Cond)}" : "";
            sb.AppendLine($"[c/55CDFF:{i + 1:00}.] [i/s1:{rule.ItemType}] " +
                          $"概率:{Display}%{condText}");
        }

        if (total > 1)
        {
            sb.AppendLine($"[c/FFFF00:输入 /{afm} lt {page + 1} 查看下一页]");
        }
        plr.SendMessage(sb.ToString(), color);
    }
    #endregion
}