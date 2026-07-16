using System.Collections.ObjectModel;
using Clockwork.Core;

namespace Clockwork.ViewModels;

// 提醒页一行（启用/时间/周期/文本/语音）。
public sealed class ReminderRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;

    public ReminderRowVm(Reminder reminder, Action onChanged)
    {
        Reminder = reminder;
        _onChanged = onChanged;
    }

    public Reminder Reminder { get; }

    public bool Enabled
    {
        get => Reminder.Enabled;
        set { if (Reminder.Enabled != value) { Reminder.Enabled = value; OnPropertyChanged(); _onChanged(); } }
    }

    public bool Speak
    {
        get => Reminder.Speak;
        set { if (Reminder.Speak != value) { Reminder.Speak = value; OnPropertyChanged(); _onChanged(); } }
    }

    public string TimeLabel => ReminderDisplay.TimeLabel(Reminder);
    public string PeriodLabel => ReminderDisplay.PeriodLabel(Reminder);
    public string Text => ReminderDisplay.TextSummary(Reminder);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Speak));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(PeriodLabel));
        OnPropertyChanged(nameof(Text));
    }
}

// 提醒页 ViewModel（增删改即存盘；无排序）。公共增删改在 ListVm。
public sealed class ReminderListVm : ListVm<Reminder, ReminderRowVm>
{
    // 换 id 时迁移运行态的钩子(旧 id→新 id)，由 App 注入；null 时不迁移。
    private readonly Action<string, string>? _migrateState;

    public ReminderListVm(RootConfig config, Action save, Action<string, string>? migrateState = null)
        : base(config, config.Reminders, r => new ReminderRowVm(r, save), save)
        => _migrateState = migrateState;

    // 编辑后必须换新 id：运行态(是否今天已触发/稍后延迟)按 id 做键，沿用旧 id 会让改了时间的提醒
    // 因旧状态「今天已触发」当天不再响。（reminder id 仅用于运行态，不被任何配置引用，可安全更换。）
    // 但正在进行的「稍后」不该因编辑丢失——把 SnoozeUntil 迁到新 id（App 负责，只迁 snooze、不迁「今天已弹」）。
    protected override void OnReplacing(Reminder newModel, Reminder oldModel)
    {
        var oldId = oldModel.Id;
        newModel.Id = Guid.NewGuid().ToString();
        _migrateState?.Invoke(oldId, newModel.Id);
    }

    public Reminder? SelectedReminder => Selected;
}
