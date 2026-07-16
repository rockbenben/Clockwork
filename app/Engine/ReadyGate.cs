using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using Clockwork.Core;

namespace Clockwork.Engine;

public sealed record ReadyResult(bool Ready, int WaitedMs, bool Shell, bool Net);

// 就绪门控：登录后环境未就绪时轮询等待，就绪即走而非傻等。
// 探测失败一律按「就绪」放行——绝不让探针自身故障把启动卡死。探针/睡眠可注入，便于确定性测试。
public static class ReadyGate
{
    public static ReadyResult WaitSystemReady(int timeoutSeconds, bool requireNetwork, int pollMs,
        Func<bool> shellProbe, Func<bool> netProbe, Action<int> sleeper)
    {
        if (pollMs < 1) pollMs = 500;
        int maxWaitMs = Math.Max(0, timeoutSeconds * 1000);
        bool shellOk = false;
        bool netOk = !requireNetwork;
        int waited = 0;
        while (true)
        {
            if (!shellOk) { try { shellOk = shellProbe(); } catch { shellOk = true; } }
            if (!netOk) { try { netOk = netProbe(); } catch { netOk = true; } }
            if (shellOk && netOk) break;              // 就绪即走
            if (waited >= maxWaitMs) break;           // 封顶：放行（宁可早跑也不挂死）
            if (StopSignal.IsRequested) break;        // 急停：不再干等就绪
            sleeper(pollMs);
            waited += pollMs;
        }
        return new ReadyResult(shellOk && netOk, waited, shellOk, netOk);
    }

    // 真实探针路径（不单测）：Shell=explorer 有主窗口；网络=网卡可用。
    public static ReadyResult WaitSystemReady(int timeoutSeconds = 90, bool requireNetwork = true)
        => WaitSystemReady(timeoutSeconds, requireNetwork, 500, ShellReady, NetworkReady, ms => Thread.Sleep(ms));

    private static bool ShellReady()
    {
        try { return Process.GetProcessesByName("explorer").Any(p => p.MainWindowHandle != IntPtr.Zero); }
        catch { return true; }
    }

    private static bool NetworkReady()
    {
        try { return NetworkInterface.GetIsNetworkAvailable(); }
        catch { return true; }
    }
}
