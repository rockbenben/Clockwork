using System.Diagnostics;
using System.Linq;

namespace Clockwork.Core;

// 「此刻谁可能排在我们的鼠标钩子前面」——一份**诊断用**的候选名单。
//
// 为什么需要：MouseHook.UpstreamMaxMs 与 App 那边的心跳已经把「钩子链被上游堵住」这个结论
// 变得可自证了（死寂大 + 上游延迟也大 = 排在我们前面的某个钩子不返回）。但**堵的人是谁**，
// 从我们这一侧永远问不到：Windows 不提供任何 API 去枚举别的进程装的低级钩子——那条链只活在
// win32k 里，连个句柄都不给外面。于是「谁的钩子卡住了」这件事，除了问「谁在场」没有第二种拿法。
//
// 而会装 WH_MOUSE_LL 的程序就那么几类：鼠标驱动（改键 / 手势按钮）、热键与手势工具、
// 截图贴图、远程控制、启动器。这份名单就是那几类里能被进程名认出来的。
//
// **它证明的是「在不在场」，不是「有没有罪」。** 名单之外的工具堵了链，这里一个字都不会有；
// 反过来，在场的几家也未必是它堵的——谁装钩子谁就排进链里，但只有**不返回**的那个才算堵。
// 所以它的用法是把「链被堵了」收敛成「当时这几家在场，一家一家关掉去试」，不是当判决书。
// 也正因为它是启发式的，只在**已经出问题**的那两条路上采（自愈、--hookprobe），不进热路径。
public static class HookSuspects
{
    // 每一行 = 「进程名里出现这个子串」→「归到哪一家」。上榜理由必须能被复核，
    // 否则这份名单会慢慢长成一份谁也不敢删的迷信（这类清单的通病）。
    //
    // 匹配是子串、不区分大小写：同一家的进程名在各版本间会变，而且一家常有好几个进程
    //（罗技那一套就同时有 agent / PluginService / PluginServiceExt 三个）。
    // 按「家」归并而不是按进程名：读日志的人要的是「关掉谁」，不是「关掉哪个 exe」。
    private static readonly (string Pattern, string Family)[] Known =
    {
        // 鼠标驱动。装 WH_MOUSE_LL 是为了「智能动作 / 手势按钮 / 改键」——按下那一刻要拦下来改判，
        // 而低级钩子是唯一能在消息进系统之前插手的办法。
        ("logioptionsplus", "LogiOptions+"),
        ("logipluginservice", "LogiOptions+"),
        ("lghub", "LogitechGHub"),
        // PowerToys：AlwaysOnTop 的鼠标快捷键、FancyZones、鼠标实用工具都靠低级钩子取键。
        ("powertoys", "PowerToys"),
        // AutoHotkey：脚本里一句 InstallMouseHook() 或任何一个鼠标热键都会装。
        // 而且它的钩子回调是在自己那条主线程上跑热键子程序的——子程序一慢，整条鼠标链跟着等。
        ("autohotkey", "AutoHotkey"),
        // Quicker 专门有一条「重新挂钩键鼠」命令，说明它自己就在链里（同类工具里最典型的一个）。
        ("quicker", "Quicker"),
        // 手势 / 改键工具，一律靠低级钩子。
        ("xmousebuttoncontrol", "XMouseButtonControl"),
        ("x-mouse", "XMouseButtonControl"),
        ("strokeit", "StrokeIt"),
        ("mouseinc", "MouseInc"),
        ("wgestures", "WGestures"),
        // 截图贴图：贴图窗口要跟着光标、快捷键要全局，多数实现走低级钩子。
        ("snipaste", "Snipaste"),
        ("sharex", "ShareX"),
        // 坚果云这个进程名自己就写着 WindowsHook。
        ("nutstore.windowshook", "Nutstore"),
        // 远程控制：鼠标事件要转发到对端，一定在链里。
        ("todesk", "ToDesk"),
        ("sunlogin", "Sunlogin"),
        ("anydesk", "AnyDesk"),
        ("teamviewer", "TeamViewer"),
        ("rustdesk", "RustDesk"),
        // 启动器：Listary 默认就是「双击 Ctrl 或鼠标快捷键呼出」，uTools / Wox 同类。
        ("utools", "uTools"),
        ("listary", "Listary"),
        ("wox", "Wox"),
    };

    /// <summary>名单本身（诊断与测试用）。暴露出来是为了让「同一家的几个进程名都列了没有」这类事可断言，而不是靠 review。</summary>
    public static IReadOnlyList<(string Pattern, string Family)> KnownPatterns => Known;

    /// <summary>从一批进程名里挑出候选，按「家」去重、按名字排序。<paramref name="processNames"/> 为空 → 空数组。</summary>
    //
    // 返回「家」而不是进程名：PowerToys 一开就是六个进程，逐进程报会把日志那一格撑成一条
    // 没人读得完的长串，而它们本来就是同一个开关。
    public static string[] Census(IEnumerable<string> processNames)
    {
        var hit = new List<string>();
        foreach (var name in processNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var (pattern, family) in Known)
                if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase) && !hit.Contains(family))
                    hit.Add(family);
        }
        hit.Sort(StringComparer.Ordinal);
        return hit.ToArray();
    }

    /// <summary>拼成日志里那一格的值：<c>none</c> 或 <c>A,B</c>。</summary>
    //
    // 独立成一个方法（而不是让两个调用点各自 string.Join）是为了让「一个都没有时写什么」
    // 只有一处定义：那一格如果留空，读日志的人分不出「当时没人」和「这版还没采」——
    // 而这两种情况该引出的结论完全相反。
    public static string Describe(IEnumerable<string> processNames)
    {
        var families = Census(processNames);
        return families.Length == 0 ? "none" : string.Join(",", families);
    }

    /// <summary>此刻在跑的进程名（去重，不区分大小写）。取不到就返回空——诊断信息不值得为它抛。</summary>
    //
    // 逐个 try：系统进程、提权进程、正在退出的进程访问 ProcessName 都会抛，
    // 而这一格的意义只是「在场名单」，少一个不影响结论；外面再包一层是为了
    // 「枚举本身失败」（句柄耗尽之类）不至于把调用它的那条自愈路一起带走。
    public static IEnumerable<string> RunningProcessNames()
    {
        try
        {
            var names = new List<string>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var n = p.ProcessName;
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }
}
