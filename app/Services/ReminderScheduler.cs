using System.IO;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Microsoft.Win32;

namespace Clockwork.Services;

// 提醒引擎的运行时外壳：计时器 tick、触发/补火/预览、事件触发线（会话/电源/显示/网络/USB）、
// 环境轮询（idle/busy/lowBattery）、勿扰、快速提醒、耐久运行态。
// 自 App 拆出（纯结构重构，重入闸/嵌套消息循环/单调 tick 等语义一字未动）。
// 出站接线由 App 装配：列表行的增删刷新走主窗 VM（Rows 与 Models 是两份平行集合，
// 绕开 VM 直接动 _config.Reminders 会造成错位——见 FireAndAdvance/QuickReminder 处），
// 显示器变更后的钩子重挂走手势域。
internal sealed class ReminderScheduler
{
    private readonly Func<RootConfig> _cfg;
    private readonly Func<string> _cfgDir;
    private readonly Action _saveConfig;
    private readonly AppDiagnostics _diag;
    private readonly RunOrchestrator _runner;

    public ReminderScheduler(Func<RootConfig> cfg, Func<string> cfgDir, Action saveConfig,
                             AppDiagnostics diag, RunOrchestrator runner)
    {
        _cfg = cfg; _cfgDir = cfgDir; _saveConfig = saveConfig; _diag = diag; _runner = runner;
    }

    // App 装配的出站接线（BuildShell 后才有主窗；显示器事件要等钩子域就位）。
    public Action<Reminder>? AddReminderRow;
    public Action<Reminder>? RemoveReminderRow;
    public Action? RefreshReminderRows;
    public Action? OnDisplayChangedRehook;      // ApplyMouseHook：主屏变了要按新宽度重建手势门槛

    private RootConfig _config => _cfg();
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                           int durationMs = -1, string? key = null, bool log = true)
        => _diag.ShowToast(title, message, level, durationMs, key, log);
    private void WarnToast(string msg) => _diag.WarnToast(msg);
    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);
    private static Dispatcher Disp => System.Windows.Application.Current.Dispatcher;

    private string _statePath = "";   // clockwork.state.json：提醒耐久运行态
    private readonly Dictionary<string, ReminderState> _reminderStates = new();
    private HashSet<string> _startupReminderIds = new();   // 启动那刻已存在的提醒 id：只有它们才允许「错过必补」（排除中途新建的）
    private DateTime _startTime;
    private int _uptimeAtLaunch;
    private bool _reminderTickBusy;   // 防重入：弹窗模态消息循环期间计时器再触发不叠窗
    private readonly Random _rng = new();
    private DispatcherTimer? _reminderTimer;

    // 提醒运行态落盘路径 + 载入上次的耐久态（上次触发日期/稍后到点）。重启后不再重复弹当天已弹过的。
    public void LoadState()
    {
        _statePath = Path.Combine(_cfgDir(), "clockwork.state.json");
        foreach (var kv in ReminderStateStore.Load(_statePath)) _reminderStates[kv.Key] = kv.Value;
        // 载入时顺手清掉过期(早于今天)的稍后，别让陈旧记录长期留在盘里（Decide 也有运行期兜底）。
        // 「错过必补」且启用的提醒例外：保留陈旧稍后交给 Decide 补发一次（跨天未应答不无声吞掉），
        // 与 Decide 的过期分支同一口径。禁用的照清——Decide 对禁用项直接 none，留着只会烂在盘里。
        // 事件型也照清（与 Decide 的事件守卫同口径）：残留的「错过必补」不该让昨晚的稍后在今早诈尸。
        bool cleaned = false;
        foreach (var kv in _reminderStates)
            if (kv.Value.SnoozeUntil is DateTime su && su.Date < DateTime.Now.Date)
            {
                var owner = _config.Reminders.FirstOrDefault(x => x.Id == kv.Key);
                if (owner is { Enabled: true, CatchUpIfMissed: true } && !ReminderEvent.IsEvent(owner.Trigger)) continue;
                kv.Value.SnoozeUntil = null; cleaned = true;
            }
        // 陈旧的「今天不再」同理清掉。它是耐久内容（Save 的跳空判断认它），不扫的话一月里跳过一次的提醒
        // 会在 clockwork.state.json 里留一行到天荒地老，且每次耐久写盘都跟着重新序列化一遍。
        foreach (var kv in _reminderStates)
            if (kv.Value.SkippedDate.Length > 0 && string.CompareOrdinal(kv.Value.SkippedDate, ReminderEngine.DateKey(DateTime.Now)) < 0)
            { kv.Value.SkippedDate = ""; cleaned = true; }
        if (cleaned) ReminderStateStore.Save(_statePath, _reminderStates);
        // 过期的「快速提醒」清干净。它靠触发后自删收尾，可机器要是整天关着、错过了那个日期，
        // once + 已过的 onceDate 就再也触发不了，于是永远删不掉——一条谁都不认识的死行留在定时任务里。
        // 必须赶在建 MainWindow（列表 VM 由 _config.Reminders 建行）之前做，否则又得走 VM 删。
        _config.Reminders.RemoveAll(x => x.Temporary && string.CompareOrdinal(x.OnceDate, ReminderEngine.DateKey(DateTime.Now)) < 0);
        // 有意不在这里 SaveConfig：它的失败路径要弹 toast=建窗口，而本方法跑在 ApplyUiCulture 之前，
        // 那里的 RTL 元数据覆盖硬性要求「建任何窗口之前」，一旦被抢跑就抛异常、整个启动流程断在半路
        //（ShutdownMode.OnExplicitShutdown 下进程还活着，却没有托盘也没有窗口）。
        // 从内存里摘掉就够了：列表 VM 稍后按 _config 建行，看不到这些死行；盘上那几行等下一次
        // 任何配置改动顺手带走，带不走也只是下次启动再清一遍，无害。
        _startupReminderIds = new HashSet<string>(_config.Reminders.Select(x => x.Id));
    }

    // 计时器与系统事件订阅一起开/关。启动时刻与开机分钟数在这里记（供「登录时」提醒门控）。
    public void Start()
    {
        _startTime = DateTime.Now;
        _uptimeAtLaunch = SystemInfo.UptimeMinutes();
        StartReminderTimer();
        WireSystemEvents();
    }

    public void Stop()
    {
        _reminderTimer?.Stop();
        UnwireSystemEvents();
    }

    // —— 提醒计时器 ——
    private void StartReminderTimer()
    {
        int tick = _config.Settings.TickSeconds;
        if (tick < 5) tick = 30;
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(tick) };
        // 鼠标钩子的死活不在这儿查：那是半秒一次的光标巡检 + 自愈的活
        //（30 秒粒度判「光标动没动」会把看屏幕的人误判成钩子死了，见 GestureCoordinator.CursorWatchTick 的实测记录）。
        _reminderTimer.Tick += (s, e) => ReminderTick();
        _reminderTimer.Start();
    }

    private void ReminderTick()
    {
        // 弹窗 ShowDialog 是 UI 线程的嵌套消息循环，其间 DispatcherTimer 仍在走。无守卫会重入本方法、
        // 在已有模态弹窗上再叠一个。首个 tick 处理完（含所有到点提醒依次弹完）前，后续 tick 直接跳过。
        if (_reminderTickBusy) return;
        // 勿扰生效：本 tick 不触发任何东西（含静默组），到期自动恢复。但基线要照常刷——
        // 不刷的话，勿扰期间拔的电源会在勿扰结束后的第一个 tick 被当成「刚拔」补弹一次，
        // 与订阅类事件（锁屏/解锁/唤醒，勿扰期间直接不算）行为相反。勿扰的语义只有一条：期间发生的事件不算数。
        if (DndRemaining != null) { PollEnvironmentTriggers(DateTime.Now, fire: false); return; }
        _reminderTickBusy = true;
        try
        {
            var now = DateTime.Now;
            bool durableChanged = false;
            // 清理孤儿运行态：已删除/已改 id 的提醒不再留状态（防长驻累积，也防按 id 串状态）。
            if (_reminderStates.Count > 0)
            {
                var live = new HashSet<string>(_config.Reminders.Select(x => x.Id));
                foreach (var dead in _reminderStates.Keys.Where(k => !live.Contains(k)).ToList())
                { _reminderStates.Remove(dead); durableChanged = true; }
                // 两个「本轮已触发」集合同样按 id 记事，一并跟着清，别让删掉的提醒在集合里长住。
                _idleFired.RemoveWhere(k => !live.Contains(k));
                _busyFired.RemoveWhere(k => !live.Contains(k));
                _lowBatteryFired.RemoveWhere(k => !live.Contains(k));
            }
            // 空闲 / 电源两类事件没有系统通知可订，只能轮询——正好搭这班车。
            durableChanged |= PollEnvironmentTriggers(now);
            foreach (var r in _config.Reminders.ToList())
            {
                if (!_reminderStates.TryGetValue(r.Id, out var st)) { st = new ReminderState(); _reminderStates[r.Id] = st; }
                string firedBefore = st.LastFiredDate;
                var d = ReminderEngine.Decide(r, now, _startTime, st, _uptimeAtLaunch, _startupReminderIds.Contains(r.Id));
                if (d.Action == "arm" && d.Base is DateTime b)
                {
                    // 到点后延迟：'arm' 交 ArmAt 算 pendingFireAt（与事件触发共用同一份口径）。
                    // 固定延时不设上限（用户可能有意配多天错峰）。
                    ArmAt(r, st, b);
                }
                else if (d.Action == "fire")
                {
                    // 仅"时间型首触发"(本次 Decide 刚把 LastFiredDate 置为今天)在弹模态前先落盘，防被杀/断电后次日重复弹。
                    // 稍后/重复型触发不预存——它们清掉的 SnoozeUntil/NextRepeatAt 若在弹窗时被杀，宁可从盘上旧值恢复重弹也别丢。
                    // durable：此处的意义就是「先写成盘再弹窗」，不能走失败转后台的快路径（后台没落地就被杀等于没存）。
                    if (st.LastFiredDate != firedBefore) ReminderStateStore.Save(_statePath, _reminderStates, durable: true);
                    FireAndAdvance(r, st);
                    durableChanged = true;   // 稍后/重复又改了状态 → 循环末再存一次
                }
            }
            // 模态弹窗挂着时攒下的会话/电源事件（FireReminderEvent 忙时排队）在这儿兜底补发——
            // 事件自己的出口通常先清掉队列，这里保证最晚一个 tick 内一定补上。
            durableChanged |= DrainPendingEvents();
            if (durableChanged) ReminderStateStore.Save(_statePath, _reminderStates);
        }
        finally { _reminderTickBusy = false; }
    }

    // 触发一条提醒并推进它的运行态。计时器与事件触发共用同一份——事件那条路要是不推进状态，
    // 「稍后」和「没确认就催」在事件型任务上会静默失效（SnoozeUntil 落了盘却没人来看）。
    private void FireAndAdvance(Reminder r, ReminderState st)
    {
        if (DeferForFullScreen(r, st)) return;
        var (action, snooze) = FireReminder(r);
        // 排下一步（稍后/重复）必须用弹窗返回后的时刻，不能用触发前的 now——弹窗是模态的，
        // 自动关闭设得长（如 30 分钟）时 now 已陈旧半小时，「稍后 10 分钟」会算出一个已经过去的
        // SnoozeUntil，下个 tick 立即重弹、再挂 30 分钟，成了永久模态循环；重复催促同理会背靠背连弹。
        var after = DateTime.Now;
        if (snooze is int m)
        {
            // 自动稍后有上限（人不在，别对着空屏幕弹一天）；手点的没有（人的明确决定）。
            // 到顶降级：链结束，改挂常驻卡片等人回来——不再抢焦点，提醒也不静默消失。
            // 卡片没有「是/否」按钮：降级只保留「看到」，onYes 动作不随卡片走。
            if (action == "autosnooze")
            {
                if (!ReminderEngine.AutoSnooze(st, after, m))
                {
                    // 降级成常驻卡片。Warn 级不是为了吓人，是为了别骗人：FireReminder 刚按同一个 key 记了一条
                    // 警示级「无人应答」，而 NotificationLog.Add 会先删同 key 的旧条目——这里用 Info 会把那条
                    // 警示改写成普通回执，托盘「最近通知」里最坏的情况反倒看着最正常。
                    ShowToast(Strings.Get("Tray_ReminderTitle"), r.Message, Views.ToastLevel.Warn, 0, key: ReminderToastKey(r));
                    // 催促链到此结束，但「循环运行」是另一条链，必须在这个出口照常排下一轮——
                    // 不排的话，配了循环的提醒一降级就把当天余下所有轮次静默丢掉，而卡片没有按钮，
                    // 用户回来也无从把它接回去（与 UpdateAfterFire 各出口都排下一轮同一条口径）。
                    ReminderEngine.UpdateAfterFire(r, after, "ok", st);
                }
                // ponytail: 降级卡片是会话态，重启即消失（LastFiredDate 已记今天，不会重弹）；「仅一次」的
                // 提醒还会在下面被自动取消勾选——即卡片被挤掉/重启后，它在界面上不留任何痕迹。
                // 要跨重启得给 ReminderState 再落一个字段并在启动时重挂卡片，等真有人踩到再说。
            }
            else ReminderEngine.Snooze(r, st, after, m);   // 手点=人在场，无人应答计数由引擎清零
        }
        else ReminderEngine.UpdateAfterFire(r, after, action, st);
        // 「仅一次」触发完成（催促/稍后链都结束）→ 自动取消勾选：条目保留（想再用改个日期重新勾上），
        // 用完即焚会让误设时间没得救。时机必须在链结束后——立刻停用会被 Decide 的 !Enabled 早退掐死在途链。
        // 例外是托盘「快速提醒」（Temporary）：那是个当场用完的计时器，不是配置，留行只会堆垃圾——整条删掉。
        // 状态字典里的那份由 ReminderTick 的孤儿清理顺手回收（按 id 比对存活提醒），这儿不必手动删。
        if (ReminderEngine.ShouldDisableAfterOnce(r, st))
        {
            // 删除必须走列表 VM：ListVm.Models 就是 _config.Reminders 这份 List 本身，而 Rows 是与它平行的
            // 另一份集合，RefreshReminderRows 只会逐行 Refresh()、加不了也删不掉行。直接动 _config.Reminders
            // 会让两者错位一位，之后每一行都映射到相邻的那条提醒（编辑/删除/预览全部张冠李戴），
            // 选中最后一行还会让 ListVm.Selected 越界抛异常。
            if (r.Temporary) RemoveReminderRow?.Invoke(r);   // VM 内部会存盘
            else { r.Enabled = false; _saveConfig(); }
            RefreshReminderRows?.Invoke();
        }
    }

    /// <summary>全屏 / 演示中让路时，往后推多久再试。见 <see cref="DeferForFullScreen"/>。</summary>
    private const int FullScreenDeferMinutes = 5;

    // 全屏游戏 / 投影演示中：不弹，改挂一次「稍后」，让路给屏幕上正在发生的事。
    // 返回 true = 本次已让路，调用方就此收手（状态已推进，不能再走正常触发路径）。
    //
    // 走手点稍后（Snooze）而不是自动稍后（AutoSnooze），差别是实打实的：后者带 MaxAutoSnoozes 轮上限，
    // 到顶就降级成常驻卡片——那是为「弹了没人理」设计的退路，用来数「我们主动没弹」的次数是错的，
    // 一场两小时的游戏会把额度耗光，然后在全屏正中央拍一张卡片，恰好是本功能要避免的那件事。
    // 手点语义没有轮数上限：全屏挂着就一直往后推，出了全屏第一个 tick 就正常弹，这才是让路该有的样子。
    //
    // 三类刻意不让路的：
    //   · 静默动作组 —— 它根本不上屏，没有可打断的东西。为它让路等于凭空推迟一次自动化。
    //   · 托盘「快速提醒」（Temporary）—— 你几分钟前亲手设的那个计时器。边打游戏边煮面正是它的用法，
    //     "你说 5 分钟" 的承诺高于"别打断全屏"的礼貌。
    //   · 探测失败 —— UserNotificationState.Busy() 取不到时返回 false，照常弹（见那边的注释）。
    private bool DeferForFullScreen(Reminder r, ReminderState st)
    {
        if (r.Temporary || !string.IsNullOrWhiteSpace(r.SilentGroupId)) return false;
        if (!Native.UserNotificationState.Busy()) return false;
        ReminderEngine.Snooze(r, st, DateTime.Now, FullScreenDeferMinutes);
        // 有意不发 toast：让路的全部意义就是此刻别在屏幕上出现，为此弹一张「已推迟」的卡片是自相矛盾。
        // 用户看到的是它在出全屏之后正常响——而不是一条解释自己为什么迟到的通知。
        return true;
    }

    // 托盘「快速提醒」：N 分钟后响一次，响完自删。复用「仅一次」那套机制，不新开一条计时路径。
    //
    // 精度：直接预置 PendingFireAt = 现在+N（秒级），让计时器的 pending 分支引爆，误差 ≤ 一个 tick(默认 30s)
    // 且绝不提前。不这么做就得走「到点判定 → arm → 下一跳才 fire」，那条路要吃两次 tick，再叠上
    // Time 只有 HH:mm 精度带来的取整——5 分钟的计时器实测能拖到近 7 分钟，对一个计时器是不能接受的误差。
    // Time 字段仍照常填（向上取整，绝不提前），它是列表和气泡上显示的那个时刻，也是 PendingFireAt
    // 这个会话态被重启抹掉之后的兜底路径。
    public void QuickReminder(int minutes)
    {
        var at = DateTime.Now.AddMinutes(minutes).AddSeconds(59);
        var r = new Reminder
        {
            Trigger = "time",
            Time = at.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            RecurType = "once",
            OnceDate = ReminderEngine.DateKey(at),   // 跨午夜时自然落到明天
            Message = Lf("Quick_Done", minutes),
            Sound = true,             // 计时器无声等于没有计时器
            PopupTimeoutSeconds = 0,  // 卡片形态下 0=常驻到点掉，别让人错过自己刚设的那一下
            GraceMinutes = 5,
            CatchUpIfMissed = true,   // 那一分钟恰好在勿扰/睡眠里错过时补响，而不是变成一条永不触发的死行
            Temporary = true,
        };
        // 必须经 VM 增加，理由同 FireAndAdvance 里的删除：Rows 与 Models 是两份平行集合。
        // 直接 _config.Reminders.Add 的话这条根本不会出现在列表里——用户想反悔都没有入口，
        // 而文档写着「响过就自己从列表里删掉」，等于承诺了一个它从没进过的列表。
        // 直接调、不做 null 兜底：BuildShell 先建 _main 再建托盘，而本方法只能由托盘菜单进来，
        // _main 必然已在；写成 null 条件调用只会造一个谁也测不到的分支，还让"气泡说已设、其实没加"成为可能。
        AddReminderRow!(r);   // VM 内部会存盘
        // 「错过必补」只对启动时就存在的提醒生效（防「刚建一条早时刻的就立刻补弹」）。快速提醒的时刻
        // 永远在未来，那道防线对它没有意义，却会让上面这个 CatchUpIfMissed 完全失效——补登记一下。
        _startupReminderIds.Add(r.Id);
        // 预置引爆时刻（见方法头的精度说明）。PendingForDate 要跟着记，否则跨午夜引爆时
        // LastFiredDate 会记成次日，与 ArmAt 同一个坑。
        var st = StateOf(r);
        st.PendingFireAt = DateTime.Now.AddMinutes(minutes);
        st.PendingForDate = ReminderEngine.DateKey(st.PendingFireAt.Value);
        // 勿扰期间 ReminderTick 整轮直接返回，这条也不例外——气泡必须说实话，不能承诺一个做不到的时刻。
        bool dnd = DndRemaining != null;
        ShowToast("Clockwork", dnd ? Lf("Toast_QuickSetDnd", minutes) : Lf("Toast_QuickSet", minutes, r.Time),
                  dnd ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
    }

    // 「今天不再提醒」：当天剩余的这条全部作废（含在途的催促/循环/稍后），明天照常。
    // 比取消勾选安全——取消勾选没有到期日，忘了打开就是一条永久静默失效的提醒。
    //
    // 做成开关：再点一次即撤销。按钮就在「运行」正上方、单击即生效、没有确认框，误点必然发生；
    // 而列表行看不出任何差别，编辑保存也撤不掉（迁移会把 SkippedDate 原样带到新 id），
    // 没有这个反向操作就只剩「删掉重建」和「手改状态文件」两条路。
    public void SkipReminderToday(Reminder r)
    {
        var st = StateOf(r);
        var now = DateTime.Now;
        bool undo = st.SkippedDate == ReminderEngine.DateKey(now);
        if (undo) st.SkippedDate = "";
        else
        {
            ReminderEngine.SkipToday(st, now);
            // 「仅一次」且这一次就是今天 → 跳过之后它永远不会再触发（周期过滤此后一直为 false），
            // 而 ShouldDisableAfterOnce 只在触发路径上判定，跳过永远走不到那儿。不在这里收尾的话，
            // 列表里就留下一条勾着的、和活提醒长得一模一样、却再也不会响的死行。
            if (r.RecurType == "once" && ReminderEvent.UsesRecurrence(r.Trigger)
                && ReminderEngine.IsRecurrenceDueToday(r, now))
            {
                if (r.Temporary) RemoveReminderRow!(r);   // 快速提醒被跳过=不要了，整条删掉
                else { r.Enabled = false; _saveConfig(); RefreshReminderRows?.Invoke(); }
            }
        }
        ReminderStateStore.Save(_statePath, _reminderStates, durable: true);
        RefreshReminderRows?.Invoke();   // 跳过状态显示在时间列上，改了就得让那一行重画
        ShowToast("Clockwork", Lf(undo ? "Toast_SkipTodayOff" : "Toast_SkipToday",
                                  ReminderDisplay.TextSummary(r, _config.ActionGroups)),
                  Views.ToastLevel.Info, key: "skip:" + r.Id);   // TextSummary 自己已经截断，别再套一层
    }

    // —— 事件触发（空闲 / 锁屏 / 解锁 / 唤醒 / 插拔电源 / 低电量）——
    // 会话与电源事件由 Windows 广播，订阅即得；空闲与电量没有通知可订，搭提醒计时器轮询。
    private readonly HashSet<string> _idleFired = new();         // 本轮离开已触发过的提醒 id（人回来即清）
    private readonly HashSet<string> _busyFired = new();          // 本轮连续使用已触发过的（离开满一分钟即清）
    private readonly HashSet<string> _lowBatteryFired = new();   // 本轮低电量已触发过的（充回阈值以上即清）
    private bool? _lastOnAc;                                     // 上次读到的电源状态；null=还没读过（首轮只记基线）
    // 当前这轮连续使用的起点；null=正在离开中（或还没起算）。
    // **单调 tick，不是墙钟。** 它量的是「这一段连续用了多久」，而墙钟会跳：夏令时前移、
    // 一次 NTP 校正，都会让差值凭空多出一个小时——用户刚坐下，所有久坐提醒同时炸出来；
    // 往回跳则差值为负、`(int)` 向零截断，久坐提醒被静默压制最多一小时，一声不响。
    // 它的镜像（空闲）本来就是单调的：Native.IdleTime.Minutes() 走的是 Environment.TickCount。
    // 两个数放在同一个判据里比较，就必须来自同一种钟（LongPressGate 里那条「单调递增且不受
    // 改系统时间影响」写的是同一件事）。
    private long? _busySinceTick;
    private string _lastEvent = "";
    private DateTime _lastEventAt = DateTime.MinValue;

    private void WireSystemEvents()
    {
        // 订阅这些是纯白拿：Windows 本来就在广播，不订就只能靠轮询猜锁屏、唤醒、插显示器。
        // 回调在各自的通知线程上来（SystemEvents 有自己的线程，NetworkChange 走线程池），
        // FireReminderEvent 内部统一切回 UI 线程，故这里不必各自 Dispatcher。
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch { }   // 无交互式桌面等边角场景下会抛；事件触发失效即可，不该拖垮启动
    }

    public void UnwireSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
        catch { }
    }

    // 插拔显示器、改分辨率、切换投影模式，都从这一个通知来（对应 SystemEvents.DisplaySettingsChanged）。
    // 一次插拔 Windows 常连发好几条（先认出设备、再重排桌面、DPI 再定一次），全靠 FireReminderEvent
    // 门口那个 3 秒同名去抖收敛成一次——这个事件是去抖最吃紧的一个，别把那道闸拆了。
    //
    // 只给一个「变了」，不区分接上还是拔掉：要真正分清得自己缓存上一轮的显示器集合并做差集，
    // 而扩展屏路由的两种写法（接上→扩展模式+开那几个窗口 / 拔掉→仅电脑）在动作组里用「显示模式」
    // 系统命令加进程条件就能各自表达。真要「仅接上时」再说，别为它先背一份显示器拓扑快照。
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        FireReminderEvent("display");
        // 手势的最小笔画长度是按主屏宽度算的，而屏幕刚变了。重装钩子自己会比
        // 「算出来的值和正在用的那个一样吗」，一样就什么都不做，所以这里无条件叫一次是安全的。
        OnDisplayChangedRehook?.Invoke();
    }

    // 网络通断。NetworkAvailabilityChanged 给的是「这台机器还有没有可用网络」这个粗粒度事实，
    // 正好对上「重连 VPN / 重挂网络盘」这类需求——它们要等的就是"网络回来了"，而不是某块网卡的状态。
    //
    // 刻意不做「限定网络名 / SSID」：那要引 WinRT 的 NetworkInformation（家里 wifi 一套、公司一套
    // 是它的招牌场景），而本程序的进程条件 + 路径存在条件已经能表达大部分「在哪台机器上」的区分。
    // 真有人要按 SSID 分流，那时再加一个条件字段，比现在先背一个 WinRT 依赖划算。
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        => FireReminderEvent(e.IsAvailable ? "netUp" : "netDown");

    // 只认 SessionLock/SessionUnlock 这两个字面意义上的「锁屏 / 解锁」。
    // 切换用户、远程连断（Console*/Remote*）刻意不并进来：那是另一件事，混进来会让「解锁时」在没人解锁时也跑。
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) FireReminderEvent("lock");
        else if (e.Reason == SessionSwitchReason.SessionUnlock) FireReminderEvent("unlock");
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) FireReminderEvent("resume");
    }

    // 忙时（模态提醒窗挂着）到达的会话/电源事件排在这儿，忙段一结束就补发。
    // 时间型提醒天然有下个 tick 重试；事件没有——丢一次就永远丢了。「锁屏提醒的弹窗还挂着，
    // 解锁提醒就被吞掉」正是这个功能的招牌场景（解锁打卡）被自己人挡掉。
    private readonly List<string> _pendingEvents = new();

    // OS 事件入口：切到 UI 线程 → 勿扰闸 → 去抖 → 忙则排队，闲则触发并顺带清队。
    // （热键钩子上的 WM_DEVICECHANGE「usb」事件也由 App 接到这里。）
    public void FireReminderEvent(string ev)
    {
        if (!Disp.CheckAccess()) { try { Disp.BeginInvoke(() => FireReminderEvent(ev)); } catch { } return; }
        if (DndRemaining != null) return;    // 勿扰期间事件不触发（与计时器一个待遇），也不排队——没发生就是没发生
        var now = DateTime.Now;
        // 去抖：同一次锁屏/唤醒 Windows 可能连发两三条（唤醒尤其明显，Resume 常与 StatusChange 挨着来）。
        // 3 秒内的同名事件当成同一次；不同事件互不影响（锁屏紧接着唤醒是真的发生了两件事）。
        // 去抖放在排队之前：连发的重复通知该在门口拦掉，而不是在队里叠三份。
        if (_lastEvent == ev && (now - _lastEventAt).TotalSeconds < 3) return;
        _lastEvent = ev; _lastEventAt = now;

        // 忙 = 有模态提醒窗在挂（它的嵌套消息循环里本回调照样进来）。排队等补发，别丢弃——
        // 队内按事件名去重，同名挤在队里补发一次就够。
        if (_reminderTickBusy) { if (!_pendingEvents.Contains(ev)) _pendingEvents.Add(ev); return; }

        _reminderTickBusy = true;
        try
        {
            bool changed = FireEventNow(ev, now);
            changed |= DrainPendingEvents();   // 触发期间又攒下的（弹窗挂着时来的事件）当场补掉
            if (changed) ReminderStateStore.Save(_statePath, _reminderStates);
        }
        finally { _reminderTickBusy = false; }
    }

    // 补发忙时攒下的事件。调用方必须已持有 _reminderTickBusy；返回是否动过运行态。
    // 勿扰开始后剩下的先留在队里（不触发也不丢），勿扰结束后的第一个出口再补——
    // 它们发生在勿扰之前，按「忙时只是延后」的口径处理，和勿扰期间发生的事件（直接不算）不同。
    private bool DrainPendingEvents()
    {
        bool changed = false;
        while (_pendingEvents.Count > 0 && DndRemaining == null)
        {
            var ev = _pendingEvents[0];
            _pendingEvents.RemoveAt(0);
            changed |= FireEventNow(ev, DateTime.Now);
        }
        return changed;
    }

    // 挑出该响应本事件的提醒，逐条触发并推进状态。返回是否动过运行态（调用方负责落盘）。
    // 重入闸与勿扰由调用方把关——轮询那条路已经在计时器的闸里了，不能在这儿再上一次。
    // 配了「触发延迟」的不当场响，而是武装 PendingFireAt 交给计时器引爆（Decide 的事件门开在 pending
    // 之后正是为此）——否则编辑器收下了延迟、运行时却当场弹，等于静默吞掉一项用户设置。
    private bool FireEventNow(string ev, DateTime now)
    {
        bool changed = false;
        foreach (var r in _config.Reminders.ToList())
        {
            if (!ReminderEvent.ShouldFire(r, ev, now, PeekState(r))) continue;
            FireOrArm(r);
            changed = true;
        }
        return changed;
    }

    // 事件到点的统一出口：配了「触发延迟」的武装 PendingFireAt 交计时器引爆，其余当场响。
    // 订阅类（FireEventNow）与轮询类（空闲/低电量）都走这儿——只改其一的话，
    // 「空闲 10 分钟后再等 5 分钟」这类配置会在轮询路径被静默吞掉，和评审 #5 同一个坑再挖一遍。
    private void FireOrArm(Reminder r)
    {
        var st = StateOf(r);
        if (r.DelaySeconds > 0 || r.RandomDelaySeconds > 0) ArmAt(r, st, DateTime.Now);
        else FireAndAdvance(r, st);
    }

    // 到点后延迟：固定 + 随机（错峰），算出 PendingFireAt。计时器 arm 分支与事件触发共用——
    // 随机上界 +1 处防 int.MaxValue 溢出（否则 _rng.Next 抛异常）；long 累加避免 int 溢出。
    private void ArmAt(Reminder r, ReminderState st, DateTime baseTime)
    {
        int rd = r.RandomDelaySeconds;
        long rand = rd > 0 ? _rng.Next(0, rd == int.MaxValue ? rd : rd + 1) : 0;
        st.PendingFireAt = baseTime.AddSeconds((long)r.DelaySeconds + rand);
        // 与 ReminderEngine 的 arm 出口同口径：记基准日而非引爆日，否则延迟把引爆推过午夜时，
        // LastFiredDate 会记成次日、把次日那次挡掉。事件触发只经这条路，Decide 的 Arm() 管不到。
        st.PendingForDate = ReminderEngine.DateKey(baseTime);
    }

    // 只读查询运行态，查不到给 null。给 ShouldFire 这类纯谓词用——C# 求值实参在前，
    // 写 StateOf(r) 的话每次锁屏/解锁/插拔电源都会给配置里每一条提醒（包括禁用的、按时间触发的）
    // 建一行状态，把「没状态就不建行」这个前提（孤儿清理与 Save 跳空都建立在它上面）打掉。
    // （主窗列表 VM 也拿它判「今天不再」的显示。）
    public ReminderState? PeekState(Reminder r) => _reminderStates.GetValueOrDefault(r.Id);

    private ReminderState StateOf(Reminder r)
    {
        if (!_reminderStates.TryGetValue(r.Id, out var st)) { st = new ReminderState(); _reminderStates[r.Id] = st; }
        return st;
    }

    // 轮询空闲时长与电源状态。计时器默认 30 秒一轮，对这两件事绰绰有余——
    // 「空闲 10 分钟」晚半分钟触发没人在意，而为它单开一个高频计时器纯属浪费。
    // 由 ReminderTick 在重入闸内调用，返回是否动过运行态。
    // fire=false（勿扰期间）：只维护基线（电源在线态、空闲/低电量的「本轮已触发」复位），一律不触发——
    // 勿扰的语义是「期间发生的事件不算数」，基线不跟着走的话，勿扰里拔的电源会在勿扰结束后被误判成「刚拔」。
    private bool PollEnvironmentTriggers(DateTime now, bool fire = true)
    {
        // 没有任何提醒用到轮询类触发（idle/busy/lowBattery/acPlugged/acUnplugged）——本方法的两次
        // 系统调用（读空闲、读电源）白做，整段跳过。判据集中在 ReminderEvent.Polled（有穷尽性测试钉着），
        // 不在这儿手列触发类型，免得加新硬件触发时漏列 → 那条提醒永远不响。
        // 但先把两格跨调用的基线作废：它们只在下面这些分支里被读写，跳过期间不更新就是「陈旧」——
        // 若不清零，用户日后第一次加「久坐 30 分钟」会用一个几小时前的起点当场判成久坐、误响一次。
        // 清成 null 后新提醒的第一次真轮询重新落桩（busyMinutes 从 0 起算、电源首轮只记基线）。
        if (!_config.Reminders.Any(r => ReminderEvent.NeedsPoll(r.Trigger)))
        {
            _busySinceTick = null;
            _lastOnAc = null;
            return false;
        }
        bool changed = false;
        int idle = Native.IdleTime.Minutes();
        if (idle < 1) _idleFired.Clear();   // 人回来了：整体复位，下次离开重新算一轮
        // 连续使用的计时基线，与「本轮已触发」的复位一起维护——它是空闲的镜像，所以两件事必须在同一处算：
        // 离开满一分钟 = 歇过了，streak 归零、下一轮重新起算；还在用则起点保持不动（第一次见到就落桩）。
        // 基线维护刻意在 fire 之外（勿扰期间也照走），与 _lastOnAc 同一条口径：勿扰的语义是「期间发生的
        // 事不算数」，而不是「期间的时间不存在」——不然勿扰结束后会拿一个两小时前的起点当场判成久坐。
        if (idle >= 1) { _busySinceTick = null; _busyFired.Clear(); }
        else _busySinceTick ??= Environment.TickCount64;
        int busyMinutes = _busySinceTick is long bs ? (int)((Environment.TickCount64 - bs) / 60000) : 0;
        var (onAc, pct) = Native.PowerStatus.Read();
        // 首轮只记基线不触发：否则程序一启动就会把「现在接着电源」当成一次刚插上。
        if (fire && _lastOnAc is bool was && was != onAc) changed |= FireEventNow(onAc ? "acPlugged" : "acUnplugged", now);
        _lastOnAc = onAc;

        foreach (var r in _config.Reminders.ToList())
        {
            if (r.Trigger == "idle")
            {
                if (!fire) continue;   // 勿扰中不触发也不置位：空闲跨过勿扰结束仍在持续的，结束后照常响
                if (!ReminderEvent.ShouldFire(r, "idle", now, PeekState(r))) continue;
                if (!ReminderEvent.IdleDue(r, idle, _idleFired.Contains(r.Id))) continue;
                _idleFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
            else if (r.Trigger == "busy")
            {
                // 与 idle 完全对称：勿扰中不触发也不置位，跨过勿扰结束仍在持续的那一轮照常响。
                // （复位不在这儿——它在循环外和 _busySinceTick 一起算，理由同 lowBattery 的复位：
                //   「人歇过了」是个与这条现在该不该响无关的事实。）
                if (!fire) continue;
                if (!ReminderEvent.ShouldFire(r, "busy", now, PeekState(r))) continue;
                if (!ReminderEvent.BusyDue(r, busyMinutes, _busyFired.Contains(r.Id))) continue;
                _busyFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
            else if (r.Trigger == "lowBattery")
            {
                // 复位判定不看 Enabled/星期/勿扰：电量涨回去了就是涨回去了，跟这条现在该不该响无关。
                if (ReminderEvent.LowBatteryReset(r, pct, onAc)) { _lowBatteryFired.Remove(r.Id); continue; }
                if (!fire) continue;
                if (!ReminderEvent.ShouldFire(r, "lowBattery", now, PeekState(r))) continue;
                if (!ReminderEvent.LowBatteryDue(r, pct, onAc, _lowBatteryFired.Contains(r.Id))) continue;
                _lowBatteryFired.Add(r.Id);
                FireOrArm(r);
                changed = true;
            }
        }
        return changed;
    }

    // 触发一条提醒：静默组 / 语音 / 通知 / 弹窗（是-否-稍后）。
    // 返回 (result, snoozeMinutes)：result ∈ yes/no/ok/snooze(手点稍后)/autosnooze(超时自动稍后)/""(超时未确认)。
    // 手点与自动必须分开传到 FireAndAdvance——只有后者计入「连续无人应答」的降级计数。
    // preview=编辑器「预览这条」：被动提醒 toast 固定几秒自动消失（预览是试看，不该常驻堆屏）。
    private (string Action, int? Snooze) FireReminder(Reminder r, bool preview = false)
    {
        if (!string.IsNullOrWhiteSpace(r.SilentGroupId))
        {
            var g = ActionGroupResolver.Resolve(_config.ActionGroups, r.SilentGroupId);
            if (g != null && g.Enabled) _runner.RunGroupAsync(g, unattended: true);   // 提醒到点自己跑的，没人在看
            // 引用的组被删/被禁用时不再静默装作成功——夜间例程停摆却零反馈是最难察觉的故障；警告但仍记已处理（不重弹刷屏）。
            else WarnToast(Lf(g == null ? "Warn_SilentGroupMissing" : "Warn_SilentGroupDisabled", StepHelpers.Ellipsis(r.Message)));
            // 静默组无确认交互，跑一次即完结——返回 "ok"：让 UpdateAfterFire 停掉催促（否则配了 repeatMinutes
            // 会每 N 分钟把整组（可能含静音/关应用/锁屏）重跑），同时排下一轮「循环运行」（intervalMinutes）——
            // 静默任务的周期轮询正是靠这个返回值成立，改动它会悄悄弄断循环。
            return ("ok", null);
        }
        // 提示音先于朗读：一声短提示把头抬起来，随后的朗读才有人在听。两者都开时必须给朗读一段前导——
        // SystemSounds.Play() 是异步的，不传前导的话「叮」会和第一个字同时响，等于两个都没听清。
        // 静默组走不到这里（上面已 return）——「静默」就该是静默的。
        if (r.Sound) ReminderActions.Ding();
        if (r.Speak) ReminderActions.Speak(r.Message, r.Sound ? ReminderActions.SoundLeadInMs : 0);
        bool confirm = r.OnYes != null && r.OnYes.Type != "none";
        // 无动作、非重复 → 走右下角提醒卡片（不置顶抢视线）。时长遵循配置的「自动关闭」（0=常驻到点击）。
        if (!confirm && r.RepeatMinutes <= 0)
        {
            int secs = ReminderEngine.PopupTimeoutSeconds(r);   // 已在源头封顶 24h，secs*1000 不会越界
            int dur = secs > 0 ? secs * 1000 : (preview ? 5000 : 0);   // 预览固定 5s 自动关；真触发 0=常驻
            // 真触发按提醒 id 合并（同一条反复触发只占一张卡、标 ×N，不堆满右下角）；预览不带合并键——
            // 带了会并入同一条提醒还没人读的常驻卡片，并把它改写成 5 秒自动关，等于替用户把未读提醒销掉。
            // 预览也不留痕（log:false）：试看不该在托盘「最近通知」里冒充一次真投递。
            ShowToast(Strings.Get("Tray_ReminderTitle"), r.Message, Views.ToastLevel.Info, dur,
                key: preview ? null : ReminderToastKey(r), log: !preview);
            return ("ok", null);
        }
        // 弹窗路径。弹窗是模态的，其嵌套消息循环期间 _reminderTickBusy 挡住所有其他提醒——
        // 所以弹窗一律有超时（用户没设就兜底 60s），引擎不能没有下车点。
        // 超时（无人应答）的去向由「是否配了重复催促」决定：
        //   配了 → ""（超时未确认，交 UpdateAfterFire 按用户设的节奏续催，受 repeatUntil/MaxRepeats 约束）；
        //   没配 → 自动「稍后 10 分钟」——这类提醒没有任何续催机制，超时记成已处理或未确认都等于静默丢弃。
        // 「未应答」因此落在引擎的持久状态（SnoozeUntil 落盘，重启也不丢，删除提醒后由孤儿清理回收），
        // 而不是落在某个 UI 构件上——卡片会被挤掉/误点/比配置活得久，投递保证不能跟着 UI 的生死走。
        // 唯一的例外在链的末端：连续 MaxAutoSnoozes 轮无人应答后降级成常驻卡片（见 FireAndAdvance）。
        // 那不是把投递保证交给 UI，而是承认「一小时没人理=人不在」，此时继续抢焦点已无收件人；
        // 卡片够撑到人回来，代价（会话态、重启即失、没有是/否按钮）都写在降级那处。
        int psecs = ReminderEngine.PopupTimeoutSeconds(r);
        int timeoutSecs = psecs > 0 ? psecs : ReminderEngine.UnattendedPopupSeconds;
        int? autoSnooze = r.RepeatMinutes > 0 ? null : ReminderEngine.UnattendedSnoozeMinutes;
        var shownAt = DateTime.Now;
        var (act, snooze) = Views.ReminderPopupWindow.Show(r.Message, confirm, timeoutSecs, autoSnooze);
        // 弹窗路径也在托盘「最近通知」留痕——卡片路径由 ShowToast 顺手记，这条路之前没人记，
        // 于是「22:00 到底弹没弹」只能去翻状态文件反推。无人应答（超时未确认/自动稍后）记警示级：
        // 那是「你需要知道」的结果，不是看过就算的回执。预览不留痕，与卡片路径同口径。
        // DurationMs 必须给真值：NotificationEntry 的契约是「重放须忠实还原原卡片的时长」，
        // 留默认 0 等于声明"常驻"，于是回看一条 60 秒的提醒会得到一张永不自动关的卡片。
        if (!preview) _diag.AddNotificationTrace(new NotificationEntry(shownAt, Strings.Get("Tray_ReminderTitle"), r.Message,
            Warn: act is "" or "autosnooze", Key: ReminderToastKey(r), DurationMs: timeoutSecs * 1000));
        if (act == "yes") ReminderActions.RunOnYes(r.OnYes, _config.ActionGroups, g => _runner.RunGroupAsync(g), WarnToast);
        if (act is "snooze" or "autosnooze") return (act, snooze);
        return (act, null);
    }

    private static string ReminderToastKey(Reminder r) => "reminder:" + r.Id;

    // 「预览这条」：立即触发一次。预览不改任何运行状态——FireReminder 的返回值（含超时自动稍后）整个丢弃。
    // 与 tick 共用重入守卫：预览的模态弹窗期间 tick 不再往上叠新提醒窗；反向 tick 正忙时预览静默忽略
    // （用户面前已经有一个提醒模态窗了）。
    public void PreviewReminder(Reminder r)
    {
        if (_reminderTickBusy) return;
        _reminderTickBusy = true;
        try { FireReminder(r, preview: true); }
        finally { _reminderTickBusy = false; }
    }

    // —— 勿扰（暂停提醒）——旧版同款：会话级、不落盘；生效期间提醒 tick 整体跳过（含静默组），
    // 到期自动恢复；期间错过的提醒按宽限/错过必补的正常规则处理。
    private DateTime? _dndUntil;

    public TimeSpan? DndRemaining
    {
        get
        {
            if (_dndUntil is DateTime du)
            {
                var left = du - DateTime.Now;
                if (left > TimeSpan.Zero) return left;
                _dndUntil = null;   // 过期即清，菜单/判定两边都干净
            }
            return null;
        }
    }

    public void PauseReminders(int hours)
    {
        _dndUntil = DateTime.Now.AddHours(hours);
        ShowToast("Clockwork", Lf("Toast_DndOn", hours), Views.ToastLevel.Info);
    }

    /// <summary>安静到今天结束（次日 0 点）。</summary>
    //
    // 按小时的档位（1/2/4/8）盖不住真实场景里最常见的那一句：「我今天不想被提醒」。
    // 而它此前唯一的表达方式是**把每一条提醒挨个取消勾选**，第二天再挨个勾回来——
    // 那既麻烦，又留下一个第二天想不起来复原的状态（表现成「提醒功能坏了」，
    // 而真相是自己昨天关的）。这一档到点自动失效，不需要记得回来开。
    //
    // 刻意**不做**「永久关掉提醒」的总开关：那正是上面那种失败模式的制度化版本。
    // 勿扰的语义一直是「暂时」，这一档也是——只是把「暂时」的上限从 8 小时推到今天结束。
    public void PauseRemindersUntilEndOfDay()
    {
        var now = DateTime.Now;
        _dndUntil = now.Date.AddDays(1);   // 次日 0 点
        ShowToast("Clockwork", Strings.Get("Toast_DndEndOfDay"), Views.ToastLevel.Info);
    }

    /// <summary>「恢复提醒（剩 …）」那一行的文案。托盘与面板腰带共用这一份。</summary>
    //
    // 一小时以内按分钟、超过就按小时。加了 8 小时和「到今天结束」两档之后，
    // 原来那句固定按分钟的写法会说出「剩 480 分钟」——一个要在心里除一遍才知道是多久的数。
    // 分界取 60 分钟：那正好是原来最小的一档，所以旧行为在旧档位上一字不变。
    //
    // 两档的取整方式**故意不同**：
    //   分钟：向上取整。还剩 30 秒时要说「剩 1 分钟」——说「剩 0 分钟」会被读成「已经结束了」。
    //   小时：四舍五入。这一档不存在读成 0 的风险（低于 60 分钟就走上面那支了），
    //         而向上取整会让「还剩 61 分钟」说成「剩 2 小时」，凭空多报 59 分钟。
    // **分支要看「要显示的那个数」，不是原始值。** 曾经写的是 `left.TotalMinutes < 60`，
    // 而分钟那支又向上取整——于是刚点下「1 小时」那一刻 left 是 59.99 分，分支选分钟、
    // 取整又抬成 60，菜单上读出来就是「剩 60 分钟」。档位写着 1 小时、回手说 60 分钟，
    // 而那正是用户点完立刻会去核对的一行。根因是分支与显示各自取整，中间那一秒两边不一致。
    // 先取整再分支，两边就永远是同一个数。
    public string DndResumeLabel(TimeSpan left)
    {
        int mins = (int)Math.Ceiling(left.TotalMinutes);
        return mins < 60
            ? Lf("Tray_DndResume", mins)
            : Lf("Tray_DndResumeHours", (int)Math.Round(left.TotalHours, MidpointRounding.AwayFromZero));
    }

    public void ResumeReminders()
    {
        _dndUntil = null;
        ShowToast("Clockwork", Strings.Get("Toast_DndOff"), Views.ToastLevel.Info);
    }

    // 编辑提醒会换新 id（借此重置「今天已弹」态），但两项在途的耐久投递不该丢：
    //   SnoozeUntil    —— 用户明确要求的一次推迟；
    //   NextIntervalAt —— 「循环运行」的下一轮。它与 SnoozeUntil 同属落盘状态（见 ReminderState），
    //                     漏迁的后果是：改一下文案，跑了一上午的「每 30 分钟」当场停到明天——
    //                     换新 id 后 LastFiredDate 也清空了，当天首发窗口早已过去，Decide 一路返回 none。
    // 只在新配置仍配了循环时迁 NextIntervalAt：在编辑里把循环关掉的，不该再多跑一轮（静默组会整组重跑）。
    // 不迁 LastFiredDate——「编辑即可当天重弹」正是换 id 的本意。迁完即耐久落盘，防编辑后崩溃丢状态。
    public void MigrateReminderState(string oldId, Reminder updated)
    {
        var newId = updated.Id;
        if (string.IsNullOrEmpty(oldId) || oldId == newId) return;
        // 「启动时就存在」资格随编辑迁移：否则编辑过的提醒 existedAtStartup=false，「错过必补」当天失效。
        if (_startupReminderIds.Remove(oldId)) _startupReminderIds.Add(newId);
        if (!_reminderStates.TryGetValue(oldId, out var old)) return;
        var carryInterval = updated.IntervalMinutes >= 1 ? old.NextIntervalAt : null;
        // SkippedDate 必须跟着迁：用户上午点了「今天不再」、随后改了一下文案，跳过就被悄悄撤销、
        // 提醒照常响——而编辑是撤销跳过的唯一入口，等于让人在毫不知情的情况下误撤。
        // 它也是三者中唯一可能单独存在的（SkipToday 会把 SnoozeUntil/NextIntervalAt 一起清成 null），
        // 所以下面的建档条件必须把它算进去，否则永远走不到赋值那一步。
        if (old.SnoozeUntil != null || carryInterval != null || !string.IsNullOrEmpty(old.SkippedDate))
        {
            if (!_reminderStates.TryGetValue(newId, out var st)) { st = new ReminderState(); _reminderStates[newId] = st; }
            st.SnoozeUntil = old.SnoozeUntil;
            st.NextIntervalAt = carryInterval;
            st.SkippedDate = old.SkippedDate;
        }
        // PendingFireAt 有意不迁：它按旧时间算出，编辑就是要按新配置重新判定。
        _reminderStates.Remove(oldId);   // 旧 id 已不被任何提醒引用，成孤儿；显式移除并落盘
        ReminderStateStore.Save(_statePath, _reminderStates);
    }
}
