using System.Collections.Generic;
using System.Windows.Input;

namespace Clockwork.Native;

// 急停键「按键捕捉」的纯逻辑：WPF 按键 → 可注册的组合键串。抽出来便于单测（避免依赖实时键盘状态）。
public static class HotkeyCapture
{
    // 把 WPF 那颗「外层键」解成真正按下的键。三层包装，按优先级：
    //   · Key.System          —— Alt 组合：主键藏在 SystemKey（Alt+` 时 e.Key=System、SystemKey=Oem3）；
    //   · Key.ImeProcessed    —— **中文输入法把裸键吃掉时**：真值在 ImeProcessedKey。
    //     实测（探针，2026-09，微软拼音）：中文 IME 开着时物理裸 ` 在 WPF 里报
    //     e.Key=ImeProcessed、ImeProcessedKey=Oem3——旧代码只解 System 这一层，于是这个键在框里
    //     永远录不进，低级钩子却看得见干净的 vk=0xC0（拦它的是 IME，不是 RunAny）。Alt 组合不受
    //     IME 接管，仍走 System 那条。
    //   · Key.DeadCharProcessed —— 死键（美式布局碰不上，欧陆布局的 ` ´ ¨ 组合重音会走这）：真值在 deadKey。
    // 取不出真值（值为 None）时原样返回外层键——后续 BuildCombo 会判无效并 Ignore，行为与今天一致。
    public static Key ResolveKey(Key reported, Key systemKey, Key imeProcessedKey, Key deadCharProcessedKey)
    {
        if (reported == Key.System) return systemKey == Key.None ? reported : systemKey;
        if (reported == Key.ImeProcessed) return imeProcessedKey == Key.None ? reported : imeProcessedKey;
        if (reported == Key.DeadCharProcessed) return deadCharProcessedKey == Key.None ? reported : deadCharProcessedKey;
        return reported;
    }

    // 是否为修饰键本身（含 Alt 时 e.Key=System）——捕捉时忽略、等主键。
    public static bool IsModifierKey(Key k)
        => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
             or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    // WPF Key → token。数字/小键盘取好看的显示名；其余用枚举名（WPF Key 名与 WinForms Keys 名基本一致，
    // 字母/F 键/Oem 符号/媒体键等都覆盖），能否注册/发送交给各执行路径判定，不再靠窄白名单。
    // WPF 对共享枚举值可能报出旧别名（PageDown→"Next" 等），规范化成两条发送路径都认的名字。
    private static readonly Dictionary<string, string> TokenAlias = new()
    {
        ["Next"] = "PageDown", ["Prior"] = "PageUp", ["Snapshot"] = "PrintScreen",
        ["Capital"] = "CapsLock", ["Return"] = "Enter",
    };

    public static string? KeyToken(Key k)
    {
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();          // 显示 0-9 而非 D0-D9
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "NumPad" + (int)(k - Key.NumPad0);
        var name = k.ToString();
        if (string.IsNullOrEmpty(name)) return null;
        return TokenAlias.TryGetValue(name, out var alias) ? alias : name;
    }

    // 组合键串的**显示**形态：把 Oem3 这类 token 换成当前键盘布局下的真字符
    // （美式布局下 Ctrl+Oem3 显示成 Ctrl+`，德语 QWERTZ 同一物理键显示成 Ctrl+^——字符随布局走，绝不写死）。
    // 只供 UI 显示用：落盘/注册仍走原 token（KeyInput.ToHotkeyParams 解析它），所以「无法识别/注册失败」
    // toast 里给用户看的那一份可以美化，但去重记账/配置存取必须继续用原值。
    // 只替换系统给出**单字符名**的非修饰键；字母/数字结果与 token 相同，F1/Space/PageUp 等多字符名保留 token。
    public static string PrettyCombo(string? combo)
    {
        if (string.IsNullOrEmpty(combo)) return combo ?? "";
        var parts = combo.Split('+');
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p is "Ctrl" or "Alt" or "Shift" or "Win") continue;   // 修饰键段不动
            if (Enum.TryParse<Key>(p, out var key))
            {
                var name = Win32.KeyNameChar((uint)KeyInterop.VirtualKeyFromKey(key));
                if (name != null) parts[i] = name;
            }
        }
        return string.Join("+", parts);
    }

    // 系统级保留组合：注册成功会把它从全系统劫走（如 Alt+F4 让所有程序关不了窗、Ctrl+Shift+Esc 打不开任务管理器）。
    // 存「规范化 (修饰键掩码, 虚拟键)」而非字符串：配置手改可以写 "Alt + F4"/"Control+Escape"/"Alt+esc"/乱序修饰键，
    // 注册解析器全都认——按字符串比对会被这些拼法绕过，恰恰放走了要拦的东西。掩码 Alt=1 Ctrl=2 Shift=4（同 RegisterHotKey）。
    // （Ctrl+Alt+Del 是 SAS，根本到不了应用层，无须列。）
    private static readonly HashSet<(uint Mods, uint Vk)> ReservedKeys = new()
    {
        (0x1, 0x73),   // Alt+F4
        (0x1, 0x09),   // Alt+Tab
        (0x1, 0x1B),   // Alt+Esc
        (0x1, 0x20),   // Alt+Space
        (0x2, 0x1B),   // Ctrl+Esc
        (0x6, 0x1B),   // Ctrl+Shift+Esc（任务管理器）
    };

    // 是否系统保留组合。捕捉 UI 与注册路径都要查——先经与注册完全相同的解析（KeyInput.ToHotkeyParams）
    // 规范化，再比对；解析不了的交由后续「无效键」路径处理，此处返回 false。
    public static bool IsReserved(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return false;
        var p = KeyInput.ToHotkeyParams(combo);
        return p != null && ReservedKeys.Contains((p.Modifiers, p.Vk));
    }

    // 由修饰键 + 主键组出组合键串。默认要求至少一个修饰键（注册裸键会把该键从全系统劫走——
    // 任何程序里都打不出这个字符）；allowBare 仅供一键直达那一个框：它刻意要 RunAny 式的裸 `
    // （Oem3）单键，代价用户已知悉。两种档都排除系统保留组合，且最终组合必须可被 RegisterHotKey
    // 注册；否则返回 null（调用方忽略本次按键）。
    public static string? BuildCombo(ModifierKeys mods, Key key, bool allowBare = false)
    {
        if (IsModifierKey(key)) return null;
        var tok = KeyToken(key);
        if (tok == null) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0 && !allowBare) return null;   // 至少一个修饰键（裸键档除外）
        parts.Add(tok);
        var combo = string.Join("+", parts);
        if (IsReserved(combo)) return null;                             // 系统保留组合：拒绝
        return KeyInput.ToHotkeyParams(combo) == null ? null : combo;   // 不可注册就作废
    }

    // 「发送键」用的组合：允许裸键（发送按键不要求修饰键，如 F5 / Enter / {ENTER}），
    // 不排除系统保留组合（只是发给目标窗口、不做全局注册），最终由 accept 校验目的地能否编码。
    public static string? BuildSendCombo(ModifierKeys mods, Key key, Func<string, bool>? accept)
    {
        if (IsModifierKey(key)) return null;
        var tok = KeyToken(key);
        if (tok == null) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(tok);
        var combo = string.Join("+", parts);
        return accept == null || accept(combo) ? combo : null;
    }

    // 滚轮捕捉：在捕捉框上滚一下就录成 WheelUp/Down（horizontal 时为 Right/Left），按住修饰键滚则带上修饰键。
    // 录键盘是「按一下」，录滚轮理应是「滚一下」——让人手输 WheelDown 这种魔法字符串，
    // 等于把功能藏起来只给知道的人用。手输仍然有效（改 json、无鼠标时的退路），但不再是唯一入口。
    //
    // 只捕捉滚轮、不捕捉点击：这个框正是**靠点击来获得焦点**的，把点击也录进去的话，
    // 点进框的那一下就会被当成用户想录的动作。点击类动作走「鼠标」步骤的下拉选，
    // 那条路本来就更好找；要 Ctrl+左键这种带修饰键的组合则手输伪键。
    // Hotkey 模式一律返回 null：RegisterHotKey 表达不了鼠标，录进去会得到一个永不触发的热键。
    // delta>0 = 向上/向右，与 Windows 的 mouseData 符号约定同向。
    public static string? BuildWheelCombo(ModifierKeys mods, int delta, KeyCaptureMode mode, Func<string, bool>? accept, bool horizontal = false)
    {
        if (mode != KeyCaptureMode.SendKeys || delta == 0) return null;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(horizontal ? (delta > 0 ? "WheelRight" : "WheelLeft") : (delta > 0 ? "WheelUp" : "WheelDown"));
        var combo = string.Join("+", parts);
        return accept == null || accept(combo) ? combo : null;
    }

    // 捕捉框有三种模式：
    //   Hotkey     = 全局热键（急停键/面板键/组热键，要修饰键、拒保留组合）；
    //   HotkeyBare = 全局热键但允许零修饰键的主键——只给一键直达用：RunAny 式裸 `（Oem3），
    //                注册后该字符在全系统都打不出来，急停/面板/组键不许走这一档；
    //   SendKeys   = 发送键（步骤里「发送按键」/「置前发送键」，允许裸键、按 accept 校验）。
    public enum KeyCaptureMode { Hotkey, HotkeyBare, SendKeys }

    // 捕捉框按键的统一决策：四个键框（急停键/组热键/组合键/发送键）共用同一状态机，不再各抄一份、也不再要「捕捉」按钮。
    public enum CaptureAction
    {
        Ignore,       // 吞掉本次按键，继续等（只按修饰键 / 组不出有效组合）
        PassThrough,  // 不拦截（e.Handled=false）：裸 Tab 留给焦点导航（Hotkey 模式下裸 Enter 也放行给默认按钮）
        Cancel,       // Esc：恢复原值、退出捕捉
        Clear,        // 裸 Delete/Backspace 清空（仅 Hotkey 模式——发送键里 Delete/Enter 是可录的键）
        Captured,     // 捕捉成功，combo 即结果
    }

    // WPF 的 Keyboard.Modifiers 只聚合 Alt/Ctrl/Shift，**从不设 ModifierKeys.Windows**
    //（KeyboardDevice.Modifiers 只查 LeftAlt/RightAlt、LeftCtrl/RightCtrl、LeftShift/RightShift）。
    // 捕捉框必须自己按 Keyboard.IsKeyDown(LWin/RWin) 补上，否则：
    //   热键模式 → 修饰键集为空、BuildCombo 判「至少一个修饰键」失败 → 按 Win 组合毫无反应；
    //   发送模式 → 悄悄录成没有 Win 的裸键（按 Win+D 存进去的是 D），比录不进更糟。
    // 运行期两条路径本来都支持 Win（SendKeyCombo 发 LWIN、ToHotkeyParams 给 MOD_WIN），坏的只有取修饰键这一步。
    public static ModifierKeys WithWin(ModifierKeys wpfMods, bool winDown)
        => winDown ? wpfMods | ModifierKeys.Windows : wpfMods;

    // 调用方先把 Key.System 解包成 SystemKey 再传入。accept 仅 SendKeys 模式用（目的地可编码校验）。
    public static CaptureAction ProcessCaptureKey(Key key, ModifierKeys mods, KeyCaptureMode mode, Func<string, bool>? accept, out string? combo)
    {
        combo = null;
        bool bare = (mods & ~ModifierKeys.Shift) == ModifierKeys.None;   // 无修饰（Shift 单独不算）
        if (key == Key.Tab && bare) return CaptureAction.PassThrough;    // Tab/Shift+Tab：移动焦点（两模式都放行——裸 Tab 罕见作发送键）
        if (IsModifierKey(key)) return CaptureAction.Ignore;
        if (key == Key.Escape) return CaptureAction.Cancel;
        if (mode is KeyCaptureMode.Hotkey or KeyCaptureMode.HotkeyBare)
        {
            // 两档热键的清空/默认按钮/保留组合语义完全一样，唯一差别：HotkeyBare 允许裸主键
            //（一键直达的裸 `，见 KeyCaptureMode 枚举注释）。
            if (key == Key.Enter && mods == ModifierKeys.None) return CaptureAction.PassThrough;  // 热键：裸 Enter 给默认按钮
            // 只有「裸」Delete/Backspace 才是清空；带修饰键的（如 Ctrl+Delete）是用户想录的组合，交给 BuildCombo。
            // HotkeyBare 也保留这一条：清空框需要一个入口，而裸 Delete/Backspace 本就不该被全局劫走。
            if (key is Key.Delete or Key.Back && mods == ModifierKeys.None) return CaptureAction.Clear;
            combo = BuildCombo(mods, key, allowBare: mode == KeyCaptureMode.HotkeyBare);
        }
        else   // SendKeys：裸键可录（含 Enter/Delete），accept 校验；无清空/默认按钮分支。
        {
            combo = BuildSendCombo(mods, key, accept);
        }
        return combo == null ? CaptureAction.Ignore : CaptureAction.Captured;
    }
}
