using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using TShockAPI;

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
    [JsonProperty("下雨")]
    public bool ZoneRain { get; set; }
    [JsonProperty("高度等级")]
    public int HeightLevel { get; set; }
    [JsonProperty("颠倒海洋")]
    public bool RolledRemixOcean { get; set; }

    [JsonProperty("液体名称")]
    public string LiqName { get; set; }
    [JsonProperty("液体数量")]
    public int MaxLiq { get; set; }
    [JsonProperty("液体坐标")]
    public Point LiquidPos { get; set; } = new Point(-1, -1);
    [JsonProperty("大气因子")]
    public float atmo { get; set; }
    [JsonProperty("额外渔力")]
    public int ExtraPower { get; set; }
    [JsonProperty("排除物品")]
    public HashSet<int> Exclude { get; set; } = new();

    // 以下仅用于运行时计算，不保存

    // 鱼竿槽位与播报标识
    [JsonIgnore]
    public int RodSlot { get; set; } = -1;
    [JsonIgnore]
    public bool RodBroadcast = false;

    // 鱼饵槽位与播报标识
    [JsonIgnore]
    public int BaitSlot { get; set; } = -1;
    [JsonIgnore]
    public bool BaitBroadcast = false;

    // 宝匣药水槽位与消耗时间
    [JsonIgnore]
    public int CratePotionSlot { get; set; } = -1;
    [JsonIgnore]
    public DateTime CratePotionTime { get; set; } = DateTime.MinValue;

    // 钓鱼药水槽位与消耗时间
    [JsonIgnore]
    public int FishingPotionSlot { get; set; } = -1;
    [JsonIgnore]
    public DateTime FishingPotionTime { get; set; } = DateTime.MinValue;

    // 鱼饵桶
    [JsonIgnore]
    public int ChumBucketSlot { get; set; } = -1;
    [JsonIgnore]
    public DateTime ChumBucketTime { get; set; } = DateTime.MinValue;  // 效果过期时间

    // 自定义消耗物品与对应BUFF和玩家表
    [JsonIgnore]
    public Dictionary<int, (int Slot, DateTime Expiry, int Bonus)> CustomConsumables { get; set; } = new();
    [JsonIgnore]
    public Dictionary<int, DateTime> ActiveZoneBuffs { get; set; } = new();
    [JsonIgnore]
    public HashSet<TSPlayer> RegionPlayers { get; set; } = new();

    // 可以钓岩浆
    [JsonIgnore]
    public bool CanFishInLava { get; set; }

    // 减少鱼饵消耗（箱子有钓具箱）
    [JsonIgnore]
    public bool HasTackle { get; set; }

    // 环境需要更新标志
    [JsonIgnore]
    public DateTime LastEnvUpdate = DateTime.MinValue;

    // 液体数量
    [JsonIgnore]
    public int WatCnt { get; set; }
    [JsonIgnore]
    public int LavCnt { get; set; }
    [JsonIgnore]
    public int HonCnt { get; set; }

    // 初始化标志
    [JsonIgnore]
    public bool IntMach { get; set; } = true;

    // 分帧执行（避免同1帧触发多台钓鱼机）
    [JsonIgnore]
    public long nextFrame { get; set; } = 0;

    public MachData() { }
}
#endregion