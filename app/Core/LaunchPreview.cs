using System.Text;
using Clockwork.I18n;

namespace Clockwork.Core;

// 手动重跑整份启动清单之前，摊给用户看的那一份清单（纯计算，不碰 UI、不碰配置）。
//
// 为什么要有它：托盘菜单里那一句「重新运行启动清单」只有七个字，而清单里的步骤多半不可逆
// ——关窗口、发按键、结束程序。点下去就没有回头路，连日志都要等跑完才写。
// 所以跑之前先把「这一次会做哪几件事」摊开，让人看着内容按下去，而不是按完才知道。
//
// **只列本次会跑的顶层条目**，不是展开后的每一个子步骤：展开要递归动作组、算重复轮次、
// 躲循环引用（见 LaunchSequence.RunGroupRounds 那套），复写一份必然与真正的运行逻辑漂移——
// 而漂移出来的那个「清单」比没有更糟：它会让用户以为看过了就等于跑过了。
// 顶层条目正是用户在「我的启动清单」页里看到的那几条，看的是同一份清单，不是另一份近似物。
//
// 过滤口径与真正运行时同一套（LaunchPlan.Build：勾选 + 时间条件），
// 所以框里列出来的就是这一次真会跑的那些条目。
public static class LaunchPreview
{
    // 一次最多摊几行。摊多了确认框就得滚动，而要滚动的确认框多半被直接点掉——
    // 那道闸就白设了。摊少了又显得清单比实际小，余下的条数由 More 那一条补上。
    public const int MaxLines = 6;

    /// <summary>摊给用户看的那份清单：总条数、摊出来的行、余下几条。</summary>
    /// <param name="total">这一次真会跑的顶层条目数。</param>
    /// <param name="lines">摊出来的行（每行一条，已带项目符号）。</param>
    /// <param name="more">余下没摊出来的条数（0 表示全摊出来了）。</param>
    public static (int total, IReadOnlyList<string> lines, int more) Build(RootConfig config, DateTime now, int maxLines = MaxLines)
    {
        // 哨兵照运行时的口径解析（hour<0 / isoDay<=0 = 取当前），所以时间条件那一层
        // 与真正跑的时候判的是同一件事。
        var (hour, isoDay) = StepCondition.ResolveSentinels(-1, 0, now);
        var plan = LaunchPlan.Build(config, hour, isoDay, now.Minute);
        var lines = plan.Take(Math.Max(0, maxLines)).Select(s => "· " + StepDisplay.StepSummary(s)).ToList();
        return (plan.Count, lines, plan.Count - lines.Count);
    }

    /// <summary>确认框上那段话：题干 + 摊开的清单 + 余下的条数。</summary>
    ///
    /// 摆在这里而不是调用点：这句话只有一处真正要说（手动重跑之前那道确认），
    /// 但**要看它长什么样**的地方有两处 —— 真机上的那个框，和截图矩阵里那一档。
    /// 两处各拼一遍，日后改文案就只改得动其中一处，于是截图验的是一个死句子。
    public static string Message(IReadOnlyList<string> lines, int more)
    {
        var sb = new StringBuilder(Strings.Get("Confirm_RerunLaunchList"));
        foreach (var line in lines) sb.Append('\n').Append(line);
        if (more > 0) sb.Append('\n').Append(Strings.Lf("Confirm_RerunLaunchListMore", more));
        return sb.ToString();
    }
}
