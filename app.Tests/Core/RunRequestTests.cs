using Clockwork.Core;
using Xunit;

// --run-group 的命令行解析。这是个用户对着计划任务的「添加参数」框手打出来的入口，
// 而它出错的样子是「任务安安静静什么都没干」——所以每种写法都钉一条。
public class RunRequestTests
{
    [Fact]
    public void Space_separated_form()
        => Assert.Equal("Focus", RunRequest.ParseGroupArg(new[] { "--run-group", "Focus" }));

    [Fact]
    public void Equals_form()
        => Assert.Equal("Focus", RunRequest.ParseGroupArg(new[] { "--run-group=Focus" }));

    // 开关本身大小写不敏感：脚本里写成 --Run-Group 的人不该因此得到静默失败。
    [Fact]
    public void Switch_is_case_insensitive()
    {
        Assert.Equal("Focus", RunRequest.ParseGroupArg(new[] { "--Run-Group", "Focus" }));
        Assert.Equal("Focus", RunRequest.ParseGroupArg(new[] { "--RUN-GROUP=Focus" }));
    }

    // 组名里的空格由 Windows 的命令行拆分负责（引号），到这儿已经是一个元素了。
    [Fact]
    public void Keeps_inner_spaces_and_trims_the_outside()
    {
        Assert.Equal("End of day", RunRequest.ParseGroupArg(new[] { "--run-group", "End of day" }));
        Assert.Equal("End of day", RunRequest.ParseGroupArg(new[] { "--run-group", "  End of day  " }));
        Assert.Equal("End of day", RunRequest.ParseGroupArg(new[] { "--run-group=  End of day " }));
    }

    // 非 ASCII 组名（本程序默认就是中文界面）必须原样带过去。
    [Fact]
    public void Handles_a_non_ascii_group_name()
        => Assert.Equal("专注", RunRequest.ParseGroupArg(new[] { "--run-group", "专注" }));

    [Fact]
    public void Finds_the_switch_among_other_args()
        => Assert.Equal("Focus", RunRequest.ParseGroupArg(new[] { "--show", "--run-group", "Focus", "--boot" }));

    // 写成一条 Fact 带用例表，而不是 [Theory] + 一堆 [InlineData]：InlineData 是 params object[]，
    // 想把 string[] 当「一个参数」传进去得写成 new object[] { new string[] { ... } } 才编得过，
    // 一层套一层之后，看的人得先解开括号才知道这条用例在说什么。
    [Fact]
    public void Returns_null_when_there_is_no_usable_name()
    {
        string[][] cases =
        {
            Array.Empty<string>(),                 // 没给
            new[] { "--show" },                    // 只有别的开关
            new[] { "--run-group" },               // 给了开关没跟值
            new[] { "--run-group", "   " },        // 跟了个空白
            new[] { "--run-group=" },              // 等号后面空的
            new[] { "--run-group=   " },
            // 后面跟的是另一个开关：这属于「漏了组名」，不能把 --show 当组名去找——
            // 否则用户收到的是「找不到动作组 --show」，而真正的问题是他少打了一个词。
            new[] { "--run-group", "--show" },
        };
        foreach (var args in cases)
            Assert.Null(RunRequest.ParseGroupArg(args));
    }

    [Fact]
    public void Null_args_is_not_a_crash()
        => Assert.Null(RunRequest.ParseGroupArg(null));
}
