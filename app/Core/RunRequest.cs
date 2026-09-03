using System.IO;

namespace Clockwork.Core;

// 跨实例的「跑这个动作组」请求。给 --run-group 用：外部（计划任务 / AHK / Stream Deck / 桌面快捷方式）
// 启一个新进程说要跑哪个组，托盘里那个常驻实例接住并执行，新进程说完就退。
//
// 为什么是「文件 + 命名事件」而不是命名管道 / WM_COPYDATA：
//   · 命名管道要一条服务端接受循环、一份取消令牌、一套断线处理，为传一个字符串背这些不划算；
//   · WM_COPYDATA 要先找到对方的窗口句柄，而本程序的常驻窗口是隐藏的、没有稳定标题可认；
//   · 事件对象已经有一个同款（_showEvent «叫醒并显示窗口»），这条只是它的兄弟，形状照抄即可。
//
// 落地位置固定在 %LOCALAPPDATA%\Clockwork\，不跟着配置走。配置可能在 exe 旁边，而 exe 可能在
// Program Files 下——那样一个非提权的请求方就写不进去，而「用计划任务定时跑一个组」恰恰是
// 最典型的请求方。LOCALAPPDATA 是每用户、必然可写，且天然与单实例的 Local\ 边界（每登录会话）对齐。
public static class RunRequest
{
    /// <summary>常驻实例监听的命名事件。与单实例的 show 事件同一命名空间，同一条 Local\ 会话边界。</summary>
    public const string EventName = @"Local\rockbenben.clockwork.run";

    /// <summary>外部触发动作组的命令行开关：<c>--run-group "专注"</c> 或 <c>--run-group=专注</c>。</summary>
    public const string Arg = "--run-group";

    /// <summary>取 <see cref="Arg"/> 的参数（组名或组 id）。没给这个开关、或给了但没跟值 → null。</summary>
    //
    // 两种写法都收：空格分隔是计划任务「添加参数」框里最自然的写法，等号形式则是快捷方式和脚本里常见的。
    // 名字里有空格要加引号，和任何命令行一样（那一层由 Windows 的命令行拆分负责，到这儿已经是一个元素）。
    //
    // 放在 Core 而不是 App 里，是为了能测：这是个用户对着一条命令行手打出来的入口，
    // 而它出错的样子是「计划任务安安静静什么都没干」——最难自查的一类，值得钉住。
    public static string? ParseGroupArg(string[]? args)
    {
        if (args == null) return null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(Arg + "=", StringComparison.OrdinalIgnoreCase))
                return Clean(args[i].Substring(Arg.Length + 1));
            if (!args[i].Equals(Arg, StringComparison.OrdinalIgnoreCase)) continue;
            // 后面那个元素也可能是另一个开关（`--run-group --show`）——那属于「给了开关没跟值」，
            // 当成 null 而不是把 "--show" 当组名去找。否则用户会收到一条「找不到动作组 --show」，
            // 而真正的问题是他漏了组名。
            var next = i + 1 < args.Length ? args[i + 1] : null;
            return next != null && !next.StartsWith("--", StringComparison.Ordinal) ? Clean(next) : null;
        }
        return null;
    }

    private static string? Clean(string s) => s.Trim() is { Length: > 0 } v ? v : null;

    public static string Path_()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clockwork");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "clockwork.request");
    }

    // 请求方：写下要跑的组（名称或 id）。写失败返回 false——调用方据此报错而不是假装送达。
    public static bool Write(string group)
    {
        try { File.WriteAllText(Path_(), group ?? ""); return true; }
        catch { return false; }
    }

    // 接收方：取走请求并删掉文件。取不到返回 null。
    //
    // 先读后删、删失败也照常返回内容：留下一个删不掉的文件最坏是下次信号来时重跑一次同一个组，
    // 而因为删不掉就吞掉这次请求，是让用户按了没反应。两害相权取其轻。
    public static string? Take()
    {
        try
        {
            var p = Path_();
            if (!File.Exists(p)) return null;
            var s = File.ReadAllText(p).Trim();
            try { File.Delete(p); } catch { }
            return s.Length == 0 ? null : s;
        }
        catch { return null; }
    }
}
