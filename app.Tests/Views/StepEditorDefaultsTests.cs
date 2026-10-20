using System.IO;
using System.Linq;
using Xunit;

// **「一个步骤类型起手是什么」只有一处说了算。**
//
// 这件事从前有两处：新建那一步在编辑器里就地写死（`kind == "volume" ? "set" : …`），
// 而「换类型」那条路靠下拉的**兜底**——下拉里没有旧类型那个动作时，落回**第一项**。
// 于是同一个类型有三种起手：
//   新建「音量」        → 「设到几成」（写死的那一份）
//   把「关掉窗口」改成「音量」 → 「静音」（音量下拉的第一项）
//   把「静音」改成「音量」 → 「静音」（认出来了，是用户自己挑的）
// 第二条那个落回**不是谁定的**，是项的顺序；而窗口与鼠标之所以看着正常，
// 只因为它们的第一项恰好就是自己的默认动作（「关掉」「按下」）。
//
// 这组断言盯的是「两条路会不会又分家」：改一处忘了另一处，症状是
// 「同一件事，建出来的和改出来的不一样」，而界面上没有任何地方会提示它。
public class StepEditorDefaultsTests
{
    private static string EditorSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return File.ReadAllText(Path.Combine(d.FullName, "app", "Views", "StepEditorWindow.xaml.cs"));
    }

    // 三个动作下拉的兜底必须走那一份。
    //
    // **兜底而不是首选**：用户自己挑过的动作当然要留着（那是他的选择），
    // 只有「这一格在���类型下压根不成立」时才轮到默认。
    [Theory]
    [InlineData("volume")]
    [InlineData("window")]
    [InlineData("mouse")]
    public void The_action_combo_falls_back_to_the_shared_default(string kind)
        => Assert.Contains($"StepHelpers.DefaultActionFor(\"{kind}\")", EditorSource());

    // 新建那一条路也必须走它——否则两条路又分家了。
    //
    // 拆开之后它落在**各自的那一格**上（音量走 VolumeAction、窗口走 WindowAction……），
    // 而「哪个类型用哪一格」那张表只有 StepHelpers 一处说了算。
    [Fact]
    public void Creating_a_step_uses_the_shared_default()
    {
        var src = EditorSource();
        Assert.Contains("StepHelpers.DefaultActionFor(\"volume\")", src);
        Assert.Contains("StepHelpers.DefaultActionFor(\"window\")", src);
        Assert.Contains("StepHelpers.DefaultActionFor(\"mouse\")", src);
        // 从前那一串是就地写死的：kind == "volume" ? "set" : (kind == "window" ? "close" : …)
        Assert.DoesNotContain("kind == \"volume\" ? \"set\"", src);
    }

    // 编辑已有的一步时，类型是**它自己的**，调用方不该再说一遍。
    //
    // 从前那一格是 `Edit(owner, step, kind, …)`，而 kind 只在 step 为 null（新建）时才有意义——
    // 编辑时它从头到尾没被读过。于是六个调用点各自写着 `step.Kind`，
    // 看着像「调用方在指定类型」，实际是「我怕忘了才抄一遍」；
    // 而它把「类型由谁说了算」说成了两处。拆成 Create（新建，类型是参数）与
    // Edit（编辑，类型是步骤自己的）之后，这一格从签名里就没了。
    [Fact]
    public void Editing_a_step_does_not_take_a_kind()
    {
        var src = EditorSource();
        Assert.Contains("public static LaunchStep? Edit(Window? owner, LaunchStep step,", src);
        Assert.DoesNotContain("LaunchStep? step, string kind", src);
    }
}
