using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using static Plugin.Plugin;

namespace Plugin;

#region 机器数据
public class MachData
{
    [JsonProperty("所有者")]
    public string Owner { get; set; } = "";
    [JsonProperty("坐标")]
    public Point Pos { get; set; } = new Point();
    [JsonProperty("鱼竿")]
    public int FishRod { get; set; } = -1;
    [JsonProperty("物品")]
    public List <int> Acc { get; set; } = new();
    [JsonProperty("环境_腐化")]
    public bool ZoneCorrupt { get; set; }
    [JsonProperty("环境_猩红")]
    public bool ZoneCrimson { get; set; }
    [JsonProperty("环境_丛林")]
    public bool ZoneJungle { get; set; }
    [JsonProperty("环境_雪地")]
    public bool ZoneSnow { get; set; }
    [JsonProperty("环境_神圣")]
    public bool ZoneHallow { get; set; }
    [JsonProperty("环境_沙漠")]
    public bool ZoneDesert { get; set; }
    [JsonProperty("环境_海洋")]
    public bool ZoneBeach { get; set; }
    [JsonProperty("环境_地牢")]
    public bool ZoneDungeon { get; set; }
    [JsonProperty("高度等级")] // 0~4
    public int HeightLevel { get; set; }
    [JsonProperty("是否remix海洋")]
    public bool RolledRemixOcean { get; set; }

    [JsonIgnore]
    public DateTime lastMissingWarning = DateTime.MinValue;

    [JsonProperty("排除物品")]
    public List<int> Exclude { get; set; } = new();

    public MachData() { }

    public void UpdateSlot(short itemType, short stack)
    {
        Item item = new Item();
        if (stack > 0)
        {
            item.SetDefaults(itemType);
            item.stack = stack;
        }

        // 判断是否为鱼竿（fishingPole > 0）
        if (item.fishingPole > 0)
        {
            FishRod = stack > 0 ? itemType : -1;
            return;
        }

        // 判断是否为饰品（accessory 或自定义加成物品）
        if (item.accessory || Config.CustomPowerItems.ContainsKey(itemType))
        {
            if (stack > 0)
            {
                if (!Acc.Contains(itemType))
                    Acc.Add(itemType);
            }
            else
            {
                Acc.Remove(itemType);
            }
            return;
        }
    }
}
#endregion