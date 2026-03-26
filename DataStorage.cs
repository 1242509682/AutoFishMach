using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static Plugin.Plugin;

namespace Plugin;

internal class DataStorage
{
    private static List<MachData> machines = new();
    public static IReadOnlyList<MachData> Machines => machines;
    private static bool dirty = false; // 脏标志
    public static bool IsDirty => dirty;
    public static void SetDirty() => dirty = true;  // 允许外部标记脏数据

    // 查找钓鱼机方法
    public static MachData FindTile(Point pos) => machines.FirstOrDefault(m => m.Pos == pos)!;
    public static MachData FindChest(int index) => machines.FirstOrDefault(m => m.ChestIndex == index)!;

    // 添加或更新机器（如果坐标已存在则替换）
    public static void AddOrUpdate(MachData data)
    {
        var existing = machines.FirstOrDefault(m => m.Pos == data.Pos);
        if (existing != null)
            machines.Remove(existing);
        machines.Add(data);
        dirty = true;
    }

    // 重置数据
    public static void Clear()
    {
        machines.Clear();
        dirty = true;
    }

    // 移除指定坐标的机器
    public static void Remove(Point pos)
    {
        machines.RemoveAll(m => m.Pos.X == pos.X && m.Pos.Y == pos.Y);
        dirty = true;
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

    // 从文件加载数据
    public static void Load()
    {
        string file = CachePath(Main.worldID);
        if (!File.Exists(file))
        {
            machines.Clear();
            dirty = false;
            return;
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<MachData>>(File.ReadAllText(file));
            machines = list ?? new List<MachData>();
            dirty = false;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[自动钓鱼机] 加载缓存失败: {ex.Message}");
            machines = new List<MachData>();
            dirty = false;
        }
    }
}