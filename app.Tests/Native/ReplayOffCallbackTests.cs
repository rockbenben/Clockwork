using System.IO;
using System.Runtime.InteropServices;
using Clockwork.Core;
using Clockwork.Native;
using Xunit;

// 「补发注入绝不在钩子回调里」的守卫与它带来的行为。
//
// 逼出这条纪律的是本机读数，不是推测。21:07 那次手动重钩把一整任钩子（19:06 装、两小时真实使用）
// 的账打出来：
//     callbackMax=313ms  chainMax=125ms  injectMax=313ms
// `chain=` 是 `CallNextHookEx`——链上别人的钩子（在场七家：AutoHotkey、Listary、LogiOptions+、
// Nutstore、PowerToys、RustDesk、Snipaste）峰值 125ms；`inject=` 是我们自己的 `SendInput`，
// 峰值 313ms，且 `callbackMax` 就等于它。**回调里最慢的一件事是我们自己干的**，而 313ms 已经
// 越过系统摘钩的预算（`LowLevelHooksTimeout`，本机 300ms，见 MouseHook 类头第 2 条）——
// 越过的后果不是报错，是钩子被静默卸掉，而此刻正被我们 `return 1` 扣着的那次按下没人补发。
// 用户看到的正是「右键多点几下就迟缓、而且经常被吞」。
//
// 另一侧的证据是同类的做法：WGestures 回调只 `Post(WM.GESTBTN_*)`、注入在队列消费侧；
// NextGestures 回调只 `tx.try_send`、注入在 `"gesture-engine"` 线程（`hook.rs:203` → `engine.rs:109`）。
// 五家里只有 MouseInc 在回调里 `::SendInput`，而它比我们的形状更重。
//
// 这几条守卫盯的都是**改回去也不会编译出错、单测也不会红**的地方——`MouseHook.cs` 类头那句
// 「SendInput 是微秒级的，留在回调里安全」就是这么写下来的，而它被实测推翻了。
public class ReplayOffCallbackTests
{
    private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public IntPtr DwExtraInfo; }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return d!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "app" }.Concat(parts).ToArray()));

    private static string Code(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));

    // 取方法体：签名往后到第一个「四个空格收尾的 }」为止（缩进取法，与 OffUiThreadTests 同一口径，
    // 用固定长度截会盖不住方法尾几行——那条老坑在这份仓库真红过一次）。
    private static string BodyOf(string src, string signature)
    {
        int start = src.IndexOf(signature);
        Assert.True(start >= 0, $"找不到 {signature}：这段守卫要跟着它走");
        int end = src.IndexOf("\n    }", start);
        Assert.True(end > start, $"{signature} 的方法体没找到收尾括号");
        return Code(src.Substring(start, end - start));
    }

    private static IntPtr Send(MouseHook hook, int msg, int x, int y)
    {
        var cb = typeof(MouseHook).GetMethod("Callback",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new MSLLHOOKSTRUCT { X = x, Y = y }, ptr, false);
            return (IntPtr)cb.Invoke(hook, new object[] { 0, new IntPtr(msg), ptr })!;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static object? Field(MouseHook hook, string name)
        => typeof(MouseHook).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(hook);

    // 四个注入出口，一个都不许漏——只盯 `SendRightDownTagged` 的话，中键那条路可以随便改回去。
    private static readonly string[] Injectors =
    {
        "Win32.SendRightDownTagged", "Win32.SendRightUpTagged",
        "Win32.SendMiddleDownTagged", "Win32.SendMiddleUpTagged",
    };

    // ── 1. 回调与它的同步下游里不许出现注入 ──
    //
    // 让它变红的生产改动：把 `ReplayClick` 改回「当场 SendInput」，或在 `CallbackCore` 里直接调
    // `Win32.Send*Tagged`（比如为了"省一次排队"）。两种都编译通过、行为照旧、只在链上有人不回话时
    // 把整条输入链按住——正是这次要治的病。
    [Fact]
    public void Nothing_is_injected_on_the_callback_path()
    {
        var src = Source("Native", "MouseHook.cs");
        var replay = BodyOf(src, "private bool ReplayClick(int which)");
        foreach (var injector in Injectors)
        {
            Assert.DoesNotContain(injector, replay);
            Assert.DoesNotContain(injector, BodyOf(src, "private IntPtr CallbackCore("));
        }
        // 例外只许有一处，且它必须待在 CallbackCore 之外、自己报名。
        // （拖拽救援那条中键按下要抢在眼前这条移动放行之前到下游，排到别的线程上就落到后面，
        //  中键自动滚动整个功能消失——理由与代价见 MouseHook.RescueDragDown / SendSync。）
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"SendSync\(\(\) => Win32"));
        Assert.Contains("RescueDragDown();", BodyOf(src, "private IntPtr CallbackCore("));
    }

    // ── 2. 注入只能发生在补发线程上，而那条线程必须是具名的后台线程 ──
    //
    // 让它变红的改动：把 worker 的代码合回回调、或者起线程时漏掉 `IsBackground`（前台线程 =
    // 用户点退出之后进程赖着不走，本仓库为写盘线程钉过同一件事）、或者漏掉 `Name`
    //（排障时它是唯一能认出这条线程的东西）。
    [Fact]
    public void The_replay_thread_is_the_only_injector_and_is_a_named_background_thread()
    {
        var src = Source("Native", "MouseHook.cs");
        // 注入的两个出口都在补发那一侧：`ReplayLoop` 发按下，抬起经 `ReplayUp` 结清
        //（那张台账只能结一次，见 ReplayUp 处的理由）。合起来必须盖满四个键，
        // 少盯一个 = 中键那条路可以随便改回回调里。
        var loop = BodyOf(src, "private void ReplayLoop()");
        var up = BodyOf(src, "private void ReplayUp()");
        Assert.Contains("Win32.SendRightDownTagged()", loop);
        Assert.Contains("Win32.SendMiddleDownTagged()", loop);
        Assert.Contains("ReplayUp();", loop);
        Assert.Contains("Win32.SendRightUpTagged()", up);
        Assert.Contains("Win32.SendMiddleUpTagged()", up);
        // 按下与抬起之间必须留着那一段间隔：同批注入的 down/up 时间戳完全相同，
        // 零长度点击会被下游当噪声丢掉（理由见 MouseHook.ClickGapMs 处，是本地的实测口径）。
        // **两档点击各一次**——只断言「出现过」的话，摘掉其中一条什么也不会红（打过这一发）。
        // 括号必须转义：正则里 `(ClickGapMs)` 是捕获组，不转义就永远 0 命中——这条守卫会因此
        // 「一直红」，而按名字筛不出来的时候很容易顺手把它删掉。同一格里 `SendSync\(` 那两处已转义。
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(loop, @"Thread\.Sleep\(ClickGapMs\)").Count);

        var start = BodyOf(src, "private void StartReplayWorker()");
        Assert.Contains("Name = \"Clockwork.Replay\"", start);
        Assert.Contains("IsBackground = true", start);
    }

    // ── 3. 探针答「注入不了」时，那一下必须原样放行，而不是吞掉 ──
    //
    // 异步化唯一真正的新风险：抬起已经吞了，之后才发现注入被 UIPI 拒，那一下右键**彻底消失**——
    // 那是把用户报的症状换了个成因。兜法是抬起前先做一次同步的「能不能注入」探测，
    // 探测不过（含读不出来）就当不能，走放行真事件那条老路。
    // 让它变红的改动：把探针写成恒 `true`、或者把 `return false` 改成 `return true`。
    [Fact]
    public void A_blocked_injection_passes_the_real_press_instead_of_eating_it()
    {
        // 探针=注入不了 → ReplayClick 必须返回 false，回调必须放行那条真抬起。
        var gate = new GestureGate(_ => false, 40);
        using var hook = new MouseHook(350, () => { }, a => a(), gesture: gate, canInject: () => false);
        Assert.Equal(new IntPtr(1), Send(hook, WM_RBUTTONDOWN, 200, 200));   // 按下照旧吞
        Assert.NotEqual(new IntPtr(1), Send(hook, WM_RBUTTONUP, 200, 200));  // 抬起必须放行
    }

    // ── 4. 补发排队失败（worker 没起来 / 队列满）也不许吃掉那一键，且丢了要看得见 ──
    //
    // NextGestures 的 `let _ = tx.try_send(ev)` 就是这个洞：队列满或消费端吊死时静默丢事件，
    // 丢掉的那一次就是用户「被吞掉的一下右键」。我们比它多两道——投递失败当场报出去让调用方
    // 放行真事件，并且把丢过的次数记下来，让"静默"这件事在日志里有名字。
    // 这里钉形状不钉行为：真去灌队列会往开发机桌面注入一连串真右键（MouseHookThreadTests 里
    // `gate.Reset()` 那条注释防的就是同一个坑），而这两条改回静默丢弃都不掉任何编译症状。
    [Fact]
    public void A_dropped_replay_is_reported_to_the_caller_and_counted()
    {
        var src = Source("Native", "MouseHook.cs");
        var enq = BodyOf(src, "private bool EnqueueReplay(int which)");
        Assert.Contains("TryAdd", enq);                                   // 有界队列 + 不等待
        Assert.Contains("_replayDropped", enq);                           // 投不进去要计数
        Assert.DoesNotContain("_ = _replay", enq);                        // 不看结果的投递 = 那个洞本身
        Assert.Contains("return EnqueueReplay(which);", BodyOf(src, "private bool ReplayClick(int which)"));
    }

    // ── 7. 「能不能注入」这句探针的判据本身 ──
    //
    // UIPI 拦注入的依据是**完整性级别**，不是"是否管理员"：目标进程的 IL 高于我们时 `SendInput`
    // 会被静默丢弃（`MouseHook._injectRejected` 记的就是这一档）。这里只钉纯函数那半截——
    // 真去开一个提权窗口的句子在单测里做不到，而读不到时的**取向**是行为决定：读不到就当送不进去，
    // 因为"放行真事件"最坏是多弹一次菜单，"吞掉再发现送不进去"最坏是那一下右键彻底没了。
    [Theory]
    [InlineData(0x2000u, 0x3000u, true)]    // 我们在 Medium，前台在 High（UAC 提权窗）→ 拦
    [InlineData(0x2000u, 0x2000u, false)]   // 同级 → 不拦
    [InlineData(0x3000u, 0x2000u, false)]   // 我们更高 → 不拦（提权运行的 Clockwork 能注入普通窗口）
    [InlineData(0x2000u, 0x1000u, false)]   // 目标更低 → 不拦
    [InlineData(0x2000u, 0x0000u, true)]    // 读不到目标的 IL → 按拦处理（取向见上）
    public void Uipi_blocks_when_the_target_is_stronger(uint self, uint target, bool blocks)
        => Assert.Equal(blocks, Win32.IntegrityBlocksInjection(self, target));

    // 让它变红的改动：探针漏掉安全桌面那一档——UAC 提示屏上没有低级钩子的事，
    // 抬起本来就收不到，但如果这里判成「能注入」，我们会把一次投不进队列的补发当成成功。
    [Fact]
    public void The_probe_also_checks_for_the_secure_desktop()
    {
        var body = BodyOf(Source("Native", "Win32.cs"), "public static bool InputNotBlockedNow()");
        Assert.Contains("IntegrityBlocksInjection", body);
        Assert.Contains("ShellWindow", body);   // 安全桌面：当前桌面上问不到 shell 窗
    }

    // ── 5. 未匹配的笔迹不再吃掉右键（行为，见 GestureGateTests）──
    // 这里只钉上层那半边：它现在走 ReplayClick，所以"你画的 X 没绑动作"那句提示必须换个地方报，
    // 不能跟着 Swallow 那一档一起消失——沉默是这个功能最难自证的状态。
    [Fact]
    public void The_unmatched_note_survives_the_verdict_change()
    {
        var src = Source("Native", "MouseHook.cs");
        var cb = BodyOf(src, "private IntPtr CallbackCore(");
        Assert.Contains("_unmatched(", cb);
        Assert.Contains("_gesture.Path", cb);
    }

    // ── 6. 「把手势关掉」这件事必须在日志里看得见 ──
    //
    // 用户 19:14 改了 `gesturesEnabled=false`，之后卡顿照旧，而日志从 19:06 起一个字没写——
    // 因为 `mouse hook reinstalled` 那行只在**重装**时写（`_prevBeat == null` 就 return），
    // `off` / `reapply` 这两条拆钩路一行都不留。于是"开关到底生效没有"从日志里判不出来，
    // 而"右键被吞"的两种解释恰恰要靠它分开。
    // 让它变红的改动：`DisposeMouseHook` 只 Dispose 不记，或记的这行不带两个开关的读数。
    [Fact]
    public void Detaching_the_hook_reports_which_switches_were_live()
    {
        var body = Code(BodyOf(Source("Services", "GestureCoordinator.cs"),
                               "private void DisposeMouseHook(string why)"));
        // 盯的是**调用形态**，不是那串文字。为什么：这一发的第一次尝试用 `if (false) AppendErrorLog(...)`
        // 去打「只 grep 文字」的守卫，打不红——注释里、死代码里的字符串都算「出现过」。
        // 所以日志内容做成纯函数 HookDetachLine（内容由下面那条断言管），这里只管「有没有无条件调它」。
        Assert.Contains("AppendErrorLog(HookDetachLine(", body);
    }

    // 那一行到底写了哪几格：两个开关的读数必须在，否则「关掉了还卡」照样分不清是谁。
    [Fact]
    public void The_detach_line_names_both_switches_and_the_readings()
    {
        var line = Clockwork.Services.GestureCoordinator.HookDetachLine(
            "off", gestures: false, panel: true, minLeg: 0, middleTolerance: 9, stranded: 2);
        Assert.Equal("mouse hook detached: source=off gestures=False panel=True minLeg=0 "
                   + "middleTolerance=9 stranded=2", line);
    }
}
