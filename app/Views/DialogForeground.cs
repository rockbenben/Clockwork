using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Clockwork.Views;

// 无主对话框（owner=null：开机清单 / 托盘重跑 / 热键 / 手势 / 提醒触发的动作组）ShowDialog 时
// 的抢前台统一入口。
//
// Topmost 只保证「看得见」，不保证键盘焦点在我们这：这些触发来源都没有前台豁免，WPF ShowDialog
// 内部那次 SetForegroundWindow 被前台锁拒绝时，框闪在最上层而键盘仍属于背后的程序——用户打字
// 落进别的窗口，整组还挂在模态上等回答（问句形态连急停键都解不开）。
// 与 QuickPanelWindow.Popup 同一套补救：源初始化即抢一次，渲染完仍非前台再补一次，
// 250ms 后还不是就记一行证据（AppendErrorLog 自带去重，不必在这再夹开关）。
internal static class DialogForeground
{
    public static void Arm(Window dlg)
    {
        bool retried = false;

        void TryGrab()
        {
            if (dlg.IsActive) return;
            var h = new WindowInteropHelper(dlg).Handle;
            if (h != IntPtr.Zero) Clockwork.Native.Win32.ForceForeground(h);   // 被拒时它内部走 AttachThreadInput 补救
            if (!dlg.IsActive) dlg.Activate();
        }

        dlg.SourceInitialized += (_, _) => TryGrab();
        dlg.ContentRendered += (_, _) =>
        {
            TryGrab();
            // 与面板的焦点看门狗同一个节奏：渲染完成不等于激活已落定，留一拍再复核，只补这一次。
            if (retried) return;
            retried = true;
            var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            watch.Tick += (_, _) =>
            {
                watch.Stop();
                // 用户在 250ms 内已经答完关窗：IsActive 必然 false，但这不是抢前台失败——
                // 窗都不在了，不能对旧句柄补抢，更不能记一条假故障。
                if (!dlg.IsVisible) return;
                if (dlg.IsActive) return;
                var h = new WindowInteropHelper(dlg).Handle;
                if (h != IntPtr.Zero && Clockwork.Native.Win32.ForceForeground(h)) return;
                App.Instance?.AppendErrorLog(
                    $"dialog shown but foreground not acquired: {dlg.GetType().Name}");
            };
            watch.Start();
        };
    }
}
