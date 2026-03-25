using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static Plugin.Plugin;

namespace Plugin;

internal class DataStorage
{
    private static List<MachData> machines = new();

    // 获取所有机器的副本
    public static List<MachData> GetAll() => machines.ToList();

    // 查找指定坐标的机器
    public static MachData Find(Point pos) => machines.FirstOrDefault(m => m.Pos.X == pos.X && m.Pos.Y == pos.Y)!;

    // 添加或更新机器（如果坐标已存在则替换）
    public static void AddOrUpdate(MachData data)
    {
        var existing = machines.FirstOrDefault(m => m.Pos == data.Pos);
        if (existing != null)
            machines.Remove(existing);
        machines.Add(data);
        Save();
    }

    // 重置数据
    public static void Clear()
    {
        machines.Clear();
        Save();
    }

    // 移除指定坐标的机器
    public static void Remove(Point pos)
    {
        machines.RemoveAll(m => m.Pos.X == pos.X && m.Pos.Y == pos.Y);
        Save();
    }

    // 保存数据到文件
    public static void Save()
    {
        if (!Directory.Exists(MainPath))
            Directory.CreateDirectory(MainPath);

        string file = CachePath(Main.worldID);
        if (machines.Count == 0)
        {
            if (File.Exists(file)) 
                File.Delete(file);
            return;
        }

        // 直接序列化整个列表，所有带 JsonProperty 的字段都会被保存
        File.WriteAllText(file, JsonConvert.SerializeObject(machines, Formatting.Indented));
    }

    // 从文件加载数据
    public static void Load()
    {
        string file = CachePath(Main.worldID);
        if (!File.Exists(file))
        {
            machines.Clear();
            return;
        }

        try
        {
            var list = JsonConvert.DeserializeObject<List<MachData>>(File.ReadAllText(file));
            machines = list ?? new List<MachData>();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[自动钓鱼机] 加载缓存失败: {ex.Message}");
            machines = new List<MachData>();
        }
    }
}