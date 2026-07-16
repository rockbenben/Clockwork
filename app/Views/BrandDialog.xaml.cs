using System.Windows;
using Clockwork.I18n;

namespace Clockwork.Views;

// 品牌化模态对话框，替代原生 MessageBox。confirm=true → 是/否(返回 true=是)；否则 → 确定(返回 true=已确认)。
public partial class BrandDialog : Window
{
    public bool Result { get; private set; }

    public BrandDialog(string? title, string message, bool confirm, ToastLevel level)
    {
        InitializeComponent();
        var t = string.IsNullOrEmpty(title) ? "Clockwork" : title!;
        Title = t;   // 无可见标题栏，仅用于 alt-tab/辅助功能
        Eyebrow.Text = t;
        MsgText.Text = message ?? "";
        var accent = (System.Windows.Media.Brush)FindResource(level == ToastLevel.Warn ? "BrushClay" : "BrushBrass");
        Rail.Background = accent;
        Eyebrow.Foreground = accent;
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Result = false; DialogResult = true; } };   // 无关闭按钮 → Esc 取消
        YesBtn.Content = Strings.Get("Reminder_Popup_Yes");
        NoBtn.Content = Strings.Get("Reminder_Popup_No");
        OkBtn.Content = Strings.Get("Reminder_Popup_Ok");
        YesBtn.Visibility = NoBtn.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { Result = true; DialogResult = true; }
    private void No_Click(object sender, RoutedEventArgs e) { Result = false; DialogResult = true; }
    private void Ok_Click(object sender, RoutedEventArgs e) { Result = true; DialogResult = true; }

    // 仅「确定」：信息/警示。返回 true=已确认。
    public static void Info(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Info);
    public static void Warn(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Warn);
    // 「是/否」确认。返回 true=是。level 决定强调轨颜色（破坏性操作传 Warn）。
    public static bool Confirm(Window? owner, string? title, string message, ToastLevel level = ToastLevel.Info)
        => Show(owner, title, message, true, level);

    public static bool Show(Window? owner, string? title, string message, bool confirm, ToastLevel level)
    {
        var dlg = new BrandDialog(title, message, confirm, level);
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }
}
