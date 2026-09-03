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
        "url" => char.ConvertFromUtf32(0xE774),       // Globe ＝ 打开网址
        "copySelection" => char.ConvertFromUtf32(0xE8C8),  // Copy ＝ 获取选中的文字
        "waitClipboard" => char.ConvertFromUtf32(0xE823),  // Sync ＝ 等剪贴板变化
        "prompt" => char.ConvertFromUtf32(0xE8AC),    // Rename ＝ 让你打一句话进去
        "choice" => char.ConvertFromUtf32(0xE8FD),    // BulletedList ＝ 从几项里挑一个
        "delay" => char.ConvertFromUtf32(0xE916),     // Timer ＝ 延时
        "message" => char.ConvertFromUtf32(0xE8BD),   // Message ＝ 消息
        _ => Fallback,
    };
}
