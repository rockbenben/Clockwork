namespace Clockwork.Core;

/// <summary>一格的尺寸（DIP）。<see cref="LabelFont"/> 为 0 表示不画文字（只显示图标）。</summary>
public sealed record PanelTileMetrics(double Width, double Height, double GlyphFont, double LabelFont);

// 面板格子的尺寸表。数字集中在这一处，而不是散在 XAML 与代码里——
// 「紧凑 / 标准 / 宽松」三档要同时改宽、高、图标字号、文字字号四个数，
// 分散之后调一档必然漏一个，表现为某一档下图标顶到边或文字被压扁。
//
// 三档的取值不是等比缩放，而是各自配平过：紧凑档把文字压到 10.5 已接近中文可读下限，
// 所以宽度只收 16px（再窄两个汉字就要换行）；宽松档反过来，宽度给得比字号涨得多，
// 好让「设置页 - 文本内容上下文菜单」这类长标签有地方换行而不是立刻省略号。
public static class PanelMetrics
{
    /// <summary>合法档位。顺序即设置页下拉的顺序。</summary>
    public static readonly string[] Sizes = { "compact", "normal", "roomy" };

    /// <summary>格与格之间的空隙（每格四周各留这么多，所以相邻两格之间是它的两倍）。
    /// 1 而不是 2：格子本身已经是透明无边框的，指到才亮——空隙的职责只是别让两块高亮连成一片，
    /// 一个像素就够。空隙给多了，面板会比它装的东西宽出一大截。</summary>
    public const double TileGap = 1;

    /// <summary>列数的合法范围。上界 8 不是怕算不过来，是过了这个数一格就窄到放不下两个汉字。</summary>
    public const int MinColumns = 3;
    public const int MaxColumns = 8;

    /// <summary>单条带的行数范围。0 = 这条带不要（下带设 0 就退回单条带的老样子）。
    /// 上界 8：两条带加起来 16 行已经比多数屏幕的可用高度还高，再往上只会滚。</summary>
    public const int MinRows = 0;
    public const int MaxRows = 8;

    public static int ClampColumns(int n) => n < MinColumns ? MinColumns : n > MaxColumns ? MaxColumns : n;

    public static int ClampRows(int n) => n < MinRows ? MinRows : n > MaxRows ? MaxRows : n;

    /// <summary>一页装得下多少格 = （上带行数 + 下带行数）× 列数。装不下的翻到下一页。</summary>
    //
    // 两条带都为 0 时给一个兜底容量而不是返回 0：容量 0 会让分页逻辑切出无穷多张空页。
    // 这种配置从界面走不到（上带的下拉从 1 起，见 MainWindow 里填下拉那段），但配置文件是能手改的。
    public static int PageCapacity(int topRows, int bottomRows, int columns)
    {
        int rows = ClampRows(topRows) + ClampRows(bottomRows);
        return rows <= 0 ? ClampColumns(columns) : rows * ClampColumns(columns);
    }

    /// <summary>格子阵摊开有多宽（DIP）。**面板的宽度该由它说了算**——
    /// 夹板上那排自带操作是 WrapPanel，本就打算窄的时候折行，可 SizeToContent 下没人给它宽度，
    /// 它就一路把窗口横向撑开，面板于是比格子阵宽出一大截。把这个数交给它当上限，折行才真会发生。</summary>
    public static double GridWidth(string? size, bool iconOnly, int columns)
        => ClampColumns(columns) * (For(size, iconOnly).Width + TileGap * 2);

    public static string NormalizeSize(string? size)
        => System.Array.IndexOf(Sizes, size ?? "") >= 0 ? size! : "normal";

    /// <summary>取一档的尺寸。<paramref name="iconOnly"/> 时不留文字的位置，格子收成接近正方。</summary>
    public static PanelTileMetrics For(string? size, bool iconOnly = false)
    {
        // 只显示图标时高度必须跟着收，否则格子里图标居中、上下各留一大片空白，
        // 看着像文字没加载出来——而那正是「只显示图标」要避免的观感。
        return (NormalizeSize(size), iconOnly) switch
        {
            ("compact", false) => new(72, 66, 18, 10.5),
            ("compact", true) => new(56, 50, 19, 0),
            ("roomy", false) => new(104, 90, 25, 12.5),
            ("roomy", true) => new(78, 70, 28, 0),
            (_, true) => new(64, 58, 23, 0),
            _ => new(88, 76, 21, 11.5),
        };
    }
}
