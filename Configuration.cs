using Newtonsoft.Json;
using Terraria.ID;
using static Plugin.Plugin;

namespace Plugin;

internal class Configuration
{
    #region 配置项成员
    [JsonProperty("自定义渔获进度参考", Order = -1)]
    public List<string> Reference = new();
    [JsonProperty("使用方法", Order = 0)]
    public List<string> Text { get; set; } = new();
    [JsonProperty("插件开关", Order = 1)]
    public bool Enabled { get; set; } = true;
    [JsonProperty("广播消息", Order = 2)]
    public bool Broadcast { get; set; } = false;
    [JsonProperty("停钓警告秒数", Order = 3)]
    public int Warning { get; set; } = 60;
    [JsonProperty("钓鱼间隔帧数", Order = 4)]
    public string FishFrames { get; set; } = "60,240";
    [JsonProperty("搜索范围格数", Order = 5)]
    public int Range { get; set; } = 62;
    [JsonProperty("鱼匣额外加成", Order = 6)]
    public int CrateChanceBonus { get; set; } = 15;
    [JsonProperty("渔力额外加成", Order = 7)]
    public int Power { get; set; } = 20;
    [JsonProperty("自定加成物品", Order = 8)]
    public Dictionary<int, int> CustomPowerItems { get; set; } = new();
    [JsonProperty("自定义渔获", Order = 9)]
    public List<CustomFishRule> CustomFishes { get; set; } = new();

    [JsonIgnore]
    public int MinFrames { get; private set; } = 60;
    [JsonIgnore]
    public int MaxFrames { get; private set; } = 60;
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
             "2.在鱼池放个箱子,使用/afm set 镐击一下箱子",
             "3.使用/afm save 保存当前钓鱼机",
             "4.给箱子放入鱼竿和鱼饵",
             "重置服务器时使用:/afm reset",
             "提示:挖掉箱子可自动销毁钓鱼机",
        };

        CustomPowerItems = new()
        {
            { ItemID.TackleBox, 10 }, // 钓具箱 +10
            { ItemID.AnglerEarring, 10 }, // 渔夫耳环 +10
            { ItemID.HighTestFishingLine, 10 }, // 优质钓线 +10
            { ItemID.AnglerTackleBag, 10 }, // 渔夫渔具袋 +10
            { ItemID.FishingPotion, 20 },  // 钓鱼药水 +20
            { ItemID.AnglerHat, 5 }, // 渔夫帽 +5
            { ItemID.AnglerVest, 5 }, // 渔夫背心 +5
            { ItemID.AnglerPants, 5 },  // 渔夫裤 +5
            { ItemID.CratePotion, 15 }  // 宝匣药水 +5
        };

        CustomFishes = new()
        {
            // 克眼后 1/30 概率钓到生命水晶
            new CustomFishRule()
            { 
                ItemType = ItemID.LifeCrystal, 
                ChanceDenominator = 30, 
                Cond = new List<string>() { "克眼" } 
            }
        };
    }
    #endregion

    #region 解析随机间隔
    public void ParseFrames()
    {
        if (string.IsNullOrWhiteSpace(FishFrames))
        {
            MinFrames = MaxFrames = 60;
            return;
        }
        var parts = FishFrames.Split(',');
        if (parts.Length == 1 && int.TryParse(parts[0], out int single))
        {
            MinFrames = MaxFrames = single;
        }
        else if (parts.Length >= 2 &&
                 int.TryParse(parts[0], out int min) &&
                 int.TryParse(parts[1], out int max))
        {
            MinFrames = Math.Min(min, max);
            MaxFrames = Math.Max(min, max);
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
            string jsonContent = File.ReadAllText(Paths);
            var config = JsonConvert.DeserializeObject<Configuration>(jsonContent)!;
            config.ParseFrames(); // 解析配置中的字符串
            return config;
        }
    }
    #endregion
}