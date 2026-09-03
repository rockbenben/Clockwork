using Clockwork.Core;
using Xunit;

// 这几条盯的是「日志少记了」和「日志被自己刷满」这两个方向——都不会崩，只会静默。
public class LogDedupTests
{
    [Fact]
    public void First_line_passes_through()
        => Assert.Equal("a", new LogDedup().Next("a"));

    [Fact]
    public void Second_identical_line_becomes_the_note()
    {
        var d = new LogDedup();
        d.Next("a");
        Assert.Equal(LogDedup.RepeatNote, d.Next("a"));
    }

    [Fact]
    public void Further_identical_lines_are_dropped()
    {
        var d = new LogDedup();
        d.Next("a");
        d.Next("a");
        Assert.Null(d.Next("a"));
        Assert.Null(d.Next("a"));
    }

    // 只比上一条：换了别的事件之后，同一条话是新事件，必须再写一次。
    [Fact]
    public void Same_line_after_a_different_one_is_a_new_event()
    {
        var d = new LogDedup();
        d.Next("a");
        Assert.Equal("b", d.Next("b"));
        Assert.Equal("a", d.Next("a"));
    }

    // 标记本身不能把「原文」顶掉：写了标记之后来一条新话，那条新话要原样出去。
    [Fact]
    public void A_new_line_after_the_note_is_written_verbatim()
    {
        var d = new LogDedup();
        d.Next("a");
        d.Next("a");
        Assert.Equal("b", d.Next("b"));
    }

    // Reset 是给「中间有别的东西写进了同一个文件」用的（崩溃堆栈、128KB 截断）。
    // 不清账的话，「同上一行」会指着一行读者看不到、或跟它毫无关系的话。
    [Fact]
    public void Reset_makes_the_next_identical_line_a_new_event()
    {
        var d = new LogDedup();
        d.Next("a");
        d.Reset();
        Assert.Equal("a", d.Next("a"));       // 不是重复了：中间插过东西
    }

    [Fact]
    public void Reset_also_clears_the_noted_flag()
    {
        var d = new LogDedup();
        d.Next("a");
        Assert.Equal(LogDedup.RepeatNote, d.Next("a"));
        d.Reset();
        Assert.Equal("a", d.Next("a"));       // 重新开始计，而不是继续丢
        Assert.Equal(LogDedup.RepeatNote, d.Next("a"));
    }

    // 反过来的边界：标记文本自己被当成一行传进来时，也走同一套判据，不该被特殊照顾。
    [Fact]
    public void The_note_text_itself_is_just_a_line()
    {
        var d = new LogDedup();
        Assert.Equal(LogDedup.RepeatNote, d.Next(LogDedup.RepeatNote));
        Assert.Equal(LogDedup.RepeatNote, d.Next(LogDedup.RepeatNote));
        Assert.Null(d.Next(LogDedup.RepeatNote));
    }
}
