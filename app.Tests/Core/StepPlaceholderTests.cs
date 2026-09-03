using Clockwork.Core;
using Xunit;

// 占位替换。这是组合动作里**唯一**的数据流：一步产出一个值，后面的步骤按名字引用它。
// 写错一处整条链就断，而断了的表现是「地址栏里出现一串没换掉的东西」或「搜了个空」。
public class StepPlaceholderTests
{
    private static RunVars Vars(params (string Name, string Value)[] kv)
    {
        var v = new RunVars();
        foreach (var (n, val) in kv) v.Set(n, val);
        return v;
    }

    // ── {clipboard}：唯一的内置名 ──

    // 大小写不敏感：这串东西是人手打进输入框的，而写错大小写的表现是
    // **地址栏里赫然出现一个 {Clipboard}** ——看得见，但看不懂为什么没生效。
    [Theory]
    [InlineData("{clipboard}")]
    [InlineData("{Clipboard}")]
    [InlineData("{CLIPBOARD}")]
    public void The_clipboard_token_is_case_insensitive(string token)
    {
        Assert.True(StepPlaceholder.Has(token));
        Assert.True(StepPlaceholder.UsesClipboard(token));
        Assert.Equal("abc", StepPlaceholder.Apply(token, "abc", urlEncode: false));
    }

    // **只有真的写了 {clipboard} 才该去读剪贴板**——读它要跳一次 STA 线程，
    // 而引用变量的步骤根本用不着付那笔钱。
    [Theory]
    [InlineData("{关键词}", false)]
    [InlineData("没有占位符", false)]
    [InlineData("{clipboard}", true)]
    [InlineData("{关键词} 和 {CLIPBOARD}", true)]
    public void Only_a_real_clipboard_reference_costs_a_clipboard_read(string text, bool want)
        => Assert.Equal(want, StepPlaceholder.UsesClipboard(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("没有占位符")]
    [InlineData("clipboard")]     // 没有花括号就不是占位符
    public void Text_without_braces_has_no_token(string? text)
    {
        Assert.False(StepPlaceholder.Has(text));
        Assert.False(StepPlaceholder.UsesClipboard(text));
    }

    // ── 变量 ──

    [Fact]
    public void A_named_variable_is_substituted()
        => Assert.Equal("搜 猫", StepPlaceholder.Apply("搜 {关键词}", null, false, Vars(("关键词", "猫"))));

    // 名字大小写不敏感，同 {clipboard} 那条理由。
    [Fact]
    public void Variable_names_are_case_insensitive()
        => Assert.Equal("x", StepPlaceholder.Apply("{Word}", null, false, Vars(("word", "x"))));

    // 名字两边的空白不算数：`{ 关键词 }` 是人手打出来的常见样子。
    [Fact]
    public void Whitespace_around_a_name_is_ignored()
        => Assert.Equal("猫", StepPlaceholder.Apply("{ 关键词 }", null, false, Vars(("关键词", "猫"))));

    // **查不到的名字原样留着。** 这是与「值是空的」刻意分开的一档：
    // 名字不存在多半就是写错了名字，把它显示在地址栏里，用户一眼看得见哪个词没生效；
    // 悄悄换成空串则只留下一个空搜索，无从查起。
    [Fact]
    public void An_unknown_name_is_left_visible()
    {
        Assert.Equal("搜 {关键辞}", StepPlaceholder.Apply("搜 {关键辞}", null, false, Vars(("关键词", "猫"))));
        Assert.Equal("{关键词}", StepPlaceholder.Apply("{关键词}", null, false, null));   // 压根没有变量表
    }

    // 而**存在但值为空**的变量换成空串：那不是写错，是那一步真的没取到东西。
    // 留着 {关键词} 只会让人看见一个打不开的地址，还以为是自己语法写错了。
    [Fact]
    public void A_known_but_empty_variable_becomes_an_empty_string()
        => Assert.Equal("https://x/?q=", StepPlaceholder.Apply("https://x/?q={关键词}", null, true, Vars(("关键词", ""))));

    // 搜索地址模板里的 {0} 是**另一套占位**（SystemCommands 自己替换）。
    // 它在变量表里查不到，于是原样留着交给下一手——两套占位互不打架。
    [Fact]
    public void The_search_template_placeholder_survives_untouched()
        => Assert.Equal("https://x/?q={0}&hl=zh",
            StepPlaceholder.Apply("https://x/?q={0}&hl={语言}", null, false, Vars(("语言", "zh"))));

    // ── 转义与整形（两条都按**目的地**决定，不看内容）──

    // 进地址栏的必须转义：选中的话里有空格、& 和中文，不转义拼出来的地址要么打不开，
    // 要么把 & 后面的参数整段吃掉。
    [Fact]
    public void A_url_escapes_the_value()
    {
        var got = StepPlaceholder.Apply("https://x/?q={w}", null, true, Vars(("w", "a b&c 中文")));
        Assert.DoesNotContain(" ", got);
        Assert.DoesNotContain("&c", got);
        Assert.DoesNotContain("中文", got);
        Assert.StartsWith("https://x/?q=", got);
    }

    // 而「发送文本」是照原样打进窗口，转义了反而会把一段中文变成一串 %E4%B8%AD。
    [Fact]
    public void Text_keeps_the_value_as_typed()
        => Assert.Equal("说：a b&c 中文", StepPlaceholder.Apply("说：{w}", null, false, Vars(("w", "a b&c 中文"))));

    // 换行压成空格：取到的往往是**一段**而不是一个词，而两个目的地都不接受换行——
    // 地址栏会编码成 %0A，发送文本则会在半路敲一个回车（可能直接把命令提交了）。
    [Theory]
    [InlineData("第一行\n第二行")]
    [InlineData("第一行\r\n第二行")]
    [InlineData("  前后空白  ")]
    public void Newlines_and_padding_collapse(string value)
    {
        var got = StepPlaceholder.Apply("{w}", null, false, Vars(("w", value)));
        Assert.DoesNotContain("\n", got);
        Assert.DoesNotContain("\r", got);
        Assert.Equal(got.Trim(), got);
    }

    // 一段文本里出现多次都要换掉（「?q={词}&title={词}」是真实写法）。
    [Fact]
    public void Every_occurrence_is_replaced()
        => Assert.Equal("a-a", StepPlaceholder.Apply("{w}-{W}", null, false, Vars(("w", "a"))));

    // 剪贴板与变量可以在同一句里混用——「把选中的和刚问来的拼在一起」是最常见的一条链。
    [Fact]
    public void The_clipboard_and_variables_mix_in_one_string()
        => Assert.Equal("猫 和 狗", StepPlaceholder.Apply("{clipboard} 和 {w}", "猫", false, Vars(("w", "狗"))));

    // ── RunVars 自己的约定 ──

    // 名字留空 = 这一步不产出值，写入应当什么都不做（而不是造出一个名为 "" 的变量）。
    [Fact]
    public void An_unnamed_output_writes_nothing()
    {
        var v = new RunVars();
        v.Set("", "x");
        v.Set(null, "x");
        v.Set("   ", "x");
        Assert.Empty(v.Names);
    }

    // 取不到返回 null，取到空串返回空串——两者必须分得开，替换那边正是靠它区分
    // 「写错了名字」和「那一步没取到东西」。
    [Fact]
    public void Missing_and_empty_are_different_answers()
    {
        var v = Vars(("有", ""));
        Assert.Null(v.Get("没有"));
        Assert.Equal("", v.Get("有"));
    }

    [Fact]
    public void A_later_write_wins()
    {
        var v = Vars(("w", "旧"));
        v.Set("W", "新");                       // 大小写不敏感：同一个变量
        Assert.Equal("新", v.Get("w"));
        Assert.Single(v.Names);
    }
}
