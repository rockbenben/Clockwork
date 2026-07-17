namespace Clockwork.Core;

// 纯数据模型：LaunchStep / Reminder / ActionGroup / 默认配置。
// 不引用 WPF / Win32，可被 xUnit 直接测。属性名 PascalCase，经 System.Text.Json 的 CamelCase 策略映射到既有 JSON 键。

public sealed class OnYes
{
    public string Type { get; set; } = "none";
    public string Target { get; set; } = "";
}

public sealed class LaunchStep
{
    public bool Enabled { get; set; } = true;
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    public int DelayMs { get; set; }
    // app
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public bool Elevated { get; set; }
    public bool ActivateIfRunning { get; set; }
    public string ActivateProcess { get; set; } = "";
    public string WindowStyle { get; set; } = "";
    public string AltTargets { get; set; } = "";
    // keys
    public string Combo { get; set; } = "";
    // group（引用动作组 id）
    public string GroupId { get; set; } = "";
    // volume/window 共用 action；时间条件「仅 N 点前」
    public string Action { get; set; } = "";
    public int Level { get; set; } = 50;
    public bool OnlyBefore8 { get; set; }
    public int BeforeHour { get; set; } = 8;
    public int BeforeMinute { get; set; }   // 「仅 N 前」的分钟位：阈值=BeforeHour:BeforeMinute，支持任意时刻（不再只整点）
    // 仅在这些星期(ISO 1..7)开机启动；空=每天
    public List<int> Days { get; set; } = new();
    // window
    public string Process { get; set; } = "";
    public string SendKey { get; set; } = "{ENTER}";
    public int WaitForWindowSeconds { get; set; }
    public int PostWindowDelaySeconds { get; set; }
    // system
    public string Command { get; set; } = "";
    // message 步骤（动作组用）
    public string Message { get; set; } = "";
    public bool Speak { get; set; }
    public bool Confirm { get; set; }
    public OnYes OnYes { get; set; } = new();
    // text 步骤：往焦点窗口输入的字面文本
    public string Text { get; set; } = "";
    // 所有步骤通用：用途说明（仅列表显示用）
    public string Note { get; set; } = "";
    // 所有步骤通用：连续执行次数（循环动作）；每次之间等 delayMs
    public int Repeat { get; set; } = 1;
}

public sealed class Reminder
{
    // 稳定身份：计时器运行时状态按它做键，改文案/同名同时刻不串状态
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Enabled { get; set; } = true;
    public string Trigger { get; set; } = "time";
    public string Time { get; set; } = "09:00";
    public List<int> Days { get; set; } = new();
    public string Message { get; set; } = "";
    public bool Speak { get; set; }
    public OnYes OnYes { get; set; } = new();
    public int GraceMinutes { get; set; } = 5;
    // 错过必补：到点没弹(PC 休眠/关机/程序没跑)时，下次程序在跑且当天还没弹过就补弹一次，不受 grace 窗口上限约束。
    public bool CatchUpIfMissed { get; set; }
    public int DelaySeconds { get; set; }
    public int RandomDelaySeconds { get; set; }
    public int RepeatMinutes { get; set; }
    public string RepeatUntil { get; set; } = "";
    public string RecurType { get; set; } = "daily";
    public int IntervalDays { get; set; } = 1;
    public int MonthlyDay { get; set; } = 1;
    public string AnchorDate { get; set; } = "";
    public int PopupTimeoutSeconds { get; set; }
    public string StartupHourMode { get; set; } = "any";
    public int StartupHour { get; set; } = 9;
    // 「登录时」只认真正的开机时段：开机超过 N 分钟后再启动本程序不算登录（0=每次启动都算）
    public int StartupWithinMinutes { get; set; } = 10;
    // 非空=到点静默(不弹窗)运行该动作组
    public string SilentGroupId { get; set; } = "";
}

public sealed class ActionGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "";   // 全局热键（如 "Ctrl+Alt+F"），空=不绑定；随时一键运行本组
    public List<LaunchStep> Steps { get; set; } = new();

    // 运行快照：浅拷贝步骤列表（步骤对象共享，字段级并发读写无害），后台枚举不受 UI 增删干扰。
    public ActionGroup SnapshotForRun() => new() { Id = Id, Name = Name, Enabled = Enabled, Hotkey = Hotkey, Steps = new List<LaunchStep>(Steps) };
}

public sealed class AppSettings
{
    public int TickSeconds { get; set; } = 30;
    public bool StartMinimized { get; set; }
    public bool StartupWaitForReady { get; set; }
    public int StartupDelaySeconds { get; set; } = 30;
    public string StopHotkey { get; set; } = "Ctrl+Alt+Q";
    public string Language { get; set; } = "";   // 空=跟随系统显示语言（App 启动时解析成具体 code 并落盘）
}

public sealed class RootConfig
{
    public List<LaunchStep> LaunchSteps { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public List<ActionGroup> ActionGroups { get; set; } = new();

    public static RootConfig Default() => new()
    {
        LaunchSteps = DefaultLaunchSteps(),
        Reminders = DefaultReminders(),
        Settings = new AppSettings(),
        ActionGroups = new(),
    };

    // 供后台运行拍快照：浅拷贝各列表，枚举不再受 UI 线程增删的并发修改干扰（开机延迟期间增删步骤会
    // 让后台 foreach 抛 Collection was modified）。步骤/提醒对象本身共享——字段级并发读写无害。
    public RootConfig SnapshotForRun() => new()
    {
        LaunchSteps = new List<LaunchStep>(LaunchSteps),
        Reminders = new List<Reminder>(Reminders),
        Settings = Settings,
        ActionGroups = ActionGroups.Select(g => g.SnapshotForRun()).ToList(),
    };

    // 首次使用的示例清单：前 8 条默认勾选，后 3 条默认不勾选。
    public static List<LaunchStep> DefaultLaunchSteps() => new()
    {
        new LaunchStep { Kind = "volume", Label = "开机先静音（示例·仅上午 8 点前生效，晚上开机就不会突然出声）", Action = "mute", OnlyBefore8 = true },
        new LaunchStep { Kind = "app", Label = "打开浏览器（示例·换成你常用的浏览器）", Target = "msedge.exe" },
        new LaunchStep { Kind = "app", Label = "打开常用网站（示例·换成你的邮箱 / 待办 / 网页版应用）", Target = "https://github.com", DelayMs = 800 },
        new LaunchStep { Kind = "app", Label = "打开常用软件（示例·把目标换成你自己的 .exe 或快捷方式）", Target = "notepad.exe", DelayMs = 1000 },
        new LaunchStep { Kind = "delay", Label = "等待 2 秒（示例·纯延时步骤，给前面的软件留出打开时间）", DelayMs = 2000 },
        new LaunchStep { Kind = "keys", Label = "回到桌面 Win+D（示例·发送组合键）", Combo = "Win+D" },
        new LaunchStep { Kind = "volume", Label = "音量调到 30%（示例·设音量会自动取消上面的静音）", Action = "set", Level = 30, DelayMs = 500 },
        new LaunchStep { Kind = "window", Label = "最小化浏览器（示例·窗口动作，按进程名操作）", Action = "minimize", Process = "msedge", DelayMs = 1000 },
        new LaunchStep { Kind = "system", Label = "显示桌面（示例·系统命令，默认不勾选）", Command = "showDesktop", Enabled = false },
        new LaunchStep { Kind = "app", Label = "Windows 设置（示例·可直接打开 URI 协议，默认不勾选）", Target = "ms-settings:", Enabled = false },
        new LaunchStep { Kind = "app", Label = "任务管理器（示例·默认不勾选，避免每次开机弹出）", Target = "taskmgr.exe", Enabled = false },
    };

    // 通用示例提醒。
    public static List<Reminder> DefaultReminders() => new()
    {
        new Reminder { Time = "10:00", Days = new() { 1, 2, 3, 4, 5 }, Message = "起来活动一下、喝口水~（示例提醒）" },
        new Reminder { Time = "12:30", Message = "午休时间到（示例）" },
        new Reminder { Time = "15:30", Days = new() { 1, 2, 3, 4, 5 }, Message = "吃口水果，补充点维生素（示例·可开语音播报）", Speak = true },
        new Reminder { Time = "18:00", Days = new() { 1, 2, 3, 4, 5 }, Message = "下班啦，收拾一下桌面（示例）" },
        new Reminder { Time = "23:00", Message = "该睡觉了（示例）" },
    };
}
