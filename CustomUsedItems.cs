using Newtonsoft.Json;

namespace FishMach;

public class CustomUsedItems
{
    [JsonProperty("物品名称")]
    public string ItemName { get; set; } = string.Empty;
    [JsonProperty("消耗物品")]
    public int ItemType { get; set; } = -1;
    [JsonProperty("增益名称")]
    public string BuffName { get; set; } = string.Empty;
    [JsonProperty("获得增益")]
    public int BuffID { get; set; } = -1;
    [JsonProperty("持续分钟")]
    public int Minutes { get; set; } = 5;
    [JsonProperty("渔力加成")]
    public int Power { get; set; } = 5;
    [JsonProperty("增益描述")]
    public string BuffDesc { get; set; } = string.Empty;
}