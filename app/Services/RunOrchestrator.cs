using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;

namespace Clockwork.Services;

// 运行编排：单步 / 整组 / 启动清单三条路共用的急停闸、执行、回执与逐步记账。
// 自 App 拆出（纯结构重构，线程模型一字未改）。_config 以回调现取——App 在启动时会把
// 字段整个换掉（LoadConfig），服务不能抓旧引用。UI 编组走 Application.Current.Dispatcher，
// 与原来「App 实例的 Dispatcher」同一个调度器。
internal sealed class RunOrchestrator
{
    private readonly Func<RootConfig> _cfg;
    private readonly Func<string> _cfgDir;
    private readonly Func<string> _exePathFn;
    private readonly AppDiagnostics _diag;

    public RunOrchestrator(Func<RootConfig> cfg, Func<string> cfgDir, Func<string> exePath, AppDiagnostics diag)
    {
        _cfg = cfg; _cfgDir = cfgDir; _exePathFn = exePath; _diag = diag;
    }

    // —— 与 App 原成员同名的私有转发：迁移体逐字不动，守卫按文本钉的表达式才有意义 ——
    private static Dispatcher Disp => System.Windows.Application.Current.Dispatcher;
    private RootConfig _config => _cfg();
    private string CfgDir => _cfgDir();
    private string _exePath => _exePathFn();
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                           int durationMs = -1, string? key = null, bool log = true)
        => _diag.ShowToast(title, message, level, durationMs, key, log);
    private void WarnToast(string msg) => _diag.WarnToast(msg);
    private void AppendErrorLog(string line) => _diag.AppendErrorLog(line);
    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    // —— 运行状态（主窗口急停按钮据此显示/隐藏）——
    // 三条运行路径（启动清单 / 单步 / 动作组，提醒的静默组走动作组）都过同一个闸，故这是唯一可信来源。
    private readonly RunGate _runGate = new();   // 启动序列/单步/动作组 共享的急停闸
    private int _launchRunning;   // 0/1 并发守卫
    private int _stepRunning;

    public bool IsRunning => _runGate.Active > 0;

    public event Action? RunStateChanged;

    // 运行闸的变化搬到 UI 线程再广播：Begin/End 都在后台运行线程上调，订阅方（急停按钮）要动控件。
    // 单独一步装配而不是构造里干：Dispatcher 要等 Application 存在（App 字段初始化时还没有）。
    public void Attach() => _runGate.ActiveChanged += () => Disp.BeginInvoke(() => RunStateChanged?.Invoke());

    // 急停唯一出口：全局热键、托盘菜单、主窗口按钮三个入口都走这里，免得日后长出第四种写法。
    // 置位后由各运行线程在动作边界自查退出，长等待（启动延迟/等窗口）被 InterruptibleSleep 立刻打断。
    // 气泡是必须的：按下去当场没有任何反应，用户会以为按钮是坏的、然后接着乱按。
    public void RequestStop()
    {
        StopSignal.Request();
        // 再把「停」推给每个在途动作组的取消闸：它们的可中断延时只等自己那一个事件，不去等全局信号的
        // 内核句柄（那会引入两份状态、可能永久分歧）。不推的话，睡在轮间延迟里的组要睡满才发现急停。
        ActionGroupRunner.CancelAll();
        ShowToast("Clockwork", Strings.Get("Hotkey_Stopped"), Views.ToastLevel.Warn);
        // 不在这儿动急停按钮：它只由「有没有东西在跑」决定，而那个变化由运行闸(RunGate)统一广播。
        // 按下急停到真正停下之间最多几百毫秒，中间态没有观察价值，回执由上面这条气泡负责。
    }

    // 开机清单跑完后的「重装鼠标钩子抢链首」——钩子域归 GestureCoordinator，这里只留一个出口，
    // 由 App 在装配时接上（见 RunLaunchAsync 里的那一句）。
    public Action? OnBootSequenceDone;

    // 取走并执行一次外部投递的「跑这个组」请求。
    // while 而不是 if：AutoReset 事件在两次 Set 挨得极近时只会唤醒一次（第二次 Set 落在同一个已置位
    // 状态上），留在文件里的那条请求就再没人来取了。循环取到空为止，这条竞态就不存在。
    // 实际上请求文件只存一条（后写覆盖先写），循环更多是防「取的时候又来一条」。
    public void DrainRunRequest()
    {
        // 循环要**以内容变化为界**，不能只看「还取得到」。Take() 是「读出来再删掉」，
        // 而它在删不掉的时候仍然把内容交出来（只读属性、被杀软/同步盘锁住、拒绝删除的 ACL——
        // 实测读得到而删会抛 Win32 错误 5）。那时 while 就成了死循环：UI 线程再也回不来
        //（托盘、热键、提醒全停），而那个组被一遍遍地重跑——它里面可能有关机、注销、发送按键。
        // 取到同一条就收手：请求文件只存一条（后写覆盖先写），重复出现只可能是没删掉。
        string? last = null;
        while (RunRequest.Take() is string want)
        {
            if (want == last) break;
            last = want;
            RunGroupByName(want);
        }
    }

    // 按名称或 id 找组并跑。找不到 / 被禁用都如实报——外部触发的场合没人盯着屏幕，
    // 静默失败会让人以为快捷方式坏了，然后去改一个根本没问题的命令行。
    // 名称匹配大小写不敏感且忽略首尾空格：命令行里的名字是手打的，不该因为大小写不对就不跑。
    public void RunGroupByName(string nameOrId)
    {
        var want = (nameOrId ?? "").Trim();
        var g = _config.ActionGroups.FirstOrDefault(x => x.Id == want)
             ?? _config.ActionGroups.FirstOrDefault(x => string.Equals(x.Name?.Trim(), want, StringComparison.OrdinalIgnoreCase));
        if (g == null) { WarnToast(Lf("Warn_GroupNotFound", want)); return; }
        if (!g.Enabled) { WarnToast(Lf("Warn_GroupDisabled", g.Name)); return; }
        RunGroupAsync(g, unattended: true);   // 外部触发（计划任务 / AHK / 快捷方式），同上面那句注释
    }

    // 后台跑启动清单（手动重跑或 --boot）。并发守卫防重入交错。
    public void RunLaunchAsync(bool boot)
    {
        if (Interlocked.Exchange(ref _launchRunning, 1) == 1) return;   // 已有一次在跑，忽略
        Native.WindowManager.MarkForegroundBaseline();   // 「恢复活动窗口」要回的就是此刻这个
        var cfg = _config.SnapshotForRun();   // 快照列表：开机延迟期间 UI 增删步骤不会让后台枚举抛「集合已修改」
        var selfPaths = new[] { _exePath };
        var cfgDir = CfgDir;
        Task.Run(() =>
        {
            try
            {
                // Begin 在 try **里面**：它（连同广播 ActiveChanged 的订阅方）可能抛，
                // 抛在 try 外的话 finally 不跑，_runGate.End() 与 _launchRunning 的复位一起被跳过——
                // 主窗口的急停按钮从此永远亮着，而 _launchRunning 卡在 1 会让「重跑清单」再也点不动。
                // RunGroupAsync 早就把这一句挪进来了并写明了理由，这两处是漏的。
                _runGate.Begin();   // 首个并发运行才清急停；不再无条件 Clear（避免抹掉在途急停）
                var result = LaunchSequence.Run(cfg, boot, -1, 0,
                    // 卡片形态的 message 在这里截下：InvokeStepAction 对 message 一律静默返回 ✓
                    // （模态形态在启动路径本就该静默），只有这一层知道该弹卡片。顶层与组展开共用本 lambda。
                    s => s.Kind == "message" && StepHelpers.MessageFormOf(s) == MessageForm.Card
                         ? ShowStepCard(s)
                         // boot 时传 null = 「这条路上没有可以问的人」（见 StepRunner 的 system 那一支）。
                         // 手动重跑清单不传 null：那时用户就在键盘前，弹一句确认是对的。
                         : StepRunner.RunStepMark(s, boot ? null : a => ConfirmDestructive(a), selfPaths),
                    () => DateTime.Now);
                LaunchSequence.WriteLog(Path.Combine(cfgDir, "clockwork.run.log"), result, DateTime.Now);
                Disp.Invoke(() =>
                {
                    // 开机那一次以前被排除在回执之外（原写法 if (!boot)），本意大概是「登录时别吵」。
                    // 但 NotifyRunResult 成功时本来就一言不发，只在撞步数上限 / 被急停 / 有步骤失败时开口——
                    // 于是「排除开机」实际排掉的只有坏消息：12 步里 3 步失败，屏幕上一声不响，
                    // 真相躺在一个要用户自己想起来去翻的日志文件里。而这恰恰是最该被告知的一次运行:
                    // 手动重跑时人就在屏幕前，失败当场就看见了；开机那次没人看着，日志是唯一的证人。
                    NotifyRunResult(result);
                    // 开机这一趟跑完，清单里延迟启动、且装了低级钩子的工具（AHK / 启动器类）都已装钩，
                    // 此刻重装一次把钩子链首抢回来（接线见 OnBootSequenceDone）。
                    if (boot) OnBootSequenceDone?.Invoke();
                });
            }
            // 没有 catch 时任何异常都让整个开机序列静默中止（无日志/无 toast/什么都没启动）——如实报出来。
            catch (Exception ex) { WarnToast(Lf("Warn_LaunchRunCrashed", ex.Message)); }
            finally { _runGate.End(); Interlocked.Exchange(ref _launchRunning, 0); }
        });
    }

    /// <param name="by">谁在触发这一步。**两件事都跟着它走**：成功要不要弹回执、要不要防连点。</param>
    //
    // 合成一个参数而不是两个 bool：它们不是两个独立开关，是同一个事实（谁按的）的两个后果，
    // 分开写迟早会出现「安静但仍然防连点」这种没人想要的组合。
    //
    // **回执**：「点了运行按钮」和「划了一道手势」要的东西相反——
    //   按钮是在试跑，屏幕上多半什么也看不出来，那张卡片就是它**唯一**的结果，必须留；
    //   手势是一挥而过的操作，十条默认手势里八条效果肉眼直接可见，再弹一张卡等于每天
    //   几十次地把一件你已经看见的事又说一遍。
    //   但**失败一定要说**：手势失败是彻底无声的（窗口被未保存对话框挡着关不掉、搜索时
    //   其实什么都没选中），没有那张卡就只剩「我画了，没反应」。所以安静的只有成功这一档。
    //
    // **防连点**：那道闸挡的是「气泡回执要几秒才出，急着连点运行按钮会把同一步跑两遍」——
    //   那是按钮的毛病。手势要花一秒认真画出来，画两次就是**故意**要跑两次
    //   （↖ 切换置顶正是这种用法：钉住、再放开）。而闸一旦拦下就是直接 return、什么都不说，
    //   于是连着画两次「最小化」时第二次无声消失——正是这个功能里最难自查的那种表现。
    //   注入本身已由 InjectionLock 串行化，手势这一档不需要再加一道。
    public void RunStepAsync(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                             IntPtr gestureOrigin = default)
    {
        bool quiet = by == StepTrigger.Gesture;
        // 消息步骤：在 UI 线程弹窗（是/否闸门 + 可选朗读/onYes），不走后台执行——否则会被当作未知类型告警。
        if (step.Kind == "message")
        {
            if (step.Speak) ReminderActions.Speak(step.Message);
            // owner 必须继续往下传：On-Yes 组若不传，闸门弹在编辑器前面、组本身抛到主窗口后面，
            // z-order 又变回这整个功能要修的那个老毛病（编辑器看着关掉了，其实主窗后面藏着一个提问框）。
            if (ShowGroupMessage(step, owner) == MsgResult.Yes)
                ReminderActions.RunOnYes(step.OnYes, _config.ActionGroups, g => RunGroupAsync(g, owner), WarnToast);
            return;
        }
        // 问句步骤：与 message 同理，得在 UI 线程弹框，不能丢进后台当普通步骤跑
        //（丢进去会撞上 InvokeStepAction 那句「这条路径上没有可以发问的界面」）。
        // 单步跑一个问句步骤，答案无处可去——后面没有步骤了——所以问完就把它显示出来，
        // 那是这一步在「试一试」语境下唯一能给出的结果。
        if (step.Kind is "prompt" or "choice" or "list")
        {
            var one = new RunVars();
            var answer = AskUser(step, one, owner);
            if (answer != null && !quiet)
                ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + answer, Views.ToastLevel.Info);
            return;
        }
        // 单飞守卫：只对按钮那一档（理由见 by 参数）。手势不设闸——它拦下时是无声的，
        // 而「画了没反应」正是这个功能最难自查的状态。
        bool guard = by == StepTrigger.Button;
        if (guard && Interlocked.Exchange(ref _stepRunning, 1) == 1) return;
        // 在 UI 线程、派后台之前记：这一刻前台还是用户那个窗口，等步骤自己去抢就晚了。
        // 手势触发额外带起笔窗口（gestureOrigin），窗口管理类「当前窗口」优先打它。
        Native.WindowManager.MarkRunBaseline(gestureOrigin);
        var selfPaths = new[] { _exePath };
        Task.Run(() =>
        {
            try
            {
                _runGate.Begin();   // 同上：放进 try，否则 finally 跳过后「运行这一步」会永久点不动
                var mark = StepRunner.RunStepMarkRepeat(step, a => ConfirmDestructive(a, owner), selfPaths);
                if (!quiet || mark.Fail > 0)
                    ShowToast(Strings.Get("Run_Title"), StepDisplay.StepSummary(step) + "  " + mark.Mark, mark.Fail > 0 ? Views.ToastLevel.Warn : Views.ToastLevel.Info);
            }
            finally { _runGate.End(); if (guard) Interlocked.Exchange(ref _stepRunning, 0); }
        });
    }

    // 「运行整组」/提醒静默组/onYes 组/组编辑器试跑：后台跑动作组。返回本次运行的取消闸——
    // 组编辑器据此把「试跑」按钮就地变「停止」，并在关窗时收掉这次运行（不留孤儿）。
    // onDone：跑完（或被取消/异常）后在 UI 线程回调一次，用于把按钮翻回去。
    //
    // 竞态窗口：本方法把 Task 派下去就返回，EnterTopLevel 的登记要等后台线程真正跑到那一行才发生——
    // 派发与登记之间有个空窗，不是「几微秒」那种可以忽略的窗口（ToggleGroupByHotkey 的连按去重正是
    // 撞在这个窗口上，那边有更完整的说明）。落进空窗期间查「是否已顶层在跑」，看到的还是登记前的状态。
    public RunCancel RunGroupAsync(ActionGroup group, Window? owner = null, Action? onDone = null,
                                   bool unattended = false, IntPtr gestureOrigin = default)
    {
        // 快照与 deps 都在调用线程（UI）上先建好，不把活对象带进后台：
        //   · SnapshotForRun 复制步骤列表——后台 foreach 组步骤时，UI 线程若在改同一个组（删除守卫的
        //     联动清理会就地改其他组的 Steps），背景枚举撞见活列表被改写会抛「集合已修改」。这条注释
        //     曾经只是给「万一手快」的历史兜底；组编辑器上线试跑（本方法的 owner/onDone 参数正是为它加的）
        //     之后，它是第一个能在运行进行中还继续编辑同一份步骤列表的调用方——背景枚举撞见活列表被
        //     UI 线程改写从「理论风险」变成「必然发生」，这份注释因此从历史说明升级成不可删的强制约束。
        //   · BuildGroupDeps 同理在 UI 线程取 _config.ActionGroups 的快照，道理一样（见其内部注释）。
        var snap = group.SnapshotForRun();
        // 同 RunStepAsync：在 UI 线程、派后台之前记下前台，供组里的「恢复活动窗口」用；
        // 手势触发额外带起笔窗口，组里的「当前窗口」窗口动作优先打它。
        Native.WindowManager.MarkRunBaseline(gestureOrigin);
        var deps = BuildGroupDeps(owner, unattended);
        Task.Run(() =>
        {
            // _runGate.Begin() 放进 try：它（含广播 ActiveChanged 的订阅方）与 EnterTopLevel 理论上都
            // 可能抛。若抛在 try 外，finally 不会跑，_runGate.End() 和 onDone 都会被跳过——编辑器的
            // 「试跑」按钮永远卡在「停止」，主窗口的急停按钮也永远显示着。挪进来后无论哪一步出岔子，
            // 配对的收尾都保证会跑。owned 先置 false：EnterTopLevel 没跑到或抛出时，finally 不会误调
            // ExitTopLevel 去清一个从未登记过的顶层运行。
            bool owned = false;
            try
            {
                _runGate.Begin();
                // 只有这一层登记为「顶层运行」——即热键/试跑停止按钮能取消的那一次。嵌套子组与它共用
                // 同一个闸但不登记，否则子组的热键/引用会把整条链（含毫不相干的父组）一起掐掉。
                owned = ActionGroupRunner.EnterTopLevel(snap.Id, deps.Cancel);
                // 顶层入口：结局有意丢弃。Aborted 是用户自己在确认框上点的「否」（他已经知道了，再弹一张
                // 卡是噪音）；Skipped 是同组已在跑的单飞去重——热键连按/循环任务撞上上一轮时本就该静默忽略。
                // 嵌套引用那层不同：那里的 Skipped 意味着配置有环，必须发声（见 RunGroupStep）。
                _ = ActionGroupRunner.RunGroup(snap, deps);
            }
            catch (Exception ex) { WarnToast(Lf("Mark_Exception", ex.Message)); }
            finally
            {
                if (owned) ActionGroupRunner.ExitTopLevel(snap.Id, deps.Cancel);
                _runGate.End();
                // 回调整体兜住：调度器正在关闭时 Invoke 会抛，逃出去会变成 Task 里的未观察异常。
                if (onDone != null) { try { Disp.Invoke(onDone); } catch { } }
            }
        });
        return deps.Cancel;
    }

    // owner：本次运行的弹窗归属窗口（null=无归属：后台触发，弹窗置顶但不认主窗为父）。嵌套子组与 onYes 组共用同一份 deps，
    // 故 owner 自动传遍整条引用链，不必在每一层再传一次。
    //
    // unattended：这一次运行有没有人在看。**不能拿 owner==null 当判据**——热键、托盘、面板格子
    // 同样没有归属窗口，而那几条路上用户就在键盘前，破坏性动作正该弹一句确认。
    // 真正没人的只有两条：提醒到点跑的静默动作组，和 --run-group 那种外部触发
    //（计划任务 / AHK / Stream Deck，RunGroupByName 的注释自己写着「没人盯着屏幕」）。
    // 传 null 给 confirmDestructive 就是告诉 StepRunner「这条路上没有可以问的人」，
    // 那边会如实跳过并记一行，而不是弹一个置顶模态把整组卡住——急停键只在步骤之间被检查，解不开它。
    private GroupDeps BuildGroupDeps(Window? owner = null, bool unattended = false)
    {
        var selfPaths = new[] { _exePath };
        var groups = _config.ActionGroups.ToList();   // 组列表快照（UI 线程取）：后台 Resolve 不再枚举 UI 正在增删的活列表
        GroupDeps deps = null!;
        deps = new GroupDeps
        {
            // 把本次运行的取消闸一路传进步骤执行层：等窗口 / 置前重试 / 置前延时都可能挂几秒到几十秒，
            // 只查全局急停的话，用户取消了动作组，这些步骤过一会儿照样把窗口拽到前台、把按键打进去。
            // 接住 InvokeStepAction 的返回值。以前这里是 `RunStep = s => StepRunner.InvokeStepAction(...)`，
            // ActionResult 被 C# 当表达式语句静默丢弃——于是「脚本不存在」「找不到窗口」「启动失败」这类
            // Warn 在动作组这条路径上一个都到不了用户面前，组照样「跑完」。同一个步骤放开机清单里
            // 有「⚠」可查（LaunchSequence 走 StepRunner.MarkOf），而热键 / 定时静默组——正是无人值守跑的
            // 那条——什么都不说。Unverified（「发出去了但没法证实」）有意不报：它不是失败，报了只会变成噪音。
            RunStep = s =>
            {
                // sites 一路带下来：siteMap 步骤要读站点映射表，而那张表在 RootConfig 上，
                // 不在这条调用链的任何参数里。不传等于「没配任何站点」，于是每条 siteMap 都报
                // 「这个网址不在配置里」——一句对用户完全成立的假话，而真相是接线漏了。
                var r = StepRunner.InvokeStepAction(s, unattended ? null : a => ConfirmDestructive(a, owner), selfPaths, deps.Cancel, deps.Vars, _config.Sites);
                if (r.HasWarning) LogGroupStepWarn(s, r);
            },
            // 这两个回调都会开模态（ShowGroupMessage 的 Confirm / Info 两种形态、AskUser 全部），
            // 而模态在没人看的运行里会把整组挂到永远——BrandDialog 自己写明急停热键破不开模态，
            // 而 deps.Cancel 只在回调返回之后才里得到。所以 unattended 必须一路传进去，
            // 与上面 RunStep 那半（`unattended ? null : ConfirmDestructive`）同一个口径。
            ShowMessage = s => ShowGroupMessage(s, owner, unattended),
            AskUser = s => AskUser(s, deps.Vars, owner, unattended),
            // onYes 组的结局也丢弃：ReminderActions.RunOnYes 是 Action<ActionGroup> 契约（提醒弹窗路径共用），
            // 且语义上 onYes 是「是」分支的副作用出口——它内部的确认框属于那条子流程，把它的中止回灌成父组中止，
            // 会变成「点了是反而整组停了」，比现状更难解释。
            RunOnYes = s => ReminderActions.RunOnYes(s.OnYes, groups, g => { _ = ActionGroupRunner.RunGroup(g.SnapshotForRun(), deps); }, WarnToast),
            Speak = t => ReminderActions.Speak(t),   // 显式 lambda：Speak 有可选参数，方法组匹配不上 Action<string>
            OnStepError = (s, ex) => LogGroupStepError(s, ex),
            OnStepSkipped = (s, skip) => LogGroupStepSkipped(s, skip),
            Budget = new RunBudget(() => WarnToast(Strings.Get("Warn_RunBudget"))),
            // 组内嵌套「动作组」步骤：跑引用组的快照（防运行中被编辑/清理）。三种「这次没跑」的结局——
            // 目标缺失、目标已禁用、重入（环引用/已在运行）——都必须发声：同一份坏配置在启动清单里有
            // 「⚠ 找不到动作组」可查，热键/计划任务这条（正是无人值守跑的那条）以前却什么都不说。
            // 解析与分类（措辞、良性判定）在 ActionGroupResolver.ResolveForRun/Reentrant 里（WPF 之外，可单测）；
            // 这里只管接结果转发给 OnStepSkipped——纯接线。三种都返回 Skipped：目标不存在/被禁/在跑，
            // 下一次迭代结论完全相同，让上层引用轮次立刻收手（否则 Repeat=999 就是 999 条重复告警）。
            RunGroupStep = s =>
            {
                var target = ActionGroupResolver.ResolveForRun(groups, s.GroupId);
                if (target.Skip != null) { deps.OnStepSkipped(s, target.Skip); return GroupRunResult.Skipped; }
                var res = ActionGroupRunner.RunGroup(target.Group!.SnapshotForRun(), deps);
                if (res == GroupRunResult.Skipped)
                {
                    deps.OnStepSkipped(s, ActionGroupResolver.Reentrant());
                }
                return res;
            },
        };
        return deps;
    }

    // 动作组内某步没有正常跑完的统一出口：记一笔到错误日志，并弹一次托盘气泡，随后整组继续（不静默中止）。
    // 三种结局只在三件事上不同——日志措辞、气泡文案与等级、合并键分桶——所以只在这里传参，别再各抄一份。
    // 措辞不能互借：说成「已跳过」会让人以为是条件没满足，说成「异常」又是给一个不存在的故障去查。
    // 合并键先按结局分桶再按步骤摘要分桶：同一步上次是真异常、这次只是良性跳过（或反过来），两张卡不该
    // 互相盖掉——用户需要分别看到「确实失败过」与「最近一次只是被跳过」；而同一步在多轮/多次引用里反复
    // 出事时靠合并键叠加计数，不会堆一摞 12 秒的卡片（整组 Repeat 会让同一步出事很多次）。
    private void LogGroupStep(LaunchStep step, string what, string detail, string bucket, string toast, Views.ToastLevel level)
    {
        var summary = StepDisplay.StepSummary(step);
        // 日志行整行英文、气泡仍然本地化：理由见 AppendErrorLog 上方那段。
        // 三种结局的措辞不能互借（见本方法上面那条），英文里同样分成 failed / had no effect / was skipped。
        // 摘要要单独按英文再渲染一次：StepDisplay 走 resx（「设音量 30%」），
        // 不换文化的话这一行会是「英文框 + 界面语言的摘要」，贴到 issue 里读的人认不出那是哪一步。
        AppendErrorLog($"action-group step {what}: {Strings.InEnglish(() => StepDisplay.StepSummary(step))} - {detail}");
        ShowToast("Clockwork", toast, level, key: bucket + ":" + summary);
    }

    // 真抛出的异常。「这次没跑」但没有异常的情况（嵌套组引用缺失/禁用/重入）走 LogGroupStepSkipped。
    private void LogGroupStepError(LaunchStep step, Exception ex)
        => LogGroupStep(step, "failed (skipped, the action carried on)", ex.Message, "groupstep",
                        Lf("Mark_Exception", StepDisplay.StepSummary(step)), Views.ToastLevel.Warn);

    // 跑了但没生效（ActionResult：脚本不存在 / 需要 PowerShell 7 / 启动失败 / 找不到窗口…）。
    // 气泡复用 Toast_GroupStepSkipped——它的文案就是中性的「{0}，{1}」，「已跳过」只出现在日志行里。
    // 收整个 ActionResult 而不是一个字符串：原因要两种语言各渲染一次（气泡跟界面、日志固定英文）。
    private void LogGroupStepWarn(LaunchStep step, ActionResult r)
        => LogGroupStep(step, "had no effect (the action carried on)", r.WarningEn ?? "", "groupwarn",
                        Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), r.Warning ?? ""),
                        Views.ToastLevel.Warn);

    // 压根没跑（嵌套组引用的目标缺失/已禁用/重入）。气泡文案必须带上具体原因，不能只报步骤摘要，
    // 否则用户只能打开日志文件才知道为什么。已禁用是正常配置状态（与 LaunchSequence 同口径），用 Info。
    private void LogGroupStepSkipped(LaunchStep step, GroupSkip skip)
        => LogGroupStep(step, "was skipped (the action carried on)", skip.TextEn(), "groupskip",
                        Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), skip.Text()),
                        skip.Benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn);

    // 卡片形态 message 的投递（启动清单路径专用）。返回 ✓——卡片弹出即算完成，没有可失败的部分。
    // 这条路径必须自己播报：ActionGroupRunner 的 message 分支会先调 deps.Speak，而启动清单
    // （LaunchSequence 的顶层与组展开）不经过那个分支，只调注入的 stepMark。
    private StepMark ShowStepCard(LaunchStep s)
    {
        if (s.Speak) ReminderActions.Speak(s.Message);
        ShowToast("Clockwork", s.Message, Views.ToastLevel.Info,
                  s.PopupSeconds > 0 ? s.PopupSeconds * 1000 : 0, key: StepCardKey(s));
        return new StepMark("✓", 0, 0);
    }

    // 卡片合并键：同一句话在整组重复轮次里合成一张 ×N，而不是叠一摞。
    private static string StepCardKey(LaunchStep s) => "stepmsg:" + (s.Message ?? "");

    // 动作组 message 步骤的呈现。三种形态见 StepHelpers.MessageFormOf。
    // 卡片：弹完立即返回 Ok（不拦路），调用方（ActionGroupRunner）照常扣预算、查取消、跑下一步。
    // 播报不在这里做——ActionGroupRunner 的 message 分支和 RunStepAsync 都已在调用前处理。
    private MsgResult ShowGroupMessage(LaunchStep step, Window? owner = null, bool unattended = false)
    {
        var form = StepHelpers.MessageFormOf(step);
        // 无人值守：降级成气泡，不开模态。消息步骤只是告知，没人点不该把整组停住，
        // 所以返回 Ok（不是 Yes/No）：那两个是「有人答了」的意思，onYes 不能凭空跑、
        // 整组也不能凭空中止。Card 形态本来就是这条路，这里只是把另两种归并过来。
        if (form == MessageForm.Card || unattended)
        {
            ShowToast("Clockwork", step.Message, Views.ToastLevel.Info,
                      step.PopupSeconds > 0 ? step.PopupSeconds * 1000 : 0, key: StepCardKey(step));
            return MsgResult.Ok;
        }
        return Disp.Invoke(() =>
        {
            if (form == MessageForm.Confirm)
                return Views.BrandDialog.Confirm(owner, Strings.Get("Confirm_Title"), step.Message) ? MsgResult.Yes : MsgResult.No;
            Views.BrandDialog.Info(owner, "Clockwork", step.Message);
            return MsgResult.Ok;
        });
    }

    // 问句步骤（用户输入 / 用户选择）的弹框。与 ShowGroupMessage 并排：同样跑在后台线程上，
    // 同样必须跳回 UI 线程才敢碰窗口。返回 null = 用户取消，调用方据此中止整组。
    //
    // 提示语走一次占位替换：「搜索『{关键词}』？」这种二次确认要用得上前面问来的值。
    // 选项同理——一串选项可以是上一步产出的（虽然现在还没有哪一步能产出多行，但替换是同一行代码）。
    // internal：--selftest 走的是**真的** AskUser（见 DevSelfTest），别绕开这份实现只测弹框自己。
    internal string? AskUser(LaunchStep step, RunVars vars, Window? owner = null, bool unattended = false)
    {
        // 无人值守：不开模态，如实说一声，当作取消。
        // 返 null （= 中止整组）而不是跳过继续：这一步的结果要写进变量给后面的步骤用，
        // 问不出答案时那些步骤本就没有正确可做的事（拿个空值接着跑更糟）。
        // 不能默不作声：热键 / 定时静默组正是无人看的那条路，它不说就真的没人知道。
        if (unattended)
        {
            LogGroupStepWarn(step, ActionResult.Warn("Warn_AskNoUi"));
            return null;
        }

        var title = Strings.Get("Ask_Title");
        var msg = FillForUi(step.Message, vars);
        if (step.Kind == "choice")
        {
            var options = StepHelpers.ChoiceOptions(FillForUi(step.Text, vars));
            // 一个选项都没配：弹一个空列表等于让用户面对一个点不动的框。如实说一声，当作取消处理。
            if (options.Count == 0)
            {
                WarnToast(Strings.Get("Warn_ChoiceNoOptions"));
                return null;
            }
            return Disp.Invoke(() => Views.BrandDialog.Pick(owner, title, msg, options));
        }
        // 「列表」与「用户输入」是同一件事的两种输入形态：一段单行 vs 一段多行。
        // 返回的都是原始文字，拆不拆由调用方定（ActionGroupRunner 按 kind 分派）。
        if (step.Kind == "list")
            return Disp.Invoke(() => Views.BrandDialog.AskMulti(owner, title, msg, FillForUi(step.Text, vars)));
        return Disp.Invoke(() => Views.BrandDialog.Ask(owner, title, msg, FillForUi(step.Text, vars)));
    }

    // 弹框上要显示的文字里的占位替换。与 StepRunner.Fill 是同一件事，只是那一份在 Engine 里、
    // 是 private，而这里替换的是**要显示给人看的**东西，一律不转义（转义了会把中文变成一串 %E4）。
    private static string FillForUi(string? text, RunVars vars)
    {
        if (!StepPlaceholder.Has(text)) return text ?? "";
        string clip = "";
        if (StepPlaceholder.UsesClipboard(text))
        { try { clip = Engine.SystemCommands.ClipboardText(); } catch { } }
        return StepPlaceholder.Apply(text, clip, urlEncode: false, vars);
    }

    private void NotifyRunResult(LaunchRunResult r)
    {
        var s = r.Summary;
        // 截停要先判：撞步数上限时 Stopped 也是 true（循环因此提前退出），但告诉用户「已手动停止」是在说
        // 一件他没做过的事——真相只躺在他得手动打开的日志里。与 WriteLog 的 stopHdr 用同一优先级。
        if (s.Truncated) ShowToast("Clockwork", Strings.Get("Warn_RunBudget"), Views.ToastLevel.Warn);
        else if (s.Stopped) ShowToast("Clockwork", Lf("Tray_LaunchStopped", s.Total), Views.ToastLevel.Warn);
        else if (s.Fail > 0) ShowToast("Clockwork", Lf("Tray_LaunchWarn", s.Total, s.Fail), Views.ToastLevel.Warn);
    }

    // 破坏性系统命令（重启/关机/注销）的确认框。owner 决定它弹在谁前面——组编辑器试跑时必须是编辑器，
    // 否则确认框藏在模态编辑器后面，用户看着「卡住了」而实际是有个框在等他。
    // owner=null（开机清单/托盘重跑/热键/提醒触发）不再补成主窗：无主时 BrandDialog 自己置顶，
    // 见得着且不会把主界面一起拽到前台。
    // 确认那颗按钮直接印动作名（重启 / 关机 / 注销）：这里比别处更不能只写「是」——
    // 手在键盘上、屏幕上是别人的程序时按下去，用户要看见的就是那两个字到底会发生什么。
    private bool ConfirmDestructive(string action, Window? owner = null)
        => Disp.Invoke(() => Views.BrandDialog.Confirm(
            owner, Strings.Get("Confirm_Title"), Lf("Confirm_Destructive", action), Views.ToastLevel.Warn,
            action, Strings.Get("Ed_Cancel")));
}
