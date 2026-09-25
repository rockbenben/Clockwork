using System.Windows.Threading;
using Clockwork.Core;

namespace Clockwork.Services;

// 面板格子点击统计（clockwork.usage.json）：载入/剪枝、计数、去抖落盘、清空。
// 自 App 拆出（纯结构重构）。成员名刻意保留原样——PanelClickHookTests 按源码文本钉住
// 「剪枝必须真的落盘」这条接线，改名等于拆守卫。
// 单写者：点击在 UI 线程，OnExit 的收尾也在 UI 线程，所以 lock 的作用不是跨线程互斥，
// 而是**禁止同一线程重入**（理由原文见 ScheduleUsageFlush/SaveUsage 处）。
internal sealed class PanelUsageTracker
{
    // 键是 LaunchStep.Id，见 Core.PanelUsageStore。App 侧以属性别名交给面板管理器。
    internal readonly Dictionary<string, PanelUsageStore.TileUsage> _panelUsage = new();
    private readonly object _usageLock = new();
    private string _usagePath = "";
    private DispatcherTimer? _panelUsageTimer;
    private bool _panelUsageDirty;

    // 启动时载入 + 剪掉已删格子的墓碑键。都是「配置之外的运行态」，与提醒状态同一时机读盘。
    public void LoadFrom(string usagePath, IEnumerable<string> liveStepIds)
    {
        _usagePath = usagePath;
        foreach (var kv in PanelUsageStore.Load(_usagePath)) _panelUsage[kv.Key] = kv.Value;
        // 顺带清掉已经删掉的格子：不清的话那份文件只增不减，行数会比面板上实际的格子多。
        // 只认 PanelPages——统计的是面板格子，主清单与动作组里的步骤不进这份统计。
        var liveIds = liveStepIds;
        var pruned = Core.PanelUsageStore.PruneUnreferenced(_panelUsage, liveIds);
        if (pruned > 0)
        {
            // 必须先置脏：PruneUnreferenced 只改内存字典，而 SaveUsage 头上有脏标记闸，
            // 不置位的话这次落盘直接早退——墓碑键每次启动读回来、忘掉、再读回来，文件只增不减。
            lock (_usageLock) _panelUsageDirty = true;
            SaveUsage();
        }
    }

    // 面板格子被点一下：计数 + 排队落盘。点的是「格子」而不是「步骤跑没跑通」——
    // 被条件挡掉的点击同样是一次点击，问的始终是「我在用哪些格子」。
    // 只从面板这一条路进来（BuildPanelPages 的唯一漏斗），手势与主清单不计。
    // 返回 false = 这一格已禁用/被拒（不计数也不落盘），调用方连步骤都不跑。
    public bool RecordClick(string stepId)
    {
        if (!PanelUsageStore.RecordClick(_panelUsage, stepId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) return false;
        ScheduleUsageFlush();
        return true;
    }

    // 落盘去抖：连点几十下只写一次盘。窗口是「点击即关」的，去抖比不写更重要——
    // 一次点击的计时器还没跑完，窗口就关了，但那是进程退出前的最后一次落盘机会，
    // 而 OnExit 会补一次同步写，所以去抖不会真的丢数据，只是把「每点一下写一次」压成「一次」。
    private void ScheduleUsageFlush()
    {
        lock (_usageLock)
        {
            _panelUsageDirty = true;
            if (_panelUsageTimer is null)
            {
                _panelUsageTimer = new DispatcherTimer(DispatcherPriority.Background,
                    System.Windows.Application.Current.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(700),
                };
                _panelUsageTimer.Tick += (_, _) => { _panelUsageTimer.Stop(); SaveUsage(); };
            }
            _panelUsageTimer.Stop();   // 一次性的：Stop 再 Start 才能重新计时（连续点击=每次都从头等 700 毫秒）
            _panelUsageTimer.Start();
        }
    }

    // 文件写本身是原子的（ConfigStore.TryWriteTextAtomic）。
    private void SaveUsage()
    {
        lock (_usageLock)
        {
            if (!_panelUsageDirty || _usagePath.Length == 0) return;
            _panelUsageTimer?.Stop();
            // 写失败就留着脏标记：不重试队列（丢一次点击无可救药），但 OnExit 那一次还会再试。
            if (Core.PanelUsageStore.Save(_usagePath, _panelUsage)) _panelUsageDirty = false;
        }
    }

    // OnExit 用的同步收尾：把最后那次去抖没跑完的写入补上。
    // 已经写过就无事可做（_panelUsageDirty 为假）。
    public void FlushUsageNow() => SaveUsage();

    // 面板管理器里的「清空点击统计」：只清这份统计，配置一根汗毛不碰——
    // 不碰 _config 就不必付 SaveConfig 那笔热键重绑的代价。立即落盘，不等去抖。
    public void ResetPanelUsage()
    {
        lock (_usageLock)
        {
            _panelUsage.Clear();
            _panelUsageDirty = true;
            _panelUsageTimer?.Stop();
        }
        SaveUsage();
    }
}
