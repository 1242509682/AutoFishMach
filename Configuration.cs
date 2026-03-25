using Newtonsoft.Json;
using Terraria.ID;
using static Plugin.Plugin;

namespace Plugin;

internal class Configuration
{
    #region 配置项成员
    [JsonProperty("使用方法")]
    public List<string> Text { get; set; } = new();
    [JsonProperty("插件开关")]
    public bool Enabled { get; set; } = true;
    [JsonProperty("无法钓鱼秒数")]
    public int Warning { get; set; } = 60;
    [JsonProperty("钓鱼间隔帧数")]
    public string FishFrames { get; set; } = "60,240";
    [JsonProperty("影响范围格数")]
    public int Range { get; set; } = 62;
    [JsonProperty("广播消息")]
    public bool Broadcast { get; set; } = false;
    [JsonProperty("额外渔力加成")]
    public int Power { get; set; } = 20;
    [JsonProperty("自定义渔力加成物品")]
    public Dictionary<int, int> CustomPowerItems { get; set; } = new();

    [JsonIgnore]
    public int MinFrames { get; private set; } = 60;
    [JsonIgnore]
    public int MaxFrames { get; private set; } = 60;
    #endregion

    #region 预设参数方法
    public void SetDefault()
    {
        Text = new List<string>()
        {
             "1.给玩家权限;/group addperm default afm.use",
             "2.在鱼池放个箱子,使用/afm set 镐击一下箱子",
             "3.使用/afm save 保存当前钓鱼机",
             "4.给箱子放入鱼竿和鱼饵",
             "当钓鱼机失效时,需重新放入鱼竿(乱动鱼饵和鱼竿引起的)",
             "重置服务器时使用:/afm reset",
             "提示:挖掉箱子可自动销毁钓鱼机",
             "在钓鱼机箱左边放其他箱子,会优先放入左边的箱子",
             "已知BUG:暂时无法钓环境匣子与板条箱",
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
            { ItemID.AnglerPants, 5 }  // 渔夫裤 +5
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