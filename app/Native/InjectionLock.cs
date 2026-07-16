using System.Threading;

namespace Clockwork.Native;

// 进程内注入互斥：发键(SendInput)/置前台
// 分散在多个后台线程，只包住单次注入动作（~120-200ms），不含等窗口/重试/延时。等 15s 拿不到就不锁照跑
// （宁可并发也不挂死）。单进程内 SemaphoreSlim(1,1) 即可，无需内核对象。
public static class InjectionLock
{
    private static readonly SemaphoreSlim _sem = new(1, 1);

    public static bool Enter()
    {
        try { return _sem.Wait(15000); } catch { return false; }
    }

    public static void Exit(bool got)
    {
        if (got) { try { _sem.Release(); } catch { } }
    }
}
