using System.Text.RegularExpressions;

namespace Clockwork.Engine;

// 「拒绝访问 / 需管理员」的错误信息识别。Autostart 与 SystemStartupReader 共用，避免两处正则各写一份、
// 改了一处漏了另一处导致把「需管理员」错判成普通错误。
internal static class AdminError
{
    public static bool IsAccessDenied(string? message)
        => Regex.IsMatch(message ?? "", "denied|Access is denied|0x80070005|拒绝|权限");
}
