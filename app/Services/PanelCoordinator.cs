using System.Windows;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Native;

namespace Clockwork.Services;

// 快捷面板：Tile 浮窗的开关/复活、页面与操作瓦片构建、面板管理器与格子的增删改。
// 自 App 拆出（纯结构重构）。对别的域的出站依赖全部显式化：运行走 RunOrchestrator，
// 勿扰四态走 ReminderScheduler，统计走 PanelUsageTracker，组开关闭环由 App 接 ToggleGroup。
// 主窗列表的行级刷新经回调（面板编辑复用的正是主界面那两个编辑器）。
internal sealed class PanelCoordinator
{
    private readonly Func<RootConfig> _cfg;
    private readonly Action _saveConfig;
    private readonly AppDiagnostics _diag;
    private readonly RunOrchestrator _runner;
    private readonly ReminderScheduler _reminders;
    private readonly PanelUsageTracker _usage;
    // 别名保留原名：「_panelUsage, ResetPanelUsage」这段传参文本是 PanelClickHookTests 的锚点。
    private Dictionary<string, PanelUsageStore.TileUsage> _panelUsage => _usage._panelUsage;
    private void ResetPanelUsage() => _usage.ResetPanelUsage();

    public PanelCoordinator(Func<RootConfig> cfg, Action saveConfig, AppDiagnostics diag,
                            RunOrchestrator runner, ReminderScheduler reminders, PanelUsageTracker usage)
    {
        _cfg = cfg; _saveConfig = saveConfig; _diag = diag; _runner = runner; _reminders = reminders; _usage = usage;
    }

    // App 装配的出站接线。
    public Action? ShowMainEdge;
    public Action? OpenGestureManager;
    public Action<ActionGroup, IntPtr>? ToggleGroup;   // 组步骤的「再触发即停」语义在 HotkeyManager
    public Func<Window?>? DialogOwner;
    public Action? ResyncMainGroups;
    public Action? RefreshMainGroupRows;

    private RootConfig _config => _cfg();
    private void AppendErrorLog(string line) => _diag.AppendErrorLog(line);
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                           int durationMs = -1, string? key = null, bool log = true)
        => _diag.ShowToast(title, message, level, durationMs, key, log);
    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    private Views.QuickPanelWindow? _panel;

    // 手势起笔/开管理器前收面板（App 接到 GestureCoordinator.DismissPanel）。
    public void Dismiss() => _panel?.Dismiss();

    // 面板格子被点一下：计数与「跑不跑」在同一个漏斗里发生（守卫见 PanelClickHookTests），
    // 统计与去抖落盘的实现在 Services/PanelUsageTracker.cs。
    // 面板格子被点一下：计数与「跑不跑」在同一个漏斗里发生（PanelClickHookTests 钉这段接线），
    // 统计的去抖落盘在 PanelUsageTracker，执行走本类的 RunStep 唯一漏斗。
    private void RecordPanelClick(LaunchStep step)
    {
        // 统计聚合到被引用条目：点击数据回答「哪个动作最常用」，格子只是入口——
        // 同一个动作摆在三页上，权重就该是一格三倍，而不是三格各记各的。
        // 悬空引用（目标已删）记在格子自己头上：它不该污染任何真条目的权重。
        var usage = step.Kind == StepDisplay.RefKind
                    && StepRefResolver.Resolve(_config.LaunchSteps, step.RefId) is { } tgt ? tgt : step;
        if (!_usage.RecordClick(usage.Id)) return;
        RunStep(step);
    }

    // 面板是开关：热键再按一次收起来。这一点必须做对——面板没有标题栏也没有关闭按钮，
    // 而用户按出它之后的第一反应就是再按一次；那一下要是把面板重开一份（旧的还在），
    // 屏幕上会叠出两个一模一样的浮窗，谁也说不清该点哪个。
    public void TogglePanel()
    {
        // 总闸放在最前面。热键不注册、托盘那项不显示之后，理论上没人还能叫到这儿，
        // 但这个方法是 public 的（托盘、热键、中键钩子三条路都调它），闸门守在入口最省心。
        if (!_config.Settings.PanelEnabled) return;
        if (_panel != null)
        {
            // 僵尸与活面板要分开答（判据见 Core.PanelFocus.DecideToggle）：
            // 从未激活过的面板按「唤出」应该是救活它，而不是把用户看不见的面板关掉——
            // 那正是「长按了没反应」的观感来源。代价是清掉一块僵尸要多按一下
            //（先复活拿到焦点，再按一下才是收起），这是拿一次按键换「召唤不再落空」，
            // 不要把这条当 bug 改回无脑 Dismiss。
            if (PanelFocus.DecideToggle(_panel.EverActivated) == PanelToggleAction.Revive)
            {
                bool ok = _panel.Revive();
                AppendErrorLog(ok ? "panel revived on toggle (foreground acquired)"
                                  : "panel revive on toggle failed (foreground not acquired)");
            }
            else _panel.Dismiss();
            return;
        }
        // 前台进程必须**在建面板之前**问：面板一显示就把前台抢走了，那之后再问只会得到 Clockwork 自己。
        // 这是场景页整个功能的时序命门，别把它挪到下面去（见 Win32.ForegroundProcessName 的注释）。
        //
        // **这一段要包一圈计时，别删。** 它是 `TogglePanel` 里唯一没有名字的部分（`Popup()` 的前台
        // 等待另有 `foreground-wait`），而它有具体嫌疑：2026-09-18 有一次 **2547ms** 的 UI 线程卡顿，
        // 同一秒落下的另一行是 `panel shown but foreground not acquired`。更要紧的是——
        // 那次跑的是 `8de52b4`，那个提交里 `trail-point` / `cursor-watch` / `config-write` **一个都没有**
        //（见 clockwork-build-deploy 的口径：判断「日志里没有某行」能不能当排除依据，先确认那个计时
        // 本来就在出故障的那个提交里）。所以当时「没有具名慢调用」**不构成任何排除**，这一段始终没被查过。
        // 它要在 UI 线程上同步建页、建动作格、建整扇窗，全是没被量过的活。
        Views.QuickPanelWindow w;
        long t0 = Core.UiStallWatch.Begin();
        try
        {
            var foreground = Win32.ForegroundProcessName();
            var s = _config.Settings;
            var look = new Views.PanelLook(s.PanelColumns, s.PanelTileSize, s.PanelIconOnly, s.PanelShowOps,
                                           s.PanelTopRows, s.PanelBottomRows, s.PanelLeftTabs, s.PanelTopTabs);
            // 一页的容量由两条带的行数×列数决定，装不下的自动续到下一页。
            var capacity = PanelMetrics.PageCapacity(s.PanelTopRows, s.PanelBottomRows, s.PanelColumns);
            w = new Views.QuickPanelWindow(BuildPanelPages(foreground, capacity), BuildPanelOps(), look,
                                           BuildPanelWaistOps());
            _panel = w;
        }
        finally { Core.UiStallWatch.End("panel-build", t0); }
        // 清引用挂在 Closed 上而不是各处手动置 null：面板有三条关闭路径（热键再按 / Esc / 焦点离开），
        // 漏掉任何一条，_panel 就会一直指着一个已经关掉的窗口，此后热键永远只走 Dismiss 分支，
        // 面板再也打不开——而这种状态重启前自己不会恢复。
        w.Closed += (_, _) => { if (ReferenceEquals(_panel, w)) _panel = null; };
        // 失败每次都记（LogDedup 收敛连续重复，见 DialogForeground.cs 的同一口径）：
        // 一次性开关会让复发无从查起——「时有时无」的故障最需要知道它复发了多少次。
        // 中键长按这条路没有「刚响应用户输入」的前台锁豁免（见 QuickPanelWindow.Popup 注释）。
        // 抢不到前台时看门狗会在额度窗口内逐拍补抢，仍不成面板仍可见、可点，
        // 下一次「唤出」走上面的 Revive 分支救活它。
        if (!w.Popup())
            AppendErrorLog("panel shown but foreground not acquired");
    }

    // 面板上「你自己的东西」那一片：摆哪些格子由 Core.PanelLayout（纯函数、有测试）决定，
    // 这里只负责给每一格接上真正要执行的动作。一格 = 一个步骤，走 RunStep（下面那个）。
    //
    // 这段注释一度写着「group 类型在 StepRunner 里展开成整组运行，所以面板只有一条执行路径」——
    // **StepRunner 里根本没有 group 分支**（`default: Warn_UnknownKind`）。于是每一个
    // 「运行动作组」的格子点下去都只弹一句「未知步骤类型：group」，什么也没跑，
    // 而 PanelLayout 还特意为它取被引用那个组的图标，看上去完全正常。
    // 另外三个调用方（手势、主列表、组编辑器的试跑）都先分流，只有这条漏了。
    /// <summary>跑一个步骤：group 类型转交整组运行，其余走单步。</summary>
    //
    // 整组运行有「再触发即停」的语义（热键与手势都是这么做的，18 种语言的说明也都这么写），
    // 走单步执行会丢掉这个停法——而一个跑了一半停不下来的组，正是急停键存在的理由。
    public void RunStep(LaunchStep step, Window? owner = null, StepTrigger by = StepTrigger.Button,
                         IntPtr gestureOrigin = default)
    {
        // 「引用启动项」格子：这一格没有自己的动作，真身在清单里。解析与三分结局同组引用一路。
        // 措辞借 Toast_GroupStepSkipped 那张通用皮（「{0}，{1}」）：主语由 StepSummary 自带
        //（「引用启动项：微信」），不必为第二种引用再造一条只有标点差别的文案。
        if (step.Kind == StepDisplay.RefKind)
        {
            var t = StepRefResolver.ResolveForRun(_config.LaunchSteps, step.RefId);
            if (t.Skip is { } skip)
            {
                ShowToast("Clockwork", Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), skip.Text()),
                          skip.Benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn,
                          key: "refskip:" + step.RefId);
                AppendErrorLog($"panel/gesture step-ref {step.RefId} - {skip.TextEn()}");
                return;
            }
            _runner.RunStepAsync(t.Step!, owner, by, gestureOrigin);
            return;
        }
        if (step.Kind == "group")
        {
            // 用 ResolveForRun 而不是 Resolve：目标缺失 / 已禁用要**说一声**。
            // 从前这里是 `if (g is { Enabled: true })`——两种落空都静默 return，于是一个指着
            // 已删动作的面板格子（或手势）画得出来、点得到、图标名字都正常，点下去什么都不发生：
            // 没有气泡、没有日志、没有任何线索。而别的每一条运行路径都会如实报
            //（LaunchSequence 有「⚠ 找不到动作组」，动作组里的嵌套引用走 OnStepSkipped）。
            // 措辞与良性判定都在 ActionGroupResolver 里定过一次，这里只是把它用上。
            var target = ActionGroupResolver.ResolveForRun(_config.ActionGroups, step.GroupId);
            if (target.Skip is { } skip)
            {
                ShowToast("Clockwork", Lf("Toast_GroupStepSkipped", StepDisplay.StepSummary(step), skip.Text()),
                          skip.Benign ? Views.ToastLevel.Info : Views.ToastLevel.Warn,
                          key: "runstepgroup:" + step.GroupId);
                AppendErrorLog($"panel/gesture group step {StepDisplay.StepSummary(step)} - {skip.TextEn()}");
                return;
            }
            var g = target.Group;
            if (g is { Enabled: true }) ToggleGroup?.Invoke(g, gestureOrigin);
            return;
        }
        _runner.RunStepAsync(step, owner, by, gestureOrigin);
    }

    private List<Views.PanelTilePage> BuildPanelPages(string? foreground = null, int capacity = 0)
        => PanelLayout.BuildPages(_config.PanelPages, foreground, capacity, actions: _config.ActionGroups,
                                  listItems: _config.LaunchSteps)
            .Select(p => new Views.PanelTilePage(
                p.Title,
                p.Items.Select(it => new Views.PanelTile(it.Label, it.Icon, () => RecordPanelClick(it.Step), it.Enabled,
                                                         OnEdit: () => EditPanelItem(it),
                                                         OnDelete: () => DeletePanelItem(it),
                                                         Tip: StepDisplay.StepSummary(it.Step),
                                                         Search: StepDisplay.StepSearchText(it.Step))).ToList(),
                AddStepTo(p), p.Tab, p.Screen))
            .ToList();

    // —— 面板上的增删改：一律复用主界面那两个编辑器 ——
    //
    // 面板不自己造编辑界面。步骤编辑器（十几种类型、条件、重复、试跑）已经在那儿了，
    // 面板要做的只是「知道这一格是谁」然后把它交给它——PanelItem 本来就带着 Page 和 Step，
    // 那两个字段一开始就是为这件事留的。
    //
    // 三个动作都先 Dismiss()：编辑器是模态窗，面板又是失焦即关，不先收掉的话面板会在编辑器打开的
    // 同一瞬间消失，而它持有的回调还指着一个正在关的窗口。收掉之后编辑完不自动重开面板——
    // 你点的是「编辑」，此刻该看到的是编辑器；要接着用面板，再按一次热键就是了。

    // 从面板自己打开管理器。先收面板：管理器是模态窗，而面板失焦即关，
    // 不先收掉的话面板会在管理器打开的同一瞬间消失，看着像"点了一下就没了"。
    public void OpenPanelManager()
    {
        _panel?.Dismiss();
        var owner = DialogOwner?.Invoke();
        var w = new Views.PanelManagerWindow(_config, _saveConfig, _panelUsage, ResetPanelUsage) { Owner = owner };
        // 没有 Owner 时（主窗口还在托盘里）必须自己置顶，否则这扇窗会开在所有窗口后面，
        // 用户只看到面板消失、什么都没出来。
        if (owner == null) { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Topmost = true; }
        w.ShowDialog();
        // 管理器现在只动 _config.PanelPages，动作列表一根汗毛都不碰（页独立成实体之后，
        // 「添加面板页」不再是「新建一个动作组」）。但它能改**格子里的步骤**，而那些步骤可能
        // 指向某个动作——重画一次动作列表最省心，也不必去分辨这次到底改没改到。
        ResyncMainGroups?.Invoke();
    }

    // 每一格都是某一页里的某一步，所以编辑一律是「编辑那一步」——没有第二种分支要判。
    // 换出来的是新对象，得替回它在组里的原位，不能只改副本。
    private void EditPanelItem(PanelItem it)
    {
        // 引用格子的「编辑」= 编辑被引用的那条清单项——这就是这个功能存在的全部意义：
        // 改路径只改一处，格子跟着活。引用悬空时退回编辑格子本身（在那里可以重指/换类型）。
        if (it.Step.Kind == StepDisplay.RefKind && StepRefResolver.Resolve(_config.LaunchSteps, it.Step.RefId) is { } target)
        {
            _panel?.Dismiss();
            var owner = DialogOwner?.Invoke();
            var edited = Views.StepEditorWindow.Edit(owner, target, target.Kind, _config.ActionGroups);
            if (edited == null) return;
            int ti = _config.LaunchSteps.IndexOf(target);
            if (ti < 0) return;   // 期间清单里被删了：格子变悬空引用，由 RunStep 那路如实报
            _config.LaunchSteps[ti] = edited;
            _saveConfig();
            RefreshMainGroupRows?.Invoke();
            return;
        }
        _panel?.Dismiss();
        var edOwner = DialogOwner?.Invoke();
        var editedItem = Views.StepEditorWindow.Edit(edOwner, it.Step, it.Step.Kind, _config.ActionGroups, _config.LaunchSteps);
        if (editedItem == null) return;
        int i = it.Page.Steps.IndexOf(it.Step);
        if (i < 0) return;   // 期间被别处删了：什么都不做，好过把它又插回去
        it.Page.Steps[i] = editedItem;
        _saveConfig();
        RefreshMainGroupRows?.Invoke();
    }

    // 删的是这一页里的这一格。指向某个动作的格子（group 类型的步骤）删掉的也只是这个引用，
    // 那个动作本身还在——要删动作仍得去主界面，那里会先把「谁在引用它」摆给你看。
    private void DeletePanelItem(PanelItem it)
    {
        _panel?.Dismiss();
        var owner = DialogOwner?.Invoke();
        // 与三个列表页、组编辑器同一条删除契约：必先确认，且确认框里点名删的是什么。
        if (!Views.BrandDialog.ConfirmDelete(owner, StepDisplay.StepListSummary(it.Step))) return;
        if (!it.Page.Steps.Remove(it.Step)) return;
        _saveConfig();
        RefreshMainGroupRows?.Invoke();
    }

    // 每一页都收得下新东西——往它的格子列表末尾加一步而已。
    // 取的是 p.Source 而不是 p.Items[0].Page：空页没有第一个格子，从格子反推会得到 null，
    // 于是「面板上唯一一张空页」反而是唯一加不进东西的那张。
    private Action? AddStepTo(PanelPageView p)
    {
        if (p.Source is not { } page) return null;
        return () =>
        {
            _panel?.Dismiss();
            var owner = DialogOwner?.Invoke();
            // 以「运行程序」开场：它是最常放上面板的一类，而编辑器顶上就是类型下拉，
            // 想加别的类型当场换即可——不必先弹一层「选类型」再弹编辑器。
            // 传 _config.LaunchSteps：面板语境独有的「引用启动项」类型靠它现身（见 StepDisplay.RefKind）。
            var s = Views.StepEditorWindow.Edit(owner, null, "app", _config.ActionGroups, _config.LaunchSteps);
            if (s == null) return;
            page.Steps.Add(s);
            _saveConfig();
            RefreshMainGroupRows?.Invoke();
        };
    }

    // 面板底部那一片：程序自带的几个高频操作。与上面那片用分隔线隔开——
    // 混在一起时，格子一多就分不清哪些是「我配的动作」哪些是「程序自带的按钮」。
    //
    // 文案复用托盘那几条（都已 18 语），不为面板另造一套说法：
    // 同一个操作在两个地方叫不同的名字，是最没必要的一种困惑。
    private List<Views.PanelTile> BuildPanelOps()
    {
        // 图标沿用托盘菜单那一套（TrayGlyph）：同一个操作在两处长得一样，认一次就够。
        var ops = new List<Views.PanelTile>
        {
            new(Strings.Get("Tray_Rerun"), TrayGlyph.Rerun, () => _runner.RunLaunchAsync(false)),
            new(Strings.Get("Tray_Stop"), TrayGlyph.Stop, _runner.RequestStop),
            // 勿扰是个开关，标签跟着当前状态走：生效中显示「恢复提醒（剩 N 分钟）」，否则「暂停 1 小时」。
            // 一格两态，而不是并排摆「暂停」和「恢复」两格——后者任何时刻都有一格是废的。
            _reminders.DndRemaining is TimeSpan left
                ? new(_reminders.DndResumeLabel(left), TrayGlyph.Run, _reminders.ResumeReminders)
                : new Views.PanelTile(Lf("Tray_Hours", 1), TrayGlyph.Dnd, () => _reminders.PauseReminders(1)),
            new(Strings.Get("Tray_Show"), TrayGlyph.Window, () => ShowMainEdge?.Invoke()),
        };
        return ops;
    }

    // 表圈右端那几颗。与夹板下沿那排分开，是因为两者答的不是同一个问题：
    // 这里是「关于这个程序自己」（去管面板 / 去管手势），那排是「拿这台机器做点什么」
    //（重跑 / 停止 / 勿扰 / 显示主窗）。它们一度都挂在腰栏上，理由见 QuickPanelWindow.xaml。
    //
    // 「管理面板」放在面板自己身上：要改面板的人此刻正看着面板，
    // 让他先去开主窗口、再翻到动作组页、再找一颗按钮，是把最近的路绕成最远的。
    // 用齿轮（MDL2 Settings），与管理器页眉上那颗「编辑这一页」同一个符号——都是「去调它」。
    // 表圈右端那几颗：都是「关于这个程序自己」的入口，不是你配的动作。
    // 手势那颗在这儿，是因为手势和面板是同一批人用的同一类东西（都是「不用键盘就能触发一个动作」），
    // 而在此之前它只在主界面的标题栏上有一个入口——想改一条手势得先把主窗口翻出来。
    private List<Views.PanelTile> BuildPanelWaistOps() => new()
    {
        // 0xE962 = MDL2 的 Mouse，与「鼠标动作」那类步骤的格子用的是同一个字形（PanelGlyph）。
        // 复用是有意的：都在说「鼠标」。代价是面板里一个「鼠标动作」格子和这颗按钮长得一样，
        // 但两者分处表圈与格子区、不挨着，比换一个语义更差的字形（触屏的手）强。
        //
        // 这里一度写的是 0xF126。**它不是「空心方框」那种错**——实测 segmdl2.ttf 里 F126 有字形，
        // 画出来是一个光秃秃的圆环，一个与手势毫无关系的真图标。这才是挑错私用区码位的典型后果：
        // 不报错、不留白，而是理直气壮地画错。GlyphCoverageTests 能挡住的只是「码位根本不存在」
        // 那一半（渲染成方框），语义挑错那一半只有人眼能验——改完跑 `--shots` 看一眼 QuickPanel。
        new(Strings.Get("Gesture_Manager"), char.ConvertFromUtf32(0xE962), () => OpenGestureManager?.Invoke()),
        // fillsEmptyPanel：面板空着时那颗行动按钮指向这里——把面板填满的正是「管理面板」。
        // 显式标记而不是靠排在第几个：上面那条手势入口就是加在前面的，靠顺序的话它会把空态按钮劫走。
        new(Strings.Get("Panel_Manager"), char.ConvertFromUtf32(0xE713), OpenPanelManager, fillsEmptyPanel: true),
    };
}
