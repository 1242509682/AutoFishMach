using Microsoft.Xna.Framework;

namespace FishMach;

// 空间索引网格搜索：用于液体放置事件与图格编辑事件,查找受影响范围
public static class SpatialIdx
{
    private const int gridSize = 50; // 网格大小
    private static Dictionary<(int, int), List<MachData>> grid = new();

    #region 增删方法
    public static void Clear() => grid.Clear();
    public static void Add(MachData data)
    {
        int cx = data.Pos.X / gridSize;
        int cy = data.Pos.Y / gridSize;
        var key = (cx, cy);
        if (!grid.ContainsKey(key)) grid[key] = new List<MachData>();
        if (!grid[key].Contains(data)) grid[key].Add(data);
    }
    public static void Remove(MachData data)
    {
        int cx = data.Pos.X / gridSize;
        int cy = data.Pos.Y / gridSize;
        var key = (cx, cy);
        if (grid.TryGetValue(key, out var list))
        {
            list.Remove(data);
            if (list.Count == 0) grid.Remove(key);
        }
    } 
    #endregion

    // 内部使用：返回相邻网格的所有机器（不进行距离筛选）
    private static List<MachData> GetNearby(Point center, int radius)
    {
        int cx = center.X / gridSize;
        int cy = center.Y / gridSize;
        // 向上取整，例如:半径=200, 网格大小=50 → 偏移=4
        int offset = (radius + gridSize - 1) / gridSize; 
        var result = new List<MachData>();
        for (int dx = -offset; dx <= offset; dx++)
            for (int dy = -offset; dy <= offset; dy++)
                if (grid.TryGetValue((cx + dx, cy + dy), out var list))
                    result.AddRange(list);
        return result;
    }

    // 公开方法：获取指定中心半径内的机器
    public static List<MachData> GetInRadius(Point center, int radius)
    {
        var nearby = GetNearby(center, radius);
        var result = new List<MachData>();
        int radSq = radius * radius;
        foreach (var m in nearby)
        {
            int dx = m.Pos.X - center.X;
            int dy = m.Pos.Y - center.Y;
            if (dx * dx + dy * dy <= radSq)
                result.Add(m);
        }
        return result;
    }
}