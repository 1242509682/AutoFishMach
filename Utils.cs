using Terraria;
using TShockAPI;
using System.Text;
using Terraria.ID;
using Terraria.Utilities;
using Microsoft.Xna.Framework;
using Terraria.GameContent.Events;
using System.Text.RegularExpressions;
using static FishMach.Plugin;

namespace FishMach;

internal class Utils
{
    #region 异步执行
    public static void Tack()
    {
        var task = Task.Run(delegate
        {
            // 写你的执行方法
        });

        task.ContinueWith(delegate
        {
            // 执行完毕后回到主线程
        });
    }
    #endregion

    #region 单色与随机色
    public static UnifiedRandom Random = Main.rand; // 随机器
    public static Color color => new(240, 250, 150); // 单色
    public static Color color2 => new(Random.Next(180, 250), // 随机色
                                      Random.Next(180, 250),
                                      Random.Next(180, 250));
    #endregion

    #region 逐行渐变色
    public static void GradMess(StringBuilder Text, TSPlayer? plr = null)
    {
        var mess = Text.ToString();
        var lines = mess.Split('\n');

        var GradMess = new StringBuilder();
        var start = new Color(166, 213, 234);
        var end = new Color(245, 247, 175);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrEmpty(lines[i]))
            {
                float ratio = (float)i / (lines.Length - 1);
                var gradColor = Color.Lerp(start, end, ratio);

                // 将颜色转换为十六进制格式
                string colorHex = $"{gradColor.R:X2}{gradColor.G:X2}{gradColor.B:X2}";

                // 使用颜色标签包装每一行
                GradMess.AppendLine($"[c/{colorHex}:{lines[i]}]");
            }
        }

        if (plr is not null)
        {
            if (plr.RealPlayer)
                plr.SendMessage(GradMess.ToString(), color);
            else
                plr.SendMessage(mess, color);
        }
    }
    #endregion

    #region 渐变色方法
    public static string TextGradient(string text, TSPlayer? plr = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = placeholder(text, plr);

        // 检查是否已包含颜色标签
        if (text.Contains("[c/"))
        {
            // 如果有颜色标签，保留它们并处理其他部分
            return MixedText(text);
        }
        else
        {
            // 如果没有颜色标签，直接应用渐变
            return ApplyGrad(text);
        }
    }
    #endregion

    #region 占位符替换方法(忽略大小写)
    private static string placeholder(string text, TSPlayer? plr)
    {
        if (plr != null)
        {
            text = Regex.Replace(text, @"\{玩家名\}", plr.Name, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{ip\}", plr.IP, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{uuid\}", plr.UUID, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{组名\}", plr.Account.Group, RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{账号\}", plr.Account.ID.ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{武器类型\}", GetWeapon(plr.SelectedItem), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{物品图标\}", ItemIcon(plr.SelectedItem.type), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{物品名\}", Lang.GetItemNameValue(plr.SelectedItem.type), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{生命\}", plr.TPlayer.statLife.ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{生命上限\}", plr.TPlayer.statLifeMax.ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{魔力\}", plr.TPlayer.statMana.ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{魔力上限\}", plr.TPlayer.statManaMax.ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{队伍\}", GetTeamCName(plr.Team), RegexOptions.IgnoreCase);

            // 同队人数
            if (Regex.IsMatch(text, @"\{同队人数\}", RegexOptions.IgnoreCase))
            {
                int teamCount = TShock.Players.Count(p => p != null && p.Active && p.Team == plr.Team);
                text = Regex.Replace(text, @"\{同队人数\}", teamCount.ToString(), RegexOptions.IgnoreCase);
            }

            // 同队玩家名称
            if (Regex.IsMatch(text, @"\{同队玩家\}", RegexOptions.IgnoreCase))
            {
                var TeamPlayer = TShock.Players
                    .Where(p => p != null && p.Active && p.Team == plr.Team)
                    .Select(p => p.Name);
                text = Regex.Replace(text, @"\{同队玩家\}",
                    string.Join(", ", TeamPlayer), RegexOptions.IgnoreCase);
            }

            // 别队人数
            if (Regex.IsMatch(text, @"\{别队人数\}", RegexOptions.IgnoreCase))
            {
                int otherTeamCount = TShock.Players
                    .Count(p => p != null && p.Active && p.Team != plr.Team);
                text = Regex.Replace(text, @"\{别队人数\}",
                    otherTeamCount.ToString(), RegexOptions.IgnoreCase);
            }
        }

        // 队伍统计
        if (Regex.IsMatch(text, @"\{队伍统计\}", RegexOptions.IgnoreCase))
        {
            var teamStats = new StringBuilder();
            for (int i = 0; i <= 5; i++)
            {
                int count = TShock.Players.Count(p => p != null && p.Active && p.Team == i);
                if (count > 0)
                {
                    teamStats.Append($"{GetTeamCName(i)}-{count}人 ");
                }
            }
            text = Regex.Replace(text, @"\{队伍统计\}", teamStats.ToString(), RegexOptions.IgnoreCase);
        }

        // 服务器名
        text = Regex.Replace(text, @"\{服务器名\}",
            TShock.Config.Settings.UseServerName ? TShock.Config.Settings.ServerName : Main.worldName,
            RegexOptions.IgnoreCase);

        // 在线人数
        text = Regex.Replace(text, @"\{在线人数\}",
            TShock.Utils.GetActivePlayerCount().ToString(),
            RegexOptions.IgnoreCase);

        // 服务器上限
        text = Regex.Replace(text, @"\{服务器上限\}",
            TShock.Config.Settings.MaxSlots.ToString(),
            RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"\{插件名\}",
            PluginName,
            RegexOptions.IgnoreCase);

        // 在线玩家
        if (Regex.IsMatch(text, @"\{在线玩家\}", RegexOptions.IgnoreCase))
        {
            var plrs = TShock.Players.Where(p => p != null && p.Active).Select(p => p.Name);
            string allPlayers = string.Join(", ", plrs);
            text = Regex.Replace(text, @"\{在线玩家\}", allPlayers, RegexOptions.IgnoreCase);
        }

        // 进度
        if (GetProgress().Count > 0)
            text = Regex.Replace(text, @"\{进度\}", string.Join(",", GetProgress()), RegexOptions.IgnoreCase);
        else
            text = Regex.Replace(text, @"\{进度\}", "无", RegexOptions.IgnoreCase);

        // 入侵事件
        if (Main.invasionType > 0)
            text = Regex.Replace(text, @"\{入侵\}", GetInvasionName(Main.invasionType), RegexOptions.IgnoreCase);
        else
            text = Regex.Replace(text, @"\{入侵\}", "无", RegexOptions.IgnoreCase);


        return text;
    }
    #endregion

    #region 混合文本（包含颜色标签、物品图标标签和普通文本）
    private static string MixedText(string text)
    {
        var res = new StringBuilder();

        // 匹配颜色标签 [c/颜色:文本] 或 物品图标标签 [i:物品ID] 或 [i/s数量:物品ID]
        var regex = new Regex(@"(\[c/([0-9a-fA-F]+):([^\]]+)\]|\[i(?:/s\d+)?:\d+\])");
        var matches = regex.Matches(text);

        if (matches.Count == 0)
            return ApplyGrad(text);

        int idx = 0;
        foreach (Match match in matches.Cast<Match>())
        {
            // 添加标签前的普通文本（应用渐变）
            if (match.Index > idx)
            {
                string plainText = text.Substring(idx, match.Index - idx);
                res.Append(ApplyGrad(plainText));
            }

            // 添加标签本身（保持不变）
            res.Append(match.Value);
            idx = match.Index + match.Length;
        }

        // 添加最后一个标签后的普通文本
        if (idx < text.Length)
        {
            string plainText = text.Substring(idx);
            res.Append(ApplyGrad(plainText));
        }

        return res.ToString();
    }
    #endregion

    #region 应用文本渐变方法
    private static string ApplyGrad(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var res = new StringBuilder();
        var start = new Color(166, 213, 234);
        var end = new Color(245, 247, 175);

        // 计算有效字符数（排除换行符）
        int cnt = 0;
        foreach (char c in text)
        {
            if (c != '\n' && c != '\r')
                cnt++;
        }

        // 如果没有有效字符，直接返回
        if (cnt == 0)
            return text;

        int idx = 0;

        foreach (char c in text)
        {
            if (c == '\n' || c == '\r')
            {
                res.Append(c);
                continue;
            }

            // 计算渐变比例
            float ratio = (float)idx / (cnt - 1);
            var clr = Color.Lerp(start, end, ratio);

            // 添加到结果
            res.Append($"[c/{clr.Hex3()}:{c}]");
            idx++;
        }

        return res.ToString();
    }
    #endregion

    #region 返回物品图标方法
    // 根据物品ID返回物品图标
    public static string ItemIcon(ItemID itemID) => ItemIcon(itemID);
    // 根据物品ID返回物品图标
    public static string ItemIcon(int itemID) => $"[i:{itemID}]";
    // 根据物品对象返回物品图标
    public static string ItemIcon(Item item) => ItemIcon(item.type, item.stack);
    // 返回带数量的物品图标
    public static string ItemIcon(int itemID, int stack = 1) => $"[i/s{stack}:{itemID}]";
    #endregion

    #region 获取入侵事件名称
    public static string GetInvasionName(int type)
    {
        return type switch
        {
            1 => "哥布林入侵",
            2 => "雪人军团",
            3 => "海盗入侵",
            4 => "火星暴乱",
            _ => "未知"
        };
    }
    #endregion

    #region 队伍名称映射
    public static string GetTeamCName(int teamId) => TeamColorMap.TryGetValue(teamId, out var name) ? name : "全体";
    private static readonly Dictionary<int, string> TeamColorMap = new()
    {
        { 0, "[c/5ADECE:白队]" },{ 1, "[c/F56470:红队]" },
        { 2, "[c/74E25C:绿队]" },{ 3, "[c/5A9DDE:蓝队]" },
        { 4, "[c/FCF466:黄队]" },{ 5, "[c/E15BC2:粉队]" }
    };
    #endregion

    #region 获取武器类型
    public static string GetWeapon(Item item)
    {
        var Held = item;
        if (Held == null || Held.type == 0) return "无";

        if (Held.melee && Held.damage > 0 && Held.ammo == 0 &&
            Held.pick < 1 && Held.hammer < 1 && Held.axe < 1) return "近战";

        if (Held.ranged && Held.damage > 0 && Held.ammo == 0 && !Held.consumable) return "远程";

        if (Held.magic && Held.damage > 0 && Held.ammo == 0) return "魔法";

        if (ItemID.Sets.SummonerWeaponThatScalesWithAttackSpeed[Held.type]) return "召唤";

        if (Held.maxStack == 9999 && Held.damage > 0 &&
            Held.ammo == 0 && Held.ranged && Held.consumable ||
            ItemID.Sets.ItemsThatCountAsBombsForDemolitionistToSpawn[Held.type]) return "投掷物";

        return "未知";
    }
    #endregion

    #region 获取进度
    public static List<string> GetProgress()
    {
        var prog = new List<string>();

        // 按照从高到低的进度检查
        if (NPC.downedMoonlord)
            prog.Add("月总");

        if (NPC.downedTowerNebula && NPC.downedTowerSolar && NPC.downedTowerStardust && NPC.downedTowerVortex)
        {
            prog.Add("四柱");
            prog.Remove("日耀");
            prog.Remove("星旋");
            prog.Remove("星尘");
            prog.Remove("星云");
        }
        else
        {
            if (NPC.downedTowerSolar)
                prog.Add("日耀");
            if (NPC.downedTowerVortex)
                prog.Add("星旋");
            if (NPC.downedTowerStardust)
                prog.Add("星尘");
            if (NPC.downedTowerNebula)
                prog.Add("星云");
        }

        if (NPC.downedAncientCultist)
            prog.Add("拜月");

        if (Terraria.GameContent.Events.DD2Event._spawnedBetsyT3)
            prog.Add("双足翼龙");

        if (NPC.downedMartians)
            prog.Add("火星");

        if (NPC.downedGolemBoss)
            prog.Add("石巨人");

        if (NPC.downedEmpressOfLight)
            prog.Add("光女");

        if (NPC.downedChristmasTree ||
            NPC.downedChristmasIceQueen ||
            NPC.downedChristmasSantank)
            prog.Add("霜月");

        if (NPC.downedHalloweenTree || NPC.downedHalloweenKing)
            prog.Add("南瓜月");

        if (NPC.downedPlantBoss)
            prog.Add("世花");

        if (NPC.downedFishron)
            prog.Add("猪鲨");

        // 机械三王判断
        if (NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3)
        {
            if (!Main.zenithWorld)
                prog.Add("三王");
            else
                prog.Add("美杜莎");

            prog.Remove("毁灭者");
            prog.Remove("机械骷髅王");
            prog.Remove("双子眼");
        }
        else
        {
            if (NPC.downedMechBoss2)
                prog.Add("双子眼");
            if (NPC.downedMechBoss3)
                prog.Add("机械骷髅王");
            if (NPC.downedMechBoss1)
                prog.Add("毁灭者");
        }

        if (NPC.downedQueenSlime)
            prog.Add("史后");

        if (NPC.downedPirates)
            prog.Add("海盗");

        if (Main.hardMode)
            prog.Add("肉山");

        if (NPC.downedBoss3)
            prog.Add("骷髅王");

        if (NPC.downedQueenBee)
            prog.Add("蜂王");

        if (NPC.downedBoss2)
            prog.Add("世吞克脑");

        if (NPC.downedDeerclops)
            prog.Add("鹿角怪");

        if (NPC.downedSlimeKing)
            prog.Add("史王");

        if (NPC.downedBoss1)
            prog.Add("克眼");

        if (NPC.downedGoblins)
            prog.Add("哥布林");

        return prog;
    }
    #endregion

    #region 进度条件
    // 检查条件组中的所有条件是否都满足
    public static bool CheckConds(List<string> conds, Player? p = null)
    {
        foreach (var c in conds)
        {
            if (!CheckCond(c, p))
                return false;
        }
        return true;
    }

    // 检查单个条件是否满足 - 直接匹配中文
    public static bool CheckCond(string cond, Player? p = null)
    {
        switch (cond)
        {
            case "0":
            case "无":
                return true;
            case "1":
            case "克眼":
            case "克苏鲁之眼":
                return NPC.downedBoss1;
            case "2":
            case "史莱姆王":
            case "史王":
                return NPC.downedSlimeKing;
            case "3":
            case "世吞":
            case "黑长直":
            case "世界吞噬者":
            case "世界吞噬怪":
                return NPC.downedBoss2 &&
                       (IsDefeated(NPCID.EaterofWorldsHead) ||
                        IsDefeated(NPCID.EaterofWorldsBody) ||
                        IsDefeated(NPCID.EaterofWorldsTail));
            case "4":
            case "克脑":
            case "脑子":
            case "克苏鲁之脑":
                return NPC.downedBoss2 && IsDefeated(NPCID.BrainofCthulhu);
            case "5":
            case "邪恶boss2":
            case "世吞或克脑":
            case "击败世吞克脑任意一个":
                return NPC.downedBoss2;
            case "6":
            case "巨鹿":
            case "鹿角怪":
                return NPC.downedDeerclops;
            case "7":
            case "蜂王":
                return NPC.downedQueenBee;
            case "8":
            case "骷髅王前":
                return !NPC.downedBoss3;
            case "9":
            case "吴克":
            case "骷髅王":
            case "骷髅王后":
                return NPC.downedBoss3;
            case "10":
            case "肉前":
                return !Main.hardMode;
            case "11":
            case "困难模式":
            case "肉山":
            case "肉后":
            case "血肉墙":
                return Main.hardMode;
            case "12":
            case "毁灭者":
            case "铁长直":
                return NPC.downedMechBoss1;
            case "13":
            case "双子眼":
            case "双子魔眼":
                return NPC.downedMechBoss2;
            case "14":
            case "铁吴克":
            case "机械吴克":
            case "机械骷髅王":
                return NPC.downedMechBoss3;
            case "15":
            case "世纪之花":
            case "花后":
            case "世花":
                return NPC.downedPlantBoss;
            case "16":
            case "石后":
            case "石巨人":
                return NPC.downedGolemBoss;
            case "17":
            case "史后":
            case "史莱姆皇后":
                return NPC.downedQueenSlime;
            case "18":
            case "光之女皇":
            case "光女":
                return NPC.downedEmpressOfLight;
            case "19":
            case "猪鲨":
            case "猪龙鱼公爵":
                return NPC.downedFishron;
            case "20":
            case "拜月":
            case "拜月教":
            case "教徒":
            case "拜月教邪教徒":
                return NPC.downedAncientCultist;
            case "21":
            case "月总":
            case "月亮领主":
                return NPC.downedMoonlord;
            case "22":
            case "哀木":
                return NPC.downedHalloweenTree;
            case "23":
            case "南瓜王":
                return NPC.downedHalloweenKing;
            case "24":
            case "常绿尖叫怪":
                return NPC.downedChristmasTree;
            case "25":
            case "冰雪女王":
                return NPC.downedChristmasIceQueen;
            case "26":
            case "圣诞坦克":
                return NPC.downedChristmasSantank;
            case "27":
            case "火星飞碟":
                return NPC.downedMartians;
            case "28":
            case "小丑":
                return NPC.downedClown;
            case "29":
            case "日耀柱":
                return NPC.downedTowerSolar;
            case "30":
            case "星旋柱":
                return NPC.downedTowerVortex;
            case "31":
            case "星云柱":
                return NPC.downedTowerNebula;
            case "32":
            case "星尘柱":
                return NPC.downedTowerStardust;
            case "33":
            case "一王后":
            case "任意机械boss":
                return NPC.downedMechBossAny;
            case "34":
            case "三王后":
                return NPC.downedMechBoss1 && NPC.downedMechBoss2 && NPC.downedMechBoss3;
            case "35":
            case "一柱后":
                return NPC.downedTowerNebula || NPC.downedTowerSolar || NPC.downedTowerStardust || NPC.downedTowerVortex;
            case "36":
            case "四柱后":
                return NPC.downedTowerNebula && NPC.downedTowerSolar && NPC.downedTowerStardust && NPC.downedTowerVortex;
            case "37":
            case "哥布林入侵":
                return NPC.downedGoblins;
            case "38":
            case "海盗入侵":
                return NPC.downedPirates;
            case "39":
            case "霜月":
                return NPC.downedFrost;
            case "40":
            case "血月":
                return Main.bloodMoon;
            case "41":
            case "雨天":
                return Main.raining;
            case "42":
            case "白天":
                return Main.dayTime;
            case "43":
            case "晚上":
                return !Main.dayTime;
            case "44":
            case "大风天":
                return Main.IsItAHappyWindyDay;
            case "45":
            case "万圣节":
                return Main.halloween;
            case "46":
            case "圣诞节":
                return Main.xMas;
            case "47":
            case "派对":
                return BirthdayParty.PartyIsUp;
            case "48":
            case "旧日一":
            case "黑暗法师":
            case "撒旦一":
                return DD2Event._downedDarkMageT1;
            case "49":
            case "旧日二":
            case "巨魔":
            case "食人魔":
            case "撒旦二":
                return DD2Event._downedOgreT2;
            case "50":
            case "旧日三":
            case "贝蒂斯":
            case "双足翼龙":
            case "撒旦三":
                return DD2Event._spawnedBetsyT3;
            case "51":
            case "2020":
            case "醉酒":
            case "醉酒种子":
            case "醉酒世界":
                return Main.drunkWorld;
            case "52":
            case "2021":
            case "十周年":
            case "十周年种子":
                return Main.tenthAnniversaryWorld;
            case "53":
            case "ftw":
            case "真实世界":
            case "真实世界种子":
                return Main.getGoodWorld;
            case "54":
            case "ntb":
            case "蜜蜂世界":
            case "蜜蜂世界种子":
                return Main.notTheBeesWorld;
            case "55":
            case "dst":
            case "饥荒":
            case "永恒领域":
                return Main.dontStarveWorld;
            case "56":
            case "remix":
            case "颠倒":
            case "颠倒世界":
            case "颠倒种子":
                return Main.remixWorld;
            case "57":
            case "noTrap":
            case "陷阱种子":
            case "陷阱世界":
                return Main.noTrapsWorld;
            case "58":
            case "天顶":
            case "天顶种子":
            case "缝合种子":
            case "天顶世界":
            case "缝合世界":
                return Main.zenithWorld;
            case "59":
            case "森林":
                if (p != null)
                    return p.ShoppingZone_Forest;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:森林");
                    return false;
                }
            case "60":
            case "丛林":
                if (p != null)
                    return p.ZoneJungle;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:丛林");
                    return false;
                }
            case "61":
            case "沙漠":
                if (p != null)
                    return p.ZoneDesert;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:沙漠");
                    return false;
                }
            case "62":
            case "雪原":
                if (p != null)
                    return p.ZoneSnow;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:雪原");
                    return false;
                }
            case "63":
            case "洞穴":
                if (p != null)
                    return p.ZoneRockLayerHeight;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:洞穴");
                    return false;
                }
            case "64":
            case "海洋":
                if (p != null)
                    return p.ZoneBeach;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:海洋");
                    return false;
                }
            case "65":
            case "地表":
                if (p != null)
                    return (p.position.Y / 16) <= Main.worldSurface;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地表");
                    return false;
                }
            case "66":
            case "太空":
                if (p != null)
                    return (p.position.Y / 16) <= (Main.worldSurface * 0.35);
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:太空");
                    return false;
                }
            case "67":
            case "地狱":
                if (p != null)
                    return (p.position.Y / 16) >= Main.UnderworldLayer;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地狱");
                    return false;
                }
            case "68":
            case "神圣":
                if (p != null)
                    return p.ZoneHallow;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:神圣");
                    return false;
                }
            case "69":
            case "蘑菇":
                if (p != null)
                    return p.ZoneGlowshroom;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:蘑菇地");
                    return false;
                }
            case "70":
            case "腐化":
            case "腐化地":
            case "腐化环境":
                if (p != null)
                    return p.ZoneCorrupt;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:腐化");
                    return false;
                }
            case "71":
            case "猩红":
            case "猩红地":
            case "猩红环境":
                if (p != null)
                    return p.ZoneCrimson;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:猩红");
                    return false;
                }
            case "72":
            case "邪恶":
            case "邪恶环境":
                if (p != null)
                    return p.ZoneCrimson || p.ZoneCorrupt;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:邪恶");
                    return false;
                }
            case "73":
            case "地牢":
                if (p != null)
                    return p.ZoneDungeon;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:地牢");
                    return false;
                }
            case "74":
            case "墓地":
                if (p != null)
                    return p.ZoneGraveyard;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:墓地");
                    return false;
                }
            case "75":
            case "蜂巢":
                if (p != null)
                    return p.ZoneHive;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:蜂巢");
                    return false;
                }
            case "76":
            case "神庙":
                if (p != null)
                    return p.ZoneLihzhardTemple;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:神庙");
                    return false;
                }
            case "77":
            case "沙尘暴":
                if (p != null)
                    return p.ZoneSandstorm;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:沙尘暴");
                    return false;
                }
            case "78":
            case "天空":
                if (p != null)
                    return p.ZoneSkyHeight;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:天空");
                    return false;
                }
            case "79":
            case "微光":
            case "以太":
                if (p != null)
                    return p.ZoneShimmer;
                else
                {
                    TShock.Log.ConsoleInfo($"[{PluginName}] 玩家不存在,无法检测条件:微光");
                    return false;
                }
            case "80":
            case "满月":
                return Main.moonPhase == 0;
            case "81":
            case "亏凸月":
                return Main.moonPhase == 1;
            case "82":
            case "下弦月":
                return Main.moonPhase == 2;
            case "83":
            case "残月":
                return Main.moonPhase == 3;
            case "84":
            case "新月":
                return Main.moonPhase == 4;
            case "85":
            case "娥眉月":
                return Main.moonPhase == 5;
            case "86":
            case "上弦月":
                return Main.moonPhase == 6;
            case "87":
            case "盈凸月":
                return Main.moonPhase == 7;
            default:
                TShock.Log.ConsoleInfo($"[{PluginName}] 未知条件: {cond}");
                return false;
        }
    }

    // 是否解锁怪物图鉴以达到解锁物品掉落的程度（用于独立判断克脑、世吞）
    private static bool IsDefeated(int type)
    {
        var unlockState = Main.BestiaryDB.FindEntryByNPCID(type).UIInfoProvider.GetEntryUICollectionInfo().UnlockState;
        return unlockState == Terraria.GameContent.Bestiary.BestiaryEntryUnlockState.CanShowDropsWithDropRates_4;
    }
    #endregion

    #region 所有条件参考
    public static readonly List<string> AllConditions = new List<string>
    {
    // BOSS进度
    "克眼", "史莱姆王", "世吞", "克脑", "巨鹿", "蜂王", "骷髅王前", "骷髅王",
    "肉前", "肉后", "毁灭者", "双子眼", "机械骷髅王", "世纪之花", "石巨人",
    "史后", "光女", "猪鲨", "拜月", "月总",
    // 事件
    "哥布林入侵", "海盗入侵", "霜月", "血月", "雨天", "白天", "晚上", "大风天",
    "万圣节", "圣诞节", "派对", "旧日一", "旧日二", "旧日三",
    // 特殊种子
    "2020", "2021", "ftw", "ntb", "dst", "remix", "noTrap", "天顶",
    // 生物群落
    "森林", "丛林", "沙漠", "雪原", "洞穴", "海洋", "地表", "太空", "地狱",
    "神圣", "蘑菇", "腐化", "猩红", "邪恶", "地牢", "墓地", "蜂巢", "神庙",
    "沙尘暴", "天空", "微光",
    // 月相
    "满月", "亏凸月", "下弦月", "残月", "新月", "娥眉月", "上弦月", "盈凸月"
    }; 
    #endregion
}
