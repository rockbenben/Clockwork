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

    // 抢焦点瞬间的误触保护：本窗不请自来、Topmost + 模态，弹出即夺走键盘。用户正在打字时，
    // 落在按钮上的一个在途空格/回车就会执行 onYes（可能是跑动作组、开程序）。故弹出后极短一段时间内
    // 只挡「是」（唯一会执行动作的键）——Esc / 否 / 确定 / 稍后都不受影响，想立刻打发掉它照样能。
    // _shownAt 在构造时即上膛：WPF 在窗口激活时就把默认焦点给了首个按钮（是），若等 ContentRendered
    // 才上膛，激活到渲染之间送达的按键正好落在无保护的「是」上——恰是本保护要防的那次误触。
    private const int GuardMs = 600;
    private readonly long _shownAt = Environment.TickCount64;
    private bool Guarded => Environment.TickCount64 - _shownAt < GuardMs;

    // timeoutSnoozeMinutes：超时（无人应答）的去向。null → Action=""（超时未确认，交重复催促续催）；
    // 有值 → 视作用户点了「稍后」这么多分钟——没配续催的提醒绝不静默丢（见 ReminderEngine.UnattendedSnoozeMinutes）。
    public ReminderPopupWindow(string message, bool confirm, int autoDismissSeconds, int? timeoutSnoozeMinutes = null)
    {
        InitializeComponent();
        _confirm = confirm;
        Title = Strings.Get("Tray_ReminderTitle");   // 无可见标题栏，仅用于 alt-tab/辅助功能
        Eyebrow.Text = Strings.Get("Tray_ReminderTitle");
        TimeText.Text = DateTime.Now.ToString("HH:mm");   // 仪表读数：这条是几点弹的
        // 左缘刻度轨＝余量表：点亮格数随自动关闭时间逐格回收。弹窗一律有超时（见 FireReminder），轨始终走时；
        // <=0 仅是防御分支（满格不动）。
        Rail.Run(autoDismissSeconds > 0 ? autoDismissSeconds * 1000 : 0, warn: false);
        // 无关闭按钮 → Esc 收尾。用"否/确定"(明确终止)而非 ""(超时未确认)——否则带重复的提醒按 Esc 会被当超时继续每 N 分钟再弹。
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Finish(_confirm ? "no" : "ok"); };
        YesBtn.Content = Strings.Get("Reminder_Popup_Yes");
        NoBtn.Content = Strings.Get("Reminder_Popup_No");
        OkBtn.Content = Strings.Get("Reminder_Popup_Ok");
        SnoozeBtn.Content = Strings.Get("Reminder_Popup_Snooze");
        // ▾ 只有一个字形，读屏软件念不出用途——名字/提示都借「稍后」这一串，指明它是那颗按钮的更多选项。
        SnoozeMoreBtn.ToolTip = Strings.Get("Reminder_Popup_SnoozeMore");
        System.Windows.Automation.AutomationProperties.SetName(SnoozeMoreBtn, Strings.Get("Reminder_Popup_SnoozeMore"));
        MsgText.Text = message;
        // 有动作 → 是/否；否则 → 确定。
        YesBtn.Visibility = NoBtn.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;

        // 初始焦点=「是」（无动作时=「确定」）：回车=执行是本窗多年的肌肉记忆，不改——
        // 改成落在「稍后」会让习惯性回车静默变成推迟，动作没跑用户却以为跑了。
        // 误触风险交给上面的 600ms 保护（只挡「是」，且从构造起算无空窗）。
        // 用 ContentRendered 显式指定：默认焦点虽通常也落在首个可见按钮，但显式才是确定性的。
        ContentRendered += (_, _) => (confirm ? YesBtn : OkBtn).Focus();

        if (autoDismissSeconds > 0)
        {
            _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoDismissSeconds) };
            // 超时：有 timeoutSnoozeMinutes 就自动「稍后」（无人应答≠已处理）；否则 ""=超时未确认（重复催促续催）。
            _autoTimer.Tick += (s, e) => { if (timeoutSnoozeMinutes is int m) Finish("snooze", m); else Finish(""); };
            _autoTimer.Start();
        }
    }

    private void Finish(string action, int? snooze = null)
    {
        if (_finished) return;
        _finished = true;
        _autoTimer?.Stop();
        Rail.Stop();
        Action = action;
        SnoozeMinutes = snooze;
        try { DialogResult = true; } catch { }
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { if (Guarded) return; Finish("yes"); }
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
    public static (string Action, int? Snooze) Show(Window? owner, string message, bool confirm, int autoDismissSeconds, int? timeoutSnoozeMinutes = null)
    {
        var dlg = new ReminderPopupWindow(message, confirm, autoDismissSeconds, timeoutSnoozeMinutes);
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        dlg.ShowDialog();
        return (dlg.Action, dlg.SnoozeMinutes);
    }
}
