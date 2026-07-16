using System.Globalization;
using Clockwork.I18n;

namespace Clockwork.Core;

// 系统启动项的类型/范围显示文案。取自 resx，随 UI 文化中/英切换。
public static class StartupLabels
{
    public static string TypeLabel(string type) => type switch
    {
        "Registry" => Strings.Get("Type_Registry"),
        "StartupFolder" => Strings.Get("Type_StartupFolder"),
        "ScheduledTask" => Strings.Get("Type_ScheduledTask"),
        _ => type,
    };

    public static string ScopeLabel(string scope, bool needsAdmin)
    {
        var baseLabel = scope == "Machine" ? Strings.Get("Scope_Machine") : Strings.Get("Scope_User");
        return needsAdmin ? string.Format(CultureInfo.CurrentUICulture, Strings.Get("Scope_NeedsAdmin"), baseLabel) : baseLabel;
    }
}
