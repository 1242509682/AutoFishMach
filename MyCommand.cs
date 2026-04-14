using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
using static FishMach.AfmPlrMag;
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

                    // 如果玩家已经在钓鱼机区域内，提示不能重复创建
                    if (plr.CurrentRegion != null && IsAfmRegion(plr.CurrentRegion.Name))
                    {
                        plr.SendMessage(Grad("\n你已经在钓鱼机区域内，不能在此创建新的钓鱼机。"), color);
                        return;
                    }

                    var afmPly = GetPlrData(plr.Name);

                    // 设置自定义数据标记，表示玩家正在等待打开箱子
                    if (plr.ActiveChest == -1)
                    {
                        afmPly.SetFlag = true;
                        plr.SendMessage(Grad("\n请打开一个箱子作为自动钓鱼机...\n"), color);
                        return;
                    }

                    var chest = Main.chest[plr.ActiveChest];
                    if (chest == null) return;
                    var pos = new Point(chest.x, chest.y);
                    var data = DataManager.FindChest(plr.ActiveChest);
                    if (data == null)
                        CreateData(plr, plr.ActiveChest, pos);
                    else
                        plr.SendMessage(Grad("\n该位置已有钓鱼机...\n"), color);
                }
                break;

            case "ls":
            case "list":
            case "列表":
                {
                    var all = Machines;
                    if (all.Count == 0)
                    {
                        plr.SendMessage(Grad("没有自动钓鱼机"), color);
                        return;
                    }
                    ListMach(plr, all);
                }
                break;

            case "if":
            case "info":
            case "信息":
                {
                    if (!plr.RealPlayer) return;

                    // 1. 如果玩家当前打开了箱子，优先检查该箱子是否为钓鱼机
                    if (plr.ActiveChest != -1)
                    {
                        var data = FindChest(plr.ActiveChest);
                        if (data != null)
                        {
                            ShowMachineInfo(plr, data);
                            return;
                        }
                    }

                    // 2. 如果玩家在钓鱼机区域内，显示当前区域的钓鱼机信息
                    if (plr.CurrentRegion != null &&
                        IsAfmRegion(plr.CurrentRegion.Name))
                    {
                        var data = FindRegion(plr.CurrentRegion.Name);
                        if (data != null)
                        {
                            ShowMachineInfo(plr, data);
                            return;
                        }
                    }

                    // 3. 否则设置标记，等待玩家打开箱子
                    GetPlrData(plr.Name).InfoFlag = true;
                    plr.SendMessage(Grad("请打开要查看的钓鱼箱...\n"), color);
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

                    // 如果已经同步了 则不再需要打开箱子
                    if (EnvManager.SyncForCmd(plr)) return;

                    // 不在区域内，让玩家打开箱子
                    GetPlrData(plr.Name).SyncFlag = true;
                    plr.SendMessage(Grad("请打开要同步数据的钓鱼机箱子..."), color);
                }
                break;

            case "e":
            case "edit":
            case "设置":
                HandleEdit(args, plr);
                break;

            case "tp":
            case "warp":
            case "传送":
                HandleTP(args, plr);
                break;

            case "o":
            case "out":
            case "sc":
            case "传输":
                HandleOut(args);
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

            case "ex":
            case "exc":
            case "exclude":
            case "排除":
                HandleExcl(args, plr);
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
            mess.AppendLine($"/{afm} e - 修改钓鱼机设置");
            mess.AppendLine($"/{afm} o - 批量修改传输箱");

            if (Config.Teleport || IsAdmin(plr))
                mess.AppendLine($"/{afm} tp - 传送到指定钓鱼机");

            mess.AppendLine($"/{afm} if - 获取钓鱼机信息");
            mess.AppendLine($"/{afm} sv - 同步更新钓鱼机");
            mess.AppendLine($"/{afm} ls - 列出所有钓鱼机");
            mess.AppendLine($"/{afm} lt - 查看自定渔获");
            mess.AppendLine($"/{afm} ex - 修改排除物品表");

            if (IsAdmin(plr))
            {
                mess.AppendLine($"/{afm} cd - 查看自定渔获条件");
                mess.AppendLine($"/{afm} i - 修改自定渔获物品");
                mess.AppendLine($"/{afm} npc - 修改自定渔获怪物");
                mess.AppendLine($"/{afm} rs - 重置插件数据");
            }

            plr.SendMessage(Grad(mess.ToString()), color);

            // 手游专供提示
            PeText(plr);
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
        var (basePower, finalPower, luckText) = GetFishingPower(data);
        var (rodInfo, baitInfo) = GetRodBaitInfo(data);
        string envStr = GetEnvString(data);
        string customBuff = GetCustomBuffString(data, true);

        // 水体质量需求
        int waterNeeded = (int)(300f * data.atmo);
        int effectiveWater = data.MaxLiq;
        if (data.LiqName == "蜂蜜") effectiveWater = (int)(effectiveWater * 1.5);
        float waterQuality = MathF.Min(1f, (float)effectiveWater / waterNeeded);
        // 消息构建
        var mess = new StringBuilder();
        mess.AppendLine($"\n[c/E8EB6E:{data.Owner}] 钓鱼机 [c/ED756F:{data.ChestIndex}] [c/75D1FF:{data.Players.Count}] 人");
        mess.AppendLine($"{rodInfo} {baitInfo}");
        mess.AppendLine($"鱼池:{data.LiqName} [c/61BFE2:{data.MaxLiq}]格");
        mess.AppendLine($"附近 水 [c/61BFE2:{data.WaterCount}]格 岩浆 [c/FF716D:{data.LavaCount}]格 蜂蜜 [c/FFE46D:{data.HoneyCount}]格");
        mess.AppendLine($"液体需求:[c/FF716D:{Config.NeedLiqStack}] 液体质量:[c/FFA866:{waterQuality:P0}]");
        if (Math.Abs(data.luck) > 0.001f)
            mess.AppendLine($"幸运值:[c/61E26C:{data.luck:F2}] (影响渔力±10%~40%)");
        mess.AppendLine($"基础渔力:[c/61E26C:{basePower}]点 实际渔力:{luckText}");

        // 收集需要显示的条件文本（不包括固定显示的区域范围）
        var items = new List<string>();

        if (data.CanFishInLava) items.Add("熔岩钓鱼:是");
        if (data.HasTackle) items.Add("节省鱼饵:是");
        if (Config.NeedWiring) items.Add("需要电路:是");
        if (data.QuestFish) items.Add("钓任务鱼:是");
        if (Config.WhenEmpty) items.Add("无人关闭:是");
        if (data.HasOut)
        {
            string limit = Config.MaxOutChest > 0 ? $"/{Config.MaxOutChest}" : string.Empty;
            mess.AppendLine($"传输箱: {data.OutChests.Count}{limit}个");
        }
        if (data.CustomNPC) items.Add("允许钓怪:是");
        if (data.SoloMonster)
        {
            items.Add("禁钓多怪:是");
            items.Add($"禁怪模式:{(data.SoloMode ? "只钓一个" : "相同不钓")}");
        }

        if (Config.RegionSafe)
        {
            if (data.Safe) items.Add($"怪物防护:{(data.Repel ? "击退" : "清除")}");
            if (data.Repel) items.Add($"击退力度:{data.Power}");
            if (data.Friendly) items.Add("防友好npc:是");
            if (data.Statue) items.Add("防雕像怪:是");
        }

        // 每两个条件合并为一行
        for (int i = 0; i < items.Count; i += 2)
        {
            string line = items[i];
            if (i + 1 < items.Count)
                line += " " + items[i + 1];
            mess.AppendLine(line);
        }

        // 区域保护 + 区域范围（区域范围始终显示）
        string regionProtect = Config.RegionBuild ? "区域保护:是 " : "";
        mess.AppendLine($"{regionProtect}区域范围:[c/61BCE3:{Config.Range}]格".Trim());

        // 药水剩余时间
        if (data.CratePotionTime > DateTime.UtcNow)
            mess.AppendLine($"宝匣药水:剩余[c/61E278:{FormatRemaining((data.CratePotionTime - DateTime.UtcNow).TotalMinutes)}]");
        if (data.FishingPotionTime > DateTime.UtcNow)
            mess.AppendLine($"钓鱼药水:剩余[c/61BBE2:{FormatRemaining((data.FishingPotionTime - DateTime.UtcNow).TotalMinutes)}]");
        if (data.ChumBucketTime > DateTime.UtcNow)
            mess.AppendLine($"鱼饵桶:剩余[c/FF766D:{FormatRemaining((data.ChumBucketTime - DateTime.UtcNow).TotalMinutes)}]");

        mess.AppendLine($"[c/63D475:环境]:{envStr}");
        plr.SendMessage(Grad(mess.ToString()), color2);

        // 缺失用品信息
        FixTip(data, plr);

        // 物品排除表
        var lines = data.Exclude.Select((id, i) => new { id, i })
            .GroupBy(x => x.i / 6)
            .Select(g => string.Join(" ", g.Select(x => Icon(x.id))));
        var text = data.Exclude.Count > 0 ? $"[c/FFA500:排除表:]\n{string.Join("\n", lines)}" : string.Empty;
        if (!string.IsNullOrEmpty(text))
            plr.SendMessage(text, color2);

        // 区域增益
        if (!string.IsNullOrEmpty(customBuff))
            plr.SendMessage("\n" + Grad(customBuff), color2);
    }
    #endregion

    #region 进入区域信息
    public static void RegionInfo(TSPlayer plr, MachData data)
    {
        // 确保物品和液体缓存为最新
        EnvManager.SyncItem(data);
        EnvManager.SyncLiquid(data);

        var (basePower, finalPower, luckText) = GetFishingPower(data);
        var (rodInfo, baitInfo) = GetRodBaitInfo(data);
        string envStr = GetEnvString(data);
        string customBuff = GetCustomBuffString(data, false);

        // 欢迎消息
        plr.SendMessage(Grad($"\n欢迎来到钓鱼机 [c/ED756F:{data.ChestIndex}] [c/75D1FF:{data.Players.Count}] 人"), color);
        plr.SendMessage($"归属 [c/E8EB6E:{data.Owner}] {rodInfo} {baitInfo}", color2);
        plr.SendMessage(Grad($"环境 {envStr}"), color);
        plr.SendMessage(Grad($"鱼池 {data.LiqName} [c/61BFE2:{data.MaxLiq}] 格 渔力 {basePower}({luckText})"), color);

        if (Config.RegionSafe && data.Safe)
        {
            string mode = data.Repel ? $"击退 力度 {data.Power}" : "清除";
            plr.SendMessage(Grad($"怪物防护: {mode}"), color);
        }

        if (data.HasOut)
        {
            string limit = Config.MaxOutChest > 0 ? $"/{Config.MaxOutChest}" : string.Empty;
            plr.SendMessage(Grad($"传输箱 {data.OutChests.Count}{limit}个"), color);
        }

        // 物品排除表
        var lines = data.Exclude.Select((id, i) => new { id, i })
            .GroupBy(x => x.i / 6)
            .Select(g => string.Join(" ", g.Select(x => Icon(x.id))));
        var text = data.Exclude.Count > 0 ? $"[c/FFA500:排除表:]\n{string.Join("\n", lines)}" : string.Empty;
        if (!string.IsNullOrEmpty(text))
            plr.SendMessage(text, color2);

        // 区域增益
        if (!string.IsNullOrEmpty(customBuff))
            plr.SendMessage(customBuff, color);
    }
    #endregion

    #region 辅助方法（渔力计算、信息获取）
    private static (int basePower, int finalPower, string luckText) GetFishingPower(MachData data)
    {
        // 基础渔力：鱼竿+鱼饵
        int rodPower = 0, baitPower = 0;
        if (data.ChestIndex >= 0 && data.ChestIndex < Main.chest.Length)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                if (data.RodSlot >= 0 && data.RodSlot < chest.item.Length)
                {
                    var rodItem = chest.item[data.RodSlot];
                    if (rodItem != null && !rodItem.IsAir)
                        rodPower = rodItem.fishingPole;
                }
                if (data.BaitSlot >= 0 && data.BaitSlot < chest.item.Length)
                {
                    var baitItem = chest.item[data.BaitSlot];
                    if (baitItem != null && !baitItem.IsAir)
                        baitPower = baitItem.bait;
                }
            }
        }
        int basePower = rodPower + baitPower + data.ExtraPower;

        // 临时加成（钓鱼药水、鱼饵桶、自定义物品）
        int tempPower = 0;
        if (DateTime.UtcNow < data.FishingPotionTime) tempPower += Config.FishingPotionPower;
        if (DateTime.UtcNow < data.ChumBucketTime) tempPower += Config.ChumBucketPower;
        foreach (var item in Config.CustomUsedItem)
            if (data.Custom.TryGetValue(item.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
                tempPower += state.Bonus;

        int totalPower = basePower + tempPower;

        // 水体质量修正
        int effectiveWater = data.MaxLiq;
        if (data.LiqName == "蜂蜜") effectiveWater = (int)(effectiveWater * 1.5);
        int waterNeeded = (int)(300f * data.atmo);
        float waterQuality = MathF.Min(1f, (float)effectiveWater / waterNeeded);
        int finalPower = (int)(totalPower * waterQuality);

        // 幸运影响
        int luckMin = finalPower, luckMax = finalPower;
        if (data.luck > 0.001f)
            luckMax = (int)(finalPower * 1.4);
        else if (data.luck < -0.001f)
            luckMin = (int)(finalPower * 0.6);
        string luckText = Math.Abs(data.luck) > 0.001f ? $"[c/FFAA6D:{luckMin}~{luckMax}]" : $"[c/FFAA6D:{finalPower}]";

        return (basePower, finalPower, luckText);
    }

    private static (string rodInfo, string baitInfo) GetRodBaitInfo(MachData data)
    {
        int rodType = -1, baitType = -1, baitStack = 0;
        if (data.ChestIndex >= 0 && data.ChestIndex < Main.chest.Length)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                if (data.RodSlot >= 0 && data.RodSlot < chest.item.Length)
                {
                    var rodItem = chest.item[data.RodSlot];
                    if (rodItem != null && !rodItem.IsAir)
                        rodType = rodItem.type;
                }
                if (data.BaitSlot >= 0 && data.BaitSlot < chest.item.Length)
                {
                    var baitItem = chest.item[data.BaitSlot];
                    if (baitItem != null && !baitItem.IsAir)
                    {
                        baitType = baitItem.type;
                        baitStack = baitItem.stack;
                    }
                }
            }
        }
        string rodInfo = rodType > 0 ? $"鱼竿:{Icon(rodType)}" : "鱼竿:无";
        string baitInfo = baitType > 0 ? $"鱼饵:{Icon(baitType, baitStack)}" : "鱼饵:无";
        return (rodInfo, baitInfo);
    }

    private static string GetEnvString(MachData data)
    {
        var env = new List<string>();
        if (data.ZoneHallow) env.Add("神圣");
        if (data.ZoneCorrupt) env.Add("腐化");
        if (data.ZoneCrimson) env.Add("猩红");
        if (data.ZoneJungle) env.Add("丛林");
        if (data.ZoneSnow) env.Add("雪原");
        if (data.ZoneDesert) env.Add("沙漠");
        if (data.ZoneBeach) env.Add("海洋");
        if (data.ZoneDungeon) env.Add("地牢");
        if (data.ZoneShimmer) env.Add("微光");
        if (data.ZoneSandstorm) env.Add("沙尘暴");
        if (data.ZoneShadowCandle) env.Add("影烛");
        if (data.ZoneWaterCandle) env.Add("水蜡烛");
        if (data.ZonePeaceCandle) env.Add("和平蜡烛");
        if (data.ZoneGraveyard) env.Add("墓地");
        if (data.ZoneGranite) env.Add("花岗岩");
        if (data.ZoneMarble) env.Add("大理石");
        if (data.ZoneMeteor) env.Add("陨石");
        if (data.ZoneGlowshroom) env.Add("蘑菇地");
        if (data.ZoneGemCave) env.Add("宝石洞");
        if (data.ZoneHive) env.Add("蜂巢");
        if (data.ZoneLihzhardTemple) env.Add("神庙");
        if (data.ZoneOldOneArmy) env.Add("旧日军团");
        if (data.ZoneTowerNebula) env.Add("星云柱");
        if (data.ZoneTowerSolar) env.Add("日耀柱");
        if (data.ZoneTowerStardust) env.Add("星尘柱");
        if (data.ZoneTowerVortex) env.Add("星旋柱");
        if (data.ZoneUndergroundDesert) env.Add("地下沙漠");
        if (data.RolledRemixOcean) env.Add("颠倒海洋");

        string height = GetHeightName(data.HeightLevel);
        return $"{height},{string.Join(",", env)}";
    }

    private static string GetCustomBuffString(MachData data, bool cmd)
    {
        if (Config.CustomUsedItem.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        int idx = 1;
        foreach (var item in Config.CustomUsedItem)
        {
            if (data.Custom.TryGetValue(item.ItemType, out var state) && state.Expiry > DateTime.UtcNow)
            {
                double min = (state.Expiry - DateTime.UtcNow).TotalMinutes;
                var desc = cmd ? "\n[c/5F9DB8:-]" + item.BuffDesc : string.Empty;
                if (item.BuffID > 0)
                {
                    sb.AppendLine($"{idx}.{Utils.Icon(item.ItemType)} 剩余[c/61BBE2:{FormatRemaining(min)}] {desc}");
                    idx++;
                }
            }
        }
        return sb.ToString();
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
            plr.SendMessage(Grad(sb.ToString()), color);
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
        plr.SendMessage(Grad(sb.ToString()), color);
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
        plr.SendMessage(Grad(sb.ToString()), color);
    }
    #endregion

    #region 缺失提示
    public static void FixTip(MachData data, TSPlayer plr)
    {
        string text = GetMissItems(data);
        if (!string.IsNullOrEmpty(text))
            plr.SendMessage(Grad(text), color);
    }

    /// <summary>
    /// 获取钓鱼机缺失项的提示文字（如 "缺:液体,鱼竿,鱼饵"），若无缺失则返回空字符串
    /// </summary>
    private static string GetMissItems(MachData data)
    {
        var missing = new List<string>();

        // 液体不足
        if (data.MaxLiq < Config.NeedLiqStack)
            missing.Add("液体");


        // 岩浆能力缺失（仅当鱼池为岩浆时）
        if (data.LiqType == LiquidID.Lava && !data.CanFishInLava)
            missing.Add("熔岩用品");

        // 鱼竿缺失
        bool hasRod = false;
        if (data.RodSlot >= 0)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                var rodItem = chest.item[data.RodSlot];
                if (rodItem != null && !rodItem.IsAir && rodItem.fishingPole > 0)
                    hasRod = true;
            }
        }
        if (!hasRod)
            missing.Add("鱼竿");

        // 鱼饵缺失
        bool hasBait = false;
        if (data.BaitSlot >= 0)
        {
            var chest = Main.chest[data.ChestIndex];
            if (chest != null)
            {
                var baitItem = chest.item[data.BaitSlot];
                if (baitItem != null && !baitItem.IsAir && baitItem.bait > 0)
                    hasBait = true;
            }
        }
        if (!hasBait)
            missing.Add("鱼饵");

        // 电路缺失
        if (Config.NeedWiring)
        {
            if (!HasWiring(data.Pos))
                missing.Add("电线");
            else if (!data.Wiring)
                missing.Add("电路");
        }

        if (missing.Count == 0)
            return string.Empty;

        return $"[c/FE6352:缺失] {string.Join(" ", missing)}";
    }
    #endregion

    #region 钓鱼机列表方法
    private static void ListMach(TSPlayer plr, List<MachData> all)
    {
        var idx = 1;
        var sb = new StringBuilder();
        sb.AppendLine("\n《自动钓鱼机列表》");
        foreach (var data in all)
        {
            var outMod = data.HasOut ? $" [{data.OutChests.Count}] " : string.Empty;

            var env = new List<string>();
            var env2 = GetHeightName(data.HeightLevel);
            if (data.ZoneHallow) env.Add("神圣");
            if (data.ZoneCorrupt) env.Add("腐化");
            if (data.ZoneCrimson) env.Add("猩红");
            if (data.ZoneJungle) env.Add("丛林");
            if (data.ZoneSnow) env.Add("雪原");
            if (data.ZoneDesert) env.Add("沙漠");
            if (data.ZoneBeach) env.Add("海洋");
            if (data.ZoneDungeon) env.Add("地牢");
            if (data.RolledRemixOcean) env.Add("颠倒海洋");
            outMod += $"\n - [c/F4FB64:环境] {env2} {string.Join(" ", env)}";

            // 显示缺失项（如果有）
            string miss = GetMissItems(data);
            if (!string.IsNullOrEmpty(miss))
                outMod += ($"\n - {miss}");


            sb.AppendLine($" [c/F4FB64:{idx}].{data.Owner}的[c/ED756F:{data.ChestIndex}] {outMod}");

            idx++;
        }
        plr.SendMessage(Grad(sb.ToString()), color);
    }
    #endregion

    #region 处理传送指令
    private static void HandleTP(CommandArgs args, TSPlayer plr)
    {
        if (!plr.RealPlayer)
        {
            plr.SendErrorMessage("请进入游戏后使用指令");
            return;
        }

        if (!Config.Teleport && !IsAdmin(plr))
        {
            plr.SendMessage(Grad("传送功能已被管理员禁用"), color);
            return;
        }

        if (args.Parameters.Count < 2)
        {
            List<MachData> all = Machines;
            if (all.Count == 0)
            {
                plr.SendMessage(Grad("没有自动钓鱼机"), color);
                return;
            }

            ListMach(plr, all);
            plr.SendMessage(Grad("指定钓鱼机: /afm tp 1"), color);
            plr.SendMessage(Grad("循环传输箱: /afm tp 1 c"), color);
            plr.SendMessage(Grad("指定传输箱: /afm tp 1 c 2\n"), color);
            return;
        }

        // 解析序号（1-based）
        if (!int.TryParse(args.Parameters[1], out int idx) || idx < 1 || idx > Machines.Count)
        {
            plr.SendErrorMessage($"序号无效，请输入 1 ~ {Machines.Count} 之间的数字");
            return;
        }

        var data = Machines[idx - 1];  // 通过序号获取钓鱼机
        if (data == null)
        {
            plr.SendErrorMessage("获取钓鱼机数据失败");
            return;
        }

        // 检查是否有第三个参数
        if (args.Parameters.Count >= 3)
        {
            string third = args.Parameters[2].ToLower();

            // 循环传送模式
            if (third == "c")
            {
                if (data.OutChests.Count == 0)
                {
                    plr.SendErrorMessage("\n该钓鱼机未设置任何传输箱");
                    return;
                }

                var afmPly = GetPlrData(plr.Name);
                if (!afmPly.TpIdx.TryGetValue(idx, out int cur) || cur >= data.OutChests.Count)
                    cur = 0;

                int targetOut = data.OutChests[cur];
                var outChest = Main.chest[targetOut];
                if (outChest == null)
                {
                    plr.SendErrorMessage("\n传输箱已不存在");
                    DataManager.RemoveOutChest(data, targetOut);
                    return;
                }

                int targetX = outChest.x * 16 + 16;
                int targetY = (outChest.y - 2) * 16;
                if (plr.Teleport(targetX, targetY))
                {
                    plr.SendMessage(Grad($"\n已传送 [{cur + 1}/{data.OutChests.Count}] 个传输箱"), color);
                    afmPly.TpIdx[idx] = cur + 1;
                }
                return;
            }

            // 尝试解析为整数（输出箱序号）
            if (int.TryParse(third, out int outIdx) && outIdx >= 1 && outIdx <= data.OutChests.Count)
            {
                int targetOut = data.OutChests[outIdx - 1];
                var outChest = Main.chest[targetOut];
                if (outChest == null)
                {
                    plr.SendErrorMessage("\n传输箱已不存在");
                    DataManager.RemoveOutChest(data, targetOut);
                    return;
                }

                int targetX = outChest.x * 16 + 16;
                int targetY = (outChest.y - 2) * 16;
                if (plr.Teleport(targetX, targetY))
                {
                    plr.SendMessage(Grad($"\n已传送第 {outIdx} 个传输箱"), color);
                }
                return;
            }

            // 无效参数，提示帮助
            plr.SendMessage(Grad($"\n用法: /afm tp 序号 [c或序号]"), color);
            return;
        }

        // 无第三个参数，传送到主箱正上方 2 格
        int mainX = data.Pos.X * 16 + 16;
        int mainY = (data.Pos.Y - 2) * 16;
        if (plr.Teleport(mainX, mainY))
            plr.SendMessage(Grad($"\n已传送钓鱼机: [c/ED756F:{data.ChestIndex}]"), color);
    }
    #endregion

    #region 批量添加排除物品
    private static void HandleExcl(CommandArgs args, TSPlayer plr)
    {
        if (!TryGetData(plr, out var data, out var err))
        {
            plr.SendMessage(Grad(err), color);
            return;
        }

        if (data == null) return;

        // 必须通过打开箱子操作（不能仅靠区域）
        if (plr.ActiveChest == -1)
        {
            plr.SendMessage(Grad("请打开要操作的箱子(可以不是钓鱼箱)"), color);
            return;
        }

        var chest = Main.chest[plr.ActiveChest];
        if (chest == null) return;

        int added = 0, removed = 0;
        foreach (var item in chest.item)
        {
            if (item == null || item.IsAir) continue;
            if (data.Exclude.Contains(item.type))
            {
                data.Exclude.Remove(item.type);
                removed++;
            }
            else
            {
                data.Exclude.Add(item.type);
                added++;
            }
        }

        if (added == 0 && removed == 0)
        {
            plr.SendMessage(Grad("箱子中没有可操作的物品"), color);
            return;
        }

        Save(data);
        plr.SendMessage(Grad($"已添加 {added} 个，移除 {removed} 个物品"), color);

        var lines = data.Exclude.Select((id, i) => new { id, i })
            .GroupBy(x => x.i / 6)
            .Select(g => string.Join(" ", g.Select(x => Icon(x.id))));
        var text = data.Exclude.Count > 0 ? $"[c/FFA500:排除表:]\n{string.Join("\n", lines)}" : string.Empty;
        if (!string.IsNullOrEmpty(text))
            plr.SendMessage(text, color2);
    }
    #endregion

    #region 批量设置传输箱指令
    private static void HandleOut(CommandArgs args)
    {
        var plr = args.Player;
        var afmPly = GetPlrData(plr.Name);
        if (afmPly.CurSel != null)
        {
            afmPly.CurSel = null;
            plr.SendMessage(Grad("\n已退出传输箱修改"), color);
            return;
        }

        if (!Config.TransferMode && !IsAdmin(plr))
        {
            plr.SendMessage(Grad("传输模式已被管理员禁用"), color);
            return;
        }

        if (!TryGetData(plr, out var data, out var err))
        {
            plr.SendErrorMessage(err);
            // 额外引导
            if (Config.Teleport || IsAdmin(plr))
                plr.SendMessage(Grad($"查看已有传送机:/{afm} tp"), color);
            return;
        }

        if (data == null) return;

        afmPly.CurSel = new SelData(data.RegName, Config.SelTimer);
        plr.SendMessage(Grad($"\n请在{Config.SelTimer} 秒内打开任意箱子"), color);
        plr.SendMessage(Grad($"如需取消修改,再次输入: /{afm} o"), color);
    }
    #endregion

    #region 编辑钓鱼机设置
    private static void HandleEdit(CommandArgs args, TSPlayer plr)
    {
        if (!TryGetData(plr, out var data, out var err))
        {
            plr.SendErrorMessage(err);
            return;
        }

        if (data == null) return;

        if (args.Parameters.Count == 1)
        {
            // 显示当前设置
            ShowEditInfo(plr, data);
            return;
        }

        string sub = args.Parameters[1].ToLower();
        switch (sub)
        {
            case "q":
            case "quest":
                data.QuestFish = !data.QuestFish;
                Save(data);
                plr.SendMessage(Grad($"钓任务鱼: {(data.QuestFish ? "开启" : "关闭")}"), color);
                break;

            case "n":
            case "npc":
                data.CustomNPC = !data.CustomNPC;
                Save(data);
                plr.SendMessage(Grad($"允许钓怪: {(data.CustomNPC ? "开启" : "关闭")}"), color);
                break;

            case "so":
            case "solo":
                // 处理 solo 子命令
                if (args.Parameters.Count >= 3 && (args.Parameters[2].ToLower() == "m" || args.Parameters[2].ToLower() == "mod"))
                {
                    // 切换模式
                    data.SoloMode = !data.SoloMode;
                    Save(data);
                    string modeDesc = data.SoloMode ? "只钓一个" : "相同不钓";
                    plr.SendMessage(Grad($"禁钓模式已切换: {modeDesc}"), color);
                }
                else
                {
                    // 切换开关
                    data.SoloMonster = !data.SoloMonster;
                    Save(data);
                    plr.SendMessage(Grad($"禁钓已有怪物: {(data.SoloMonster ? "开启" : "关闭")}"), color);
                }
                break;

            case "sf":
            case "safe":
                HandleSafeMode(args, plr, data);
                break;

            default:
                ShowEditInfo(plr, data);
                break;
        }
    }

    private static void ShowEditInfo(TSPlayer plr, MachData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n{data.Owner}的[c/47D3C3:钓鱼机设置] [c/ED756F:{data.ChestIndex}]");
        sb.AppendLine($"钓任务鱼: /{afm} e q [{(data.QuestFish ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");
        sb.AppendLine($"允许钓怪: /{afm} e n [{(data.CustomNPC ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");

        sb.AppendLine($"禁钓已有: /{afm} e so [{(data.SoloMonster ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");
        sb.AppendLine($"禁钓模式: /{afm} e so mod [{(data.SoloMode ? "只钓一个" : "相同不钓")}]");

        if (Config.RegionSafe || IsAdmin(plr))
        {
            sb.AppendLine($"怪物防护: /{afm} e sf [{(data.Safe ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");
            sb.AppendLine($"防护类型: /{afm} e sf r [{(data.Repel ? "[c/61E26C:击退]" : "[c/FF716D:清除]")}]");
            if (data.Repel)
                sb.AppendLine($"击退力度: /{afm} e sf p [{data.Power}]");
            sb.AppendLine($"防雕像怪: /{afm} e sf s [{(data.Statue ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");
            sb.AppendLine($"防友好npc: /{afm} e sf f [{(data.Friendly ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}]");
        }

        plr.SendMessage(Grad(sb.ToString()), color);
    }
    #endregion

    #region 尝试获取玩家当前操作的钓鱼机（优先打开箱子，否则根据区域）
    private static bool TryGetData(TSPlayer plr, out MachData? data, out string error)
    {
        data = null;
        error = string.Empty;

        if (!plr.RealPlayer)
        {
            error = "请进入游戏后使用指令";
            return false;
        }

        // 1. 优先检查是否打开了箱子且是钓鱼机
        if (plr.ActiveChest != -1)
        {
            data = FindChest(plr.ActiveChest);
            if (data != null)
            {
                if (Config.RegionBuild && !IsAdmin(plr) && data.Owner != plr.Name)
                {
                    error = $"你没有权限修改 {data.Owner} 的钓鱼机";
                    data = null;
                    return false;
                }
                return true;
            }
        }

        // 2. 检查是否在钓鱼机区域内
        if (plr.CurrentRegion != null && IsAfmRegion(plr.CurrentRegion.Name))
        {
            data = FindRegion(plr.CurrentRegion.Name);
            if (data != null)
            {
                if (Config.RegionBuild && !IsAdmin(plr) && data.Owner != plr.Name)
                {
                    error = $"你没有权限修改 {data.Owner} 的钓鱼机";
                    data = null;
                    return false;
                }
                return true;
            }
            error = "当前区域未找到钓鱼机数据";
            return false;
        }

        error = "请先打开钓鱼机箱子或进入钓鱼机区域";
        return false;
    }
    #endregion

    #region 处理防护模式
    private static void HandleSafeMode(CommandArgs args, TSPlayer plr, MachData data)
    {
        if (!Config.RegionSafe && !IsAdmin(plr))
        {
            plr.SendMessage(Grad("怪物防护功能已被管理员禁用"), color);
            return;
        }

        if (args.Parameters.Count == 2)
        {
            // 切换开关
            data.Safe = !data.Safe;
            Save(data);
            plr.SendMessage(Grad($"怪物防护已{(data.Safe ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}"), color);
            return;
        }

        string sub = args.Parameters[2].ToLower();
        switch (sub)
        {
            case "r":
            case "repel":
                data.Repel = !data.Repel;
                Save(data);
                plr.SendMessage(Grad($"防护类型: {(data.Repel ? "排斥" : "清除")}"), color);
                break;
            case "f":
            case "friend":
                data.Friendly = !data.Friendly;
                Save(data);
                plr.SendMessage(Grad($"防友好npc: {(data.Friendly ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}"), color);
                break;
            case "s":
            case "statue":
                data.Statue = !data.Statue;
                Save(data);
                plr.SendMessage(Grad($"防雕像怪: {(data.Statue ? "[c/61E26C:开启]" : "[c/FF716D:关闭]")}"), color);
                break;
            case "p":
            case "power":
                {
                    if (args.Parameters.Count < 4)
                    {
                        plr.SendMessage(Grad($"当前力度: {data.Power}"), color);
                        plr.SendMessage(Grad($"设置力度: /{afm} e sf p 数值"), color);
                        return;
                    }
                    if (!float.TryParse(args.Parameters[3], out float power) || power <= 0 || power > 100)
                    {
                        plr.SendMessage(Grad("力度请输入 1~100 之间的数字"), color);
                        return;
                    }
                    data.Power = power;
                    Save(data);
                    plr.SendMessage(Grad($"击退力度已设为: {power}"), color);
                }
                break;
            default:
                break;
        }
    }
    #endregion

}