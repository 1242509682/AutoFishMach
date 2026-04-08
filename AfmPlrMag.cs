namespace FishMach;

internal class AfmPlrMag
{
    // 玩家数据类
    public static Dictionary<string, AfmPlayer> PlyDatas = new();
    public static AfmPlayer GetPlyData(string name)
    {
        if (!PlyDatas.TryGetValue(name, out var ply))
        {
            ply = new AfmPlayer();
            PlyDatas[name] = ply;
        }
        return ply;
    }
}

public class AfmPlayer
{
    public bool SetFlag; // /afm set 等待状态
    public bool InfoFlag; // /afm info 等待状态
    public bool SyncFlag; // /afm sync 等待状态
    public SelData? CurSel; // 当前会话（传输箱）
    public Dictionary<int, int> TpIdx; // 钓鱼机循环传送索引 (key: 钓鱼机箱子索引, value: 当前索引)

    public AfmPlayer() => TpIdx = new Dictionary<int, int>();

    public void ClearFlags()
    {
        SetFlag = false;
        InfoFlag = false;
        SyncFlag = false;
    }

    public void ClearAll()
    {
        ClearFlags();
        CurSel = null;
        TpIdx?.Clear();
    }
}

public class SelData
{
    public string RegionName { get; set; }
    public long Frame { get; set; }
    public int LastRemainSec { get; set; } = -1;
    public bool IsExpired => Plugin.Timer >= Frame;

    // 传输箱专用（记录箱子索引）
    public HashSet<int> Chests { get; set; } = new();

    public SelData(string regName, int seconds)
    {
        RegionName = regName;
        Frame = Plugin.Timer + seconds * 60;
    }

    public void AddChest(int idx) => Chests.Add(idx);
    public void Clear() => Chests.Clear();
}
