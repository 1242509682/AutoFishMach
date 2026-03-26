using Newtonsoft.Json;

namespace Plugin;

public class CustomFishRule
{
    [JsonProperty("自动描述")]
    public string Desc { get; set; } = string.Empty;
    [JsonProperty("物品ID")]
    public int ItemType { get; set; } = 0;
    [JsonProperty("怪物ID")]
    public int NPCType { get; set; } = 0;   // 新增：若>0则生成NPC
    [JsonProperty("概率分母")]
    public int Chance { get; set; }
    [JsonProperty("条件")]
    public List<string> Cond { get; set; } = new();
}
