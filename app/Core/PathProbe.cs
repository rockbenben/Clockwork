using System.Collections.Concurrent;
using System.IO;

namespace Clockwork.Core;

/// <summary>「这个路径存在吗」的**不阻塞**问法：缓存里有就答，没有就先当它在，同时在后台去问。</summary>
//
// 为什么需要它：面板每呼出一次，都要为每一个「运行程序」格子决定图标取自哪个文件——
// 那一步会调 LaunchTarget.ResolveLaunchTarget，而它做的是 File.Exists / Directory.Exists。
// **这些全在 UI 线程上**（面板构造、管理器每次重画、三个编辑器的图标预览）。
// 路径是用户填的，可以指向一个已断开的网络盘或关机的 NAS，那时一次 File.Exists 就卡满 SMB 超时。
//
// 而 UI 线程同时是低级鼠标钩子的泵：被占住超过 LowLevelHooksTimeout（默认 300ms），
// Windows 会把钩子**静默卸掉**，中键长按和全部右键手势从此失灵，直到重启。
// IconLoader 早已为此改成一秒都不等（见其注释），可这条探测把那道防线整个绕了过去。
//
// 「先当它在」这个乐观答案是安全的：它只影响**取图标时先试哪个路径**，而取图标本身
// 也是不阻塞的（IconLoader），文件真不在就回退到线描字形。下一次呼出面板时，
// 后台那次探测早已回填，答案就是真的了。
//
// 只给取图标这条路用。真正要启动程序时仍走 LaunchTarget 原来的同步探测——
// 那发生在工作线程上，等得起，而且那时「路径到底在不在」必须是真答案。
public static class PathProbe
{
    // 比较器与 IconLoader.Cache 一致：Windows 路径不区分大小写，
    // 各记一份会让同一个文件按两种写法各探一次、甚至各自记住不同的答案。
    private static readonly ConcurrentDictionary<string, (bool Ok, long At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);

    // **否定结果会过期，肯定结果不会。**
    //
    // 这个程序在托盘里挂几周。NAS 关机 / 网络盘断开那一刻探到的「不存在」，
    // 原先会被永久记住——盘回来之后那一格仍然是线描字形，重启才好，而用户看不出
    // 那是缓存在骗他（图标本来就允许取不到，两种情形长得一模一样）。
    //
    // 肯定结果不必过期：这个答案只决定「取图标先试哪个路径」，文件真没了，
    // 取图标那一步自己会失败并回退到字形。所以只让否定的那一半重新去问。
    private const long NegativeTtlMs = 60_000;

    public static bool ExistsOptimistic(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return false;
        if (Cache.TryGetValue(p, out var hit)
            && (hit.Ok || Environment.TickCount64 - hit.At < NegativeTtlMs)) return hit.Ok;
        if (InFlight.TryAdd(p, 0))
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok;
                try { ok = File.Exists(p) || Directory.Exists(p); }
                catch { ok = false; }
                Cache[p] = (ok, Environment.TickCount64);
                InFlight.TryRemove(p, out _);
            });
        return true;   // 这一轮先当它在；取图标那一步自己会失败并回退到字形
    }

}
