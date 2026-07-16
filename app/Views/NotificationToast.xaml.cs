using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Clockwork.Views;

public enum ToastLevel { Info, Warn }

// 品牌化非模态通知卡片。屏幕右下角自底向上堆叠、自动消失、点击即关、不抢焦点(ShowActivated=false)。
// 替代系统托盘气泡，与提醒弹窗同一套擒纵视觉。全部操作须在 UI 线程。
public partial class NotificationToast : Window
{
    private static readonly List<NotificationToast> Active = new();   // 当前在屏的所有 toast，最新在末尾
    private const int MaxOnScreen = 4;             // 软上限：优先挤掉状态类以维持在此
    private const double EstToastHeight = 130;     // 单卡片高度粗估(含外边距)，据工作区高算能容纳几张

    private readonly DispatcherTimer? _timer;   // durationMs<=0 → 不自动关，常驻到点击(如提醒类)
    private readonly bool _persistent;          // 常驻(不自动消失)——超量挤出时尽量保留，不静默关掉未读提醒
    private bool _closing;

    public NotificationToast(string title, string message, ToastLevel level, int durationMs)
    {
        InitializeComponent();
        TitleText.Text = title ?? "";
        TitleText.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
        MsgText.Text = message ?? "";
        Rail.Background = (System.Windows.Media.Brush)FindResource(level == ToastLevel.Warn ? "BrushClay" : "BrushBrass");
        Opacity = 0;
        Loaded += OnLoaded;
        MouseLeftButtonUp += (_, _) => Dismiss();
        _persistent = durationMs <= 0;
        if (!_persistent)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(2500, durationMs)) };
            _timer.Tick += (_, _) => Dismiss();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 超量挤出：优先挤会自动消失的(状态类)；常驻的(提醒类)尽量保留——未读提醒不该被状态 toast 静默顶掉。
        // 硬上限按工作区高度算(不让最老的堆到屏幕外)；全常驻且未到硬上限时才允许暂时超出软上限。必须 Close()(同步移出 Active)。
        int cap = Math.Clamp((int)(SystemParameters.WorkArea.Height / EstToastHeight), 1, 8);
        int soft = Math.Min(MaxOnScreen, cap);
        while (Active.Count >= soft)
        {
            var evictable = Active.FirstOrDefault(t => !t._persistent && !t._closing);
            if (evictable != null) { evictable.Close(); continue; }   // 有状态类可挤：挤掉，回到软上限
            if (Active.Count >= cap) { Active[0].Close(); continue; } // 全常驻且到屏幕硬上限：挤最老的
            break;                                                    // 全常驻但未到硬上限：允许暂时超软上限
        }

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width;
        Top = wa.Bottom;                 // 起点贴屏幕底，Reflow 把它动画滑到目标槽位
        Active.Add(this);
        Reflow();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        _timer?.Start();
    }

    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _timer?.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { try { Close(); } catch { } };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();   // 直接 Close(被挤掉)时也停掉计时器，避免关闭后的 toast 上再触发一次 Dismiss
        Active.Remove(this);
        Reflow();
        base.OnClosed(e);
    }

    // 自屏幕右下角向上堆叠所有活动 toast（最新贴底）。卡片间距由 Border 的 12px 透明外边距天然给出。
    private static void Reflow()
    {
        var wa = SystemParameters.WorkArea;
        double y = wa.Bottom;
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var t = Active[i];
            double h = t.ActualHeight > 0 ? t.ActualHeight : 96;
            y -= h;
            t.Left = wa.Right - t.Width;
            // 正在淡出的也一并滑到其槽位（而非停在旧位）：否则栈在它淡出期间变动时，它与重排后的其余 toast 会出现空档/重叠
            t.BeginAnimation(TopProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    public static void Show(string title, string message, ToastLevel level, int durationMs)
        => new NotificationToast(title, message, level, durationMs).Show();
}
