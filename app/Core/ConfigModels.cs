using Clockwork.I18n;

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
    // 默认 100ms：多数动作（开程序后发按键、切窗口后发文本）需要一点缓冲；0 会打空。
    // 只影响新建对象——盘上既有步骤都带显式 delayMs（JsonOptions 不忽略默认值），读回原样。
    public int DelayMs { get; set; } = 100;
    // app
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public bool Elevated { get; set; }
    // 默认开：「启动程序」步骤的常见意图是「让它到前面来」，不是「再开一个」——尤其开机清单里，
    // 手动开过的程序会被清单又开一份。目标是 URL/.lnk/.ps1/文档时 TargetProcessName 返回空串
    // （见 LaunchTarget），本选项自动空转、照常启动，故打开它对这些步骤无副作用。
    // 只影响新建步骤：盘上既有步骤都带显式 activateIfRunning（JsonOptions 不忽略默认值），读回原样。
    public bool ActivateIfRunning { get; set; } = true;
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
    // 「仅 N 点后」：与「仅 N 点前」对称，两者是 AND（同时开 = 交集，如 09:00 后且 18:00 前 = 上班时段）。
    // 有意不做「或」：18:00 后或 08:00 前这种跨午夜窗口写成两条步骤更好懂，不值得让一个复选框带两种语义。
    public bool OnlyAfter { get; set; }
    public int AfterHour { get; set; } = 18;
    public int AfterMinute { get; set; }
    // 仅在这些星期(ISO 1..7)开机启动；空=每天
    public List<int> Days { get; set; } = new();
    // 环境条件（都是「空=不限」）。星期/时刻只看钟表，这三条看的是机器此刻的状态：
    //   IfProcess + IfProcessMode —— 该进程在跑 / 没跑（进程名，裸名即可）
    //   IfPower                   —— "ac"=仅接电源、"battery"=仅用电池
    //   IfPathExists              —— 该文件 / 文件夹存在时才执行（U 盘挂上了没、报告导出了没）
    public string IfProcess { get; set; } = "";
    public string IfProcessMode { get; set; } = "";   // ""=不限 | running | notRunning
    public string IfPower { get; set; } = "";         // ""=不限 | ac | battery
    public string IfPathExists { get; set; } = "";
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
    // message 步骤的呈现方式：""=沿用旧推导（Confirm/OnYes 决定弹是否框还是确定框），"card"=右下角卡片、不拦路。
    // 空是刻意的默认：盘上老步骤没有这个字段，读回即空 → 行为与加本字段之前逐字一致。
    public string Present { get; set; } = "";
    // 卡片形态的自动关闭秒数（0=常驻到点击），与提醒的 PopupTimeoutSeconds 同语义。
    // 默认 5 而非 0：动作组里的进度提示常驻会堆满右下角，而提醒那边 0=常驻是因为它是「必须被看到」的投递。
    public int PopupSeconds { get; set; } = 5;
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
    // 到点时先响一声系统提示音（卡片与弹窗两种形态都响；静默组不响——「静默」的字面意思）。
    // 默认关：升级不该让所有存量提醒突然开始出声。内置样例开着，新用户一上手就是有声的。
    // 卡片不抢焦点、不置顶，人在看别的屏幕就是错过——朗读能补，但那是为了"听清内容"，
    // 而这里要的只是"抬头看一眼"。用 SystemSounds 而非自带音频：不增体积，且跟随用户的系统声音方案。
    public bool Sound { get; set; }
    // 托盘「快速提醒」建出来的一次性条目：响完即从配置里删掉，而不是像普通「仅一次」那样取消勾选留行。
    // 不删的话，用几周就在定时任务列表里攒一堆"25 分钟到了"的死行，得手动清。
    public bool Temporary { get; set; }
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
    // 循环运行：>0 则本条到点后每隔 N 分钟再跑一轮（确认不终止——与「催促」的区别），直到 intervalUntil（空=当天 23:59）。
    public int IntervalMinutes { get; set; }
    public string IntervalUntil { get; set; } = "";
    // 周期=once 时的目标日期（yyyy-MM-dd，空=今天）。触发完成后由 App 自动取消勾选（条目保留）。
    public string OnceDate { get; set; } = "";
    // 事件触发（Trigger ∈ EventTrigger.All）专用参数：
    //   idle       —— 连续无键鼠操作满 IdleMinutes 分钟触发一次，人回来即复位（一次离开只触发一次）
    //   lowBattery —— 电量跌到 BatteryPercent 以下触发一次，充回阈值以上才复位
    // 其余事件（解锁/锁屏/唤醒/插拔电源）没有参数，两个字段留默认即可。
    public int IdleMinutes { get; set; } = 10;
    public int BatteryPercent { get; set; } = 20;
}

public sealed class ActionGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "";   // 全局热键（如 "Ctrl+Alt+F"），空=不绑定；随时一键运行本组
    public int Repeat { get; set; } = 1;       // 整组重复轮数（每次被触发时内部跑几轮）；与 group 引用步骤的 Repeat 相乘
    public int RepeatDelayMs { get; set; }     // 每轮之间间隔
    // 是否在托盘菜单里列一行。新建的组默认 false——组一多托盘就被撑成长条，而多数组是靠热键 / 提醒 /
    // 被别的组引用来触发的，不需要占一行；隐藏的组仍可从主窗口「运行」按钮跑，不会变成死组。
    // 可空是为了区分「老配置没有这个字段」与「显式关掉」：null 由 ConfigStore.Normalize 补成 true，
    // 否则升级后老用户托盘里的组会一起消失，看起来像功能坏了。
    public bool? ShowInTray { get; set; }
    public List<LaunchStep> Steps { get; set; } = new();

    // 运行快照：浅拷贝步骤列表（步骤对象共享，字段级并发读写无害），后台枚举不受 UI 增删干扰。
    public ActionGroup SnapshotForRun() => new() { Id = Id, Name = Name, Enabled = Enabled, Hotkey = Hotkey, Repeat = Repeat, RepeatDelayMs = RepeatDelayMs, ShowInTray = ShowInTray, Steps = new List<LaunchStep>(Steps) };
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
        ActionGroups = DefaultActionGroups(),
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

    // 首次使用的示例清单：按「一个真实的早晨」排序，而不是按功能覆盖率排列——
    // 先静音、开浏览器、开常用网站、把聊天软件最小化挂后台，最后 Win+D 清屏收尾。
    // 全部默认不勾选：样例是照着改的模板，不该在用户还没看过一眼时就替他动电脑。
    // 文案经 resx 本地化，与 ActionGroupTemplates 同口径；进程名/路径是全球通用的字面量，不本地化。
    public static List<LaunchStep> DefaultLaunchSteps() => new()
    {
        new LaunchStep { Kind = "volume", Label = Strings.Get("Smp_Mute"), Action = "mute", Enabled = false },
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenApp"), Target = "msedge.exe", Enabled = false },
        // 条件执行的演示放这条：「工作日才打开工作网站」是自证的，比原来的「仅 8 点前静音」好懂。
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenSite"), Target = "https://github.com", DelayMs = 800, Days = new() { 1, 2, 3, 4, 5 }, Enabled = false },
        // windowStyle 一步挂后台，比「开完再用窗口动作最小化」更贴近真实做法。
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenChat"), Target = "Slack.exe", WindowStyle = "minimized", Enabled = false },
        // 放最后才成立：前面开了 4 个东西，这一下是清屏收尾。
        new LaunchStep { Kind = "keys", Label = Strings.Get("Smp_ShowDesktop"), Combo = "Win+D", Enabled = false },
    };

    // 通用示例提醒：工作日两条（补水 / 收工，后者带语音）、每天一条（睡前）、每月一条（账单）。
    // 每月那条是唯一演示「按月」周期的样例——原来三条全是每天/工作日。同样默认不启用。
    public static List<Reminder> DefaultReminders() => new()
    {
        // 样例一律开提示音：卡片不抢焦点、不置顶，静默弹出等于没弹——新用户第一次见到提醒就该听见它。
        // 模型默认仍是关（Reminder.Sound=false），升级的存量提醒不会突然集体出声。
        new Reminder { Time = "10:00", Days = new() { 1, 2, 3, 4, 5 }, Message = Strings.Get("Smp_RemWater"), Sound = true, Enabled = false },
        new Reminder { Time = "17:30", Days = new() { 1, 2, 3, 4, 5 }, Message = Strings.Get("Smp_RemWrapUp"), Speak = true, Sound = true, Enabled = false },
        new Reminder { Time = "23:00", Message = Strings.Get("Smp_RemSleep"), Sound = true, Enabled = false },
        new Reminder { Time = "09:00", RecurType = "monthly", MonthlyDay = 1, Message = Strings.Get("Smp_RemBills"), Sound = true, Enabled = false },
    };

    // 首次预置的动作组：直接从模板里挑，避免同一套步骤在两处各写一份、日后漂移。
    // 挑「离开一下」（零配置、任何机器都能跑）与「收工·下班」（最有代表性，含确认闸门）。
    // 启用但不带热键——组不会自己跑，禁用只会让托盘里多两条灰的；而开箱占用全局组合键太越界。
    public static List<ActionGroup> DefaultActionGroups()
    {
        var all = ActionGroupTemplates.All();
        var names = new[] { Strings.Get("Tpl_Away"), Strings.Get("Tpl_EndOfDay") };
        var picked = names.Select(n => all.FirstOrDefault(g => g.Name == n))
                          .Where(g => g != null).Select(g => g!)
                          .ToList();
        // 显式进托盘：首启时没有任何提醒/热键指向它们，托盘是唯一顺手的入口。
        // 显式写值（而非留 null 靠 Normalize 补）→ 首份配置就是规范形，读回不会再触发一次写盘。
        foreach (var g in picked) g.ShowInTray = true;
        return picked;
    }
}
