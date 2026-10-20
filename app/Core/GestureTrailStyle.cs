using System;
using System.Collections.Generic;

namespace Clockwork.Core;

// 手势笔迹的样式表：**纯数据**，不认识 WPF。
//
// 为什么要把它从画笔迹的那扇窗里拆出来：样式是一份「可枚举的目录」，
// 而这份目录要能被界面列出来（管理器里那一个下拉）、能被配置文件记住（用户选了哪一个）、
// 还能被单测逐条验（每一种都画得出来、认不出来的回落到哪一个）。
// 这些事要是都塞在画笔迹的窗里，就都变成了「只能靠眼睛看」。
//
// **只换颜色和粗细**是本项目既定的设计（理由见 GestureTrailWindow 顶上那段说明）：
// 手势反馈的全部价值是「一眼看出画对没有」，花哨的笔迹会把注意力从动作本身引开。
// 所以这里的「样式」是**同一条线**的不同画法——层次（描边 / 辉光 / 线芯）、
// 笔触（实线 / 虚线 / 珠点）、颜色走向（单色 / 彩虹）——不是一套新的符号系统。

/// <summary>笔迹的一层画什么。</summary>
public enum GestureTrailRole
{
    /// <summary>压底的可读性描边：半透明的黑，垫在线芯下面。</summary>
    // 这一层不是装饰。覆盖窗压在**任意**背景上——白文档、深色 IDE、花壁纸——
    // 单一颜色总有一种底色让它读不出来。描边解决的是「任何底色上都还看得见」，
    // 不是「配色好看」，这两件事在样式表里是分开的两个角色。
    Halo,

    /// <summary>线芯外面的辉光：同色、低不透明、画得更粗。</summary>
    // 描边与辉光是同一件事的两种做法：一个是垫一层暗的，一个是罩一层亮的。
    // 亮的那一种在深色背景上更好看，暗的那一种在浅色背景上更稳。
    Glow,

    /// <summary>线芯本身：用户真正看见的那条线。</summary>
    Core,
}

/// <summary>笔迹的一层。</summary>
public sealed record GestureTrailLayer
{
    /// <summary>这一层画什么。</summary>
    public GestureTrailRole Role { get; init; } = GestureTrailRole.Core;

    /// <summary>线宽倍数（乘在基准粗细上）。</summary>
    /// <remarks>
    /// 基准粗细来自粗细档（GestureTrailWidths）：线芯那一层用它自己的两个数，
    /// 描边用描边自己的两个数，辉光拿线芯的数乘上这个倍数。
    /// </remarks>
    public double WidthFactor { get; init; } = 1;

    /// <summary>这一层的不透明度（0–1）。</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>一种笔迹样式。</summary>
public sealed record GestureTrailStyle
{
    /// <summary>样式 id（写进配置的就是它）。</summary>
    public string Id { get; init; } = GestureTrailStyles.DefaultId;

    /// <summary>由外到内排列：先画的在下，后画的压在上面。</summary>
    public IReadOnlyList<GestureTrailLayer> Layers { get; init; } = Array.Empty<GestureTrailLayer>();

    /// <summary>线芯是否用彩虹渐变（沿笔迹扫过一圈色相）。</summary>
    public bool Rainbow { get; init; }

    /// <summary>笔触：""（实线）| "dash"（虚线）| "dot"（珠点）。</summary>
    /// <remarks>
    /// **笔触是整条线的事，不是某一层的事。** 它从前挂在层上，于是描边那一层仍是实线——
    /// 虚线档就成了一条灰实线上面压着一条蓝虚线，珠点档更是一条实线上浮着几颗点。
    /// 而描边是「任何底色上都读得出来」的那一层，它一实心，整档样式就白做了。
    /// </remarks>
    public string Dash { get; init; } = "";
}

/// <summary>笔迹的粗细档。</summary>
/// <remarks>
/// 四个数而不是一个：**描边那一层与线芯不是同一个宽度**，而「命中」时两者都要加粗。
/// 拆成四个数是为了让「描边比线芯宽一倍」这件事落在数据里，
/// 画笔迹的窗只管把数用出去，不必自己知道这个比例。
/// </remarks>
public sealed record GestureTrailWidths(double CoreIdle, double CoreArmed, double HaloIdle, double HaloArmed)
{
    /// <summary>"off"（不显轨迹）| "thin"（细）| "normal"（默认标准）| "thick"（粗）。</summary>
    /// <remarks>
    /// 认不出来的值一律按 normal 走——配置文件是用户能改的，
    /// 而这里只该决定「画成什么样」，不该顺手改掉别的设置。
    /// </remarks>
    public static GestureTrailWidths Resolve(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
    {
        "thin" => new GestureTrailWidths(2.5, 3.5, 5, 7),
        "thick" => new GestureTrailWidths(6, 8.5, 12, 15),
        _ => new GestureTrailWidths(4, 5.5, 8, 10),
    };
}

/// <summary>可用的笔迹样式。</summary>
public static class GestureTrailStyles
{
    /// <summary>默认那一种的 id。</summary>
    public const string DefaultId = "classic";

    /// <summary>样式目录，顺序即界面里的顺序。</summary>
    /// <remarks>
    /// **第一项必须是 classic，而且它就是原来那一条线**：深色描边 + 主题强调色的线芯，
    /// 命中时线芯转近白、两者一起加粗。存量用户的配置里没有样式这一项，
    /// 落到默认值上时看到的应当与升级前一模一样——这是「新增可选项」的第一条规矩。
    /// </remarks>
    public static IReadOnlyList<GestureTrailStyle> All { get; } = new[]
    {
        // 经典：原来那一条。深色描边垫底，任何底色上都读得出来。
        new GestureTrailStyle
        {
            Id = "classic",
            Layers = new[]
            {
                new GestureTrailLayer { Role = GestureTrailRole.Halo, WidthFactor = 1 },
                new GestureTrailLayer { Role = GestureTrailRole.Core, WidthFactor = 1 },
            },
        },
        // 霓虹：没有深色描边，改成三层同色辉光。
        // 深色背景上好看得多；浅色底上就吃亏——所以它得是「选的」，不能是默认的。
        new GestureTrailStyle
        {
            Id = "neon",
            Layers = new[]
            {
                new GestureTrailLayer { Role = GestureTrailRole.Glow, WidthFactor = 3.4, Opacity = 0.16 },
                new GestureTrailLayer { Role = GestureTrailRole.Glow, WidthFactor = 2, Opacity = 0.30 },
                new GestureTrailLayer { Role = GestureTrailRole.Core, WidthFactor = 0.9 },
            },
        },
        // 彩虹：线芯扫过一圈色相，底下照旧垫一层深色描边。
        // 描边不能省——彩虹里有浅色那一段，没有描边它在白文档上就没了。
        new GestureTrailStyle
        {
            Id = "rainbow",
            Layers = new[]
            {
                new GestureTrailLayer { Role = GestureTrailRole.Halo, WidthFactor = 1 },
                new GestureTrailLayer { Role = GestureTrailRole.Core, WidthFactor = 1 },
            },
            Rainbow = true,
        },
        // 虚线：整条线都走虚线——描边那一层也是（笔触是整条线的事，见 GestureTrailStyle.Dash）。
        new GestureTrailStyle
        {
            Id = "dashed",
            Layers = new[]
            {
                new GestureTrailLayer { Role = GestureTrailRole.Halo, WidthFactor = 1 },
                new GestureTrailLayer { Role = GestureTrailRole.Core, WidthFactor = 1 },
            },
            Dash = "dash",
        },
        // 珠点：整条线走珠点（圆头 + 点线）。比虚线更碎，像一串珠子跟着笔走。
        new GestureTrailStyle
        {
            Id = "dots",
            Layers = new[]
            {
                new GestureTrailLayer { Role = GestureTrailRole.Halo, WidthFactor = 1 },
                new GestureTrailLayer { Role = GestureTrailRole.Core, WidthFactor = 1.35 },
            },
            Dash = "dot",
        },
    };

    /// <summary>按 id 取样式；认不出来（或为空）时回落到默认那一种。</summary>
    public static GestureTrailStyle Resolve(string? id)
    {
        var key = (id ?? "").Trim().ToLowerInvariant();
        foreach (var s in All)
            if (string.Equals(s.Id, key, StringComparison.Ordinal)) return s;
        return All[0];
    }

    /// <summary>这个 id 是不是目录里的某一种（配置读进来时用来判断要不要回落）。</summary>
    public static bool IsKnown(string? id)
    {
        var key = (id ?? "").Trim().ToLowerInvariant();
        foreach (var s in All)
            if (string.Equals(s.Id, key, StringComparison.Ordinal)) return true;
        return false;
    }
}
