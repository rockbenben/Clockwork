using System.Text;
using System.Windows.Automation;

namespace Clockwork.Native;

/// <summary>
/// 从浏览器地址栏读当前网址。
///
/// 为什么走 UI Automation 而不是「按窗口标题猜」：**标题里没有网址**。
/// Chrome 开着 https://newzone.top/blog.html 时，标题是
/// 「LearnData开源笔记博客 | LearnData 开源笔记 - Google Chrome」——
/// 只有页面标题和浏览器名，Win32 那套（EnumWindows / GetWindowText）
/// 再全也取不到地址栏的内容。所以这不是「没写」，是那一层写不了。
///
/// 为什么不用 Ctrl+L / Ctrl+C 剪贴板那条路：它**依赖焦点**。
/// 由本程序启动脚本步骤时，ShellExecute 起 powershell.exe 会弹控制台窗口
/// 并把焦点抢过去（实测前台从 chrome 变成控制台），Ctrl+L 于是打到控制台自己身上。
/// UIA 直接问窗口要，不碰焦点、不碰剪贴板。
///
/// 只覆盖 Chromium 系（Chrome / Edge / Brave / Vivaldi / Opera）。
/// Firefox 的 UIA 是经 IAccessible2 桥接的、默认不暴露可用的 ValuePattern，
/// 与其为它单写一套（还大概率不稳），不如让调用方退回剪贴板那招。
/// </summary>
public static class BrowserUrlReader
{
    /// <summary>Chromium 系所有窗口的窗口类名。</summary>
    // **不能只靠它认浏览器**：这个类名被一堆 Chromium 应用共用——
    // 实测一台机器上 VS Code、GitHub Desktop、QQ 全在这个类名下，能枚举出 9 个窗口。
    // 真正的判据是地址栏那个控件（下面的 AddressBarId），这才是浏览器独有的。
    private const string ChromiumWindowClass = "Chrome_WidgetWin_1";

    /// <summary>Chromium 系地址栏控件的 AutomationId。</summary>
    // 这是上面那条的判据：VS Code 的编辑框、GitHub Desktop 的输入框都不是这个 id。
    private const string AddressBarId = "view_1012";

    // Chromium 系应用自己的“搜索/地址合一框”也可能挂在这个 id 上（浏览器下载页、
    // 应用商店页会这样），值会是 chrome://… / edge://…。那种不是网址，调用方按 scheme 挡掉。

    /// <summary>
    /// 读当前网址。读不到返回 null（没有浏览器在跑、或浏览器不是 Chromium 系）。
    /// </summary>
    /// <remarks>
    /// 窗口按 **Z 序**枚举，EnumWindows 自顶向下 = 最近激活的在前。
    /// 所以返回的是「你最近在看的那一个浏览器」，Chrome 和 Edge 同时开着也不会挑错。
    /// 不用 GetForegroundWindow 是因为本程序自己可能就是前台（面板、手势、主窗口），
    /// 那一刻真正的目标浏览器并不在 Z 序第一位。
    /// </remarks>
    public static string? Read()
    {
        var handles = new List<IntPtr>();
        Win32.EnumTopLevelWindows(h =>
        {
            if (!Win32.IsWindowVisible(h)) return true;
            if (Win32.GetClassName(h) != ChromiumWindowClass) return true;
            handles.Add(h);
            return true;
        });

        foreach (var h in handles)
        {
            var url = FromWindow(h);
            if (url != null) return url;
        }
        return null;
    }

    private static string? FromWindow(IntPtr hwnd)
    {
        AutomationElement? win;
        try { win = AutomationElement.FromHandle(hwnd); }
        catch (Exception) { return null; }
        if (win == null) return null;

        AutomationElementCollection edits;
        try
        {
            edits = win.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        }
        catch (Exception) { return null; }   // 窗口正在关 / 跨进程 UIA 被拒

        foreach (AutomationElement e in edits)
        {
            string id;
            try { id = e.Current.AutomationId; } catch (Exception) { continue; }
            if (!string.Equals(id, AddressBarId, StringComparison.Ordinal)) continue;

            string value;
            try
            {
                var vp = (ValuePattern)e.GetCurrentPattern(ValuePattern.Pattern);
                value = vp.Current.Value;
            }
            catch (Exception) { continue; }

            value = (value ?? "").Trim();
            if (value.Length == 0) continue;
            if (!Normalize(value, out var url)) continue;
            return url;
        }
        return null;
    }

    /// <summary>
    /// 补上 scheme 并确认它是 http(s)。UIA 给的值是 <c>newzone.top/blog.html</c> 这样
    /// **不带 scheme** 的，不补的话下游 Uri.TryCreate 当相对路径处理，整条映射失效。
    /// </summary>
    internal static bool Normalize(string raw, out string url)
    {
        url = raw;
        if (raw.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("edge://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("chrome-error:", StringComparison.OrdinalIgnoreCase))
        {
            url = "";
            return false;
        }

        // **先按「有没有 ://」分家，再交给 Uri.TryCreate。** 顺序反过来会坏掉本地预览：
        // Uri.TryCreate("localhost:8080/posts/a.html", Absolute) 是**成功**的——它把
        // "localhost" 当 scheme、8080 当 scheme 专属部分。于是下面那个「只认 http/https」的
        // 分支会把一条好端端的本地地址判成不是网址。而靠那个分支救不回来：判据用错了方向，
        // 真正的区别是**有没有 ://**，不是 Uri 认不认得它。
        if (raw.IndexOf("://", StringComparison.Ordinal) <= 0)
        {
            // localhost 默认 http（本地预览不会上 https），其余一律 https。
            if (raw.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
                url = "http://" + raw;
            else
                url = "https://" + raw;
            return true;
        }

        // 带 scheme 的（用户手敲过完整地址、或某些浏览器会给全）原样放行，只认 http(s)。
        if (Uri.TryCreate(raw, UriKind.Absolute, out var abs))
        {
            var s = abs.Scheme.ToLowerInvariant();
            if (s != "http" && s != "https") { url = ""; return false; }
            url = abs.ToString();
            return true;
        }
        url = "";
        return false;
    }
}
