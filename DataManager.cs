using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

internal class DataManager
{
    private static List<MachData> machines = new();
    private static Dictionary<Point, MachData> posMap = new();
    private static Dictionary<int, MachData> chestMap = new();
    private static Dictionary<string, MachData> regionMap = new();

    // 通过区域名查找玩家
    public static MachData FindByRgn(string name) => regionMap.GetValueOrDefault(name)!;

    // 对外暴露只读列表
    public static IReadOnlyList<MachData> Machines => machines;

    private static bool CanSave = false; // 脏标志
    public static bool IsCanSave => CanSave; // 标记为自动保存
    public static void NeedSave()
    {
        CanSave = true;
        Save();
    }

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

            if (!string.IsNullOrEmpty(old.RegName)) 
                regionMap.Remove(old.RegName);
        }

        machines.Add(data);
        posMap[data.Pos] = data;
        chestMap[data.ChestIndex] = data;
        chestMap[data.ChestIndex] = data;
        if (!string.IsNullOrEmpty(data.RegName)) 
            regionMap[data.RegName] = data;

        NeedSave();
    }

    // 重置清空数据
    public static void Clear()
    {
        machines.Clear();
        posMap.Clear();
        chestMap.Clear();
        regionMap.Clear();
        NeedSave();
    }

    // 移除
    public static void Remove(Point pos)
    {
        if (posMap.TryGetValue(pos, out var data))
        {
            machines.Remove(data);
            posMap.Remove(pos);
            chestMap.Remove(data.ChestIndex);
            if (!string.IsNullOrEmpty(data.RegName)) 
                regionMap.Remove(data.RegName);
            CanSave = true;
        }
    }

    // 保存数据到文件（仅在脏标志为 true 时执行）
    public static void Save()
    {
        if (!CanSave) return;

        if (!Directory.Exists(MainPath))
            Directory.CreateDirectory(MainPath);

        string file = CachePath(Main.worldID);
        if (machines.Count == 0)
        {
            if (File.Exists(file))
                File.Delete(file);
            CanSave = false;
            return;
        }

        File.WriteAllText(file, JsonConvert.SerializeObject(machines, Formatting.Indented));
        CanSave = false;
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
            regionMap = machines.ToDictionary(m => m.RegName, m => m);
            CanSave = false;
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[自动钓鱼机] 加载缓存失败: {ex.Message}");
            Clear();
        }
    }
}