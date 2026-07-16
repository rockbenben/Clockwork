using Clockwork.Core;
using Clockwork.ViewModels;
using Xunit;

public class ReminderGroupVmTests
{
    [Fact]
    public void Reminder_add_delete_toggle()
    {
        var cfg = new RootConfig { Reminders = new() { new Reminder { Message = "a" } } };
        int saves = 0;
        var vm = new ReminderListVm(cfg, () => saves++);
        Assert.Single(vm.Rows);

        vm.SelectedIndex = -1;
        vm.Add(new Reminder { Message = "b" });
        Assert.Equal(2, cfg.Reminders.Count);

        vm.Rows[0].Speak = true;
        Assert.True(cfg.Reminders[0].Speak);

        vm.SelectedIndex = 0;
        vm.DeleteSelected();
        Assert.Single(cfg.Reminders);
        Assert.Equal("b", cfg.Reminders[0].Message);
        Assert.True(saves >= 3);
    }

    [Fact]
    public void ReplaceSelected_mints_new_id_so_edited_reminder_rearms()
    {
        var cfg = new RootConfig { Reminders = new() { new Reminder { Id = "old-id", Time = "10:00" } } };
        var vm = new ReminderListVm(cfg, () => { });
        vm.SelectedIndex = 0;
        vm.ReplaceSelected(new Reminder { Time = "14:00" });   // 改到当天更晚
        Assert.NotEqual("old-id", cfg.Reminders[0].Id);        // 换新 id → 丢掉「今天已触发」旧状态
        Assert.False(string.IsNullOrWhiteSpace(cfg.Reminders[0].Id));
        Assert.Equal("14:00", cfg.Reminders[0].Time);
    }

    [Fact]
    public void Reminder_row_labels()
    {
        var cfg = new RootConfig { Reminders = new() { new Reminder { Trigger = "startup", StartupHourMode = "before", StartupHour = 8, RecurType = "everyNDays", IntervalDays = 2 } } };
        var vm = new ReminderListVm(cfg, () => { });
        Assert.Equal("登录时·8点前", vm.Rows[0].TimeLabel);
        Assert.Equal("每2天", vm.Rows[0].PeriodLabel);
    }

    [Fact]
    public void Group_add_delete_toggle_and_count()
    {
        var cfg = new RootConfig
        {
            ActionGroups = new() { new ActionGroup { Name = "组A", Steps = new() { new LaunchStep(), new LaunchStep() } } }
        };
        int saves = 0;
        var vm = new GroupListVm(cfg, () => saves++);
        Assert.Equal("组A", vm.Rows[0].Name);
        Assert.Equal("2", vm.Rows[0].StepCount);

        vm.Add(new ActionGroup { Name = "组B" });
        Assert.Equal(2, cfg.ActionGroups.Count);

        vm.Rows[0].Enabled = false;
        Assert.False(cfg.ActionGroups[0].Enabled);

        vm.SelectedIndex = 1;
        vm.DeleteSelected();
        Assert.Single(cfg.ActionGroups);
        Assert.True(saves >= 3);
    }
}
