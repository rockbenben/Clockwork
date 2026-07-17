using System.Windows.Input;
using Clockwork.I18n;
using Clockwork.Native;
using TextBox = System.Windows.Controls.TextBox;

namespace Clockwork.Views;

// 通用「点击即录键」文本框：点击 → 提示「按下快捷键…」→ 按下组合 → 回填。
// 四个键框统一走它——急停键 / 组热键（Hotkey 模式）、组合键 / 发送键（SendKeys 模式）——
// 不再各写一份状态机、也不再要单独的「捕捉」按钮。
internal static class KeyCaptureBox
{
    // box：目标文本框；mode：热键（要修饰键+拒保留）还是发送键（允许裸键+accept 校验）；
    // accept：目的地可编码校验（SendKeys 用；不过则忽略、继续等）；get/set：读/写当前值
    //         （急停键写配置、组热键写工作副本、发送键写自身文本，各传各的）。
    public static void Attach(TextBox box, HotkeyCapture.KeyCaptureMode mode, System.Func<string, bool>? accept,
                              System.Func<string> get, System.Action<string> set)
    {
        string prompt = Strings.Get("Hotkey_PressPrompt");
        // 内部记住「已提交值」：聚焦时 box.Text 变成提示文字，失焦复原不能再读 box.Text/get()，否则会把提示当成值。
        string committed = get();
        box.Text = committed;

        box.GotKeyboardFocus += (_, _) =>
        {
            box.Text = prompt;
            App.Instance?.SuspendHotkeys();   // 捕捉期间注销全部全局热键，避免按到已注册组合触发急停/跑组
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (box.Text == prompt) box.Text = committed;   // 未捕捉就离开：复原显示
            App.Instance?.ResumeHotkeys();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;   // 捕捉一切按键（PassThrough 分支除外）
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            switch (HotkeyCapture.ProcessCaptureKey(key, Keyboard.Modifiers, mode, accept, out var combo))
            {
                case HotkeyCapture.CaptureAction.PassThrough:            // 裸 Tab（热键模式还含裸 Enter）：放行给焦点导航/默认按钮
                    e.Handled = false; break;
                case HotkeyCapture.CaptureAction.Cancel:                 // Esc：复原、退出捕捉
                    box.Text = committed; Keyboard.ClearFocus(); break;
                case HotkeyCapture.CaptureAction.Clear:                  // 裸 Delete/Backspace（仅热键模式）：清空停用
                    committed = ""; set(""); box.Text = ""; Keyboard.ClearFocus(); break;
                case HotkeyCapture.CaptureAction.Captured:
                    committed = combo!; set(combo!); box.Text = combo; Keyboard.ClearFocus(); break;
                default: break;                                         // Ignore：继续等
            }
        };
    }
}
