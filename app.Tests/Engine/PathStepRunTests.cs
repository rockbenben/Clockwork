using Clockwork.Core;
using Clockwork.Engine;
using Xunit;

// 「打开文件或文件夹」的执行分支。真正 ShellExecute 开东西不在单测里做，
// 这里只钉两条「什么都不该被打开」的路径：空目标告警、自指拦截。
public class PathStepRunTests
{
    [Fact]
    public void An_empty_target_warns_without_starting_anything()
    {
        var mark = StepRunner.RunStepMark(new LaunchStep { Kind = "path" }, null, System.Array.Empty<string>());
        Assert.Equal(1, mark.Fail);
    }

    // 与 app/url 同一道闸：目标若就是 Clockwork（这里借测试宿主自己的路径当「self」），
    // 拦下并告警，绝不能再拉起一个进程。
    [Fact]
    public void A_self_target_is_skipped()
    {
        var self = new[] { GetType().Assembly.Location };
        var mark = StepRunner.RunStepMark(new LaunchStep { Kind = "path", Target = self[0] }, null, self);
        Assert.Equal(1, mark.Fail);
    }
}
