using System.Windows.Interop;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Native;

namespace Clockwork.Services;

// 全局热键：急停 / 快捷面板 / 一键直达三个功能键 + 动作组键的注册、重绑、挂起与派发。
// 自 App 拆出（纯结构重构）。主窗口句柄以回调取用（注册/挂接都发生在 BuildShell 之后）；
// 面板与提醒两条出站路径在装配后由 App 接线（OnPanelHotkey / OnDeviceEvent），
// 运行/急停/单步直接持有 RunOrchestrator。
internal sealed class HotkeyManager
{
    private readonly Func<RootConfig> _cfg;
    private readonly Func<nint> _mainHandle;   // 主窗口 EnsureHandle：注册与挂钩共用
    private readonly AppDiagnostics _diag;
    private readonly RunOrchestrator _runner;

    public HotkeyManager(Func<RootConfig> cfg, Func<nint> mainHandle, AppDiagnostics diag, RunOrchestrator runner)
    {
        _cfg = cfg; _mainHandle = mainHandle; _diag = diag; _runner = runner;
    }

    // App 装配的出站接线（构造期还没有面板/提醒实例）。
    public Action? OnPanelHotkey;              // 面板热键按下
    public Action<string>? OnDeviceEvent;      // U 盘插入事件（"usb"），归提醒事件线

    private RootConfig _config => _cfg();
    private void ShowToast(string title, string message, Views.ToastLevel level = Views.ToastLevel.Info,
                           int durationMs = -1, string? key = null, bool log = true)
        => _diag.ShowToast(title, message, level, durationMs, key, log);
    private void WarnToast(string msg) => _diag.WarnToast(msg);
    private void AppendErrorLog(string line) => _diag.AppendErrorLog(line);
    private static string Lf(string key, params object[] args) => Strings.Lf(key, args);

    private const int HotkeyId = 0xB001;          // 急停
    private const int PanelHotkeyId = 0xB002;     // 快捷面板（与急停同属「功能键」，占固定号，不进组的槽位区间）
    private const int QuickOpenHotkeyId = 0xB003; // 一键直达（选中即开）
    private const int GroupHotkeyBase = 0xB100;   // 动作组热键 id 区间起点
    private const int GroupSlotMax = 0xBFF0;      // RegisterHotKey 应用侧 id 上限 0xBFFF，留余量
    private nint _hotkeyHwnd;                     // 主窗口句柄，注册/注销共用
    private bool _hotkeysSuspended;               // 捕捉期间为真：SaveConfig 不得重建热键（否则组会抢走正被改绑的急停组合）
    private readonly Dictionary<int, string> _groupHotkeyIds = new();   // 当前已注册的 id → 组 Id（WM_HOTKEY 查表）
    private readonly Dictionary<string, int> _groupIdSlots = new();     // 组 Id → 固定 id 槽位：重建不换号，队列里滞留的旧 WM_HOTKEY 不会错派到别的组
    private int _nextGroupSlot = GroupHotkeyBase;
    private HashSet<string> _hotkeyFails = new();                       // 上一轮注册失败的「组Id|键」：同一失败只 toast 一次，不随每次保存刷屏
    private string? _stopHotkeyFail;                                    // 急停键上次失败的组合：同一失败只 toast 一次（每次捕捉进出都会重注册）
    private string? _panelHotkeyFail;                                   // 面板键同上，各记各的：两个键失败的原因通常不同，去重不能共用一个格子
    private string? _quickOpenHotkeyFail;                               // 一键直达键，同上各记各的
    // 上一轮成功绑定后的组热键签名（判据与理由见 Core.HotkeyRebindGate）。
    // 只在真跑完一轮重建后置位；任何注销路径都清空它，免得「挂起前绑过」冒充「现在还绑着」。
    private string _groupHotkeySig = "";

    public void RegisterStopHotkey()
    {
        try
        {
            _hotkeyHwnd = _mainHandle();   // 即便主窗未显示也拿得到句柄
            HwndSource.FromHwnd(_hotkeyHwnd)?.AddHook(HotkeyHook);          // 钩子只挂一次
        }
        catch { return; }
        ResumeHotkeys();
    }

    // 按当前配置重注册全部热键（急停 + 各组）。捕捉结束/配置变更后调用，无需重启。
    // 急停先注册——组热键与急停撞车时输的是组（注册失败并 toast 点名），急停永远保命优先。
    public void ResumeHotkeys()
    {
        _hotkeysSuspended = false;
        RebindFunctionHotkey(HotkeyId, _config.Settings.StopHotkey, ref _stopHotkeyFail);
        // 面板关掉就别占着这个组合键：留着它既会拦住别的程序，按下去又什么都不会发生
        // （TogglePanel 已经在开头返回了）。传空串走的正是 RebindFunctionHotkey 里「不注册」那条路。
        RebindFunctionHotkey(PanelHotkeyId, _config.Settings.PanelEnabled ? _config.Settings.PanelHotkey : "",
                             ref _panelHotkeyFail);
        RebindFunctionHotkey(QuickOpenHotkeyId, _config.Settings.QuickOpenHotkey, ref _quickOpenHotkeyFail);
        RebindGroupHotkeys();
    }

    // 捕捉期间暂时注销全部热键：避免录键时按到已注册组合触发急停/跑组（e.Handled 拦不住 OS 级 WM_HOTKEY）。
    // 置起挂起标志：挂起期间任何 SaveConfig 都不得重建组热键——否则捕捉中途保存（改急停键正是此路径）
    // 会让某个组抢注用户刚指给急停的组合，恢复时急停注册失败、保命键从此哑掉。
    public void SuspendHotkeys()
    {
        _hotkeysSuspended = true;
        if (_hotkeyHwnd == 0) return;
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, HotkeyId); } catch { }
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, PanelHotkeyId); } catch { }
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, QuickOpenHotkeyId); } catch { }
        UnregisterGroupHotkeys();
    }

    // 每次保存配置后的即时重建（捕捉挂起期间一律不动键；捕捉一定以 ResumeHotkeys 收尾）。
    // 面板总开关管的是**热键注册**：关掉就得当场把 Ctrl+Alt+Space 让出来，开回来就得当场抢回。
    // 只有录键那条路会走 ResumeHotkeys（那里统一重建功能键），而设置页那个复选框不经过录键——
    // 不在这儿补一句，改完要等下一次录键或重启才生效。与组热键同一道 _hotkeysSuspended 闸。
    public void RebindAfterConfigSave()
    {
        // 组增删改/启停/改键都走此保存——热键跟着当前配置即时重建。
        if (!_hotkeysSuspended) RebindGroupHotkeys();
        if (!_hotkeysSuspended)
        {
            RebindFunctionHotkey(PanelHotkeyId, _config.Settings.PanelEnabled ? _config.Settings.PanelHotkey : "",
                                 ref _panelHotkeyFail);
            RebindFunctionHotkey(QuickOpenHotkeyId, _config.Settings.QuickOpenHotkey, ref _quickOpenHotkeyFail);
        }
    }

    // 功能键（急停 / 快捷面板）：先注销旧的、再注册新的（空/无效=停用）。失败/无效 toast 按组合去重——
    // 每次进出捕捉框都会重注册，被占用时不该每次都弹同一条（组热键同款策略）。
    //
    // 两个键共用这一份而不是各抄一遍：它们的注册流程逐字相同（注销 → 空则停用 → 解析 → 拦保留组合 →
    // 注册 → 分类报错 → 去重记账），差别只有热键 id 和「上次失败的组合」记在哪个格子里。
    // 抄一遍的代价不是多 25 行，是日后修 bug 只修了一边——去重逻辑尤其容易只补一处。
    // lastFail 用 ref 传：去重状态必须留在字段里跨调用存活，两个键各记各的（失败原因通常不同）。
    private void RebindFunctionHotkey(int id, string? combo, ref string? lastFail)
    {
        // 句柄已销毁（退出时主窗 Closed 触发的恢复）就跳过：否则在死句柄上注册失败、又弹「注册失败」气泡。
        if (_hotkeyHwnd == 0 || !HotKey.IsWindow(_hotkeyHwnd)) return;
        try { HotKey.UnregisterHotKey(_hotkeyHwnd, id); } catch { }
        if (string.IsNullOrWhiteSpace(combo)) { lastFail = null; return; }   // 空=禁用
        var p = KeyInput.ToHotkeyParams(combo);
        // 保留组合也在此拦：配置可手改/可带旧版遗留，只挡捕捉 UI 挡不住 JSON 里写进来的 Alt+F4。
        // 文案分开：解析不了 → 无法识别；解析得了但系统保留 → 明说保留，别让用户以为拼写错了反复试。
        if (p == null || HotkeyCapture.IsReserved(combo))
        {
            var msgKey = p == null ? "Hotkey_Unrecognized" : "Hotkey_Reserved";
            if (lastFail != combo) ShowToast("Clockwork", Lf(msgKey, HotkeyCapture.PrettyCombo(combo)), Views.ToastLevel.Warn);
            lastFail = combo;
            return;
        }
        bool ok = false;
        try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, id, p.Modifiers | HotKey.MOD_NOREPEAT, p.Vk); } catch { }
        if (!ok)
        {
            if (lastFail != combo) ShowToast("Clockwork", Lf("Hotkey_RegisterFail", HotkeyCapture.PrettyCombo(combo)), Views.ToastLevel.Warn);
            lastFail = combo;
            return;
        }
        lastFail = null;
    }

    // 动作组热键：全量重建（先注销全部旧注册，再按当前配置注册启用组的非空热键）。
    // 注册失败（被其它程序占用 / 与急停或其它组重复）toast 点名组与键——但同一失败只报一次，
    // 不随之后每次无关的保存反复刷屏；失败消除（改键/解除占用）后再失败会重新提示。
    private void RebindGroupHotkeys()
    {
        if (_hotkeyHwnd == 0 || !HotKey.IsWindow(_hotkeyHwnd)) return;   // 句柄已销毁则跳过（同 RebindStopHotkey）
        // 脏检查：组热键配置一字未动、当前注册仍挂着、且没有失败项等着重试 → 省掉整轮摘装。
        // 「有失败就照旧重绑」是这条优化的边界：RegisterHotKey 失败后没有事件可等，
        // 全靠每次保存无条件重建去抢回被别的程序占用的组合（判据见 Core.HotkeyRebindGate）。
        var sig = HotkeyRebindGate.Signature(_config.ActionGroups
            .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Hotkey))
            .Select(x => (x.Id, x.Hotkey)));
        if (HotkeyRebindGate.ShouldSkip(_groupHotkeySig, sig, _hotkeyFails.Count, _groupHotkeyIds.Count)) return;
        UnregisterGroupHotkeys();
        // 清掉已删除组的槽位映射：字典有界（≤当前组数），回绕后它们的号可安全复用。
        var liveIds = new HashSet<string>(_config.ActionGroups.Select(x => x.Id));
        foreach (var dead in _groupIdSlots.Keys.Where(k => !liveIds.Contains(k)).ToList())
            _groupIdSlots.Remove(dead);
        var fails = new HashSet<string>();
        foreach (var g in _config.ActionGroups)
        {
            if (!g.Enabled || string.IsNullOrWhiteSpace(g.Hotkey)) continue;   // 禁用组不占键，把组合让给别人
            // 每组用固定 id 槽位（按组 Id 分配、正常不复用）：重建后 id 不换号，
            // 消息队列里滞留的旧 WM_HOTKEY 要么派给同一组、要么查表落空被忽略，绝不错派到别的组。
            // 槽位逼近 RegisterHotKey 的 id 上限（同进程建删数千个组）才回绕复用已删组让出的号；
            // 回绕时逐号跳过仍在映射中的槽位——绝不与现存组（含本轮已注册的）撞号。
            if (!_groupIdSlots.TryGetValue(g.Id, out int slot))
            {
                var inUse = new HashSet<int>(_groupIdSlots.Values);
                int probes = 0;
                do
                {
                    if (_nextGroupSlot >= GroupSlotMax) _nextGroupSlot = GroupHotkeyBase;
                    slot = _nextGroupSlot++;
                } while (inUse.Contains(slot) && ++probes < GroupSlotMax - GroupHotkeyBase);
                _groupIdSlots[g.Id] = slot;
            }
            var p = KeyInput.ToHotkeyParams(g.Hotkey);
            bool reserved = HotkeyCapture.IsReserved(g.Hotkey);   // 手改配置写进来的 Alt+F4 等：拒注册并明说保留
            bool ok = false;
            if (p != null && !reserved)
            {
                try { ok = HotKey.RegisterHotKey(_hotkeyHwnd, slot, p.Modifiers | HotKey.MOD_NOREPEAT, p.Vk); } catch { }
            }
            if (!ok)
            {
                var key = g.Id + "|" + g.Hotkey;
                fails.Add(key);
                if (!_hotkeyFails.Contains(key))
                    WarnToast(reserved ? Lf("Hotkey_Reserved", HotkeyCapture.PrettyCombo(g.Hotkey))
                                       : Lf("Hotkey_GroupRegisterFail", g.Name, HotkeyCapture.PrettyCombo(g.Hotkey)));
                continue;
            }
            _groupHotkeyIds[slot] = g.Id;
        }
        _hotkeyFails = fails;
        // 记下的始终是「这一轮跑完后配置长什么样」——失败项已在上面挡住 ShouldSkip，
        // 所以下一轮只要配置不变且仍有失败，registeredCount>0 && failCount>0 照样走重建去重试。
        _groupHotkeySig = sig;
    }

    private void UnregisterGroupHotkeys()
    {
        foreach (var id in _groupHotkeyIds.Keys)
            try { HotKey.UnregisterHotKey(_hotkeyHwnd, id); } catch { }
        _groupHotkeyIds.Clear();
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // U 盘插入：Windows 把 WM_DEVICECHANGE 广播给所有顶层窗口，不需要 RegisterDeviceNotification。
        // 搭这个钩子的车而不是另开一个消息窗口——主窗口的句柄本来就常驻（热键注册就依赖它），
        // 为一个事件再造一个隐藏窗口纯属多一份生命周期要管。
        // 不置 handled：设备通知是广播，吞掉它不礼貌也没好处（本程序不是唯一的收听者）。
        // 一个多分区的 U 盘会连发几条，靠提醒事件线门口的 3 秒去抖收敛成一次。
        if (msg == Native.Win32.WM_DEVICECHANGE)
        {
            // **异步派发，不在窗口过程里直接跑。** 这条消息是 Windows 用 SendMessage 广播的：
            // 我们不返回，它就一直卡在那儿等——而事件触发可能弹一个提醒框，
            // 那是个模态窗（自带消息循环），一等就是整个提醒时长，甚至等到用户来点。
            // 那段时间里整个即插即用广播被这一个窗口拖住。
            //
            // 其余每一个事件源（会话切换、电源、显示器、网络）本来就在工作线程上到达，
            // 靠事件入口自己编组回 UI 线程；只有这一条是在 UI 线程上
            // 直接进来的，于是那句编组是空转——只能在这里显式排队。
            if (wParam.ToInt32() == Native.Win32.DBT_DEVICEARRIVAL && Native.Win32.IsVolumeArrival(lParam))
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => OnDeviceEvent?.Invoke("usb")));
            return IntPtr.Zero;
        }
        if (msg != HotKey.WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id == HotkeyId)
        {
            _runner.RequestStop();
            handled = true;
        }
        else if (id == PanelHotkeyId)
        {
            OnPanelHotkey?.Invoke();
            handled = true;
        }
        else if (id == QuickOpenHotkeyId)
        {
            // 走单步执行的手势档：无单飞闸、成功静默，取不到选区/识别不了才 toast。
            // 传 system 步骤而不是直接调 SystemCommands，为的是复用整条失败回执链（Mark→toast/日志）。
            _runner.RunStepAsync(new LaunchStep { Kind = "system", Command = "quickOpen" }, by: StepTrigger.Gesture);
            handled = true;
        }
        else if (_groupHotkeyIds.TryGetValue(id, out var gid))
        {
            // 按组 Id 现查（注册后组可能已被编辑），禁用兜底跳过——正常情况下禁用组根本不会注册。
            var g = _config.ActionGroups.FirstOrDefault(x => x.Id == gid);
            if (g is { Enabled: true }) ToggleGroupByHotkey(g);
            handled = true;
        }
        return IntPtr.Zero;
    }

    // 动作组热键是开关：没在跑→跑；正在跑→取消这一次运行。「按了没反应」是这个键以前最大的毛病——
    // 组还在跑时再按一次，旧实现被运行集当成重入静默丢弃，屏幕上一点动静都没有，用户只能怀疑热键坏了。
    //
    // 取消的边界刻意收窄到「这一次运行」：不动全局急停，启动清单和别的组照跑。要停一切请用急停键
    // （设置页可改，托盘/主窗按钮同款）——一个组的热键不该有掀桌子的权力。
    //
    // 回执必须给，而且两个分支都要给、还要共用同一张卡（同一合并键）：
    //   · 只给取消发卡 → 启动分支静默，用户分不出「按了启动」和「这键没注册上」；
    //   · 两个分支各发各的卡 → 取消卡（原来是 Warn，默认挂 12 秒）会在屏上过期说谎：期间再按一次，
    //     组其实已经重新跑起来了，用户看到的还是「已请求取消」，于是判定 toggle 坏了。
    // 同键合并后，卡片就地更新成最后一次按键的结果，连按也只有一张。两条都用 Info：这是用户对自己
    // 的组主动做的开关，不是故障；Warn 的 12 秒时长正是过期卡片能骗人的原因。
    //
    // 竞态说明：RunGroupAsync 先派 Task、再由后台线程登记，中间有个空窗。这个窗口不是「几微秒」——
    // 每个在途运行都占着一个线程池线程阻塞，线程注入延迟在争用时可达约 1 秒，人手连按完全够得着。
    // 落进空窗的第二次按键会判成「没在跑」而再派一次，那一次随即被运行集判重入跳过（不会跑两遍），
    // 代价只是这一次按键白按。要根治得把登记提到派发线程上做两段式握手，收益不抵复杂度，暂留。
    public void ToggleGroupByHotkey(ActionGroup g, IntPtr gestureOrigin = default)
    {
        var key = "grouptoggle:" + g.Id;
        if (ActionGroupRunner.RequestCancel(g.Id))
        {
            ShowToast("Clockwork", Lf("Toast_GroupCancelled", g.Name), Views.ToastLevel.Info, key: key);
            return;
        }
        // 在跑、但这一次运行不是热键管得着的那种（它是别的组的嵌套步骤，或开机清单内联展开的）：
        // 既不能取消，也绝不能再开一份并发副本。照单飞的老规矩不跑，但必须说清楚——
        // 这里要是直接调 RunGroupAsync，气泡会报「已启动」，而实际那一次随即被判重入丢弃，纯属骗人。
        if (ActionGroupRunner.IsRunning(g.Id))
        {
            ShowToast("Clockwork", Lf("Toast_GroupBusy", g.Name), Views.ToastLevel.Info, key: key);
            return;
        }
        _runner.RunGroupAsync(g, gestureOrigin: gestureOrigin);
        ShowToast("Clockwork", Lf("Toast_GroupStarted", g.Name), Views.ToastLevel.Info, key: key);
    }
}
