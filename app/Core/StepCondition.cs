using System.Diagnostics;
using System.IO;

namespace Clockwork.Core;

// 步骤条件求值需要的三个「机器现状」探针。收成一个 record 是为了可注入：
// 判定逻辑本身留在纯函数里可单测，真去查进程 / 电源 / 磁盘的那三下才是不纯的部分。
public sealed record StepEnv(Func<string, bool> ProcessRunning, Func<bool> OnAcPower, Func<string, bool> PathExists)
{
    public static readonly StepEnv Live = new(
        name =>
        {
            try
            {
                var ps = Process.GetProcessesByName(name);
                foreach (var p in ps) p.Dispose();   // GetProcessesByName 每个都持一个句柄，不释放就是每次判定漏一批
                return ps.Length > 0;
            }
            catch { return false; }
        },
        () => Native.PowerStatus.OnAc(),
        // 「存在」= 文件或目录，与 LaunchTarget.ResolveLaunchTarget 的备用路径同一口径
        // （用户填个文件夹路径当条件是很自然的事，只认文件会让它永远不成立）。
        path => { try { return File.Exists(path) || Directory.Exists(path); } catch { return false; } });
}

// 步骤执行条件（星期 / 时刻窗口 / 进程 / 电源 / 路径）是否满足。
// 顶层启动清单与动作组内步骤统一遵守——不满足即跳过。缺失字段按「无限制」处理，条件之间是 AND。
public static class StepCondition
{
    // .NET DayOfWeek（周日=0）→ ISO（周一=1..周日=7）。
    public static int IsoDayOfWeek(DateTime d)
    {
        var iso = (int)d.DayOfWeek;
        return iso == 0 ? 7 : iso;
    }

    // 哨兵解析：hour<0 / isoDay<=0 约定为「取当前」，统一在此解析为具体值，避免各调用点各写一份。
    // now 由调用方传入——开机序列用可注入时钟(nowDt)、动作组用 DateTime.Now，各保留自己的时间源。
    public static (int hour, int isoDay) ResolveSentinels(int hour, int isoDay, DateTime now)
        => (hour < 0 ? now.Hour : hour, isoDay <= 0 ? IsoDayOfWeek(now) : isoDay);

    // currentMinute 默认 0 → 只给小时的老调用（及测试）等价于「N:00」，行为不变；
    // 生产调用点传入当前分钟，实现分钟级精度。
    // env=null → 用真实探针（StepEnv.Live）。三个环境条件都只在「用户确实配了」时才去查，
    // 没配的步骤一次探针都不会跑——否则每条开机步骤都白白枚举一遍进程表。
    public static bool IsSatisfied(LaunchStep s, int currentHour, int currentIsoDay, int currentMinute = 0, StepEnv? env = null)
    {
        if (currentIsoDay <= 0) currentIsoDay = IsoDayOfWeek(DateTime.Now);
        int nowMinutes = currentHour * 60 + currentMinute;
        if (s.OnlyBefore8 && nowMinutes >= StepHelpers.BeforeMinutesOfDay(s)) return false;
        // 「仅 N 后」含阈值那一分钟（18:00 勾了「仅 18:00 后」要执行），与「仅 N 前」不含阈值互补，
        // 两条同时开就正好把一天切成不重不漏的区间。
        if (s.OnlyAfter && nowMinutes < StepHelpers.AfterMinutesOfDay(s)) return false;
        var days = s.Days ?? new();
        if (days.Count > 0 && !days.Contains(currentIsoDay)) return false;

        var probe = env ?? StepEnv.Live;
        if (s.IfProcessMode is "running" or "notRunning" && !string.IsNullOrWhiteSpace(s.IfProcess))
        {
            bool running = probe.ProcessRunning(StepHelpers.ToProcessName(s.IfProcess));
            if (running != (s.IfProcessMode == "running")) return false;
        }
        if (s.IfPower is "ac" or "battery")
        {
            if (probe.OnAcPower() != (s.IfPower == "ac")) return false;
        }
        if (!string.IsNullOrWhiteSpace(s.IfPathExists) && !probe.PathExists(s.IfPathExists.Trim())) return false;
        return true;
    }
}
