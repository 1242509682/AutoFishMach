using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

internal class DataManager
{
    public static List<MachData> Machines = new();

    // 缓存目录
    private static string CacheDir => Path.Combine(MainPath, "钓鱼机缓存");

    // 查找钓鱼机方法
    public static MachData FindTile(Point pos)
         => Machines.FirstOrDefault(m => m.Pos == pos)!;
    public static MachData FindChest(int index)
        => Machines.FirstOrDefault(m => m.ChestIndex == index)!;
    public static MachData FindRegion(string name)
        => Machines.FirstOrDefault(m => m.RegName == name)!;
    public static bool IsAfmRegion(string name) => name.StartsWith("afm_");
    public static MachData? GetDataByXY(int x, int y)
    {
        var Regions = TShock.Regions.InAreaRegion(x, y);
        foreach (var r in Regions)
            if (IsAfmRegion(r.Name))
                return DataManager.FindRegion(r.Name);
        return null;
    }

    // 获取机器文件路径
    private static string GetPath(MachData data) =>
        Path.Combine(CacheDir, $"{data.ChestIndex}-{data.Owner}-{data.Pos.X}-{data.Pos.Y}.json");

    #region 保存指定文件
    public static void Save(MachData data)
    {
        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);

        File.WriteAllText(GetPath(data), JsonConvert.SerializeObject(data, Formatting.Indented));
    }
    #endregion

    #region 删除指定文件
    public static void Del(MachData data)
    {
        string path = GetPath(data);
        if (File.Exists(path))
            File.Delete(path);
    }
    #endregion

    #region 添加或更新机器
    public static void AddOrUpdate(MachData data)
    {
        // 查找相同坐标的旧机器
        var old = Machines.FirstOrDefault(m => m.Pos == data.Pos);
        if (old != null)
        {
            // 移除旧机器（内存列表和文件）
            Machines.Remove(old);
            Del(old);
        }

        // 添加新机器
        Machines.Add(data);
        Save(data);
    }
    #endregion

    #region 读取所有文件
    public static void LoadAll()
    {
        if (!Directory.Exists(CacheDir))
        {
            Clear();
            return;
        }

        var files = Directory.GetFiles(CacheDir, "*.json");
        var newMachines = new List<MachData>();

        foreach (var file in files)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<MachData>(File.ReadAllText(file));

                // 仅加载当前世界的机器，其他世界文件跳过不删除
                if (data?.WorldId != Main.worldID.ToString())
                    continue;

                newMachines.Add(data);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[自动钓鱼机] 加载文件 {file} 失败: {ex.Message}");
            }
        }

        Machines = newMachines;
    }
    #endregion

    #region 移除指定缓存
    public static void Remove(Point pos)
    {
        var data = Machines.FirstOrDefault(m => m.Pos == pos);
        if (data != null)
        {
            // 删除区域
            if (!string.IsNullOrEmpty(data.RegName))
            {
                try
                {
                    TShock.Regions.DeleteRegion(data.RegName);
                }
                catch (Exception ex)
                {
                    TShock.Log.ConsoleError($"[自动钓鱼机] 删除区域失败: {ex.Message}");
                }
            }

            Machines.Remove(data);
            Del(data);
        }
    }
    #endregion

    #region 清空所有
    public static void Clear()
    {
        var Regions = TShock.Regions.Regions.Where(r => IsAfmRegion(r.Name)).ToList();
        foreach (var r in Regions)
            TShock.Regions.DeleteRegion(r.Name);

        Machines.Clear();

        if (Directory.Exists(CacheDir))
            Directory.Delete(CacheDir, true);
    }
    #endregion
}