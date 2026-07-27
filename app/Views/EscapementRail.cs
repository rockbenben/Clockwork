using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
// 项目全局引了 System.Drawing（托盘用），Brush/Rectangle 两边同名——显式取 WPF 那一套。
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace Clockwork.Views;

// 擒纵刻度轨：提醒卡片 / 提醒弹窗左缘那条黄铜轨，同时就是这张界面的「余量表」。
//
// 齿距沿用 Theme.xaml 里 TabItem 选中下划线的刻度节拍（6px 齿 + 4px 齿隙）——横着是「当前在这一页」，
// 竖着是「这张界面还剩多久」。所以它读起来是 Clockwork 本来就有的擒纵语汇，不是一条通用进度条。
//
// 两种状态，各说一件真事：
//   走时(durationMs>0) —— 点亮的齿自下而上逐格回收，格数=剩余时间。一格一格地跳，不做平滑过渡：
//                          擒纵机构的本职就是把连续的力矩放成可数的离散格，会滑的钟不是钟。
//   常驻(durationMs<=0) —— 满格不动 = 「这张在等你」（自动关闭设为 0 的提醒卡片）。
// 第三种「压根没有钟」——你自己点出来的对话框——不用本控件，用实心细轨（见 BrandDialog）。
public sealed class EscapementRail : Grid
{
    public const double Pitch = 10;      // 一格 = 6px 齿 + 4px 齿隙
    private const int PollMs = 250;      // 轮询而非按格排期：跳格频率天然被它封顶，短卡片也不会跳成闪烁

    // 底轨不设显式高度（默认 Stretch 铺满分到的空间），点亮轨的显式高度封顶于 ActualHeight——
    // 本控件绝不参与决定父级高度。曾经 Height=ceil(H/10)*10 会把 DesiredSize 撑得比分到的空间大，
    // 反过来驱动 SizeToContent 的宿主长高、再触发自己的 SizeChanged，MinHeight/EstToastHeight
    // 等常量全被它悄悄改写。末格不足一整齿时由 ClipToBounds 裁掉，这正是刻度尺该有的样子。
    private readonly Rectangle _track = new();
    private readonly Rectangle _lit = new() { VerticalAlignment = VerticalAlignment.Top };

    private DispatcherTimer? _timer;
    private int _ticks = 1;          // 轨道总格数（由实际高度算；宿主卡片有 MinHeight，实际至少六格）
    private int _shown = -1;         // 当前点亮格数，-1=还没画过
    private int _durationMs;
    private long _endAt;             // Environment.TickCount64 口径的清零时刻
    private bool _warn;

    public EscapementRail()
    {
        Width = 3;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left;   // 同名属性遮蔽了枚举，须全限定
        // 四周内缩：轨道不再贴着 1px 描边、也不从圆角切到圆角，读作刻在面板上的一把尺，
        // 而不是卡片的一条描边。（贴边的虚线会被读成「边框断了」——实测第一眼就会看错。）
        Margin = new Thickness(9, 14, 0, 14);
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Children.Add(_track);
        Children.Add(_lit);
        Loaded += (_, _) => Rebuild();
        SizeChanged += (_, _) => Rebuild();
        Unloaded += (_, _) => Stop();   // 窗口关掉后计时器不该还在走
    }

    // 起算/重算。durationMs<=0 = 常驻（满格不动）。重复调用即重新起算（卡片合并时用）。
    public void Run(int durationMs, bool warn)
    {
        _warn = warn;
        _durationMs = durationMs;
        _endAt = Environment.TickCount64 + Math.Max(0, durationMs);
        _shown = -1;   // 强制重画：合并后格数要立刻满回去
        Rebuild();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    // 测量恒报 0 高：本控件绝不参与决定父级高度。子矩形的显式 Height（点亮轨）否则会经 Grid 汇入
    // DesiredSize——在 SizeToContent 的卡片里表现为「能长不能缩」：正文合并变短后，旧轨高把窗口
    // 卡在历史最高点，SizeChanged 不再触发、Rebuild 不再重算。排布仍按父级实际给的全高进行。
    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);   // 子元素仍需测量，否则不参与排布
        return new Size(0, 0);              // 宽度由显式 Width=3 在框架层校正
    }

    // 幂等：Loaded / SizeChanged / Run 都走这里。不重置 _endAt，故尺寸变化不会把倒计时拨回去。
    private void Rebuild()
    {
        if (!IsLoaded || ActualHeight <= 0) return;
        _ticks = Math.Max(1, (int)(ActualHeight / Pitch));   // 向下取整：只数装得下的整齿
        _shown = -1;   // 几何变了，强制按新高度重画（Paint 的去重比较只看格数）
        _track.Fill = Brush("TickSteel");
        _lit.Fill = Brush(_warn ? "TickClay" : "TickBrass");

        if (_durationMs <= 0) { Stop(); Paint(_ticks); return; }   // 常驻：满格不动
        Paint(Remaining());
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollMs) };
        _timer.Tick += (_, _) =>
        {
            int left = Remaining();
            Paint(left);
            if (left <= 0) Stop();   // 收完就停，别空转到窗口关闭
        };
        _timer.Start();
    }

    // 剩余格数：由绝对时刻现算，不做累减——休眠 / 掉帧 / 尺寸变化后都自动对得上。
    private int Remaining()
    {
        long remain = _endAt - Environment.TickCount64;
        if (remain <= 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(_ticks * (double)remain / _durationMs));
    }

    private void Paint(int lit)
    {
        if (lit == _shown) return;
        _shown = lit;
        _lit.Height = Math.Min(lit * Pitch, Math.Max(0, ActualHeight));   // 封顶于分到的高度，不撑大 DesiredSize
        _lit.Visibility = lit > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);
}
