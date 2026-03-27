using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace FishMach;

#region 机器数据
public class MachData
{
    [JsonProperty("所有者")]
    public string Owner { get; set; } = "";
    [JsonProperty("区域名")]
    public string RegName { get; set; } = "";
    [JsonProperty("坐标")]
    public Point Pos { get; set; } = new Point();
    [JsonProperty("箱子索引")]
    public int ChestIndex { get; set; } = -1;
    [JsonProperty("神圣")]
    public bool ZoneHallow { get; set; }
    [JsonProperty("腐化")]
    public bool ZoneCorrupt { get; set; }
    [JsonProperty("猩红")]
    public bool ZoneCrimson { get; set; }
    [JsonProperty("丛林")]
    public bool ZoneJungle { get; set; }
    [JsonProperty("雪原")]
    public bool ZoneSnow { get; set; }
    [JsonProperty("沙漠")]
    public bool ZoneDesert { get; set; }
    [JsonProperty("海洋")]
    public bool ZoneBeach { get; set; }
    [JsonProperty("地牢")]
    public bool ZoneDungeon { get; set; }
    [JsonProperty("高度等级")]
    public int HeightLevel { get; set; }
    [JsonProperty("颠倒海洋")]
    public bool RolledRemixOcean { get; set; }

    [JsonProperty("液体名称")]
    public string LiqName { get; set; }
    [JsonProperty("液体总数")]
    public int MaxLiq { get; set; }
    [JsonProperty("液体坐标")]
    public Point LiquidPos { get; set; } = new Point(-1, -1);
    [JsonProperty("大气因子")]
    public float atmo { get; set; }
    [JsonProperty("额外渔力总和")]
    public int BonusTotal { get; set; }
    [JsonProperty("排除物品")]
    public List<int> Exclude { get; set; } = new();

    // 以下仅用于运行时计算，不保存
    [JsonIgnore]
    public int RodChest { get; set; } = -1;
    [JsonIgnore]
    public int RodSlot { get; set; } = -1;
    [JsonIgnore]
    public DateTime lastRodWarning = DateTime.MinValue;

    [JsonIgnore]
    public int BaitChest { get; set; } = -1;
    [JsonIgnore]
    public int BaitSlot { get; set; } = -1;
    [JsonIgnore]
    public DateTime lastBaitWarning = DateTime.MinValue;

    [JsonIgnore]
    public bool HasCratePotion { get; set; }
    [JsonIgnore]
    public bool CanFishInLava { get; set; }
    [JsonIgnore]
    public bool HasTackle { get; set; }

    // 环境缓存标志
    [JsonIgnore]
    public bool EnvDirty { get; set; } = true;
    [JsonIgnore]
    public DateTime LastEnvUpd = DateTime.MinValue;

    // 区域玩家计数
    [JsonIgnore]
    public int PlrCnt { get; set; } = 0;

    // 钓到物品后的计数器，用于自动整理
    [JsonIgnore]
    public int PutCounter { get; set; } = 0;

    [JsonIgnore]
    public int WatCnt { get; set; }
    [JsonIgnore]
    public int LavCnt { get; set; }
    [JsonIgnore]
    public int HonCnt { get; set; }


    public MachData() { }
}
#endregion