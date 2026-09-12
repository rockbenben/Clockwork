using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// **终端里 Ctrl+C 不是复制，是中断。**
//
// 「搜索选中的文字」与「一键直达」靠替用户按一次复制键再读剪贴板——这个机制本身没问题，Quicker 的
// 故障排除文档写的是同一套（模拟复制 → 目标软件写剪贴板 → 等剪贴板变化后读）。
// 但在 cmd / PowerShell / Windows Terminal 上，发 Ctrl+C 会把正在跑的命令打断：
// 用户只想搜个词或一键直达，结果服务或脚本挂了，事后完全看不出是谁干的。
//
// 这不是「取不到文本」那种良性失败，是**造成了破坏**，所以值得单独钉住。
public class CopyComboTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("pwsh.exe")]            // 带扩展名也要认（经 ToProcessName 归一）
    [InlineData("conhost")]
    [InlineData("mintty")]
    public void A_classic_terminal_gets_ctrl_insert(string process)
        => Assert.Equal("Ctrl+Insert", SystemCommands.CopyComboFor(process));

    [Theory]
    [InlineData("WindowsTerminal")]
    [InlineData("windowsterminal")]     // 大小写不敏感
    [InlineData("windowsterminalpreview")]
    [InlineData("wt")]
    [InlineData("alacritty")]
    [InlineData("wezterm-gui")]
    public void A_modern_terminal_gets_ctrl_shift_c(string process)
        => Assert.Equal("Ctrl+Shift+C", SystemCommands.CopyComboFor(process));

    // 反面同样重要：在编辑器/浏览器里发 Ctrl+Insert 也能复制，但没有理由改口径，
    // 而**内嵌终端的宿主必须留在 Ctrl+C 这边**——按进程名分不开「焦点在编辑区还是终端面板」，
    // 猜错的代价是编辑器里复制不了，比在终端里少拦一次中断更常见。
    [Theory]
    [InlineData("notepad")]
    [InlineData("chrome")]
    [InlineData("Code")]        // VS Code：内嵌终端，但前台进程名是编辑器本身
    [InlineData("devenv")]
    [InlineData("explorer")]
    public void Everything_else_keeps_ctrl_c(string process)
        => Assert.Equal("Ctrl+C", SystemCommands.CopyComboFor(process));

    // 取不到前台进程名时（权限受限、桌面切换）必须回落到 Ctrl+C：
    // 那是绝大多数窗口上正确的一下，宁可在终端里偶尔中断一次，也不能让所有窗口都复制不了。
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unknown_window_keeps_ctrl_c(string? process)
        => Assert.Equal("Ctrl+C", SystemCommands.CopyComboFor(process));

    // **终端里绝不能发 Ctrl+C。** 它在那儿是中断，会把正在跑的命令（编译、服务、脚本）打断——
    // 宁可取不到选区如实报错「没有选中文字」，也绝对不能向终端发送 Ctrl+C 破坏用户的工作。
    [Theory]
    [InlineData("cmd")]
    [InlineData("WindowsTerminal")]
    [InlineData("windowsterminalpreview")]
    [InlineData("pwsh")]
    [InlineData("alacritty")]
    public void A_terminal_never_gets_ctrl_c(string process)
    {
        var keys = SystemCommands.CopyKeysFor(process);
        Assert.DoesNotContain("Ctrl+C", keys);
    }

    // 两档各给两个安全候选：单发一个键会被「这个宿主到底认哪个」绊住。
    // 现代终端首选 Ctrl+Shift+C、经典控制台首选 Ctrl+Insert，都不含 Ctrl+C。
    [Theory]
    [InlineData("WindowsTerminal", "Ctrl+Shift+C", "Ctrl+Insert")]
    [InlineData("cmd", "Ctrl+Insert", "Ctrl+Shift+C")]
    [InlineData("notepad", "Ctrl+C", "Ctrl+Insert")]
    public void The_safe_keys_are_tried_first(string process, string first, string second)
    {
        var keys = SystemCommands.CopyKeysFor(process);
        Assert.Equal(first, keys[0]);
        Assert.Equal(second, keys[1]);
    }

    // **每一个候选都必须真发得出去。** 这条是承重的：CopyComboFor 返回的串会直接交给
    // SendKeyCombo，而一个 SendKeys 编码不出来的键名不会报错，只会安安静静什么都不做——
    // 表现为「搜索手势在终端里永远说没选中」，查起来毫无线索。
    // 而且要问**真正发键的那条路**（KeysVk 虚拟键码表），不是 SendKeys 编码器——
    // 那是两条不同的路，只验后者会漏掉「键名解析得出、却发不出去」这一整类。
    [Theory]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+Insert")]
    [InlineData("Ctrl+Shift+C")]
    public void Every_candidate_resolves_to_a_real_virtual_key(string combo)
    {
        var key = KeyCombo.ParseCombo(combo).Key!;
        Assert.True(Clockwork.Native.KeyInput.KeysVk(key) != 0, combo + " 的主键解析不出虚拟键码");
        Assert.True(KeyCombo.CanEncodeForSendKeys(combo), combo + " 发不出去");
    }

    // —— 发键前等修饰键松开（Alt 系一键直达键把 Ctrl+C 发成 Ctrl+Alt+C 的那台修复）——

    [Fact]
    public void Modifier_wait_returns_immediately_when_nothing_is_held()
    {
        int polls = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SystemCommands.WaitForModifiersUp(() => { polls++; return false; }, timeoutMs: 1000, tickMs: 20);
        sw.Stop();
        Assert.Equal(1, polls);                  // 只问一声、不睡
        Assert.True(sw.ElapsedMilliseconds < 15);
    }

    [Fact]
    public void Modifier_wait_polls_until_released()
    {
        int polls = 0;
        SystemCommands.WaitForModifiersUp(() => ++polls <= 3, timeoutMs: 1000, tickMs: 1);
        Assert.Equal(4, polls);                  // 按着问 3 次、第 4 次松开即走
    }

    [Fact]
    public void Modifier_wait_is_capped_when_held_forever()
    {
        // 手一直按着（或键卡住）不能把这一步永久挂死：到点照发，退回旧失败模式。
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SystemCommands.WaitForModifiersUp(() => true, timeoutMs: 30, tickMs: 10);
        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 25, 200);
    }

    [Theory]
    [InlineData("cmd", true)]
    [InlineData("powershell", true)]
    [InlineData("windowsterminal", true)]
    [InlineData("windowsterminalpreview", true)]
    [InlineData("wt", true)]
    [InlineData("alacritty", true)]
    [InlineData("conhost", true)]
    [InlineData("chrome", false)]
    [InlineData("notepad++", false)]
    [InlineData("", false)]
    public void Esc_rescue_gate_matches_the_terminal_list(string proc, bool expected)
    {
        // Esc 会清空 cmd / PSReadLine 已输入未执行的命令行——终端进程绝不许走 Esc 恢复阶梯
        Assert.Equal(expected, SystemCommands.IsTerminalProcess(proc));
    }

    [Fact]
    public void Esc_rescue_retries_the_first_choice_key()
    {
        // 非终端首选 Ctrl+C（Esc 后重试它）；终端首选安全键但根本走不到 Esc 这一档
        Assert.Equal("Ctrl+C", SystemCommands.CopyKeysFor("chrome")[0]);
        Assert.Equal("Ctrl+Shift+C", SystemCommands.CopyKeysFor("WindowsTerminal")[0]);
        Assert.Equal("Ctrl+Insert", SystemCommands.CopyKeysFor("conhost")[0]);
    }
}
