using System;
using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 前台易主的事件监听（out-of-context WinEvent）。
//
// 它补的是 250ms 轮询的粒度：「失焦即关」本来就该在焦点丢的那一拍发生，轮询最快也要
// 等下一拍。但**轮询不能删**——从未激活/半激活的面板压根不会有任何易主事件经它的
// 手，观察拍是那一类唯一的关闭路（见 Core.PanelFocus）。
//
// OUTOFCONTEXT 的回调由**装钩线程**经消息派发：必须在有消息泵的线程（UI 线程）上构造；
// 回调里不做阻塞活、不碰窗口状态——只把「出去看看」交给调用方在派发里复盘一次。
// 回调委托必须字段持有：它一旦被 GC，钩子持有的指针就成了野指针，症状是偶发的
// 原生崩溃，查起来比面板不关远得多。
public sealed class ForegroundWatch : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventDelegate(IntPtr hHook, uint evt, IntPtr hwnd, uint idObject, uint idChild, uint idEventThread, uint dwmsEventTime);

    private readonly WinEventDelegate _callback;
    private IntPtr _hook;

    /// <summary>必须在有消息泵的线程（UI 线程）上构造。onForeignForeground 经该线程的消息循环回调。</summary>
    public ForegroundWatch(Action onForeignForeground)
    {
        _callback = (_, _, hwnd, _, _, _, _) =>
        {
            try
            {
                // 自家进程的窗口上来不算「用户走了」：弹右键菜单/主窗/toast 的焦点流转
                // 由 WPF 自己的 Activated/Deactivated 那一对负责。
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0 || pid == GetCurrentProcessId()) return;
                onForeignForeground();
            }
            catch { /* 钩子回调里绝不让异常穿回 user32：丢这一次事件，轮询兜底仍在 */ }
        };
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                _callback, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        try { UnhookWinEvent(_hook); } catch { }
        _hook = IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
}
