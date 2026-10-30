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

    /// <summary>面板除格子区以外的那些高度（DIP）：表圈、底部开关带、窗口内边距与投影留白。
    /// 格子区的上限就是「工作区高度 − 这个数」——面板因此正好待在屏幕里，不会顶出工作区。</summary>
    //
    // 这一个数此前散在两处：面板那边写死 110（当工作区边距用），管理器那边根本没有。
    // 散着的后果是**设置说空话**：行数设了 4，小屏上实际只露得下 1 行，而界面从不说这件事。
    // 现在两边共用这一份算式（ScreenRows），管理器就能如实告诉用户「这块屏放得下几行」。
    public const double WindowChrome = 110;

    /// <summary>两坑自己那点高度（DIP）：各自的边框与内边距（各 8）+ 中间那道缝（6）。</summary>
    public const double BandChrome = 23;

    /// <summary>面板一行有多高（DIP）：格子高 + 上下各一个空隙。</summary>
    public static double RowHeight(string? size, bool iconOnly)
        => For(size, iconOnly).Height + TileGap * 2;

    /// <summary>列数的合法范围。上界 8 不是怕算不过来，是过了这个数一格就窄到放不下两个汉字。</summary>
    public const int MinColumns = 3;
    public const int MaxColumns = 8;

    /// <summary>单条带的行数范围。0 = 这条带不要（下带设 0 就退回单条带的老样子）。
    /// 上界 8：两条带加起来 16 行已经比多数屏幕的可用高度还高，再往上只会滚。</summary>
    public const int MinRows = 0;
    public const int MaxRows = 8;

    public static int ClampColumns(int n) => n < MinColumns ? MinColumns : n > MaxColumns ? MaxColumns : n;

    public static int ClampRows(int n) => n < MinRows ? MinRows : n > MaxRows ? MaxRows : n;

    /// <summary>一屏能看见多少格 = （上带行数 + 下带行数）× 列数。</summary>
    //
    // **这不是一页的上限。** 一页装多少由用户自己决定，装不下的在面板里往下滚——
    // 一页就是一页，不再按容量切成好几屏（理由见 PanelLayout.BuildPages）。
    // 这个数只回答「一屏放得下吗」：搜索要不要装、搜索结果留多少、管理器里的读数。
    //
    // 两条带都为 0 时给一个兜底值而不是返回 0：0 会让「一屏放得下吗」这个问题没有答案。
    // 这种配置从界面走不到（上带的下拉从 1 起，见 MainWindow 里填下拉那段），但配置文件是能手改的。
    public static int VisibleCapacity(int topRows, int bottomRows, int columns)
    {
        int rows = ClampRows(topRows) + ClampRows(bottomRows);
        return rows <= 0 ? ClampColumns(columns) : rows * ClampColumns(columns);
    }

    /// <summary>一屏最多几行（两条带加起来）。</summary>
    //
    // 面板内部记的是「上带几行 + 下带几行」，设置面上给用户的却是两个别的数
    // （每屏行数、上带行数）——换算在这里做一次，两边才不会各算各的。
    public const int MaxScreenRows = MaxRows * 2;

    /// <summary>「每屏几行」与「分带在哪」换算成面板用的那两个数。</summary>
    //
    // 设置面只有两个下拉：**每屏行数**（一屏装得下几行）与**上带行数**（分带的位置）。
    // 面板内部用的仍是「上带几行 + 下带几行」，下带行数 = 每屏 − 上带。
    //
    // 夹在中间的分带按「上带 = ⌊每屏/2⌋」分：分带略高于视觉中心，七行就是 3 + 4——
    // 一整块七行的方格阵读起来是一堵墙，切开之后眼睛才有落点（键盘分区是同一个道理）。
    //
    // 越界一律夹住：上带不能超过每屏（否则下带成了负数），每屏至少一行。
    public static (int Top, int Bottom) BandsOf(int rowsPerScreen, int topRows)
    {
        int rows = Math.Max(1, Math.Min(rowsPerScreen, MaxScreenRows));
        int top = Math.Max(1, Math.Min(topRows, rows));
        return (top, rows - top);
    }

    /// <summary>这块屏上，面板能显示几行格子。装不下的在页内滚，不是不显示。</summary>
    //
    // 算的是「格子区能有多高」：工作区高度减去窗口自己的那些高度（表圈、开关带），
    // 再减去两坑的边，然后除以一行的高度。
    //
    // **那把刻度不占这里的高度**：它从前在腰上（两坑之间），现在搬到了表圈里、挨着页名
    // （它答的是「哪一页」，该和页名待在一起）。腰于是回到它本来的身份：一道夹板。
    //
    // 拿不到工作区高度时返回 0——那表示「量不到」，不是「一行也放不下」。
    public static int ScreenRows(double workAreaHeight, string? size, bool iconOnly)
    {
        if (workAreaHeight <= 0) return 0;
        double usable = workAreaHeight - WindowChrome - BandChrome;
        return Math.Max(1, (int)Math.Floor(usable / RowHeight(size, iconOnly)));
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
