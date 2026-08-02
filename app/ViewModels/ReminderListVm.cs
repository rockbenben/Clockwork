using System.Collections.ObjectModel;
using Clockwork.Core;

namespace Clockwork.ViewModels;

// 提醒页一行（启用/时间/周期/文本/语音）。
public sealed class ReminderRowVm : ObservableObject, IRowVm
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<ActionGroup> _groups;   // 静默任务解析组名用（见 Text）

    public ReminderRowVm(Reminder reminder, Action onChanged, IReadOnlyList<ActionGroup> groups)
    {
        Reminder = reminder;
        _onChanged = onChanged;
        _groups = groups;
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
    public string Text => ReminderDisplay.TextSummary(Reminder, _groups);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(Speak));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(PeriodLabel));
        OnPropertyChanged(nameof(Text));
    }
}

// 提醒页 ViewModel（增删改移即存盘）。公共增删改移在 ListVm。
public sealed class ReminderListVm : ListVm<Reminder, ReminderRowVm>
{
    // 换 id 时迁移运行态的钩子(旧 id → 编辑后的提醒)，由 App 注入；null 时不迁移。
    // 传整个新 Reminder 而非只传 newId：迁移要按新配置决定迁什么（如循环已关掉就不该续上下一轮）。
    private readonly Action<string, Reminder>? _migrateState;

    public ReminderListVm(RootConfig config, Action save, Action<string, Reminder>? migrateState = null)
        : base(config, config.Reminders, r => new ReminderRowVm(r, save, config.ActionGroups), save)
        => _migrateState = migrateState;

    // 编辑后必须换新 id：运行态(是否今天已触发/稍后延迟)按 id 做键，沿用旧 id 会让改了时间的提醒
    // 因旧状态「今天已触发」当天不再响。（reminder id 仅用于运行态，不被任何配置引用，可安全更换。）
    // 但在途的「稍后」与「循环运行下一轮」不该因编辑丢失——迁到新 id（App 负责，不迁「今天已弹」）。
    protected override void OnReplacing(Reminder newModel, Reminder oldModel)
    {
        var oldId = oldModel.Id;
        newModel.Id = Guid.NewGuid().ToString();
        _migrateState?.Invoke(oldId, newModel);
    }

    // 复制出的提醒换新 id（运行态按 id 做键，共用会串状态）。
    // 有意不给 Message 加「副本」后缀：Message 就是弹窗/通知里念给用户的正文（不像组名是纯元数据），
    // 后缀会原样出现在提醒弹窗里。列表区分靠「插到原条目之后并选中」+ 用户随后改时间/文案。
    protected override void OnDuplicating(Reminder clone) => clone.Id = Guid.NewGuid().ToString();

    public Reminder? SelectedReminder => Selected;
}
