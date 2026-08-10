using System.Windows;
using Clockwork.I18n;

namespace Clockwork.Views;

// 品牌化模态对话框，替代原生 MessageBox。confirm=true → 是/否(返回 true=是)；否则 → 确定(返回 true=已确认)。
public partial class BrandDialog : Window
{
    public bool Result { get; private set; }

    // 误触保护，同提醒弹窗：本框可能由后台动作组/破坏性命令确认突然弹出并夺走焦点，
    // 且默认焦点落在「是」。弹出后极短一段时间内只挡「是」，Esc/否/确定不受影响。
    // _shownAt 构造即上膛：等 ContentRendered 才上膛的话，激活到渲染之间的在途按键正落在无保护的「是」上。
    private const int GuardMs = 600;
    private readonly long _shownAt = Environment.TickCount64;
    private bool Guarded => Environment.TickCount64 - _shownAt < GuardMs;

    public BrandDialog(string? title, string message, bool confirm, ToastLevel level)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);   // 崩溃提示的载体：消息长度不可控，小屏上也不能把是/否挤下屏
        var t = string.IsNullOrEmpty(title) ? "Clockwork" : title!;
        Title = t;   // 无可见标题栏，仅用于 alt-tab/辅助功能
        Eyebrow.Text = t;
        MsgText.Text = message ?? "";
        var accent = (System.Windows.Media.Brush)FindResource(level == ToastLevel.Warn ? "BrushClay" : "BrushBrass");
        Rail.Background = accent;
        // 眉标平时退到钢灰（强调色交给轨），只有警示才让它跟着变 clay——升级信号要看得出来，不能人人都亮。
        Eyebrow.Foreground = level == ToastLevel.Warn ? accent : (System.Windows.Media.Brush)FindResource("BrushMuted");
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Result = false; DialogResult = true; } };   // 无关闭按钮 → Esc 取消
        YesBtn.Content = Strings.Get("Reminder_Popup_Yes");
        NoBtn.Content = Strings.Get("Reminder_Popup_No");
        OkBtn.Content = Strings.Get("Reminder_Popup_Ok");
        YesBtn.Visibility = NoBtn.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
        // 破坏性确认（删除 / 关机 / 覆盖导入）反转轻重：把「是」做成面板上最亮的黄铜主按钮，等于让危险选项
        // 当视觉焦点——眼睛先被它拽走，键盘焦点却在「否」上，两边打架。这里让安全的一侧当主按钮，
        // 「是」退成 clay 文字＋clay 描边：看得见、够得着，但不再是这块面板上最响的东西。
        if (confirm && level == ToastLevel.Warn)
        {
            YesBtn.Style = (Style)FindResource("DangerButton");
            YesBtn.BorderBrush = accent;   // DangerButton 本身无描边(列表内联用)，对话框里给回一圈，仍读作按钮
            NoBtn.Style = (Style)FindResource("PrimaryButton");
        }
        // 初始焦点=「是」/「确定」：回车=确认是既有肌肉记忆，不改（误触由 600ms 保护挡，且从构造起算无空窗）。
        ContentRendered += (_, _) => (confirm ? YesBtn : OkBtn).Focus();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { if (Guarded) return; Result = true; DialogResult = true; }
    private void No_Click(object sender, RoutedEventArgs e) { Result = false; DialogResult = true; }
    private void Ok_Click(object sender, RoutedEventArgs e) { Result = true; DialogResult = true; }

    // 仅「确定」：信息/警示。返回 true=已确认。
    public static void Info(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Info);
    public static void Warn(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Warn);
    // 「是/否」确认。返回 true=是。level 决定强调轨颜色（破坏性操作传 Warn）。
    public static bool Confirm(Window? owner, string? title, string message, ToastLevel level = ToastLevel.Info)
        => Show(owner, title, message, true, level);

    // 删除确认的唯一口径（标题/文案键/Warn 红轨）：主窗口三列表、系统启动项、组编辑器共用，
    // 契约变更（换键、加「不再询问」等）只改这一处。
    public static bool ConfirmDelete(Window? owner, string label)
        => Confirm(owner, Strings.Get("Confirm_Title"), Strings.Lf("Confirm_DeleteItem", label), ToastLevel.Warn);

    public static bool Show(Window? owner, string? title, string message, bool confirm, ToastLevel level)
    {
        var dlg = new BrandDialog(title, message, confirm, level);
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Result;
    }
}
