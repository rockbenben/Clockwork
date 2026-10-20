using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 步骤编辑器是**整份重建**一步的：它从表单收值、造一个新步骤，再把「界面上没有控件的那几格」
// 从原件抄回来。抄漏一格，那一格就在保存时**静默消失**——而用户只是改了个参数。
//
// 这一族盯的就是那几格。它们分两类：
//   · **位置与身份**：ParentId（这一步在块里的位置）、Id（面板点击统计的键）——
//     丢了的症状是「步骤从循环里跳出来了」「点击统计归零」。
//   · **决定跑不跑的条件**：变量条件（IfVar / IfVarOp / IfVarValue）——
//     编辑器里**没有**这一行的控件（那是模板与手改配置才写得出来的），
//     而它决定这一步跑不跑。丢了的症状是「打开再保存，条件悄悄没了」。
//
// 另有一档是「等按键」的 0 = 一直等：它被同一把尺子（1..60）夹成了 5，
// 于是那个选项从来就没生效过，编辑器那一行的说明写的却是「填 0 = 一直等」。
public class StepEditorFieldLossTests
{
    private static string EditorSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return File.ReadAllText(Path.Combine(d.FullName, "app", "Views", "StepEditorWindow.xaml.cs"));
    }

    // 层级：这一步属于哪一段。抹掉它 = 把这一步从循环里拽出来，而界面上它还缩在那一段里。
    [Fact]
    public void The_step_keeps_its_place_in_the_block()
        => Assert.Contains("r.ParentId = _original.ParentId;", EditorSource());

    // 变量条件：它决定这一步跑不跑，而编辑器里没有它的控件。
    [Theory]
    [InlineData("r.IfVar = _original.IfVar;")]
    [InlineData("r.IfVarOp = _original.IfVarOp;")]
    [InlineData("r.IfVarValue = _original.IfVarValue;")]
    public void The_variable_condition_survives_an_edit(string line)
        => Assert.Contains(line, EditorSource());

    // 「等按键」的 0 = 一直等：这一档的 0 与「等剪贴板」的 0 量纲不同，
    // 不能共用同一把 1..60 的尺子（那一档的 0 是「至少等 5 秒」）。
    [Theory]
    [InlineData(0, 0)]      // 0 = 一直等，放行
    [InlineData(-1, 0)]     // 负数也当「一直等」（与 0 同义）
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(80, 60)]    // 越界夹到 60
    public void The_wait_key_timeout_keeps_zero_as_unlimited(int input, int expected)
        => Assert.Equal(expected, StepHelpers.ClampWaitKeySeconds(input));

    // 而「等剪贴板」那一档的 0 仍然是「至少等 5 秒」——两档的 0 不是一回事。
    [Fact]
    public void The_wait_clipboard_timeout_still_clamps_zero_to_five()
        => Assert.Equal(5, StepHelpers.ClampWaitSeconds(0));
}
