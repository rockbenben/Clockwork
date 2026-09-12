using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// **修饰键按着时，钩子一个键都不该吞。**
//
// Ctrl/Shift/Alt/Win + 鼠标键是别的工具的组合手势地盘（WindowShuffle 的 Ctrl+右键拖窗、
// Explorer 的 Shift+右键扩展菜单、浏览器的 Ctrl/Shift+中键开标签）。低级鼠标钩子后装先调、
// 回调 return 1 截断整条链——我们扣下按下，排在后面的工具连按下都收不到；补发的又是注入事件，
// 对方按防回环会丢掉。所以豁免必须发生在「扣下按下」之前：整笔放行，gate 根本不起笔。
// 右键手势和中键长按是同一个形状（OnRightDown / OnMiddleDown 都无条件 Swallow），两条路都要守。
//
// 这条测试直接反射驱动 MouseHook.Callback（私有，但纯托管、不装钩子也能跑），
// 注入 modifierHeld 探针模拟修饰键按住，不依赖且不污染系统全局真实键盘状态。
public class MouseHookModifierBailTests
{
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    // holdMs=0：不管中键，只留手势那条路。post 同步执行，回调里的 trail 投递当场跑完，不依赖消息循环。
    private static MouseHook GestureHook(GestureGate gate, Func<bool>? modifierHeld = null)
        => new(0, () => { }, a => a(), gesture: gate, modifierHeld: modifierHeld);

    // holdMs=350、gesture=null：只留中键长按那条路（与 DevChecks 同款构造）。
    private static MouseHook MiddleHook(Func<bool>? modifierHeld = null)
        => new(350, () => { }, a => a(), gesture: null, modifierHeld: modifierHeld);

    private static IntPtr SendDown(MouseHook hook, int msg)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = 200, Y = 200 }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // Dispose 在 gate pending 时会向系统注入一次按键按下（生产里配对用户真抬起），测试里没有那一下——
    // 先把 gate Reset 掉，让它走不到那条注入路。
    private static void ResetGate(MouseHook hook, string field)
    {
        var gate = typeof(MouseHook).GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook);
        gate!.GetType().GetMethod("Reset")!.Invoke(gate, null);
    }

    // ── 右键手势 ──

    [Fact]
    public void Right_down_with_ctrl_is_passed_through_and_never_starts_a_gesture()
    {
        var gate = new GestureGate(_ => false, 40);
        using var hook = GestureHook(gate, modifierHeld: () => true);
        var verdict = SendDown(hook, WM_RBUTTONDOWN);
        // 契约是「1 = 吞掉」，其余都是放行（CallNextHookEx 的返回值无钩子链时为 0，不钉死它）。
        Assert.NotEqual(new IntPtr(1), verdict);
        Assert.False(gate.Pending);   // gate 没起笔：随后的 move/up 天然全部 Pass，WindowShuffle 收得到完整一笔
    }

    [Fact]
    public void Right_down_without_modifier_is_swallowed_as_before()
    {
        var gate = new GestureGate(_ => false, 40);
        using var hook = GestureHook(gate, modifierHeld: () => false);
        Assert.Equal(new IntPtr(1), SendDown(hook, WM_RBUTTONDOWN));
        Assert.True(gate.Pending);
        gate.Reset();
    }

    // ── 中键长按 ──

    [Fact]
    public void Middle_down_with_ctrl_is_passed_through_and_never_arms()
    {
        using var hook = MiddleHook(modifierHeld: () => true);
        Assert.NotEqual(new IntPtr(1), SendDown(hook, WM_MBUTTONDOWN));
    }

    [Fact]
    public void Middle_down_without_modifier_is_swallowed_as_before()
    {
        using var hook = MiddleHook(modifierHeld: () => false);
        Assert.Equal(new IntPtr(1), SendDown(hook, WM_MBUTTONDOWN));
        ResetGate(hook, "_gate");
    }
}
