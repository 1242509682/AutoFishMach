using static FishMach.DataManager;
using static FishMach.Utils;
using Terraria;
using TShockAPI;

namespace FishMach;

/// <summary>
/// 无电路模式下的钓鱼机调度器（基于优先队列）
/// </summary>
internal static class FishSched
{
    private static readonly PriorityQueue<MachData, long> PQ = new();
    private static readonly HashSet<MachData> Invalid = new();   // 失效机器集合

    /// <summary>清空调度器</summary>
    public static void Clear()
    {
        PQ.Clear();
        Invalid.Clear();
    }

    /// <summary>标记机器为失效，下次调度时会被丢弃</summary>
    public static void Invalidate(MachData data)
    {
        if (data != null)
            Invalid.Add(data);
    }

    /// <summary>初始化调度器（清空并重新加入所有机器）</summary>
    public static void Init()
    {
        PQ.Clear();
        Invalid.Clear();
        foreach (var data in Machines)
        {
            if (data.nextFrame == 0)
            {
                int min = Plugin.Config.MinFrames;
                int max = Plugin.Config.MaxFrames;
                int delay = min == max ? min : Main.rand.Next(min, max + 1);
                data.nextFrame = Plugin.Timer + delay;
            }
            PQ.Enqueue(data, data.nextFrame);
        }
    }

    /// <summary>
    /// 处理到期的机器
    /// </summary>
    /// <param name="timer">当前帧计数</param>
    /// <param name="exec">执行钓鱼逻辑的委托，返回新的 nextFrame</param>
    public static void Update(long timer)
    {
        while (PQ.TryPeek(out var data, out var next) && timer >= next)
        {
            PQ.Dequeue();

            // 检查失效标记或已被移除
            if (data == null || data.ChestIndex == -1 || Invalid.Contains(data))
            {
                if (data != null)
                {
                    Invalid.Remove(data);   // 清理标记
                    data.ClearAnim();       // 清理动画
                }
                continue;
            }

            // 正常执行钓鱼
            var engine = data.Engine ?? (data.Engine = new AutoFishing(data));
            engine.Execute();

            int min = Plugin.Config.MinFrames;
            int max = Plugin.Config.MaxFrames;
            int delay = min == max ? min : Main.rand.Next(min, max + 1);
            long newNext = timer + delay;
            data.nextFrame = newNext;
            PQ.Enqueue(data, newNext);
        }
    }
}