using System.Windows;
using System.Windows.Threading;
using Clockwork.I18n;

namespace Clockwork.Views;

// 提醒弹窗：文本 + 是/否（或确定）+ 稍后 + 可选自动关闭。
// 返回 (Action, SnoozeMinutes)：Action ∈ yes/no/ok/""(超时未确认)；SnoozeMinutes 非空=点了稍后。
public partial class ReminderPopupWindow : Window
{
    public string Action { get; private set; } = "";
    public int? SnoozeMinutes { get; private set; }

    private DispatcherTimer? _autoTimer;
    private readonly bool _confirm;
    private bool _finished;   // 幂等收尾：超时/按钮/菜单/Esc 谁先到都只收一次，防在已关窗口上再设 DialogResult 抛异常

    public ReminderPopupWindow(string message, bool confirm, int autoDismissSeconds)
    {
        InitializeComponent();
        _confirm = confirm;
        Title = Strings.Get("Tray_ReminderTitle");   // 无可见标题栏，仅用于 alt-tab/辅助功能
        Eyebrow.Text = Strings.Get("Tray_ReminderTitle");
        // 无关闭按钮 → Esc 收尾。用"否/确定"(明确终止)而非 ""(超时未确认)——否则带重复的提醒按 Esc 会被当超时继续每 N 分钟再弹。
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Finish(_confirm ? "no" : "ok"); };
        YesBtn.Content = Strings.Get("Reminder_Popup_Yes");
        NoBtn.Content = Strings.Get("Reminder_Popup_No");
        OkBtn.Content = Strings.Get("Reminder_Popup_Ok");
        SnoozeBtn.Content = Strings.Get("Reminder_Popup_Snooze");
        MsgText.Text = message;
        // 有动作 → 是/否；否则 → 确定。
        YesBtn.Visibility = NoBtn.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;

        if (autoDismissSeconds > 0)
        {
            _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoDismissSeconds) };
            _autoTimer.Tick += (s, e) => Finish("");   // 超时=未确认
            _autoTimer.Start();
        }
    }

    private void Finish(string action, int? snooze = null)
    {
        if (_finished) return;
        _finished = true;
        _autoTimer?.Stop();
        Action = action;
        SnoozeMinutes = snooze;
        try { DialogResult = true; } catch { }
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => Finish("yes");
    private void No_Click(object sender, RoutedEventArgs e) => Finish("no");
    private void Ok_Click(object sender, RoutedEventArgs e) => Finish("ok");
    private void Snooze_Click(object sender, RoutedEventArgs e) => Finish("snooze", 10);   // 主按钮：默认 10 分钟

    // ▾：其它稍后时长菜单。
    private void SnoozeMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (int m in new[] { 5, 10, 15, 30, 60 })
        {
            int mins = m;
            var mi = new System.Windows.Controls.MenuItem { Header = Strings.Lf("Unit_Minutes", mins) };
            mi.Click += (_, _) => Finish("snooze", mins);
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = SnoozeMoreBtn;
        menu.IsOpen = true;
    }

    // 在 UI 线程弹出并等待。返回 (Action, SnoozeMinutes)。
    public static (string Action, int? Snooze) Show(Window? owner, string message, bool confirm, int autoDismissSeconds)
    {
        var dlg = new ReminderPopupWindow(message, confirm, autoDismissSeconds);
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        dlg.ShowDialog();
        return (dlg.Action, dlg.SnoozeMinutes);
    }
}
