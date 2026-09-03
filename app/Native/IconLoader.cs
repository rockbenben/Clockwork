using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Clockwork.Native;

// 把一个路径变成可以画在面板格子上的位图：图片直接解码，其余（exe / lnk / ico / 文档）取系统图标。
//
// 取不到一律返回 null，调用方回退到线描字形——一格没图标不好看，但一格空着或者整个面板开不出来更糟。
// 磁盘上的东西随时可能被删、被锁、被杀软拦，这条路径上的每一步都得当它会失败。
//
// 尺寸取 64 而不是默认的 32：格子里的图标框在「宽松」档是 34 DIP，
// 150% 缩放下要 51 物理像素——喂 32 的话是放大后的糊图，而图标恰恰是这个面板好不好看的全部。
// 再往上（256 的 JUMBO）要走 IImageList 那套 COM，收益追不上那几十行互操作。
public static class IconLoader
{
    private const int Px = 64;

    // 缓存：一次面板打开要取二三十个图标，而同一批目标会被反复打开。
    // 键含尺寸，日后要多档尺寸不必推翻缓存。值可为 null——「这个路径取不到」同样值得记住，
    // 否则每次打开面板都要为同一个坏路径重试一遍磁盘。
    // **取不到（null）的那一半会过期，取到的不会。**
    //
    // 「失败也记 null」本身是对的（同一条死路径不必每次呼出都重试一遍），但这个程序
    // 在托盘里挂几周：NAS 关机那一刻记下的 null 原先会被永久记住，盘回来之后那一格
    // 仍然是线描字形，重启才好。取到的那一半不过期——图标不会自己变。
    private const long NullTtlMs = 60_000;
    private static readonly ConcurrentDictionary<string, (BitmapSource? Bmp, long At)> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, int count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO psfi, uint cb, uint flags);

    private const uint SHGFI_ICON = 0x000000100, SHGFI_LARGEICON = 0x000000000, SHGFI_USEFILEATTRIBUTES = 0x000000010;

    /// <summary>取路径对应的位图。**绝不阻塞**：缓存里有就给，没有就返回 null 并在后台去取，
    /// 取到后回 UI 线程调 <paramref name="onReady"/>（调用方借此把字形换成图标）。
    /// 结果已 Freeze，可跨线程用。</summary>
    //
    // 为什么一秒都不能等：这一路全在 UI 线程上跑（面板每个格子、面板管理器每一页、
    // 三个编辑器的预览，六处同步调用），而图标路径是用户填的，可以指向一个已断开的网络盘——
    // 那时第一步的 File.Exists 会卡满 SMB 超时。**而 UI 线程同时是低级鼠标钩子的泵**：
    // 它被占住超过 LowLevelHooksTimeout（默认 300ms），Windows 就会把钩子**静默卸掉**，
    // 而且不会通知任何人——中键长按和全部右键手势从此失灵，直到重启。
    // 一页里几个网络图标随便一乘就远超那个上限，所以「等一个短预算」也不成立，只能一点都不等。
    public static BitmapSource? Load(string? path, Action? onReady = null)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return null;
        var key = p + "|" + Px;
        if (Cache.TryGetValue(key, out var hit)
            && (hit.Bmp != null || Environment.TickCount64 - hit.At < NullTtlMs)) return hit.Bmp;
        // 同一条路径正在取时，把这一位的回调**排进那一轮**，而不是丢掉。
        // 原来这里直接 return null，只有第一个调用方的 onReady 会被触发——而一页里
        // 二十几个格子指向同一个 exe 是常态（这一点下面那行注释自己就写着），
        // 于是第一个格子换上真图标，其余全部停在线描字形上，直到下次呼出走缓存才好。
        var waiters = Waiting.GetOrAdd(key, _ => new List<Action>());
        if (onReady != null) lock (waiters) waiters.Add(onReady);
        if (!InFlight.TryAdd(key, 0)) return null;
        System.Threading.Tasks.Task.Run(() =>
        {
            var bmp = LoadCore(p);
            Cache[key] = (bmp, Environment.TickCount64);   // 失败也记 null，但那一半会过期（见 NullTtlMs）
            InFlight.TryRemove(key, out _);
            Waiting.TryRemove(key, out _);
            if (bmp == null) return;                 // 取不到就不惊动界面：各处本来画的就是回退字形
            Action[] ready;
            lock (waiters) ready = waiters.ToArray();
            if (ready.Length == 0) return;
            var d = System.Windows.Application.Current?.Dispatcher;
            if (d == null) return;
            foreach (var cb in ready) d.BeginInvoke(cb);
        });
        return null;
    }

    // 同一条路径上还在等回填的那些格子。取回来之后一次性全通知——
    // 只通知第一个的话，其余的会在这一次界面里一直停在回退字形上。
    // 比较器必须与 Cache 一致。Cache 是 OrdinalIgnoreCase，而这两份曾经用默认的
    // 区分大小写比较器，于是 "C:\App\a.exe" 与 "c:\app\A.EXE" 在缓存里是同一条、
    // 在这两份里却是两条——上面那句「防止同一条路径被并发拉起好几次」在大小写
    // 不同时就不成立了，每一种写法各自去做一次 shell 取图。
    private static readonly ConcurrentDictionary<string, List<Action>> Waiting = new(StringComparer.OrdinalIgnoreCase);

    // 正在后台取的路径，防止同一条路径被并发拉起好几次（一页 24 个格子指同一个 exe 是常事）。
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);

    private static BitmapSource? LoadCore(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            var bmp = Core.PanelIcon.IsImageFile(path) ? LoadImage(path) : null;
            // 图片解码失败也往下走一次系统图标：.ico 偶有 WPF 解不了的变体，而 shell 认得。
            bmp ??= ExtractIcon(path) ?? ShellIcon(path);
            if (bmp == null) return null;
            bmp.Freeze();   // 冻结后可跨线程、且 WPF 不再为它维护变更通知
            return bmp;
        }
        catch { return null; }
    }

    // 图片文件：按目标像素解码，不把 4000×3000 的照片整张读进内存再缩到 34 像素。
    private static BitmapSource? LoadImage(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.CacheOption = BitmapCacheOption.OnLoad;     // 立刻读完并关闭文件句柄，别锁住用户的图片
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = Px;
            bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    // exe / dll / ico：按指定尺寸取，会挑最合适的那一帧（而不是拿 32 的去放大）。
    private static BitmapSource? ExtractIcon(string path)
    {
        var handles = new IntPtr[1];
        var ids = new int[1];
        try
        {
            if (PrivateExtractIcons(path, 0, Px, Px, handles, ids, 1, 0) <= 0 || handles[0] == IntPtr.Zero) return null;
            try { return FromHIcon(handles[0]); }
            finally { DestroyIcon(handles[0]); }   // 句柄必须还，GDI 对象是有上限的进程级资源
        }
        catch { return null; }
    }

    // .lnk / 文档 / 文件夹：走 shell 的文件关联，拿它在资源管理器里的那个图标。
    private static BitmapSource? ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        try
        {
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                              SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES) == IntPtr.Zero) return null;
            if (info.hIcon == IntPtr.Zero) return null;
            try { return FromHIcon(info.hIcon); }
            finally { DestroyIcon(info.hIcon); }
        }
        catch { return null; }
    }

    private static BitmapSource? FromHIcon(IntPtr hIcon)
    {
        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(hIcon, System.Windows.Int32Rect.Empty,
                                                       BitmapSizeOptions.FromEmptyOptions());
        }
        catch { return null; }
    }
}
