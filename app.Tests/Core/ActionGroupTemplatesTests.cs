using Clockwork.Core;
using Xunit;

public class ActionGroupTemplatesTests
{
    [Fact]
    public void Six_templates_with_steps_and_names()
    {
        var all = ActionGroupTemplates.All();
        Assert.Equal(6, all.Count);
        Assert.All(all, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
        Assert.All(all, g => Assert.NotEmpty(g.Steps));
        Assert.All(all, g => Assert.True(g.Enabled));
    }

    [Fact]
    public void Each_call_generates_fresh_ids()   // 重复添加同一模板不撞 id（运行态/引用都按 id 做键）
    {
        var a = ActionGroupTemplates.All();
        var b = ActionGroupTemplates.All();
        for (int i = 0; i < a.Count; i++) Assert.NotEqual(a[i].Id, b[i].Id);
        Assert.Equal(a.Count, a.Select(g => g.Id).Distinct().Count());   // 同一批内也不重复
    }

    [Fact]
    public void Steps_kinds_are_valid()   // 模板步骤类型必须是引擎认识的（防手滑写错 kind 静默不执行）
    {
        var known = new HashSet<string>(StepDisplay.StepKinds);
        foreach (var g in ActionGroupTemplates.All())
            foreach (var s in g.Steps)
                Assert.Contains(s.Kind, known);
    }
}
