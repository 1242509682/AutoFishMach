using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using TShockAPI.Hooks;
using static FishMach.DataManager;
using static FishMach.Plugin;
using static FishMach.Utils;

namespace FishMach;

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
                    if (!plr.RealPlayer) return;

                    // 设置自定义数据标记，表示玩家正在等待打开箱子
                    if (plr.ActiveChest == -1)
                    {
                        plr.SetData("set", true);
                        plr.SendMessage(TextGradient("请打开一个箱子作为自动钓鱼机...\n"), color);
                        return;
                    }

                    var chest = Main.chest[plr.ActiveChest];
                    if (chest == null) return;
                    var pos = new Point(chest.x, chest.y);
                    var data = DataManager.FindChest(plr.ActiveChest);
                    if (data == null)
                    {
                        CreateData(plr, plr.ActiveChest, pos);
                    }
                }
                break;

            case "ls":
            case "list":
            case "列表":
                {
                    var all = Machines;
                    if (all.Count == 0)
                    {
                        plr.SendMessage($"没有自动钓鱼机", color);
                        return;
                    }

                    int page = 1;
                    if (args.Parameters.Count > 1 && !int.TryParse(args.Parameters[1], out page)) page = 1;

                    // 每页显示的数量（可调整此数值）
                    int max = 4; // 每页最多显示4个自动钓鱼机

                    // 计算当前页的起始和结束索引
                    int start = (page - 1) * max; // 当前页第一个项目的索引
                    int end = (int)MathF.Min(start + max, all.Count); // 当前页最后一个项目的索引

                    // 计算总页数（根据实际项目数量计算）
                    int total = (int)MathF.Ceiling((float)((double)all.Count / max)); // 总页数

                    // 验证页码是否超出范围
                    if (page > total)
                    {
                        plr.SendErrorMessage($"页码超出范围，总共有 {total} 页");
                        return;
                    }

                    // 创建包含所有行的StringBuilder
                    var sb = new StringBuilder();
                    sb.AppendLine($"自动钓鱼机列表 ({page}/{total})"); // 显示当前页/总页数

                    // 遍历当前页的所有项目
                    for (int i = start; i < end; i++)
                    {
                        var data = all[i];
                        int idx = i - start + 1; // 在当前页中的显示序号（从1开始）

                        var env = new List<string>();
                        var env2 = MyCommand.GetHeightName(data.HeightLevel);
                        if (data.ZoneHallow) env.Add("神圣");
                        if (data.ZoneCorrupt) env.Add("腐化");
                        if (data.ZoneCrimson) env.Add("猩红");
                        if (data.ZoneJungle) env.Add("丛林");
                        if (data.ZoneSnow) env.Add("雪原");
                        if (data.ZoneDesert) env.Add("沙漠");
                        if (data.ZoneBeach) env.Add("海洋");
                        if (data.ZoneDungeon) env.Add("地牢");
                        if (data.RolledRemixOcean) env.Add("颠倒海洋");

                        // 修复性能问题：缓存鱼竿和鱼饵类型
                        int rodType = data.RodSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.RodSlot]?.type ?? -1 : -1;
                        string rodInfo = rodType > 0 ? $"鱼竿:{ItemIcon(rodType)}" : "鱼竿:无";

                        int baitType = data.BaitSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.type ?? -1 : -1;
                        int baitStack = baitType > 0 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.stack ?? 0 : 0;
                        string baitInfo = baitType > 0 ? $"鱼饵:{ItemIcon(baitType, baitStack)}" : "鱼饵:无";

                        string line = $"{idx}.{data.Owner}的钓鱼机 [c/ED756F:{data.ChestIndex}] " +
                                     $"{rodInfo} {baitInfo}\n" +
                                     $"坐标 {data.Pos.X},{data.Pos.Y} " +
                                     $"环境 {env2},{string.Join(",", env)}";

                        sb.AppendLine(line);
                    }

                    // 根据实际页数显示下一页提示
                    if (page < total)
                        sb.AppendLine($"输入 /{afm} list {page + 1} 查看第 {page + 1} 页");

                    plr.SendMessage(TextGradient(sb.ToString()), color);
                }
                break;

            case "if":
            case "info":
                {
                    if (!plr.RealPlayer) return;

                    if (plr.CurrentRegion != null &&
                        IsAfmRegion(plr.CurrentRegion.Name))
                    {
                        var data = GetDataByXY(plr.TileX, plr.TileY);
                        if (data == null) return;
                        ShowMachineInfo(plr, data);
                        return;
                    }

                    if (plr.ActiveChest == -1)
                    {
                        plr.SetData("info", true);
                        plr.SendMessage(TextGradient("请打开要查看的钓鱼箱...\n"), color);
                        return;
                    }
                }
                break;

            case "sv":
            case "save":
            case "sync":
            case "更新":
            case "同步":
                {
                    if (!plr.RealPlayer)
                    {
                        plr.SendErrorMessage("请进入游戏后再使用指令");
                        return;
                    }

                    // 检查玩家是否在钓鱼机区域内
                    if (plr.CurrentRegion != null && IsAfmRegion(plr.CurrentRegion.Name))
                    {
                        var data = DataManager.FindRegion(plr.CurrentRegion.Name);

                        // 区域存在但数据丢失，删除区域
                        if (data == null)
                        {
                            TShock.Regions.DeleteRegion(plr.CurrentRegion.Name);
                            plr.SendMessage(TextGradient($"数据丢失,已删除无效区域 {plr.CurrentRegion.Name}"), color);
                            return;
                        }

                        // 区域存在,箱子丢失,删除区域
                        var chest2 = Main.chest[data.ChestIndex];
                        if (chest2 == null || chest2.x != data.Pos.X || chest2.y != data.Pos.Y)
                        {
                            // 箱子已被移除或移动，区域无效，删除区域和机器
                            TShock.Regions.DeleteRegion(plr.CurrentRegion.Name);
                            DataManager.Remove(data.Pos);
                            TShock.Utils.Broadcast($"钓鱼机 [c/ED756F:{data.ChestIndex}] 已不存在\n" +
                                                   $"删除无效区域: {plr.CurrentRegion.Name}", color);
                            return;
                        }

                        // 距离检查,太远了,让玩家打开箱子
                        int dx = plr.TileX - data.Pos.X;
                        int dy = plr.TileY - data.Pos.Y;
                        int dist = (int)MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > Config.UpdateTileRange)
                        {
                            plr.SetData("sync", true);
                            plr.SendMessage(TextGradient("请打开要同步数据的钓鱼机箱子..."), color);
                            return;
                        }

                        // 立即更新数据
                        UpdateData(data, plr.TPlayer);
                        EnvManager.UpdateItemCache(data);
                        plr.SendMessage(TextGradient($"钓鱼机 [c/ED756F:{data.ChestIndex}] 数据已同步"), color);
                        return;
                    }

                    // 不在区域内，让玩家打开箱子
                    plr.SetData("sync", true);
                    plr.SendMessage(TextGradient("请打开要同步数据的钓鱼机箱子..."), color);
                }
                break;

            case "i":
            case "item":
            case "物品":
                HandleCustomFish(args);
                break;

            case "npc":
            case "怪物":
                HandleNPCFish(args);
                break;

            case "exc":
            case "exclude":
            case "排除":
                {
                    bool flowControl = HandleExclude(args, plr);
                    if (!flowControl)
                    {
                        return;
                    }
                }
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
            plr.SendMessage($"/{afm} ls - 列出钓鱼机表", color);
            plr.SendMessage($"/{afm} lt - 查看自定渔获", color);
            plr.SendMessage($"/{afm} cd - 查看自定条件", color);
            plr.SendMessage($"/{afm} rs - 重置插件数据", color);
            plr.SendMessage($"部分指令需进入游戏后查看", color);
        }
        else
        {
            plr.SendMessage("\n[i:3455][c/AD89D5:自动][c/D68ACA:钓][c/DF909A:鱼][c/E5A894:机][i:3454] " +
                            "[i:3456][C/F2F2C7:开发] [C/BFDFEA:by] [c/00FFFF:羽学] [i:3459]", color);

            var mess = new StringBuilder();
            mess.AppendLine($"/{afm} s - 将箱子设为钓鱼机");
            mess.AppendLine($"/{afm} if - 获取钓鱼机信息");
            mess.AppendLine($"/{afm} sv - 同步更新钓鱼机");
            mess.AppendLine($"/{afm} ls - 列出所有钓鱼机");
            mess.AppendLine($"/{afm} lt - 查看自定渔获");
            mess.AppendLine($"/{afm} exc - 批量修改排除物品表");
            if (IsAdmin(plr))
            {
                mess.AppendLine($"/{afm} cd - 查看自定渔获条件");
                mess.AppendLine($"/{afm} i - 修改自定渔获物品");
                mess.AppendLine($"/{afm} npc - 修改自定渔获怪物");
                mess.AppendLine($"/{afm} rs - 重置插件数据");
            }
            GradMess(mess, plr);
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

    #region 将高度等级转换为可读字符串
    public static string GetHeightName(int level) => level switch
    {
        0 => "太空",
        1 => "地表",
        2 => "地下",
        3 => "洞穴",
        4 => "地狱",
        _ => "未知"
    };
    #endregion

    #region 显示信息方法
    public static void ShowMachineInfo(TSPlayer plr, MachData data)
    {
        // 确保环境最新
        EnvManager.RefreshEnv(data);

        // 大气因子
        float atmo = data.atmo;

        // 基础渔力 鱼竿+鱼饵
        int rodPower = data.RodSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.RodSlot]?.fishingPole ?? -1 : -1;
        int baitPower = data.BaitSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.bait ?? -1 : -1;
        int power = rodPower + baitPower;
        power += data.ExtraPower;

        // 钓鱼药水临时加成（+20）
        if (DateTime.UtcNow < data.FishingPotionTime) power += Config.FishingPotionPower;
        // 鱼饵桶临时加成 +10
        if (DateTime.UtcNow < data.ChumBucketTime) power += Config.ChumBucketPower;

        // 自定义物品临时加成（渔力）
        int customBonus = 0;
        if (Config.CustomUsedItem.Count > 0)
            foreach (var consumable in Config.CustomUsedItem)
                if (data.Custom.TryGetValue(consumable.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                    customBonus += state.Bonus;

        power += customBonus;

        // 水体质量（蜂蜜 1.5 倍）
        int effectiveWater = data.MaxLiq;
        if (data.LiqName == "蜂蜜")
            effectiveWater = (int)(effectiveWater * 1.5);
        int waterNeeded = (int)(300f * data.atmo);
        float waterQuality = MathF.Min(1f, (float)effectiveWater / waterNeeded);

        // 含水鱼力(实际渔力)
        int finalPower = (int)(power * waterQuality);

        // 幸运值影响范围（基于含水渔力）
        int luckMin = finalPower;
        int luckMax = finalPower;
        if (data.luck > 0.001f)
        {
            luckMin = finalPower;
            luckMax = (int)(finalPower * 1.4);
        }
        else if (data.luck < -0.001f)
        {
            luckMin = (int)(finalPower * 0.6);
            luckMax = finalPower;
        }
        string luckText = Math.Abs(data.luck) > 0.001f ? $"实际渔力:[c/FFAA6D:{luckMin}~{luckMax}]" : $"实际渔力:[c/FFAA6D:{finalPower}]";

        // 输出环境信息
        plr.SendMessage(TextGradient($"\n[c/E8EB6E:{data.Owner}] " +
                                     $"钓鱼机 [c/ED756F:{data.ChestIndex}] " +
                                     $"[c/75D1FF:{data.RegionPlayers.Count}] 人"), color);

        // 使用缓存的鱼竿和鱼饵信息
        int rodType = data.RodSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.RodSlot]?.type ?? -1 : -1;
        string rodInfo = rodType > 0 ? $"鱼竿:{ItemIcon(rodType)}" : "鱼竿:无";

        int baitType = data.BaitSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.type ?? -1 : -1;
        int baitStack = baitType > 0 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.stack ?? 0 : 0;
        string baitInfo = baitType > 0 ? $"鱼饵:{ItemIcon(baitType, baitStack)}" : "鱼饵:无";


        // 其他环境参数
        var mess = new StringBuilder();
        mess.AppendLine($"{rodInfo} {baitInfo}");
        mess.AppendLine($"鱼池:{data.LiqName} [c/61BFE2:{data.MaxLiq}]格");
        mess.AppendLine($"附近 水 [c/61BFE2:{data.WatCnt}]格 岩浆 [c/FF716D:{data.LavCnt}]格 蜂蜜 [c/FFE46D:{data.HonCnt}]格");
        mess.AppendLine($"液体需求:[c/FF716D:{waterNeeded}] 液体质量:[c/FFA866:{waterQuality:P0}]");

        // 幸运值显示
        if (Math.Abs(data.luck) > 0.001f)
            mess.AppendLine($"幸运值:[c/61E26C:{data.luck:F2}] (影响渔力±10%~40%)");

        mess.AppendLine($"基础渔力:[c/61E26C:{power}]点 {luckText}");
        mess.AppendLine($"熔岩钓鱼:{(data.CanFishInLava ? "是" : "否")} 节省鱼饵:{(data.HasTackle ? "是" : "否")}");
        mess.AppendLine($"需要电路:{(Config.NeedWiring ? "是" : "否")} 钓任务鱼:{(Config.QuestFish ? "是" : "否")}");
        mess.AppendLine($"区域保护:{(Config.RegionBuild ? "是 " : "否 ")} 范围:[c/61BCE3:{Config.Range}]格");
        mess.AppendLine($"无人关闭:{(Config.AutoStopWhenEmpty ? "是" : "否")} 自动整理:{(Config.AutoPut ? "是" : "否")}");
        mess.AppendLine($"允许怪物:{(Config.EnableCustomNPC ? "是" : "否")} 禁钓多怪:{(Config.SoloCustomMonster ? "是" : "否")}");
        mess.AppendLine($"禁怪模式:{(Config.SoloMode == 0 ? "不同类各[c/61BBE2:1]个" : "只钓[c/FFAC6D:1]个")}");

        // 宝匣药水
        if (data.CratePotionTime > DateTime.UtcNow)
        {
            double min = (data.CratePotionTime - DateTime.UtcNow).TotalMinutes;
            mess.AppendLine($"宝匣药水:剩余[c/61E278:{FormatRemaining(min)}]");
        }

        // 钓鱼药水
        if (data.FishingPotionTime > DateTime.UtcNow)
        {
            double min = (data.FishingPotionTime - DateTime.UtcNow).TotalMinutes;
            mess.AppendLine($"钓鱼药水:剩余[c/61BBE2:{FormatRemaining(min)}]");
        }

        // 鱼饵桶
        if (data.ChumBucketTime > DateTime.UtcNow)
        {
            double min = (data.ChumBucketTime - DateTime.UtcNow).TotalMinutes;
            mess.AppendLine($"鱼饵桶:剩余[c/FF766D:{FormatRemaining(min)}]");
        }

        var env = new List<string>();
        var env2 = MyCommand.GetHeightName(data.HeightLevel);
        if (data.ZoneHallow) env.Add("神圣");
        if (data.ZoneCorrupt) env.Add("腐化");
        if (data.ZoneCrimson) env.Add("猩红");
        if (data.ZoneJungle) env.Add("丛林");
        if (data.ZoneSnow) env.Add("雪原");
        if (data.ZoneDesert) env.Add("沙漠");
        if (data.ZoneBeach) env.Add("海洋");
        if (data.ZoneDungeon) env.Add("地牢");
        if (data.RolledRemixOcean) env.Add("颠倒海洋");
        mess.AppendLine($"[c/63D475:钓鱼环境]:{env2},{string.Join(",", env)}");
        plr.SendMessage(TextGradient(mess.ToString()), color2);

        // 自定义消耗物品
        var sb = new StringBuilder();
        if (Config.CustomUsedItem.Count > 0)
        {
            int idx = 1;  // 序号从1开始
            foreach (var UsedItem in Config.CustomUsedItem)
                if (data.Custom.TryGetValue(UsedItem.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                {
                    double min = (state.Expiry - DateTime.UtcNow).TotalMinutes;
                    if (UsedItem.BuffID > 0)
                    {
                        string buffName = $"{UsedItem.BuffName} ";
                        string buffDesc = $"[c/5F9DB8:-] {UsedItem.BuffDesc}";
                        sb.AppendLine($"{idx}.{buffName} 剩余[c/61BBE2:{FormatRemaining(min)}] \n{buffDesc}");
                        idx++;
                    }
                }
        }

        if (!string.IsNullOrEmpty(sb.ToString()))
            plr.SendMessage("[区域增益]\n" + TextGradient(sb.ToString()), color2);

        // 显示排除物品列表
        string NoItem = data.Exclude.Count > 0 ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id)}")) : string.Empty;
        if (!string.IsNullOrEmpty(NoItem))
            plr.SendMessage($"排除物品: {NoItem}", color);
    }
    #endregion

    #region 进入区域信息
    public static void RegionInfo(RegionHooks.RegionEnteredEventArgs args, MachData data)
    {
        var plr = args.Player;

        var env = new List<string>();
        var env2 = MyCommand.GetHeightName(data.HeightLevel);
        if (data.ZoneHallow) env.Add("神圣");
        if (data.ZoneCorrupt) env.Add("腐化");
        if (data.ZoneCrimson) env.Add("猩红");
        if (data.ZoneJungle) env.Add("丛林");
        if (data.ZoneSnow) env.Add("雪原");
        if (data.ZoneDesert) env.Add("沙漠");
        if (data.ZoneBeach) env.Add("海洋");
        if (data.ZoneDungeon) env.Add("地牢");
        if (data.RolledRemixOcean) env.Add("颠倒海洋");

        // 计算当前渔力
        int rodPower = data.RodSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.RodSlot]?.fishingPole ?? -1 : -1;
        int baitPower = data.BaitSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.bait ?? -1 : -1;
        int extraPower = data.ExtraPower;
        int tempPower = 0;
        if (DateTime.UtcNow < data.FishingPotionTime) tempPower += Config.FishingPotionPower;
        if (DateTime.UtcNow < data.ChumBucketTime) tempPower += Config.ChumBucketPower;
        if (Config.CustomUsedItem.Count > 0)
            foreach (var UsedItem in Config.CustomUsedItem)
                if (data.Custom.TryGetValue(UsedItem.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                    tempPower += state.Bonus;
        int Power = rodPower + baitPower + extraPower + tempPower;

        // 水体质量（蜂蜜 1.5 倍）
        int effectiveWater = data.MaxLiq;
        if (data.LiqName == "蜂蜜")
            effectiveWater = (int)(effectiveWater * 1.5);
        int waterNeeded = (int)(300f * data.atmo);
        float waterQuality = MathF.Min(1f, (float)effectiveWater / waterNeeded);
        int finalPower = (int)(Power * waterQuality);

        // 幸运值影响范围（基于含水渔力）
        int luckMin = finalPower;
        int luckMax = finalPower;
        if (data.luck > 0.001f)
        {
            luckMin = finalPower;
            luckMax = (int)(finalPower * 1.4);
        }
        else if (data.luck < -0.001f)
        {
            luckMin = (int)(finalPower * 0.6);
            luckMax = finalPower;
        }
        string luckText = Math.Abs(data.luck) > 0.001f ? $"[c/FFAA6D:{luckMin}~{luckMax}]" : $"[c/FFAA6D:{finalPower}]";

        var PowerText = string.Empty;
        if (Power > 0)
            PowerText = TextGradient($"渔力 {Power}({luckText})");

        // 获取鱼竿和鱼饵数量的图标（使用缓存）
        int rodType = data.RodSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.RodSlot]?.type ?? -1 : -1;
        string rodInfo = rodType > 0 ? $"鱼竿 {ItemIcon(rodType)}" : "鱼竿 无";

        int baitType = data.BaitSlot != -1 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.type ?? -1 : -1;
        int baitStack = baitType > 0 ? Main.chest[data.ChestIndex]?.item[data.BaitSlot]?.stack ?? 0 : 0;
        string baitInfo = baitType > 0 ? $"鱼饵 {ItemIcon(baitType, baitStack)}" : "鱼饵 无";

        plr.SendMessage(TextGradient($"欢迎来到钓鱼机 [c/ED756F:{data.ChestIndex}] " +
                                     $"[c/75D1FF:{data.RegionPlayers.Count}] 人"), color);
        plr.SendMessage($"归属 [c/E8EB6E:{data.Owner}] {rodInfo} {baitInfo}", color2);
        plr.SendMessage(TextGradient($"环境 {env2},{string.Join(",", env)}"), color);
        plr.SendMessage(TextGradient($"鱼池 {data.LiqName} [c/61BFE2:{data.MaxLiq}] 格 {PowerText}"), color);

        var mess = new StringBuilder();
        if (Config.CustomUsedItem.Count > 0)
        {
            int idx = 1;
            foreach (var UsedItem in Config.CustomUsedItem)
                if (data.Custom.TryGetValue(UsedItem.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                {
                    double min = (state.Expiry - DateTime.UtcNow).TotalMinutes;
                    if (UsedItem.BuffID > 0)
                    {
                        string ItemIcon = $"{Utils.ItemIcon(UsedItem.ItemType)}";
                        string buffDesc = $"[c/5F9DB8:-] {UsedItem.BuffDesc}";
                        mess.AppendLine($"{idx}.{ItemIcon} 剩余[c/61BBE2:{FormatRemaining(min)}] \n{buffDesc}");
                        idx++;
                    }
                }
        }

        if (!string.IsNullOrEmpty(mess.ToString()))
            plr.SendMessage(TextGradient("[区域增益]\n" + mess.ToString()), color);

        string NoItem = data.Exclude.Count > 0 ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id)}")) : string.Empty;
        if (!string.IsNullOrEmpty(NoItem))
            plr.SendMessage($"排除物品: {NoItem}", color);
    }
    #endregion

    #region 排除表操作指令方法
    private static bool HandleExclude(CommandArgs args, TSPlayer plr)
    {
        if (!plr.RealPlayer)
        {
            plr.SendErrorMessage("请进入游戏后再使用指令");
            return false;
        }

        // 无参数：进入记录模式
        if (args.Parameters.Count == 1)
        {
            // 创建该玩家的列表
            if (!pend.ContainsKey(plr.Name))
                pend[plr.Name] = new HashSet<int>();

            // 显示帮助
            ShowExcludeHelp(plr);
            return false;
        }

        string sub = args.Parameters[1].ToLower();
        if (sub != "add" && sub != "del")
        {

            pend.Remove(plr.Name); // 清除模式
            return false;
        }

        if (!pend.TryGetValue(plr.Name, out var set) || set.Count == 0)
        {
            plr.SendErrorMessage("没有待处理的物品，请先放入物品到钓鱼箱中。");
            return false;
        }

        // 获取当前打开的箱子
        var data = DataManager.FindChest(plr.ActiveChest);
        if (data == null)
        {
            plr.SendErrorMessage("请先打开要修改的钓鱼机箱子");
            return false;
        }

        // 权限检查
        if (!IsAdmin(plr) && data.Owner != plr.Name)
        {
            plr.SendErrorMessage($"你没有权限修改{data.Owner}的钓鱼机排除表");
            pend.Remove(plr.Name);
            return false;
        }

        var yes = new List<string>();
        var no = new List<string>();
        bool isAdd = sub == "add";
        foreach (var type in set)
        {
            if (isAdd)
            {
                if (data.Exclude.Add(type))
                    yes.Add(ItemIcon(type));
            }
            else // 删除
            {
                if (data.Exclude.Remove(type))
                    no.Add(ItemIcon(type));
            }
        }

        Save(data);
        pend.Remove(plr.Name);
        string NoItem = data.Exclude.Count > 0 ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id)}")) : "无";
        plr.SendMessage($"\n更新后的排除物品表: {NoItem}", color2);
        if (yes.Count > 0)
            plr.SendMessage(TextGradient($"[c/47D3C3:本次添加]:{string.Join(",", yes)}"), color2);
        if (no.Count > 0)
            plr.SendMessage(TextGradient($"[c/47D3C3:本次删除]:{string.Join(",", no)}"), color2);
        return true;
    }

    private static void ShowExcludeHelp(TSPlayer plr)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n[c/47D3C3:自动钓鱼机 - 批量排除管理]");
        sb.AppendLine($"[c/55CDFF:•] /{afm} exc - 进入批量记录模式");
        sb.AppendLine($"[c/55CDFF:•] 在钓鱼箱中放入需要操作的物品");
        sb.AppendLine($"[c/55CDFF:•] /{afm} exc add - 批量添加到排除表");
        sb.AppendLine($"[c/55CDFF:•] /{afm} exc del - 批量所有物品从排除表移除");
        plr.SendMessage(TextGradient(sb.ToString()), color);

        // 显示排除物品列表
        plr.SendMessage($"已进入批量排除模式，请将物品放入钓鱼箱中。", color2);
        var data = DataManager.FindRegion(plr.CurrentRegion.Name);
        if (data == null) return;
        string NoItem = data.Exclude.Count > 0 ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id)}")) : "无";
        plr.SendMessage($"当前排除物品: {NoItem}", color2);
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
            Config.AutoDesc();
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
            Chance = Denom,
            Cond = final
        };
        Config.CustomFishes.Add(newRule);
        Config.AutoDesc();
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

    #region 修改NPC渔获（支持列出、添加/移除）
    private static void HandleNPCFish(CommandArgs args)
    {
        var plr = args.Player;
        if (!IsAdmin(plr))
        {
            plr.SendErrorMessage("你没有权限使用此命令");
            return;
        }

        if (!plr.RealPlayer)
        {
            plr.SendErrorMessage("请进入游戏后使用指令");
            return;
        }

        // 无参数时显示帮助
        if (args.Parameters.Count == 1)
        {
            NpcHelp(plr);
            return;
        }

        // 获取玩家位置（图格）
        var playerPos = new Point((int)(plr.TPlayer.position.X / 16), (int)(plr.TPlayer.position.Y / 16));
        var nearbyNPCs = new Dictionary<int, (int npcType, string name, int dist, bool exists)>(); // 按类型去重

        // 扫描附近NPC（距离 ≤ 85 格）
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            var npc = Main.npc[i];
            if (!npc.active || npc.type <= 0 ||
                npc.friendly || npc.townNPC ||
                npc.SpawnedFromStatue ||
                npc.type == NPCID.WallofFlesh ||
                npc.type == NPCID.TargetDummy) continue;

            int npcX = (int)(npc.position.X / 16);
            int npcY = (int)(npc.position.Y / 16);
            int dist = (int)MathF.Abs(npcX - playerPos.X) + (int)MathF.Abs(npcY - playerPos.Y); // 曼哈顿距离
            if (dist <= 85)
            {
                bool exists = Config.CustomFishes.Any(r => r.NPCType == npc.type);
                // 如果该类型尚未记录，或者当前距离更近，则更新
                if (!nearbyNPCs.ContainsKey(npc.type) || nearbyNPCs[npc.type].dist > dist)
                {
                    nearbyNPCs[npc.type] = (npc.type, npc.GivenOrTypeName, dist, exists);
                }
            }
        }

        // 转换为列表并按距离排序
        var sortedNPCs = nearbyNPCs.Values.OrderBy(n => n.dist).ToList();

        // 处理 "list" 子命令
        if (args.Parameters[1].ToLower() == "ls" || args.Parameters[1].ToLower() == "list")
        {
            if (sortedNPCs.Count == 0)
            {
                plr.SendInfoMessage("附近85格内没有找到任何NPC");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"附近[c/47D3C3:85格内]NPC列表 (共{sortedNPCs.Count}个)");
            int idx = 1;
            foreach (var npc in sortedNPCs)
            {
                string color = npc.exists ? "00FF00" : "FF5555";
                var Desc = npc.exists ? " [已加]" : " [未加]";
                sb.AppendLine($"{idx++}.[c/{color}:{npc.name}({npc.npcType})] 距离:{npc.dist}格 {Desc}");
            }
            plr.SendMessage(TextGradient(sb.ToString()), color);
            return;
        }

        // 解析NPC ID
        if (!int.TryParse(args.Parameters[1], out int npcId))
        {
            plr.SendErrorMessage("无效的NPC ID，请使用数字ID");
            NpcHelp(plr);
            return;
        }

        // 解析概率和条件
        int Chance = 100;
        List<string> conds = new List<string>();
        if (args.Parameters.Count >= 3)
        {
            string param = args.Parameters[2];
            // 尝试解析为整数（概率分母）
            if (int.TryParse(param, out int denom))
            {
                Chance = denom;
                // 后续参数为条件
                for (int i = 3; i < args.Parameters.Count; i++)
                    conds.Add(args.Parameters[i]);
            }
            else
            {
                // 第一个非数字参数作为条件的一部分
                conds.Add(param);
                for (int i = 3; i < args.Parameters.Count; i++)
                    conds.Add(args.Parameters[i]);
            }
        }

        // 处理条件（支持逗号、空格分隔）
        var fcond = new List<string>();
        foreach (var cond in conds)
        {
            var parts = cond.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                if (!string.IsNullOrWhiteSpace(part))
                    fcond.Add(part.Trim());
        }

        // 查找所有匹配的规则
        var matchedRules = Config.CustomFishes.Where(r => r.NPCType == npcId).ToList();
        string npcName = Lang.GetNPCNameValue(npcId);
        if (matchedRules.Count > 0)
        {
            // 移除所有匹配的规则
            foreach (var rule in matchedRules)
                Config.CustomFishes.Remove(rule);
            Config.AutoDesc();
            Config.Write();
            TShock.Utils.Broadcast($"管理员 [c/47D3C3:{plr.Name}] 移除了 {matchedRules.Count} 个自定渔获npc {npcName}", color);
        }
        else
        {
            var newRule = new CustomFishRule
            {
                NPCType = npcId,
                Chance = Chance,
                Cond = fcond.ToList()
            };
            Config.CustomFishes.Add(newRule);
            Config.AutoDesc();
            Config.Write();

            string condText = fcond.Count > 0 ? $" 条件: {string.Join(", ", fcond)}" : "";
            TShock.Utils.Broadcast($"管理员 [c/47D3C3:{plr.Name}] 添加了自定渔获npc {npcName} 概率:1/{Chance}{condText}", color);
        }
    }

    //  显示NPC渔获管理帮助
    private static void NpcHelp(TSPlayer plr)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[c/47D3C3:自定义渔获 - NPC管理帮助]");
        sb.AppendLine($"[c/55CDFF:•] /{afm} npc - 显示本帮助");
        sb.AppendLine($"[c/55CDFF:•] /{afm} npc ls - 列出附近85格内NPC");
        sb.AppendLine($"[c/55CDFF:•] /{afm} npc id - 添加/移除NPC");
        sb.AppendLine($"[c/55CDFF:•] /{afm} npc id [概率] [条件] - 添加并设置参数");
        sb.AppendLine($"[c/FFFF00:提示] 只写怪物id 存在移除,不在添加");
        sb.AppendLine($"[c/FFFF00:提示] 列出附近npc时名字为 红=不在 | 绿=存在");
        sb.AppendLine($"[c/FFFF00:示例] /{afm} npc 586 50 血月,肉后");
        plr.SendMessage(TextGradient(sb.ToString()), color);
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
        int total = (int)MathF.Ceiling((float)(conds.Count / (double)perPage));
        if (page > total) page = total;

        int start = (page - 1) * perPage;
        int end = (int)MathF.Min(start + perPage, conds.Count);

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

        int perPage = 10;
        int total = (int)MathF.Ceiling((float)(Config.CustomFishes.Count / (double)perPage));
        if (page > total) page = total;

        int start = (page - 1) * perPage;
        int end = (int)MathF.Min(start + perPage, Config.CustomFishes.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"[c/47D3C3:自定义渔获列表 (第{page}/{total}页)]");

        for (int i = start; i < end; i++)
        {
            var rule = Config.CustomFishes[i];

            // 构造显示文本
            string icon;
            if (rule.ItemType > 0)
            {
                icon = $"[i/s1:{rule.ItemType}]";
            }
            else if (rule.NPCType > 0)
            {
                icon = $"{Lang.GetNPCNameValue(rule.NPCType)}";
            }
            else
            {
                icon = "[c/FF5555:无]";
            }

            var Desc = string.Empty;
            if (!string.IsNullOrEmpty(rule.Desc))
                Desc = rule.Desc;

            sb.AppendLine($"[c/55CDFF:{i + 1:00}.] {icon} - {Desc}");


        }

        if (total > 1)
        {
            sb.AppendLine($"[c/FFFF00:输入 /{afm} lt {page + 1} 查看下一页]");
        }
        plr.SendMessage(TextGradient(sb.ToString()), color);
    }
    #endregion

}