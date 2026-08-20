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

        // 行上的可写属性写穿到模型并触发存盘。「语音播报」列去掉后，列表里唯一可直接勾的就是启用态。
        vm.Rows[0].Enabled = false;
        Assert.False(cfg.Reminders[0].Enabled);

        vm.SelectedIndex = 0;
        vm.DeleteSelected();
        Assert.Single(cfg.Reminders);
        Assert.Equal("b", cfg.Reminders[0].Message);
        Assert.True(saves >= 3);
    }

    [Fact]
    public void Row_shows_the_skipped_today_state()
    {
        // 「今天不再」的状态住在 App 的运行态里，列表靠注入的谓词读它。谓词返回 true 时
        // 时间列要带上跳过后缀——这是去掉侧栏按钮后，用户唯一能看出「这条今天不响」的地方。
        var cfg = new RootConfig { Reminders = new() { new Reminder { Time = "22:00", Message = "a" } } };
        var plain = new ReminderListVm(cfg, () => { });
        var skipped = new ReminderListVm(cfg, () => { }, null, _ => true);
        Assert.DoesNotContain("·", plain.Rows[0].TimeLabel);
        Assert.Contains(plain.Rows[0].TimeLabel, skipped.Rows[0].TimeLabel);   // 原文案仍在，只是加了后缀
        Assert.NotEqual(plain.Rows[0].TimeLabel, skipped.Rows[0].TimeLabel);
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
        // 登录时触发不走 recurType 判定（见 ReminderDisplay.PeriodLabel）：即使配了 everyNDays，
        // 本列的真实答案仍是「每次登录」，不是「每2天」——那会陈述一件不成立的事。
        Assert.Equal("每次登录", vm.Rows[0].PeriodLabel);
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
        Assert.Equal(2, vm.Rows[0].Group.Steps.Count);

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
