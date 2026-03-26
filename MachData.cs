using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace Plugin;

#region 机器数据
public class MachData
{
    [JsonProperty("所有者")]
    public string Owner { get; set; } = "";
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

    [JsonProperty("水体数量")]
    public int WatCnt { get; set; }
    [JsonProperty("岩浆数量")]
    public int LavCnt { get; set; }
    [JsonProperty("蜂蜜数量")]
    public int HonCnt { get; set; }
    [JsonProperty("大气因子")]
    public float atmo { get; set; }
    [JsonProperty("最近水体坐标")]
    public Point WaterPos { get; set; } = new Point(-1, -1);
    [JsonProperty("额外渔力总和")]
    public int BonusTotal { get; set; }
    [JsonProperty("排除物品")]
    public List<int> Exclude { get; set; } = new();

    // 新增：鱼竿/鱼饵槽位缓存（仅用于运行时，不保存）
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

    [JsonIgnore]
    public HashSet<string> NearbyPlayers { get; set; } = new HashSet<string>();

    [JsonIgnore]
    public bool HasMonsterRule { get; set; }

    [JsonIgnore]
    public DateTime CacheTime { get; set; }

    public MachData() { }
}
#endregion