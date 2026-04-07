namespace FishMach;

/// <summary>
/// 玩家传输箱批量选择会话
/// </summary>
public class OutSelData
{
    /// <summary>目标钓鱼机区域名</summary>
    public string RegionName { get; set; }

    /// <summary>已记录的箱子索引集合</summary>
    public HashSet<int> LogChests { get; set; } = new();

    /// <summary>计时结束的帧数（Plugin.Timer）</summary>
    public long Frame { get; set; }

    /// <summary>上次提醒的剩余秒数（Plugin.Timer）</summary>
    public int LastRemainSec { get; set; } = -1;

    /// <summary>是否已过期（用于清理）</summary>
    public bool IsExpired => Plugin.Timer >= Frame;

    /// <summary>创建新会话</summary>
    /// <param name="plrName">玩家名</param>
    /// <param name="regName">钓鱼机区域名</param>
    /// <param name="Seconds">持续秒数</param>
    public OutSelData(string regName, int Seconds)
    {
        RegionName = regName;
        Frame = Plugin.Timer + Seconds * 60; // 60帧/秒
    }

    /// <summary>添加箱子索引</summary>
    public void AddChest(int chestIdx)
    {
        if (!LogChests.Contains(chestIdx))
            LogChests.Add(chestIdx);
    }

    /// <summary>清空记录</summary>
    public void Clear() => LogChests.Clear();
}