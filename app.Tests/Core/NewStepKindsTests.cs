using System.Linq;
using Clockwork.Core;
using Xunit;

// 这一轮新增的三个步骤类型：打开网址 / 获取选中的文字 / 等剪贴板变化。
//
// Kind 全集那批测试（分节覆盖、字形唯一、图标回退）已经自动罩住它们——
// 这里补的是那几条罩不到的：摘要不能是空白、参数字段的夹取、以及「网址与程序共用 Target」这条约定。
public class NewStepKindsTests
{
    [Theory]
    [InlineData("url")]
    [InlineData("copySelection")]
    [InlineData("waitClipboard")]
    public void The_new_kinds_are_in_the_catalogue(string kind)
    {
        Assert.Contains(kind, StepDisplay.StepKinds);
        // 分节必须收下它，否则「新增 ▾」菜单里根本没有入口（UxSummaryTests 也会红，这里说清原因）
        Assert.Contains(StepDisplay.StepKindSections, sec => sec.Kinds.Contains(kind));
    }

    // 配好的步骤，摘要里要能看见「打开的是哪一页」——砍成域名就答不了这个问题。
    [Fact]
    public void A_url_step_shows_the_address()
        => Assert.Contains("example.com/a", StepDisplay.StepSummary(
            new LaunchStep { Kind = "url", Target = "https://example.com/a" }));

    // 空值不许留白（与 BlankRowTests 同一条规矩，这里点名新类型）。
    [Theory]
    [InlineData("url")]
    [InlineData("copySelection")]
    [InlineData("waitClipboard")]
    public void The_new_kinds_never_render_blank(string kind)
    {
        var s = new LaunchStep { Kind = kind };
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepSummary(s)));
        Assert.False(string.IsNullOrWhiteSpace(StepDisplay.StepTitle(s)));
    }

    // 等待秒数借用 Level 字段。夹到 1..60：0 秒等于不等（那就别放这一步），
    // 60 秒以上不像在等剪贴板，像挂住了。
    [Theory]
    [InlineData(0, 5)]        // 没填 → 给个像样的默认
    [InlineData(-3, 5)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(999, 60)]
    public void The_wait_is_clamped(int raw, int want)
        => Assert.Equal(want, StepHelpers.ClampWaitSeconds(raw));

    // **网址与「运行程序」共用 Target 字段。** 这条是有意的：把一条 app 步骤的类型改成
    // 「打开网址」时，已经填好的地址不该凭空消失。约定破了不会报错，只会让用户白填一次。
    [Fact]
    public void A_url_lives_in_the_same_field_as_a_program()
    {
        var s = new LaunchStep { Kind = "app", Target = "https://example.com" };
        s.Kind = "url";                       // 只改类型，不动字段
        Assert.Contains("example.com", StepDisplay.StepSummary(s));
    }
}
