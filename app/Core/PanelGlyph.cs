namespace Clockwork.Core;

// 面板格子的图标：Segoe MDL2 Assets 码位，与托盘菜单（TrayGlyph）同一个图标族，笔画一致。
//
// 用系统自带的图标字体而不是自备图片，理由和托盘那边一样：单文件发布时 exe 旁没有资源目录，
// 而这套字体 Windows 10 起就内置，一个码位就是一个图标，不占体积、不受 DPI 影响、跟着前景色走。
//
// 写成 ConvertFromUtf32(0x...) 而不是把字符直接贴进源码（那样也能编过）：这些码位落在
// Unicode 私用区，贴进去在编辑器里是一个看不出所以然的方框，改的人无从判断它本来该是什么。
// 与 TrayGlyph 同一种写法，两处一眼可比。
//
// 码位是照着字体表挑的，但**验收靠眼睛**：挑错的码位不会报错，只会渲染成一个空心方框、
// 或者一个语义完全无关的图标。改这里之后跑 `--shots` 看一眼 QuickPanel 那几张，比读码表可靠。
public static class PanelGlyph
{
    /// <summary>整组一格用的图标（一叠东西＝一串动作）。</summary>
    public static readonly string Group = char.ConvertFromUtf32(0xE71D);      // AllApps

    /// <summary>未知步骤类型的兜底。给个中性的「运行」，总比空白强——
    /// 空字符串会让那一格只剩文字，在一片有图标的格子里显得像是坏了。</summary>
    public static readonly string Fallback = char.ConvertFromUtf32(0xE768);   // Play

    /// <summary>控制台窗口（写着 C:\ 的那台）——批处理用。</summary>
    public static readonly string Console = char.ConvertFromUtf32(0xE756);    // CommandPrompt

    /// <summary>一对花括号——其余脚本用。</summary>
    public static readonly string Code = char.ConvertFromUtf32(0xE943);       // CodeBraces

    private static readonly string[] ConsoleExt = { ".bat", ".cmd" };
    // 只列「点它会跑起来」的那些。.lnk / .exe 不在此列——它们有自己的图标可借（见 PanelIcon）。
    private static readonly string[] CodeExt =
    {
        ".ps1", ".psm1", ".ahk", ".ah2", ".au3", ".vbs", ".js", ".jse", ".wsf", ".py", ".rb", ".lua", ".pl",
    };

    /// <summary>目标是一段脚本 → 该画的线描字形；不是脚本返回 null。
    ///
    /// 为什么脚本不走「借目标自己的图标」那条：脚本文件的关联图标是**这台机器上装了哪个编辑器**
    /// 的产物——一台默认配置的机器上 `.ps1` 就是记事本那支笔，改了关联又变成编辑器的图标，
    /// 同一份配置换台机器还会长得不一样。而这一格要说的是「跑它」，不是「打开它看」：
    /// 面板的质感规则本来就是「彩色 = 打开一个东西，线描 = 做一件事」，脚本属于后者。</summary>
    public static string? ForScript(string? path)
    {
        var p = (path ?? "").Trim().TrimEnd('"');
        foreach (var e in ConsoleExt)
            if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return Console;
        foreach (var e in CodeExt)
            if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return Code;
        return null;
    }

    /// <summary>按步骤类型取图标。</summary>
    public static string ForKind(string? kind) => kind switch
    {
        "app" => char.ConvertFromUtf32(0xECAA),       // AppIconDefault ＝ 运行程序 / 打开文件
        "keys" => char.ConvertFromUtf32(0xE765),      // KeyboardClassic ＝ 发送按键
        "mouse" => char.ConvertFromUtf32(0xE962),     // Mouse ＝ 鼠标动作
        "text" => char.ConvertFromUtf32(0xE70F),      // Edit ＝ 发送文本
        "volume" => char.ConvertFromUtf32(0xE767),    // Volume ＝ 音量
        "window" => char.ConvertFromUtf32(0xE7C4),    // TaskView ＝ 窗口操作
        "system" => char.ConvertFromUtf32(0xE713),    // Settings ＝ 系统命令
        "group" => Group,                             // 引用另一个组：与「整组一格」同图标，说的是同一件事
        "step" => Group,                              // 引用清单项：同一个「复用」语义，同一个字形
        "url" => char.ConvertFromUtf32(0xE774),       // Globe ＝ 打开网址
        "path" => char.ConvertFromUtf32(0xE838),      // FolderOpen ＝ 打开文件或文件夹（取不到目标自己的图标时才露这个）
        // 下面五个是新加的类型（网址→本地源文件 / 读地址栏 / URL 解码 / 正则 / 替换）。
        // 码位经 GlyphCoverageTests 验过「在字体里存在」，但**语义是挑的、没经过人眼确认**——
        // 那条测试的注释写明了它只挡得住「不存在」（画成空心方框），挡不住「存在但语义不对」。
        // 上线前照它说的跑一次 `--shots` 看一眼这一排。
        "siteMap" => char.ConvertFromUtf32(0xE8B7),   // OpenFile ＝ 打开本地源文件
        "browserUrl" => char.ConvertFromUtf32(0xE71B),// Link ＝ 读浏览器地址栏
        "urlDecode" => char.ConvertFromUtf32(0xE945),   // 花括号 = 处理编码/代码
        "regex" => char.ConvertFromUtf32(0xE721),      // Find ＝ 正则提取是「从一段里找出一段」
        "replace" => char.ConvertFromUtf32(0xE8AB),    // 双向箭头 ＝ 来回替换
        // 交互式流程那五个。码位验过存在、与既有那一批不撞（PanelGlyphTests 钉唯一性），
        // **语义是渲染出来看图挑的**——上面那个测试只挡得住「不存在」，挡不住「存在但语义不对」
        // （原先随手挑的五个里有画成 (o) 的、有画成实心方块的，全是这个盲区）。
        // 改这里之后重跑 tools/_glyph-probe 那类探针看一眼，别只信测试。
        "waitKey" => char.ConvertFromUtf32(0xE8E1),    // 摊开的手掌 ＝ 伸手等你按一下
        "splitLines" => char.ConvertFromUtf32(0xE8E3), // 文本行堆 ＝ 一段变几行
        "math" => char.ConvertFromUtf32(0xE8EF),       // 计算器
        "skip" => char.ConvertFromUtf32(0xE711),       // X ＝ 跳出这一段
        "each" => char.ConvertFromUtf32(0xE895),       // 双向循环箭头 ＝ 反复
        "list" => char.ConvertFromUtf32(0xE8F1),       // 一摞书 ＝ 一叠条目
        // 「合」挑得最费劲：双向箭头 ⇄（已在 replace 用着）是「来回替换」，
        // 循环箭头（已在 each 用着）是「反复」——两个都不对。最后用一个向下的箭头
        // 压着一条横线：好几段收成一段。
        "join" => char.ConvertFromUtf32(0xE8C5),
        "copySelection" => char.ConvertFromUtf32(0xE8C8),  // Copy ＝ 获取选中的文字
        "waitClipboard" => char.ConvertFromUtf32(0xE823),  // Sync ＝ 等剪贴板变化
        "prompt" => char.ConvertFromUtf32(0xE8AC),    // Rename ＝ 让你打一句话进去
        "choice" => char.ConvertFromUtf32(0xE8FD),    // BulletedList ＝ 从几项里挑一个
        "delay" => char.ConvertFromUtf32(0xE916),     // Timer ＝ 延时
        "message" => char.ConvertFromUtf32(0xE8BD),   // Message ＝ 消息
        _ => Fallback,
    };
}
