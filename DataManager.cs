using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Terraria;
using TShockAPI;
using static FishMach.Plugin;

namespace FishMach;

internal class DataManager
{
    // 缓存目录
    private static string CacheDir => Path.Combine(MainPath, "钓鱼机缓存");

    // 查找钓鱼机方法
    public static List<MachData> Machines = new();
    public static Dictionary<Point, MachData> MachByPos = new();
    public static Dictionary<int, MachData> MachByChest = new();
    public static Dictionary<string, MachData> MachByRegName = new();
    // 查找传输箱方法
    public static Dictionary<int, HashSet<MachData>> OutChestMap = new();


    public static MachData? FindTile(Point pos)
        => MachByPos.TryGetValue(pos, out var data) ? data : null;
    public static MachData? FindChest(int idx)
        => MachByChest.TryGetValue(idx, out var data) ? data : null;
    public static MachData? FindRegion(string name)
        => MachByRegName.TryGetValue(name, out var data) ? data : null;

    public static bool IsAfmRegion(string name) => name.StartsWith("afm_");

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
    public static void AddOrSave(MachData data)
    {
        // 移除旧映射（先查找旧位置）
        if (MachByPos.ContainsKey(data.Pos))
            MachByPos.Remove(data.Pos);
        // 移除旧箱子映射
        var oldChest = MachByChest.FirstOrDefault(x => x.Value == data).Key;
        if (oldChest != 0)
            MachByChest.Remove(oldChest);
        // 移除旧区域映射
        var oldRegion = MachByRegName.FirstOrDefault(x => x.Value == data).Key;
        if (oldRegion != null)
            MachByRegName.Remove(oldRegion);

        // 添加新映射
        if (!Machines.Contains(data))
            Machines.Add(data);

        MachByPos[data.Pos] = data;

        if (data.ChestIndex != -1)
            MachByChest[data.ChestIndex] = data;

        if (!string.IsNullOrEmpty(data.RegName))
            MachByRegName[data.RegName] = data;

        // 处理传输箱映射（一对多）
        foreach (var outIdx in data.OutChests)
        {
            if (!OutChestMap.TryGetValue(outIdx, out var set))
            {
                set = new HashSet<MachData>();
                OutChestMap[outIdx] = set;
            }
            set.Add(data);
        }

        Save(data);
    }
    #endregion

    #region 设置输出机映射
    /// <summary>添加一个传输箱，自动维护映射并保存</summary>
    public static void AddOutChest(MachData data, int newOut)
    {
        if (data == null || newOut == -1) return;
        if (data.OutChests.Contains(newOut)) return;

        data.OutChests.Add(newOut);
        if (!OutChestMap.TryGetValue(newOut, out var set))
        {
            set = new HashSet<MachData>();
            OutChestMap[newOut] = set;
        }
        set.Add(data);
        Save(data);
    }

    /// <summary>移除一个传输箱，自动维护映射并保存</summary>
    public static void RemoveOutChest(MachData data, int outIdx)
    {
        if (data == null || !data.OutChests.Remove(outIdx)) return;

        if (OutChestMap.TryGetValue(outIdx, out var set))
        {
            set.Remove(data);
            if (set.Count == 0)
                OutChestMap.Remove(outIdx);
        }
        Save(data);
    }

    /// <summary>清空所有传输箱</summary>
    public static void ClearOutChests(MachData data)
    {
        if (data == null) return;
        foreach (var outIdx in data.OutChests.ToList())
            RemoveOutChest(data, outIdx);
    }
    #endregion

    #region 读取所有文件
    public static void LoadAll()
    {
        if (!Directory.Exists(CacheDir))
        {
            // 目录不存在：清空内存数据（无需删除物理目录）
            Machines.Clear();
            MachByPos.Clear();
            MachByChest.Clear();
            MachByRegName.Clear();
            OutChestMap.Clear();
            return;
        }

        var files = Directory.GetFiles(CacheDir, "*.json");
        var newMachines = new List<MachData>();

        foreach (var file in files)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<MachData>(File.ReadAllText(file));
                if (data?.WorldId != Main.worldID.ToString())
                    continue;
                newMachines.Add(data);
            }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[自动钓鱼机] 加载文件 {file} 失败: {ex.Message}");
            }
        }

        // 清空现有字典
        MachByPos.Clear();
        MachByChest.Clear();
        MachByRegName.Clear();
        OutChestMap.Clear();

        // 赋值新列表
        Machines = newMachines;

        // 重建字典映射
        foreach (var data in Machines)
        {
            MachByPos[data.Pos] = data;

            if (data.ChestIndex != -1)
                MachByChest[data.ChestIndex] = data;

            if (!string.IsNullOrEmpty(data.RegName))
                MachByRegName[data.RegName] = data;

            if (data.HasOut)
                foreach (var outIdx in data.OutChests)
                {
                    if (!OutChestMap.TryGetValue(outIdx, out var set))
                    {
                        set = new HashSet<MachData>();
                        OutChestMap[outIdx] = set;
                    }
                    set.Add(data);
                }

            foreach (var kv in data.Custom)
            {
                if (kv.Value.Expiry.Kind != DateTimeKind.Utc)
                    kv.Value.Expiry = DateTime.SpecifyKind(kv.Value.Expiry, DateTimeKind.Utc);
            }

            foreach (var kv in data.ActiveZoneBuffs)
            {
                if (kv.Value.Kind != DateTimeKind.Utc)
                    data.ActiveZoneBuffs[kv.Key] = DateTime.SpecifyKind(kv.Value, DateTimeKind.Utc);
            }

        }
    }
    #endregion

    #region 移除指定缓存
    public static void Remove(Point pos)
    {
        var data = FindTile(pos);
        if (data == null) return;

        data.ClearAnim();

        // 从动画活跃集合中移除
        ActiveAnim.Remove(data);

        MachByPos.Remove(pos);

        if (!string.IsNullOrEmpty(data.RegName))
        {
            try { TShock.Regions.DeleteRegion(data.RegName); }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[自动钓鱼机] 删除区域 {data.RegName} 失败: {ex.Message}");
            }
        }

        if (data.ChestIndex != -1)
            MachByChest.Remove(data.ChestIndex);

        if (!string.IsNullOrEmpty(data.RegName))
            MachByRegName.Remove(data.RegName);

        // 从传输箱映射中移除
        foreach (var outIdx in data.OutChests)
        {
            if (OutChestMap.TryGetValue(outIdx, out var set))
            {
                set.Remove(data);
                if (set.Count == 0)
                    OutChestMap.Remove(outIdx);
            }
        }

        Machines.Remove(data);

        // 标记调度器中的机器为失效
        FishSched.Invalidate(data);
        Del(data);
    }
    #endregion

    #region 清空所有
    public static void Clear()
    {
        // 清除所有区域
        var Regions = TShock.Regions.Regions.Where(r => IsAfmRegion(r.Name)).ToList();
        foreach (var r in Regions)
        {
            try { TShock.Regions.DeleteRegion(r.Name); }
            catch (Exception ex)
            {
                TShock.Log.ConsoleError($"[自动钓鱼机] 删除区域 {r.Name} 失败: {ex.Message}");
            }
        }

        ActiveAnim.Clear(); // 清空动画活跃集合
        Machines.Clear();
        MachByPos.Clear();
        MachByChest.Clear();
        MachByRegName.Clear();
        OutChestMap.Clear();
        FishSched.Clear();

        if (Directory.Exists(CacheDir))
            Directory.Delete(CacheDir, true);
    }
    #endregion
}