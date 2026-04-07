using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

#region 机器数据
// 动画请求
public enum AnimType { Move, Sparkle, Transfer }
public class AnimReq
{
    public AnimType Type;
    public Item item;           // 需要转移的物品
    public Vector2 from;        // 动画起点
    public Point toPos;         // 目标箱子坐标（用于动画和定位）
    public int chestIdx;        // 目标箱子索引
    public bool skipFake;       // 是否跳过假鱼
    public MachData data;
}
public class CustomState
{
    [JsonProperty("名称")]
    public string ItemName { get; set; } = string.Empty;
    [JsonProperty("槽位")]
    public int Slot { get; set; }
    [JsonProperty("时间")]
    public DateTime Expiry { get; set; }
    [JsonProperty("加成")]
    public int Bonus { get; set; }

    public CustomState(int slot, DateTime expiry, int bonus,string name)
    {
        Slot = slot;
        Expiry = expiry;
        Bonus = bonus;
        ItemName = name;
    }
}
public class MachData
{
    [JsonProperty("世界ID")]
    public string WorldId { get; set; } = "";
    [JsonProperty("所有者")]
    public string Owner { get; set; } = "";
    [JsonProperty("区域名")]
    public string RegName { get; set; } = "";
    [JsonProperty("坐标")]
    public Point Pos { get; set; } = new Point();
    [JsonProperty("箱子索引")]
    public int ChestIndex { get; set; } = -1;
    [JsonProperty("输出列表")]
    public List<int> OutChests { get; set; } = new();
    [JsonProperty("禁钓已有怪物")]
    public bool SoloCustomMonster { get; set; } = true;
    [JsonProperty("禁钓模式")]
    public int SoloMode { get; set; } = 0;
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
    [JsonProperty("地下沙漠")]
    public bool ZoneUndergroundDesert { get; set; }
    [JsonProperty("海洋")]
    public bool ZoneBeach { get; set; }
    [JsonProperty("地牢")]
    public bool ZoneDungeon { get; set; }
    [JsonProperty("下雨")]
    public bool ZoneRain { get; set; }
    [JsonProperty("沙尘暴")]
    public bool ZoneSandstorm { get; set; }
    [JsonProperty("蘑菇地")]
    public bool ZoneGlowshroom { get; set; }
    [JsonProperty("微光")]
    public bool ZoneShimmer { get; set; }
    [JsonProperty("影烛")]
    public bool ZoneShadowCandle { get; set; }
    [JsonProperty("水蜡烛")]
    public bool ZoneWaterCandle { get; set; }
    [JsonProperty("和平蜡烛")]
    public bool ZonePeaceCandle { get; set; }
    [JsonProperty("墓地")]
    public bool ZoneGraveyard { get; set; }
    [JsonProperty("花岗岩")]
    public bool ZoneGranite { get; set; }
    [JsonProperty("大理石")]
    public bool ZoneMarble { get; set; }
    [JsonProperty("陨石坑")]
    public bool ZoneMeteor { get; set; }
    [JsonProperty("宝石洞")]
    public bool ZoneGemCave { get; set; }
    [JsonProperty("蜂巢")]
    public bool ZoneHive { get; set; }
    [JsonProperty("神庙")]
    public bool ZoneLihzhardTemple { get; set; }
    [JsonProperty("撒旦入侵")]
    public bool ZoneOldOneArmy { get; set; }
    [JsonProperty("星云天塔柱")]
    public bool ZoneTowerNebula { get; set; }
    [JsonProperty("日耀天塔柱")]
    public bool ZoneTowerSolar { get; set; }
    [JsonProperty("星尘天塔柱")]
    public bool ZoneTowerStardust { get; set; }
    [JsonProperty("星漩天塔柱")]
    public bool ZoneTowerVortex { get; set; }
    [JsonProperty("高度等级")]
    public int HeightLevel { get; set; }
    [JsonProperty("颠倒海洋")]
    public bool RolledRemixOcean { get; set; }
    [JsonProperty("钓鱼药水时间")]
    public DateTime FishingPotionTime { get; set; } = DateTime.MinValue;
    [JsonProperty("宝匣药水时间")]
    public DateTime CratePotionTime { get; set; } = DateTime.MinValue;
    [JsonProperty("鱼饵桶时间")]
    public DateTime ChumBucketTime { get; set; } = DateTime.MinValue;
    [JsonProperty("鱼池名称")]
    public string LiqName { get; set; }
    [JsonProperty("鱼池格数")]
    public int MaxLiq { get; set; }
    [JsonProperty("液体坐标")]
    public Point LiqPos { get; set; } = new Point(-1, -1);
    [JsonProperty("幸运值")]
    public float luck { get; set; }
    [JsonProperty("额外渔力")]
    public int ExtraPower { get; set; }
    [JsonProperty("可钓岩浆")]
    public bool CanFishInLava { get; set; }
    [JsonProperty("钓具箱")]
    public bool HasTackle { get; set; }
    [JsonProperty("自定义消耗品")]
    public Dictionary<int, CustomState> Custom { get; set; } = new();
    [JsonProperty("区域增益")]
    public Dictionary<int, DateTime> ActiveZoneBuffs { get; set; } = new();
    [JsonProperty("排除物品")]
    public HashSet<int> Exclude { get; set; } = new();

    // 以下仅用于运行时计算，不保存

    // 液体播报标识
    [JsonIgnore]
    public bool LiquidBroadcast = false;

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

    // 宝匣药水槽位
    [JsonIgnore]
    public int CratePotionSlot { get; set; } = -1;
    // 大气因子
    [JsonIgnore]
    public float atmo { get; set; }
    // 钓鱼药水槽位与消耗时间
    [JsonIgnore]
    public int FishingPotionSlot { get; set; } = -1;
    // 鱼饵桶
    [JsonIgnore]
    public int ChumBucketSlot { get; set; } = -1;

    // 区域玩家表
    [JsonIgnore]
    public HashSet<TSPlayer> RegionPlayers { get; set; } = new();

    // 液体数量
    [JsonIgnore]
    public int WaterCount { get; set; }
    [JsonIgnore]
    public int LavaCount { get; set; }
    [JsonIgnore]
    public int HoneyCount { get; set; }

    // 初始化标志
    [JsonIgnore]
    public bool IntMach { get; set; } = true;

    // 分帧执行（避免同1帧触发多台钓鱼机）
    [JsonIgnore]
    public long nextFrame { get; set; } = 0;
    [JsonIgnore] 
    public AutoFishing Engine { get; set; }

    // 液体不足停止检测
    [JsonIgnore]
    public bool LiqDead { get; set; } = false;
    [JsonIgnore]
    public int LiqType { get; set; } = -1;
    [JsonIgnore]
    public bool[] Visited { get; set; }          // BFS 访问标记
    [JsonIgnore]
    public Queue<(int x, int y)> LiqQueue { get; set; } = new(); // BFS 队列
    [JsonIgnore]
    public DateTime LastZoneUpdate { get; set; } = DateTime.MinValue; // 环境同步冷却

    // 动画队列和计时
    [JsonIgnore]
    public Queue<AnimReq> AnimQueue { get; set; } = new();
    [JsonIgnore]
    public long AnimFrame { get; set; } = 0;
    [JsonIgnore]
    public int AnimOutIdx { get; set; } = 0;   // 当前动画指向的传输箱索引

    // 清理动画队列方法
    public void ClearAnim()
    {
        AnimQueue.Clear();
        Plugin.ActiveAnim.Remove(this);
        AnimFrame = 0;
    }

    // 缓存非转移物品
    [JsonIgnore]
    public HashSet<int> SafeTypes { get; set; } = new();
    // 是否需要转移积压物品
    [JsonIgnore]
    public bool NeedPut { get; set; } = false;
    // 上次转移的帧数（用于冷却）
    [JsonIgnore]
    public long LastPutFrame { get; set; } = 0;

    // 缓存NPC
    [JsonIgnore]
    public Dictionary<int, int> Monsters { get; set; } = new(); // 怪物类型 -> 数量

    // 最后一次电路触发的帧数
    [JsonIgnore]
    public long WiringFrame { get; set; } = 0;
    // 上次激活状态（用于打开箱子时检测）
    [JsonIgnore]
    public bool Wiring
    {
        get
        {
            if (WiringFrame == 0) return false;
            return (Plugin.Timer - WiringFrame) < 120;
        }
    }

    // 传输箱表辅助属性
    [JsonIgnore]
    public bool HasOut => OutChests.Count > 0;


    public MachData() { }
}
#endregion