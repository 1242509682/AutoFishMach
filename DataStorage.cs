using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

internal class DataStorage
{
    private static List<MachData> machines = new();
    private static Dictionary<Point, MachData> posMap = new();
    private static Dictionary<int, MachData> chestMap = new();

    // 对外暴露只读列表
    public static IReadOnlyList<MachData> Machines => machines;

    private static bool dirty = false; // 脏标志
    public static bool IsDirty => dirty;
    public static void SetDirty() => dirty = true;  // 允许外部标记脏数据

    // 查找钓鱼机方法
    public static MachData FindTile(Point pos) => posMap.GetValueOrDefault(pos)!;
    public static MachData FindChest(int index) => chestMap.GetValueOrDefault(index)!;

    // 添加或更新
    public static void AddOrUpdate(MachData data)
    {
        // 移除旧的映射（如果坐标已存在）
        if (posMap.TryGetValue(data.Pos, out var old))
        {
            machines.Remove(old);
            chestMap.Remove(old.ChestIndex);
        }

        machines.Add(data);
        posMap[data.Pos] = data;
        chestMap[data.ChestIndex] = data;
        SpatialIdx.Add(data);
        dirty = true;
    }

    // 重置清空数据
    public static void Clear()
    {
        machines.Clear();
        posMap.Clear();
        chestMap.Clear();
        SpatialIdx.Clear();
        dirty = true;
    }

    // 移除
    public static void Remove(Point pos)
    {
        if (posMap.TryGetValue(pos, out var data))
        {
            machines.Remove(data);
            posMap.Remove(pos);
            chestMap.Remove(data.ChestIndex);
            SpatialIdx.Remove(data);
            dirty = true;
        }
    }

    // 保存数据到文件（仅在脏标志为 true 时执行）
    public static void Save()
    {
        if (!dirty) return;

        if (!Directory.Exists(MainPath))
            Directory.CreateDirectory(MainPath);

        string file = CachePath(Main.worldID);
        if (machines.Count == 0)
        {
            if (File.Exists(file))
                File.Delete(file);
            dirty = false;
            return;
        }

        File.WriteAllText(file, JsonConvert.SerializeObject(machines, Formatting.Indented));
        dirty = false;
    }

    // 加载时重建映射
    public static void Load()
    {
        string file = CachePath(Main.worldID);
        if (!File.Exists(file))
        {
            Clear();
            return;
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<MachData>>(File.ReadAllText(file));
            machines = list ?? new List<MachData>();
            posMap = machines.ToDictionary(m => m.Pos, m => m);
            chestMap = machines.ToDictionary(m => m.ChestIndex, m => m);
            dirty = false;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[自动钓鱼机] 加载缓存失败: {ex.Message}");
            Clear();
        }
    }
}