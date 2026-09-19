using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
// UseWindowsForms 的全局 using 让这几个类型撞名，显式钉到 WPF（同 Pickers.cs / StepMenu.cs 的惯例）。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Clockwork.Views;

/// <summary>画手势时**看得见**的那条笔迹：一个铺满当前屏、点不着、不抢焦点的覆盖窗。</summary>
//
// 没有它的时候，画手势是这样的：按住右键划一道，屏幕上什么都没发生，松手才知道自己画的是什么。
// 画错了不知道错在哪，画对了也没有「画对了」的感觉——手势这类操作的全部信心都来自即时反馈。
// 管理器里的笔迹缩略图（GestureGlyph）回答的是「我配了什么」，这条线回答的是「我现在画到哪了」，
// 两件事，缺一件都不够。
//
// 四条硬约束，任何一条破了都会造成比「没有笔迹」严重得多的问题：
//
//   1. **绝不能抢前台。** 「最小化当前窗口」这类动作读的就是 GetForegroundWindow——
//      覆盖窗一旦拿到前台，用户的动作会全部落到这个透明窗上（并被自家进程的守卫拒掉）。
//      ShowActivated=false 只管 Show 那一下，WS_EX_NOACTIVATE 才管住后续每一次点击。
//   2. **绝不能吃鼠标事件。** WS_EX_TRANSPARENT 让命中测试穿过去。少了它，
//      手势画到一半，指针下面的程序就再也收不到鼠标了。
//   3. **绝不能在钩子回调里画。** 低级鼠标钩子有 LowLevelHooksTimeout（默认 300ms）的预算，
//      超了整个钩子会被系统摘掉。所以坐标一律由 MouseHook 经 _post 派发过来（同 Fire 那条路）。
//   4. **收笔之后绝不能立刻 Hide。** Hide 不擦分层窗那张位图，而重显时 DWM 贴的正是它——
//      上一笔那条线会原样回到屏幕上（「画手势为什么还出现上一次的轨迹」就是这个）。收笔一律走
//      HideSoon：先把清空后的画面合成出去，再藏。
//
// 只铺**手势起点所在的那一块屏**，不铺整个虚拟桌面：跨屏画手势基本不存在，
// 而单屏意味着只有一个 DPI 系数要换算（混合 DPI 下 WPF 给整窗一个系数，跨屏的点会偏），
// 顺带把分层窗口每帧要刷的面积压到最小。
public sealed class GestureTrailWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004;
    private const int MONITOR_DEFAULTTONEAREST = 2, MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    // 一条线画两遍：底下一层半透明的黑当描边，上面一层强调色当线芯。
    // 覆盖窗压在**任意**背景上——白文档、深色 IDE、花花的壁纸——单一颜色总有一种底色让它读不出来。
    // 描边不解决「配色好看」，解决的是「任何底色上都还看得见」。
    private readonly Polyline _halo = Stroke(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), 8);
    private readonly Polyline _core;
    private readonly Brush _idleBrush, _armedBrush;
    private bool _armed;
    private double _coreIdle = 4, _coreArmed = 5.5;
    private double _haloIdle = 8, _haloArmed = 10;
    private bool _disabled;

    // 画完没匹配上时，就地显示「你画的是什么」的那颗小药丸。
    //
    // 从前这句话走的是通知卡（ShowToast）——一张带标题、带图标、从屏幕角落滑进来的卡片，
    // 只为说一句「↑↓ 没绑动作」。份量和事情完全不匹配：手势本来是个一划而过的操作，
    // 反馈却比操作本身还重，而且出现在离你手 1500 像素远的另一个角落。
    // 挪到笔迹这扇窗上：它已经点得穿、不抢焦点、按起点那块屏摆好了，是现成的、最轻的那块地方。
    // 就地显示还顺带答了「我画到哪了」——你在哪收的笔，答案就出在哪。
    private readonly TextBlock _noteText = new()
    {
        FontSize = 20,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)),
    };

    private readonly Border _note;
    private readonly System.Windows.Threading.DispatcherTimer _noteTimer = new();
    // 收笔之后**推迟**藏窗用的表。为什么要推迟，见 HideSoon——那是「上一笔的轨迹又回来了」
    // 唯一的解，也是这扇窗上最容易被人顺手改回 `Hide()` 的一处。
    private readonly System.Windows.Threading.DispatcherTimer _hideTimer = new();

    // 收笔之后窗多留一会儿的时长：够让那张空位图真的合成出去。
    //
    // **不能是 0（0 就是现在这个 bug）**：Hide 不擦分层窗的位图，而空画面得先由渲染线程推一次；
    // UI 线程这边没有任何「已经推出去了」的信号可等——CompositionTarget.Rendering 在渲染**之前**
    // 触发，在那一刻 Hide 会把这帧整个取消掉。取 120ms：60Hz 上约 7 帧、120Hz 上约 14 帧，
    // 正常负载下足够渲染线程把这一帧推出去；再长就纯粹是让那扇空窗多挂一会儿，而它只吃滚轮。
    private const int HideDelayMs = 120;

    private bool _placed;
    private double _scale = 1;
    private int _originX, _originY, _spanX, _spanY;   // 覆盖窗在屏幕上的物理像素矩形
    private Point _last;                              // 收笔那一点（DIP，窗内坐标）：药丸摆这儿
    private bool _hasLast;                            // 这一笔到底有没有点——(0,0) 是合法坐标，不能拿它当哨兵
    private System.Windows.Threading.DispatcherOperation? _reveal;   // ShowGated 排的「淡回不透明」

    private static Polyline Stroke(Brush b, double t) => new()
    {
        Stroke = b,
        StrokeThickness = t,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    public GestureTrailWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;          // 必须在 Show 之前设，且要求 WindowStyle=None
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // 笔迹与管理器里的缩略图同色：屏幕上画出来的那条线，和列表里存下来的那条，是同一样东西。
        _idleBrush = (Brush)(System.Windows.Application.Current?.TryFindResource("BrushAccentText")
                             ?? new SolidColorBrush(Color.FromRgb(0x75, 0x9C, 0xD7)));
        // 命中色不从主题里取：主题色是给界面用的，而这条线压在**任意**程序的窗口上，
        // 要的是「和未命中那一档一眼分得开」。同一个蓝往亮里推到近白，比换一个色相稳——
        // 换色相（比如绿）在花壁纸和深色 IDE 上各是一种观感，而提亮在任何底色上都是同一个信号。
        _armedBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xEF, 0xFF));
        _core = Stroke(_idleBrush, 4);
        // 药丸自己压一层半透明的黑底——同笔迹描边一个理由：覆盖窗压在任意背景上，
        // 白文档和深色 IDE 上都得读得出来。不用主题色，这不是要好看，是要在任何底色上可读。
        _note = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1C, 0x1E, 0x22)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Child = _noteText,
            Visibility = Visibility.Collapsed,
        };
        // 药丸到时收掉药丸，窗也一起藏——收笔后窗本就该藏着（见 Finish）。藏之前先让空画面
        // 合成出去，理由与收笔那条一模一样（见 HideSoon）：藏的那一刻位图里留的是什么，
        // 下一笔开头贴到屏上的就是什么——药丸同样是「上一笔留下的东西」。
        _noteTimer.Tick += (_, _) => { _noteTimer.Stop(); HideNote(); if (_core.Points.Count == 0) HideSoon(); };
        _hideTimer.Interval = TimeSpan.FromMilliseconds(HideDelayMs);
        // 到点再核一次：这 120ms 里可能已经开了新的一笔（那时 Point 会把它停掉），
        // 也可能药丸又亮了起来（那是 _noteTimer 的活，它到点自己会走同一条路）。
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_core.Points.Count == 0 && !_noteTimer.IsEnabled) Hide();
        };

        var canvas = new Canvas();
        canvas.Children.Add(_halo);
        canvas.Children.Add(_core);
        canvas.Children.Add(_note);
        Content = canvas;
    }

    /// <summary>就地说一句「你画的是这个」。<paramref name="text"/> 为空则什么都不做。</summary>
    //
    // 紧跟在 Finish 之后调用（MouseHook 先派 trailEnd 再派 unmatched，同一条 UI 队列，顺序有保证）。
    // 那时窗通常还亮着——Finish 走的是「推迟藏」（见 HideSoon），120ms 还没到——所以这儿多半只是
    // 把药丸摆上去；真已经被藏了（比如这一笔画得久、Finish 之后隔了一会儿才报），再由 ShowGated 显回来。
    public void Note(string? text, int ms = 900)
    {
        if (string.IsNullOrEmpty(text) || !_hasLast) return;
        _noteText.Text = text;
        _note.Visibility = Visibility.Visible;
        // 摆在收笔点的右下方一点，别正好压在指针下面挡住你要看的东西。
        _note.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_note, _last.X + 14);
        Canvas.SetTop(_note, _last.Y + 14);
        if (!IsVisible) ShowGated();
        // 收笔时排的那次「推迟藏窗」得撤掉：药丸要亮 0.9 秒，让它掐掉就等于没报。
        // 0.9 秒后由 _noteTimer 走同一条推迟路藏窗，那时位图里已经是空画面了。
        _hideTimer.Stop();
        _noteTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    /// <summary>应用笔迹宽度与样式："off"（不显轨迹）| "thin"（细）| "normal"（默认标准）| "thick"（粗）。</summary>
    public void ApplyStyle(string? widthMode)
    {
        switch ((widthMode ?? "").Trim().ToLowerInvariant())
        {
            case "off":
                _disabled = true;
                break;
            case "thin":
                _disabled = false;
                _coreIdle = 2.5; _coreArmed = 3.5;
                _haloIdle = 5; _haloArmed = 7;
                break;
            case "thick":
                _disabled = false;
                _coreIdle = 6; _coreArmed = 8.5;
                _haloIdle = 12; _haloArmed = 15;
                break;
            default: // "normal"
                _disabled = false;
                _coreIdle = 4; _coreArmed = 5.5;
                _haloIdle = 8; _haloArmed = 10;
                break;
        }
        _core.StrokeThickness = _armed ? _coreArmed : _coreIdle;
        _halo.StrokeThickness = _armed ? _haloArmed : _haloIdle;
    }

    // 点亮 / 熄灭。会来回切：画出 ↑ 命中「复制」，继续往下画成 ↑↓ 就该灭掉，再成为「搜索」时重新亮起——
    // 命中是**此刻这一串**的属性，不是一旦点亮就锁住的状态。
    private void SetArmed(bool on)
    {
        if (_armed == on) return;
        _armed = on;
        _core.Stroke = on ? _armedBrush : _idleBrush;
        _core.StrokeThickness = on ? _coreArmed : _coreIdle;
        _halo.StrokeThickness = on ? _haloArmed : _haloIdle;   // 描边跟着加粗，任何底色上都还托得住线芯
    }

    private void HideNote()
    {
        _note.Visibility = Visibility.Collapsed;
        _noteText.Text = "";
    }

    /// <summary>显示窗口，但先把整窗 <see cref="UIElement.Opacity"/> 压到 0、下一轮调度再淡回 1。</summary>
    //
    // 藏→重显那一帧，DWM 会先把这扇窗**上次合成的位图**（上一笔那条线）重新顶到屏上，WPF 随后才
    // 按清空后的内容重绘——外面看就是「新手势开头先残留上一笔的形状」。压成全透明，那一帧旧位图
    // 哪怕被顶出来也是不可见的；等这一轮 Show/渲染过去（Background 档，排在渲染之后）再淡回。
    // 淡回（改 Opacity）本身会让 WPF 按**当前**可视树（新笔迹 / 药丸，旧线早已 Clear）重新合成一帧，
    // 所以淡回后屏上只会是新东西。最坏情况也只是新线晚一帧（约十几毫秒）出现，绝不会是上一笔。
    //
    // **但这一手只是「赌赢的时候有用」，别把它当成保证。** Show() 贴的是**已经推上去的那张位图**，
    // 而改 Opacity 只影响**之后**推的帧——两者谁先到，取决于渲染线程推送与 DWM 下一次合成谁跑在前面。
    // 赌赢了，旧位图被一张全透明的帧盖掉，什么都看不见；赌输了，上一笔照样闪一下（用户报的
    // 「画手势还出现上一次的手势轨迹」就是这一档）。**真正的保证在 HideSoon**：藏之前先让空画面
    // 合成出去，位图里根本没有上一笔，贴出来也无所谓。这里留着是因为它代价为零、又能盖住少数情况。
    //
    // 不用「窗常驻不藏」来躲这一帧：那扇窗铺满整屏、压在最上层，即便点得穿、不抢焦，鼠标滚轮这类
    // 不走命中测试的输入仍会落到它头上被 WPF 吞掉（实测常驻后底下程序滑轮失效）。所以才要笔一收就藏、
    // 重显时再用这个闸门挡住旧帧。
    private void ShowGated()
    {
        _reveal?.Abort();
        Opacity = 0;
        Show();
        // 显示之后再摆一次：位置是直接用 SetWindowPos 写进 HWND 的，WPF 的 Left/Top 仍是「未设」，
        // 它在显示流程里若按自己那份记账同步一次窗口，笔迹就会跑到屏幕另一个角落。再写一遍是幂等的。
        Reposition();
        _reveal = Dispatcher.BeginInvoke(new Action(() =>
        {
            _reveal = null;
            // 排队这档之前窗已被 Finish / 药丸定时器藏回去了：别碰它，下次 ShowGated 会重新压 0 再显。
            if (IsVisible) Opacity = 1;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 必须停表：CloseTrail 关窗时药丸计时器可能还在 0.9 秒的弦上（Finish 不停它）。
        // 不停的话 Tick 会落在已关闭的窗上——Finish 已清空 Points，Tick 里那句 Hide() 是必走分支，
        // 对已关窗口调 Hide() 抛 InvalidOperationException。关手势 / 装钩失败那两条 CloseTrail
        // 路径上应用还活着，这一下会冒到 UI 线程上。同 NotificationToast.OnClosed 的口径。
        // 收笔那张「推迟藏窗」的表同理，而且更容易撞上：CloseTrail 里 Finish() 刚给它上了弦
        //（Finish 与 Close 就是前后两行），紧接着窗就关了——不停的话 120ms 后那一跳正好落在死窗上。
        _noteTimer.Stop();
        _hideTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        // TRANSPARENT=点得穿，NOACTIVATE=永远不抢焦点，TOOLWINDOW=不进 Alt+Tab。
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>笔迹又到了一个点（钩子给的物理像素）。一笔的第一个点顺带把窗口摆到那块屏上。</summary>
    //
    // 第二个点到位才真正显示：右键**点一下**（没画）只会有起点这一个点，
    // 那时闪出一条线，等于把「右键菜单」变成「屏幕上闪一下再出菜单」。
    /// <param name="armed">画到此刻已经命中某条手势了吗——命中就把笔迹点亮，松手之前就看得见。</param>
    public void Point(int px, int py, bool armed = false)
    {
        // **命中就变色。** 从前这条线从头到尾一个样，画对没画对只有松手才知道；
        // 而手势最缺的正是「按下去之前的确认」。Quicker 的手册把这一条写在触发说明里：
        // 「识别到手势后（线条变色）松开鼠标即可立即触发」——它是那套体验里最值钱的一半。
        //
        // 只换颜色和粗细，不加第二种反馈（不闪、不弹、不出字）：这条线本来就在你眼睛正看的地方，
        // 变亮一档就够；再加东西只会把「一划而过」变成一场演出。
        SetArmed(armed);
        // 撤掉「推迟藏窗」（见 HideSoon）：这一笔正在画，窗得留着。不撤的话，上一笔收笔时排的
        // 那一跳会在 120ms 后落到正在画的新笔迹上——线和窗一起消失，看着像手势画丢了。
        // 放在最前面（连 _placed 那次摆位之前）：任何一个采样点都该算「正在画」。
        _hideTimer.Stop();
        if (!_placed)
        {
            if (!CoverMonitorOf(px, py)) return;   // 摆不了就整笔都不画，别拿旧的原点画到错的地方去
            _placed = true;
        }
        // 新的一笔开始：上一笔的药丸立刻收掉，别让它挂在这一笔的旁边。
        if (_noteTimer.IsEnabled) { _noteTimer.Stop(); HideNote(); }
        double x = (px - _originX) / _scale, y = (py - _originY) / _scale;
        _last = new Point(x, y);
        _hasLast = true;
        if (_disabled) return;
        _halo.Points.Add(new Point(x, y));
        _core.Points.Add(new Point(x, y));
        if (IsVisible || _core.Points.Count < 2) return;   // 不写 ==2：那一下万一没显示成，后面就再也不试了
        // 重显走 ShowGated（含 Show + 重定位）：藏→显那一帧先整窗全透明，别把上一笔的旧位图顶到屏上。
        ShowGated();
    }

    /// <summary>收笔：清掉这条线，把窗藏起来——铺满整屏的覆盖窗不能在两笔之间常驻。</summary>
    //
    // **必须 Hide。** 窗只要亮着，哪怕全透明、点得穿（WS_EX_TRANSPARENT）、不抢焦（NOACTIVATE），
    // 它仍是一块压在所有窗口上的整屏分层窗：鼠标滚轮这类**不走命中测试**的输入会落到它头上、被
    // WPF 静默吞掉，底下的程序再也滚不动（实测：改成常驻后「点窗口后滑轮失效」，手势输入也跟着乱）。
    // 所以笔一收就藏，只在按住右键拖的那几百毫秒里亮着——那时你本来也不会去滚。
    //
    // **藏→下一笔再显的旧帧问题，在「藏」这一头治（见 HideSoon），不在 Show 那一头。**
    // 重显那一帧 DWM 会先贴上这扇窗上次合成的位图；Hide 不擦位图，所以只要位图里还留着上一笔，
    // 它就会原样回到屏上。ShowGated 那套 Opacity 只是赌渲染线程比 DWM 快，赌输就照样闪一下——
    // 真正的保证是「藏之前先让清空后的画面合成出去」，见 HideSoon。
    //
    // 不 Close：手势一笔接一笔，重建分层窗口不便宜。真不用了（关手势 / 钩子卸载）由 App.CloseTrail 关。
    public void Finish()
    {
        _placed = false;
        SetArmed(false);   // 复位，否则下一笔起手就是亮的（还什么都没画呢）
        _halo.Points.Clear();
        _core.Points.Clear();
        // **`_hasLast` / `_last` 故意不清。** 紧跟着的 Note() 靠它们把「你画的是这个」
        // 摆在收笔点旁边（见 Note 的注释：MouseHook 先派 trailEnd 再派 unmatched）。
        // 在这里清掉它们，Note 会直接 return，那句提示永远不再出现。
        // 没有遗留风险：unmatched 只在 drawn.Length > 0 时发，也就是本笔确实有点。
        // （已经被当成「忘了重置」提过一次。）
        //
        // 藏之前先把空画面推出去，见 HideSoon——**这一句就是「上一笔的轨迹又回来了」的开关**。
        // 无条件走它（不判 IsVisible）：手很快时 Finish 可能赶在布局跑完之前到，带条件会跳过，
        // 于是铺满全屏的透明窗一直留在最上层。藏完若 Note 要亮药丸，由 Note 走 ShowGated 再显。
        HideSoon();
    }

    /// <summary>收笔之后把窗藏起来——但要先等这一帧空画面真的合成出去（见 <see cref="HideDelayMs"/>）。</summary>
    //
    // **这是「上一笔的轨迹又回来了」唯一的解，也是这扇窗上最容易被顺手改回 `Hide()` 的一处。**
    //
    // 藏起来的窗再显出来时，DWM 贴的是它**上次合成的那张位图**；Hide 不擦位图，所以上一笔那条线
    // 一直躺在里面，直到有人把新的一帧推上去——而 Show() 贴的就是它，那一瞬躲不掉
    //（ShowGated 那套 Opacity 只是赌渲染线程比 DWM 快，赌输就照样闪一下）。
    // 于是把顺序倒过来：**先让空画面覆盖掉旧位图，再藏**。线在 Finish 那一刻就没了
    //（Points 清空当场生效，屏上是它自己消失的），窗多留这一小会儿什么也看不见。
    private void HideSoon()
    {
        // 药丸还亮着：它到点会走同一条路，而且那时位图里早就是空的了（药丸同样是「上一笔的东西」）。
        if (_noteTimer.IsEnabled) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    // 摆到起点所在那块屏上，全程物理像素（同 QuickPanelWindow.PlaceAtCursor 的做法与理由）。
    // 任何一步取不到信息就当没有笔迹——少一条线是小事，摆错位置糊住半个屏幕不是。
    private bool CoverMonitorOf(int px, int py)
    {
        try
        {
            var h = new WindowInteropHelper(this).EnsureHandle();
            var mon = MonitorFromPoint(new POINT { X = px, Y = py }, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            _scale = GetDpiForMonitor(mon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
            _originX = mi.rcMonitor.Left;
            _originY = mi.rcMonitor.Top;
            // 铺满整块屏（不是工作区）：手势可以画到任务栏上面去。
            _spanX = mi.rcMonitor.Right - _originX;
            _spanY = mi.rcMonitor.Bottom - _originY;
            // **WPF 自己那份 Left/Top/Width/Height 也必须一起设。** 位置一直是靠 Reposition 的
            // SetWindowPos 写进 HWND 的，而那是在窗还**隐藏**时做的；等到第二个采样点调 Show()，
            // WPF 发现自己的 Left/Top 从没被设过（NaN + Manual），就按系统默认位置把窗建出来——
            // 覆盖掉隐藏时的定位——闪一帧在屏幕默认角落，Show 之后的 Reposition 才把它挪回来。
            // 外面看就是「先弹个位置错的轨迹、迅速又跳对」。把 DIP 坐标喂给 WPF，Show 直接建在对的
            // 屏上，那一帧不再错；SetWindowPos 仍留着，在混合 DPI 下按物理像素兜底（同 QuickPanelWindow）。
            Left = _originX / _scale;
            Top = _originY / _scale;
            Width = _spanX / _scale;
            Height = _spanY / _scale;
            Reposition();
            return true;
        }
        catch { return false; }
    }

    private void Reposition()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero && _spanX > 0 && _spanY > 0)
                SetWindowPos(h, IntPtr.Zero, _originX, _originY, _spanX, _spanY, SWP_NOACTIVATE | SWP_NOZORDER);
        }
        catch { }
    }
}
