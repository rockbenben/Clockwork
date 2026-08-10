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

    // 系统「显示动画」关掉时（辅助功能 / 远程桌面 / 省电）不做淡入淡出与滑移，直接落位。
    // 刻度轨的逐格回收不在此列：那是读数，不是修饰。
    private static bool Animate => SystemParameters.ClientAreaAnimation;

    private DispatcherTimer? _timer;   // durationMs<=0 → 不自动关，常驻到点击(如提醒类)
    private bool _persistent;          // 常驻(不自动消失)——超量挤出时尽量保留，不静默关掉未读提醒
    private bool _closing;
    private readonly ToastLevel _level;
    private readonly string? _key;     // 合并键(提醒按 id)：同键的重复触发只更新已在屏的那张，不再新开
    private int _count = 1;            // 已合并的次数，>1 时在眉标右端显示 ×N

    // at：眉标时刻。null=现在（真触发）；重放传原发生时刻——卡片说的是「这条何时发生」，不是「何时被重放」。
    public NotificationToast(string title, string message, ToastLevel level, int durationMs, string? key = null, DateTime? at = null)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);   // 长文本封进工作区（超出部分裁掉——瞬态卡片，全文在弹窗/日志里）
        _level = level;
        TitleText.Text = title ?? "";
        TitleText.Visibility = string.IsNullOrEmpty(title) ? Visibility.Collapsed : Visibility.Visible;
        MsgText.Text = message ?? "";
        Stamp(at ?? DateTime.Now);
        Opacity = Animate ? 0 : 1;
        _key = string.IsNullOrEmpty(key) ? null : key;
        Loaded += OnLoaded;
        MouseLeftButtonUp += (_, _) => Dismiss();   // 卡片只有一种交互：点击即关。提醒的投递保证在引擎侧（自动稍后），不在这里
        // 高度变化后按真实高度重排（初次排布、合并后正文变长、字体缩放）。合并处不能自己调 Reflow——
        // 那一刻布局还没跑，ActualHeight 仍是旧值，会把下面的卡片永久压住。
        SizeChanged += (_, _) => Reflow();
        SetDuration(durationMs);
    }

    // 眉标左端的时刻读数：这张卡片说的事是几点发生的。离屏回来后，它告诉你这条搁了多久。
    private void Stamp(DateTime at) => TimeText.Text = at.ToString("HH:mm");

    // 设定/重设自动关闭，并让左缘刻度轨按同一时长起算。<=0 → 常驻（轨道满格不动）。
    // 下限 2.5s：再短的卡片来不及读。
    private void SetDuration(int durationMs)
    {
        _timer?.Stop();
        _timer = null;
        _persistent = durationMs <= 0;
        int dur = _persistent ? 0 : (int)Math.Max(2500, durationMs);
        Rail.Run(dur, _level == ToastLevel.Warn);
        if (_persistent) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(dur) };
        _timer.Tick += (_, _) => Dismiss();
    }

    // 同键的重复触发：就地更新已在屏的这张（正文换成最新、时刻刷新、计数 +1、自动关闭与刻度轨重新起算），
    // 而不是再摞一张。否则一条每天弹一次的常驻提醒放一周就把右下角糊满，还会把别的卡片挤掉。
    // 高度若因新正文而变，ctor 挂的 SizeChanged 会在布局后自动 Reflow。
    // count=false（重放）：重放不是一次新触发——不涨 ×N、不改时刻戳，只按原时长重新给一轮可读时间。
    private void Merge(string message, int durationMs, bool count)
    {
        if (count)
        {
            _count++;
            CountText.Text = "×" + _count;
            CountText.Visibility = Visibility.Visible;
            Stamp(DateTime.Now);
        }
        MsgText.Text = message ?? "";
        // 常驻代表「未读」：合并只能刷新内容、重置计时，不能把一张没人处理过的常驻卡降级成会自己消失的
        // ——同键的旁路来源（重放等）与真触发混用时，这一层兜底防止未读提醒被悄悄销掉。
        SetDuration(_persistent ? 0 : durationMs);
        _timer?.Start();
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
        if (Animate) BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        _timer?.Start();
    }

    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        _timer?.Stop();
        Rail.Stop();
        if (!Animate) { try { Close(); } catch { } return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => { try { Close(); } catch { } };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();   // 直接 Close(被挤掉)时也停掉计时器，避免关闭后的 toast 上再触发一次 Dismiss
        Rail.Stop();
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
            double h = t.ActualHeight > 0 ? t.ActualHeight : 116;
            y -= h;
            t.Left = wa.Right - t.Width;
            if (!Animate) { t.BeginAnimation(TopProperty, null); t.Top = y; continue; }
            // 正在淡出的也一并滑到其槽位（而非停在旧位）：否则栈在它淡出期间变动时，它与重排后的其余 toast 会出现空档/重叠
            t.BeginAnimation(TopProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }

    // key 非空且同键卡片仍在屏(未在淡出) → 合并到那张；否则新开一张。
    // countMerge=false=重放路径：合并时不计数、不改时刻（见 Merge）。
    public static void Show(string title, string message, ToastLevel level, int durationMs,
                            string? key = null, DateTime? at = null, bool countMerge = true)
    {
        if (!string.IsNullOrEmpty(key))
        {
            var same = Active.FirstOrDefault(t => t._key == key && !t._closing);
            if (same != null) { same.Merge(message, durationMs, countMerge); return; }
        }
        new NotificationToast(title, message, level, durationMs, key, at).Show();
    }
}
