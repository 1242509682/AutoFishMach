using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using TShockAPI;
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
                    // 设置自定义数据标记，表示玩家正在等待打开箱子
                    plr.SetData("set", true);
                    plr.SendMessage(TextGradient("请打开一个箱子作为自动钓鱼机..."), color);
                }
                break;

            case "ls":
            case "list":
            case "列表":
                {
                    var all = Machines;
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
                        int baitCount = AutoFishing.GetBaitCount(data.ChestIndex);
                        string baitInfo = baitCount > 0 ? $"鱼饵:{baitCount}" : "鱼饵:无";
                        string rodName = AutoFishing.GetRodName(data); // 动态获取鱼竿名称

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
                    plr.SetData("info", true);
                    plr.SendMessage(TextGradient("请打开要查看的钓鱼箱..."), color);
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

                    plr.SetData("exc", true);
                    plr.SendMessage(TextGradient("请打开要修改排除列表的钓鱼箱..."), color);
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
            plr.SendMessage($"/{afm} ls - 列出自钓机表", color);
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
            mess.AppendLine($"/{afm} s - 打开箱子设为自钓机");
            mess.AppendLine($"/{afm} sv - 打开箱子添加/移除选择的自钓机");
            mess.AppendLine($"/{afm} if - 打开箱子获取自钓机信息");
            mess.AppendLine($"/{afm} ls - 列出所有自钓机");
            mess.AppendLine($"/{afm} lt - 查看自定渔获");
            mess.AppendLine($"/{afm} exc - 打开箱子添加/移除手上排除物品");
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
        0 => "太空(0)",
        1 => "地表(1)",
        2 => "地下(2)",
        3 => "洞穴(3)",
        4 => "地狱(4)",
        _ => "未知"
    };
    #endregion

    #region 显示信息方法
    public static void ShowMachineInfo(TSPlayer plr, MachData data)
    {
        // 确保环境最新
        if (data.EnvDirty || (DateTime.UtcNow - data.LastEnvUpd).TotalSeconds > 5)
            EnvManager.RefreshEnv(data);

        // 实时统计水体（水、岩浆、蜂蜜）
        int all = 0, water = 0, lava = 0, honey = 0;
        EnvManager.NewGetLiquid(data, out water, out lava, out honey);
        all = water + lava + honey;

        // 大气因子
        float atmo = data.atmo;

        int waterNeeded = (int)(300f * atmo);
        float waterQuality = Math.Min(1f, (float)all / waterNeeded);

        // 基础渔力
        int basePower = Config.PowerChanceBonus;
        basePower += AutoFishing.GetRodPower(data);
        // 更新所有物品缓存，data.BonusTotal 会被设置
        EnvManager.UpdateMachineCache(data); 
        basePower += data.BonusTotal;

        int finalPower = (int)(basePower * waterQuality);

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
        plr.SendMessage($"高度等级:{GetHeightName(data.HeightLevel)}", color);
        plr.SendMessage($"大气因子:{atmo:F2}", color);
        plr.SendMessage($"水体统计: 水{water} 岩浆:{lava} 蜂蜜:{honey}", color);
        plr.SendMessage($"需水体量:{waterNeeded}", color);
        plr.SendMessage($"水体质量:{waterQuality:P0}", color);
        plr.SendMessage($"无饵渔力:{basePower}", color);
        plr.SendMessage($"含水渔力:{finalPower}", color);
        plr.SendMessage($"熔岩钓鱼:{(data.CanFishInLava ? "是" : "否")}", color);
        plr.SendMessage($"颠倒海洋:{(data.RolledRemixOcean ? "是" : "否")}", color);

        // 显示排除物品列表
        string excludeText = data.Exclude.Count > 0
            ? string.Join(", ", data.Exclude.Select(id => $"{ItemIcon(id, 1)}"))
            : "无";
        plr.SendMessage($"排除物品: {excludeText}", color);
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
            int dist = Math.Abs(npcX - playerPos.X) + Math.Abs(npcY - playerPos.Y); // 曼哈顿距离
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

        int perPage = 10;
        int total = (int)Math.Ceiling(Config.CustomFishes.Count / (double)perPage);
        if (page > total) page = total;

        int start = (page - 1) * perPage;
        int end = Math.Min(start + perPage, Config.CustomFishes.Count);

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