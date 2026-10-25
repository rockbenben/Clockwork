using System;
using Clockwork.Views;
using Xunit;

public class ReminderEditorDefaultsTests
{
    [Fact]
    public void New_reminder_starts_as_a_one_off_about_an_hour_later()
    {
        var r = ReminderEditorWindow.NewReminderSeed(new DateTime(2026, 10, 24, 22, 30, 0));
        Assert.Equal("once", r.RecurType);
        Assert.Equal("23:30", r.Time);
        Assert.Equal("2026-10-24", r.OnceDate);
    }

    [Fact]
    public void New_reminder_seed_rolls_the_date_when_it_crosses_midnight()
    {
        var r = ReminderEditorWindow.NewReminderSeed(new DateTime(2026, 10, 24, 23, 30, 0));
        Assert.Equal("00:30", r.Time);
        Assert.Equal("2026-10-25", r.OnceDate);
    }
}
