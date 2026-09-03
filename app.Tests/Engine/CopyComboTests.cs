using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// **终端里 Ctrl+C 不是复制，是中断。**
//
// 「搜索选中的文字」靠替用户按一次 Ctrl+C 再读剪贴板——这个机制本身没问题，Quicker 的
// 故障排除文档写的是同一套（模拟 Ctrl+C → 目标软件写剪贴板 → 等剪贴板变化后读）。
// 但在 cmd / PowerShell / Windows Terminal 上，那一下会把正在跑的命令打断：
// 用户只想搜个词，结果 npm install 挂了，而且事后完全看不出是谁干的。
//
// 这不是「取不到文本」那种良性失败，是**造成了破坏**，所以值得单独钉住。
public class CopyComboTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("pwsh.exe")]            // 带扩展名也要认（经 ToProcessName 归一）
    [InlineData("WindowsTerminal")]
    [InlineData("windowsterminal")]     // 大小写不敏感
    [InlineData("conhost")]
    [InlineData("mintty")]
    public void A_terminal_gets_ctrl_insert(string process)
        => Assert.Equal("Ctrl+Insert", SystemCommands.CopyComboFor(process));

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

    // **终端里 Ctrl+C 绝不能排在前面。** 它在那儿是中断，先发它就会把正在跑的命令打断——
    // 这是整条规则存在的理由。它排在最后是实测逼出来的（见 CopyKeysFor 的注释：
    // WT 默认没把 ctrl+c 绑成 copy，于是很多人自己改，而那种配置下前两个安全键都不生效），
    // 但「最后」和「第一」是两件事，这条盯的就是别有人图省事把它挪上去。
    [Theory]
    [InlineData("cmd")]
    [InlineData("WindowsTerminal")]
    [InlineData("pwsh")]
    public void A_terminal_never_gets_ctrl_c_first(string process)
    {
        var keys = SystemCommands.CopyKeysFor(process);
        Assert.NotEqual("Ctrl+C", keys[0]);
        Assert.NotEqual("Ctrl+C", keys[1]);          // 第二个也不行：安全键要先试完
        Assert.Equal("Ctrl+C", keys[^1]);            // 但它得在，否则自定义键位的机器上功能完全不可用
    }

    // 两档各给两个候选：单发一个键会被「这个宿主到底认哪个」绊住，
    // 实测 Windows Terminal 默认把 copy 绑在 ctrl+shift+c / ctrl+insert / enter 三个键上。
    [Theory]
    [InlineData("WindowsTerminal", "Ctrl+Insert", "Ctrl+Shift+C")]
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
}
