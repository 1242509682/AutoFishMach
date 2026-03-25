using Newtonsoft.Json;

namespace Plugin;

public class CustomFishRule
{
    [JsonProperty("物品ID")]
    public int ItemType { get; set; }
    [JsonProperty("概率分母")]
    public int ChanceDenominator { get; set; }
    [JsonProperty("条件")]
    public List<string> Cond { get; set; } = new();
}
