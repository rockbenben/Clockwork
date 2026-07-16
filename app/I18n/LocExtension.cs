using System.Windows.Markup;

namespace Clockwork.I18n;

// XAML 本地化标记扩展：{loc:Loc Key=Tab_Launch} 或 {loc:Loc Tab_Launch}。
// 在 XAML 加载时（窗口构造，已设好 CurrentUICulture）取值 → 语言切换经重启生效。
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
