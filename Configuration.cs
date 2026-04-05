using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Terraria;
using Terraria.ID;
using static FishMach.Plugin;
using static Terraria.ID.ProjectileID;

namespace FishMach;

internal class Configuration
{
    #region 配置项成员
    [JsonProperty("自定渔获进度参考", Order = -15)]
    public List<string> Reference = new();
    [JsonProperty("使用方法", Order = -14)]
    public List<string> Text { get; set; } = new();
    [JsonProperty("插件开关", Order = -13)]
    public bool Enabled { get; set; } = true;
    [JsonProperty("钓鱼机数", Order = -12)]
    public int MaxMachines { get; set; } = 0; // 0表示无限制
    [JsonProperty("传输模式", Order = -11)]
    public bool TransferMode { get; set; } = true;
    [JsonProperty("传输间隔", Order = -10)]
    public int TransferInterval { get; set; } = 5;
    [JsonProperty("同时传输", Order = -9)]
    public int TransferStack { get; set; } = 5;
    [JsonProperty("传输箱数", Order = -8)]
    public int MaxOutChest { get; set; } = 3;
    [JsonProperty("传输钱币", Order = -7)]
    public bool TransferCoins { get; set; } = true;
    [JsonProperty("传输消息")]
    public bool ShowTransferMsg { get; set; } = true;
    [JsonProperty("钓任务鱼", Order = -6)]
    public bool QuestFish { get; set; } = true;
    [JsonProperty("假鱼动画", Order = -5)]
    public bool FakeFish { get; set; } = true;
    [JsonProperty("物品动画", Order = -4)]
    public bool ChestTransfer { get; set; } = true;
    [JsonProperty("闪光动画", Order = -3)]
    public bool Sparkle { get; set; } = true;
    [JsonProperty("区域保护", Order = -2)]
    public bool RegionBuild { get; set; } = false;
    [JsonProperty("区域广播", Order = 0)]
    public bool RegionBroadcast { get; set; } = true;
    [JsonProperty("区域范围", Order = 1)]
    public int Range { get; set; } = 62;
    [JsonProperty("无人关闭", Order = 2)]
    public bool AutoStopWhenEmpty { get; set; } = false;
    [JsonProperty("需要水量", Order = 3)]
    public int NeedLiqStack { get; set; } = 75;
    [JsonProperty("需要电路", Order = 4)]
    public bool NeedWiring { get; set; } = false;
    [JsonProperty("钓鱼间隔", Order = 6)]
    public string FishInterval { get; set; } = "60,240";
    [JsonProperty("环境范围", Order = 7)]
    public int ZoneRange { get; set; } = 10;
    [JsonProperty("区域BUFF", Order = 8)]
    public bool RegionBuffEnabled { get; set; } = true;
    [JsonProperty("宝匣药水加成", Order = 9)]
    public int CratePotionBonus { get; set; } = 15;
    [JsonProperty("钓鱼药水加成", Order = 10)]
    public int FishingPotionPower { get; set; } = 20;
    [JsonProperty("鱼饵桶加成", Order = 11)]
    public int ChumBucketPower { get; set; } = 10;
    [JsonProperty("允许钓出怪物", Order = 12)]
    public bool EnableCustomNPC { get; set; } = true;
    [JsonProperty("禁钓已有怪物", Order = 13)]
    public bool SoloCustomMonster { get; set; } = true;
    [JsonProperty("禁钓模式(0不同类/1仅单挑)", Order = 14)]
    public int SoloMode { get; set; } = 0;
    [JsonProperty("永久渔力加成物品", Order = 15)]
    public Dictionary<int, int> CustomPowerItems { get; set; } = new();
    [JsonProperty("区域Buff消耗物品", Order = 16)]
    public List<CustomUsedItems> CustomUsedItem { get; set; } = new();
    [JsonProperty("自定义渔获表", Order = 17)]
    public List<CustomFishRule> CustomFishes { get; set; } = new();
    [JsonProperty("液体弹幕检测", Order = 18)] 
    public List<int> LiqProj { get; set; } = [];

    [JsonIgnore] public int TransferFrames => TransferInterval * 60;
    [JsonIgnore] public int MinFrames { get; private set; } = 60;
    [JsonIgnore] public int MaxFrames { get; private set; } = 60;
    #endregion

    #region 预设参数方法
    public void SetDefault()
    {
        Reference =
        [
            "0 无 | 1 克眼 | 2 史王 | 3 世吞 | 4克脑 | 5世吞或克脑 | 6 巨鹿 | 7 蜂王 | 8 骷髅王前 | 9 骷髅王后",
            "10 肉前 | 11 肉后 | 12 毁灭者 | 13 双子魔眼 | 14 机械骷髅王 | 15 世花 | 16 石巨人 | 17 史后 | 18 光女 | 19 猪鲨",
            "20 拜月 | 21 月总 | 22 哀木 | 23 南瓜王 | 24 尖叫怪 | 25 冰雪女王 | 26 圣诞坦克 | 27 火星飞碟 | 28 小丑",
            "29 日耀柱 | 30 星旋柱 | 31 星云柱 | 32 星尘柱 | 33 一王后 | 34 三王后 | 35 一柱后 | 36 四柱后",
            "37 哥布林 | 38 海盗 | 39 霜月 | 40 血月 | 41 雨天 | 42 白天 | 43 夜晚 | 44 大风天 | 45 万圣节 | 46 圣诞节 | 47 派对",
            "48 旧日一 | 49 旧日二 | 50 旧日三 | 51 醉酒种子 | 52 十周年 | 53 ftw种子 | 54 蜜蜂种子 | 55 饥荒种子",
            "56 颠倒种子 | 57 陷阱种子 | 58 天顶种子",
            "59 森林 | 60 丛林 | 61 沙漠 | 62 雪原 | 63 洞穴 | 64 海洋 | 65 地表 | 66 太空 | 67 地狱 | 68 神圣 | 69 蘑菇",
            "70 腐化 | 71 猩红 | 72 邪恶 | 73 地牢 | 74 墓地 | 75 蜂巢 | 76 神庙 | 77 沙尘暴 | 78 天空 | 79 微光",
            "80 满月 | 81 亏凸月 | 82 下弦月 | 83 残月 | 84 新月 | 85 娥眉月 | 86 上弦月 | 87 盈凸月"
        ];

        Text = new List<string>()
        {
             "1.给玩家权限;/group addperm default afm.use",
             "2.打开个箱子,使用/afm s指令",
             "3.给箱子放入鱼竿和鱼饵",
             "4.如果开启【需要电路】,则需要连上电线与计时器",
             "重置服务器时使用:/afm reset",
             "挖掉箱子自动销毁钓鱼机",
        };

        CustomPowerItems = new()
        {
            { ItemID.TackleBox, 10 }, // 钓具箱 +10
            { ItemID.AnglerEarring, 10 }, // 渔夫耳环 +10
            { ItemID.HighTestFishingLine, 10 }, // 优质钓线 +10
            { ItemID.AnglerTackleBag, 10 }, // 渔夫渔具袋 +10
            { ItemID.AnglerHat, 5 }, // 渔夫帽 +5
            { ItemID.AnglerVest, 5 }, // 渔夫背心 +5
            { ItemID.AnglerPants, 5 },  // 渔夫裤 +5
        };

        CustomUsedItem = new List<CustomUsedItems>
        {
            new CustomUsedItems{ ItemType = ItemID.FeatherfallPotion, Minutes = 5, Power = 5, BuffID = BuffID.Featherfall, },
            new CustomUsedItems{ ItemType = ItemID.SonarPotion, Minutes = 5, Power = 5, BuffID = BuffID.Sonar, },
        };

        CustomFishes = new()
        {
            // 克眼后 1/30 概率钓到生命水晶
            new CustomFishRule()
            {
                ItemType = ItemID.LifeCrystal,
                Chance = 30,
                Cond = new List<string>() { "克眼" }
            },

            // 血月肉前敌怪（基础概率 1/30）
            new CustomFishRule() { NPCType = NPCID.ZombieMerman, Chance = 30, Cond = new List<string>() { "血月", "肉前" } },
            new CustomFishRule() { NPCType = NPCID.EyeballFlyingFish, Chance = 30, Cond = new List<string>() { "血月", "肉前" } },
            new CustomFishRule() { NPCType = NPCID.Drippler, Chance = 30, Cond = new List<string>() { "血月", "肉前" } },

            // 血月肉后敌怪（基础概率 1/30）
            new CustomFishRule() { NPCType = NPCID.GoblinShark, Chance = 30, Cond = new List<string>() { "血月", "肉后" } },
            new CustomFishRule() { NPCType = NPCID.BloodEelHead, Chance = 30, Cond = new List<string>() { "血月", "肉后" } },
            new CustomFishRule() { NPCType = NPCID.ZombieMerman, Chance = 30, Cond = new List<string>() { "血月", "肉后" } },
            new CustomFishRule() { NPCType = NPCID.EyeballFlyingFish, Chance = 30, Cond = new List<string>() { "血月", "肉后" } },

            // 恐惧鹦鹉螺（肉后，低概率 1/60）
            new CustomFishRule() { NPCType = NPCID.BloodNautilus, Chance = 60, Cond = new List<string>() { "血月", "肉后" } },
        };

        LiqProj =
        [
            // 液体炸弹
            WetBomb, LavaBomb, HoneyBomb, DryBomb,
            // 液体火箭、手榴弹、地雷
            LavaRocket, LavaGrenade, LavaMine,
            HoneyRocket, HoneyGrenade, HoneyMine,
            DryRocket, DryGrenade, DryMine,
            // 雪人火箭液体变种
            WetSnowmanRocket, LavaSnowmanRocket, HoneySnowmanRocket, DrySnowmanRocket
        ];
        LiqProj.Sort(); // 排序
    }
    #endregion

    #region 解析随机间隔
    public void ParseFrames()
    {
        var (success, min, max) = Utils.ParseIntRange(FishInterval);
        if (success)
        {
            MinFrames = min;
            MaxFrames = max;
        }
        else
        {
            MinFrames = MaxFrames = 60;
        }
    }
    #endregion

    #region 读取与创建配置文件方法
    public void Write()
    {
        string json = JsonConvert.SerializeObject(this, Formatting.Indented);
        File.WriteAllText(Paths, json);
    }
    public static Configuration Read()
    {
        if (!File.Exists(Paths))
        {
            var NewConfig = new Configuration();
            NewConfig.SetDefault();
            NewConfig.ParseFrames(); // 解析默认值
            NewConfig.Write();
            return NewConfig;
        }
        else
        {
            try
            {
                string json = File.ReadAllText(Paths);
                var config = JsonConvert.DeserializeObject<Configuration>(json)!;
                config.ParseFrames(); // 解析配置中的字符串
                return config;
            }
            catch (JsonReaderException ex)
            {
                string json = File.ReadAllText(Paths);
                string[] lines = json.Split('\n');
                int line = ex.LineNumber;
                // 防止越界：确保索引至少为 0，且不超过数组长度
                int idx = Math.Max(0, Math.Min(line - 2, lines.Length - 1));
                string text = lines[idx].Trim();
                throw new Exception($"位置: 第 {line - 1} 行\n" +
                                    $"内容: {text ?? string.Empty}\n" +
                                    $"路径: {FormatPath(ex.Path ?? string.Empty)}", ex);
            }
        }
    }
    public static string FormatPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // 使用正则表达式匹配 "[数字]"
        return Regex.Replace(path,@"\[(\d+)\]",match =>
        {
                int index = int.Parse(match.Groups[1].Value);
                return $":第{index + 1}项";
        });
    }
    #endregion

    #region 自动描述
    public void AutoDesc()
    {
        if (CustomFishes == null || !CustomFishes.Any()) return;

        foreach (var rule in CustomFishes)
        {
            rule.Desc = string.Empty;

            if (rule.Chance <= 0) continue;

            if (rule.ItemType == 0 && rule.NPCType == 0)
            {
                rule.Desc = "[c/FF5555:无效规则]";
                continue;
            }

            string chanceStr = (100.0 / rule.Chance).ToString("F2") + "%";
            string target;
            if (rule.ItemType > 0)
                target = $"{Lang.GetItemNameValue(rule.ItemType)}";
            else
                target = $"{Lang.GetNPCNameValue(rule.NPCType)}";

            string condStr = rule.Cond.Count > 0 ? string.Join("、", rule.Cond) + "条件下" : "无条件";
            rule.Desc = $"{condStr} 有 {chanceStr} 概率钓出 {target}";
        }
    }
    #endregion

    #region 自动填充显示名称
    public void AutoFillNames()
    {
        if (CustomUsedItem == null || !CustomUsedItem.Any()) return;

        foreach (var item in CustomUsedItem)
        {
            if (item.ItemType != -1)
                item.ItemName = Lang.GetItemNameValue(item.ItemType);

            if (item.BuffID != -1)
            {
                item.BuffName = Lang.GetBuffName(item.BuffID);
                item.BuffDesc = Lang.GetBuffDescription(item.BuffID);
            }
        }
    }
    #endregion
}