using System.Windows.Input;
using Clockwork.I18n;
using Clockwork.Native;
using TextBox = System.Windows.Controls.TextBox;

namespace Clockwork.Views;

// 通用「点击即录键」文本框：点击 → 提示「按下快捷键…」→ 按下组合 → 回填。
// 键框统一走它——急停键 / 面板键 / 组热键（Hotkey 模式）、一键直达（HotkeyBare，允许裸键）、
// 组合键 / 发送键（SendKeys 模式）——不再各写一份状态机、也不再要单独的「捕捉」按钮。
internal static class KeyCaptureBox
{
    // box：目标文本框；mode：哪种热键档（Hotkey/HotkeyBare）还是发送键（允许裸键+accept 校验）；
    // accept：目的地可编码校验（SendKeys 用；不过则忽略、继续等）；get/set：读/写当前值
    //         （急停键写配置、组热键写工作副本、发送键写自身文本，各传各的）。
    // allowTyping：双击切到手输模式。给「发送」类的框用——Win+D / Win+E 这类被 Explorer 全局注册的组合，
    //         系统在应用拿到之前就吃掉了按键，任何应用都捕捉不到；而它们作为**发送**内容完全有效
    //         （SendKeyCombo 会真的发 LWIN），没有手输口就等于 UI 里永远录不进来。
    //         热键框一般不需要：捕捉不到的那些同样注册不了（RegisterHotKey 会失败），给了也没用。
    //         一键直达框也开着：裸 ` 的正常入口是物理录制（IME 拦截已由下面关 IME + ResolveKey
    //         两层处理掉），手输只作「装了奇怪钩子 / 远程桌面键事件变形」时的兜底——直接敲 token Oem3。
    public static void Attach(TextBox box, HotkeyCapture.KeyCaptureMode mode, System.Func<string, bool>? accept,
                              System.Func<string> get, System.Action<string> set, bool allowTyping = false)
    {
        // 这是录键框，不是文本录入框：IME 没有任何用处，只会把裸键改写成 ImeProcessed
        // （中文输入法开着时裸 ` 实测被它吃掉，见 HotkeyCapture.ResolveKey 注释）。直接对这个焦点目标
        // 关掉 IME，让键以原貌进来；ResolveKey 那层解包是第二道保险（不同 IME 对这个开关的遵守程度不一）。
        InputMethod.SetIsInputMethodEnabled(box, false);

        string prompt = Strings.Get("Hotkey_PressPrompt");
        // 内部记住「已提交值」：聚焦时 box.Text 变成提示文字，失焦复原不能再读 box.Text/get()，否则会把提示当成值。
        string committed = get();
        box.Text = HotkeyCapture.PrettyCombo(committed);   // 静止态显示美化名（Oem3→当前布局字符），编辑/落盘仍用 token
        bool typing = false;   // 手输模式：双击进入，期间不捕捉、按键照常落进文本框

        // 「双击可手输」的说明不在这里挂 tooltip：它和「哪些组合捕捉不到」是同一件事，
        // 合并写在框下方那行常驻提示里（Ed_KeysHint），免得两处各说一半、日后改一处漏一处。

        void EndTyping(bool commit)
        {
            if (commit)
            {
                var v = box.Text.Trim();
                // 校验不过就丢弃本次手输（与捕捉模式「组不出有效组合就继续等」同口径，不存一个跑不了的值）
                if (v.Length > 0 && (accept == null || accept(v))) { committed = v; set(v); }
            }
            typing = false;
            box.IsReadOnly = true;
            box.Text = HotkeyCapture.PrettyCombo(committed);
        }

        box.GotKeyboardFocus += (_, _) =>
        {
            if (typing) return;
            box.Text = prompt;
            App.Instance?.SuspendHotkeys();   // 捕捉期间注销全部全局热键，避免按到已注册组合触发急停/跑组
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (typing) EndTyping(commit: true);              // 手输后直接点走：按已输入的值提交（校验不过则复原）
            else if (box.Text == prompt) box.Text = HotkeyCapture.PrettyCombo(committed); // 未捕捉就离开：复原显示
            App.Instance?.ResumeHotkeys();
        };
        if (allowTyping)
            box.MouseDoubleClick += (_, _) =>
            {
                // 双击进手输（与「双击一行编辑」同一个手势习惯）。此时已 GotFocus、框里是提示文字，换回真值再放开只读。
                typing = true;
                box.IsReadOnly = false;
                box.Text = committed;
                box.SelectAll();
            };
        // 关窗兜底：捕捉框仍持焦点时窗口被关（如裸 Enter 触发默认按钮）不保证走 LostFocus——
        // 由共享设施挂宿主窗口 Closed→恢复，宿主不必各写一份 OnClosed，将来新宿主也不会漏。ResumeHotkeys 幂等。
        // 构造时通常已能取到宿主窗口；取不到（少见）则等 Loaded 再挂一次，绝不让关窗恢复漏掉——否则急停键会静默失效。
        if (System.Windows.Window.GetWindow(box) is { } host)
            host.Closed += (_, _) => App.Instance?.ResumeHotkeys();
        else
        {
            System.Windows.RoutedEventHandler? onLoaded = null;
            onLoaded = (_, _) =>
            {
                box.Loaded -= onLoaded;   // 只挂一次
                if (System.Windows.Window.GetWindow(box) is { } w) w.Closed += (_, _) => App.Instance?.ResumeHotkeys();
            };
            box.Loaded += onLoaded;
        }
        // 滚轮捕捉：与按键捕捉同一个手势模型——进了捕捉态（点一下框），滚一下就录。
        // 必须限定「框持键盘焦点」：WPF 的滚轮事件按鼠标指针所在元素路由，不看焦点，
        // 不加这道闸的话，用户滚动编辑器对话框、指针恰好扫过这个框，就会被静默录进一条滚轮动作。
        // 组不出结果（热键模式 / accept 不认）就不吞事件，让滚动照常传给外层滚动条。
        box.PreviewMouseWheel += (_, e) =>
        {
            if (typing || !box.IsKeyboardFocused) return;
            var wmods = HotkeyCapture.WithWin(Keyboard.Modifiers,
                                              Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin));
            // Shift+滚轮录成横向滚：这是 Windows 里「用普通滚轮横向滚」的通行手势，绝大多数人没有倾斜滚轮，
            // 不认它的话横向滚就只剩手输一条路，等于又回到「藏起来只给知道的人用」。
            // 录成 WheelLeft/WheelRight 后 Shift 本身不再进组合——发的是真 HWHEEL，比 Shift+竖滚兼容性更好。
            bool horiz = wmods.HasFlag(ModifierKeys.Shift);
            if (horiz) wmods &= ~ModifierKeys.Shift;
            if (HotkeyCapture.BuildWheelCombo(wmods, e.Delta, mode, accept, horiz) is not string wc) return;
            e.Handled = true;
            committed = wc; set(wc); box.Text = HotkeyCapture.PrettyCombo(wc);
            Keyboard.ClearFocus();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            // System=Alt 组合的主键；ImeProcessed=中文 IME 吃掉的裸键；DeadCharProcessed=死键。真值全在对应属性里。
            var key0 = HotkeyCapture.ResolveKey(e.Key, e.SystemKey, e.ImeProcessedKey, e.DeadCharProcessedKey);
            if (typing)
            {
                // 手输模式只认 Enter/Esc，其余按键照常落进文本框（不拦截）。
                if (key0 == Key.Enter) { EndTyping(commit: true); Keyboard.ClearFocus(); e.Handled = true; }
                else if (key0 == Key.Escape) { EndTyping(commit: false); Keyboard.ClearFocus(); e.Handled = true; }
                return;
            }
            e.Handled = true;   // 捕捉一切按键（PassThrough 分支除外）
            // WPF 的 Keyboard.Modifiers 只聚合 Alt/Ctrl/Shift，从不含 Windows——必须自己按键态补上，
            // 否则 Win 组合在热键模式录不进、在发送模式会被静默录成没有 Win 的裸键。详见 HotkeyCapture.WithWin。
            var mods = HotkeyCapture.WithWin(Keyboard.Modifiers,
                                             Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin));
            switch (HotkeyCapture.ProcessCaptureKey(key0, mods, mode, accept, out var combo))
            {
                case HotkeyCapture.CaptureAction.PassThrough:            // 裸 Tab（热键模式还含裸 Enter）：放行给焦点导航/默认按钮
                    e.Handled = false; break;
                case HotkeyCapture.CaptureAction.Cancel:                 // Esc：复原、退出捕捉
                    box.Text = HotkeyCapture.PrettyCombo(committed); Keyboard.ClearFocus(); break;
                case HotkeyCapture.CaptureAction.Clear:                  // 裸 Delete/Backspace（仅热键模式）：清空停用
                    committed = ""; set(""); box.Text = ""; Keyboard.ClearFocus(); break;
                case HotkeyCapture.CaptureAction.Captured:
                    committed = combo!; set(combo!); box.Text = HotkeyCapture.PrettyCombo(combo!); Keyboard.ClearFocus(); break;
                default: break;                                         // Ignore：继续等
            }
        };
    }
}
