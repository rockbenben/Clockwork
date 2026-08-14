using System.Text.RegularExpressions;

namespace Clockwork.Engine;

// 「拒绝访问 / 需管理员」的错误信息识别。**只是最后一层兜底**：所有计划任务操作都已走 COM，
// 主判据是与系统语言无关的 E_ACCESSDENIED(0x80070005) HResult（见 SystemStartupReader.GuardAdminErrors）。
// 这条正则只认中英文，别再让它成为唯一判据——那正是非中英文系统上拿不到一键提权的来路。
internal static class AdminError
{
    public static bool IsAccessDenied(string? message)
        => Regex.IsMatch(message ?? "", "denied|Access is denied|0x80070005|拒绝|权限");
}
