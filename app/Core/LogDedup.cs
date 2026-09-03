namespace Clockwork.Core;

// clockwork.error.log 的「连续重复行」收敛判据。
//
// 起因是**整组 Repeat**：一个坏步骤在 Repeat=200 的组里会出事 200 次，从前就照写 200 行一模一样的话。
// 气泡早就按合并键叠加成一张卡了（见 App.LogGroupStep），日志却没有同样的收敛，于是一次跑动
// 就能吃掉 128KB 窗口的一大块，把之前几天的真线索顶出去。而这个文件是排障时唯一的事后线索。
//
// 「同一步反复失败」这件事，一条原文加一句标记就说清了；确切次数由 clockwork.run.log 逐步记着。
//
// **只比上一条，不做全文去重**：日志的价值一半在时间顺序上——隔了别的事件之后再出现的同一条，
// 是一个新事件，不是重复。
//
// 抽成纯类只为可测：它错了不会崩，只会静默地少记或多记日志，而静默的错正是这里最贵的一种
// （少记 = 排障时没线索；多记 = 线索被自己刷掉，两个方向都指向同一个后果）。
public sealed class LogDedup
{
    /// <summary>顶掉第二条重复行的那句标记。英文：这个文件是拿去贴 issue 的。</summary>
    public const string RepeatNote = "(same as the line above, and it kept happening; further repeats are not logged)";

    private string? _last;
    private bool _noted;

    /// <summary>忘掉「上一行」。**中间有别的东西写进了同一个文件时必须调它。**</summary>
    //
    // 两个场合：崩溃日志（它要额外空一行，故不走 AppendErrorLog）和 128KB 截断（前一半被丢掉了）。
    // 不清账的话，「同上一行」会指着一行读者根本看不到、或跟它毫无关系的话——
    // 而判据本来就是「隔了别的事件之后再出现的同一条，是一个新事件」。
    public void Reset()
    {
        _last = null;
        _noted = false;
    }

    /// <summary>要真正写进日志的那一行；<c>null</c> 表示这一行不写。</summary>
    public string? Next(string line)
    {
        if (line != _last)
        {
            _last = line;
            _noted = false;
            return line;
        }
        if (_noted) return null;   // 标记已经出过一次，后面同样的行一律丢掉
        _noted = true;
        return RepeatNote;
    }
}
