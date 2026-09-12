using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Clockwork.Native;

// App 执行别名（App Execution Alias）背后的真 exe 解析。
//
// wt.exe / pwsh.exe / winget.exe 这类东西在 %LOCALAPPDATA%\Microsoft\WindowsApps 下是一个
// **0 字节的重解析点**（IO_REPARSE_TAG_APPEXECLINK），不是真程序。PrivateExtractIcons /
// SHGetFileInfo 直接问它要图标，拿回来的都是那个近空白的通用字形——面板上看着像「没图标」。
// 真图标在包里的真 exe 上（实测 wt 别名 →
// C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_*_x64__8wekyb3d8bbwe\wt.exe，
// 对它 PrivateExtractIcons 一次就拿到终端图标）。别名本身的**启动**不受影响：ShellExecute
// 认这个重解析点，所以只在取图标这条路上做解析，启动目标原样不动。
//
// 重解析数据的布局在 Win11 24H2/26200 上实测为（文档只给了结构名、没给字节布局）：
//   偏移 8：DWORD 串数 N（=3）
//   其后直接是 N 个以 NUL 结尾的 UTF-16LE 串：[0]=包族名 [1]=AUMID [2]=真 exe 全路径
// 不按串数扫到 dataLen 结束：尾部还有版本字段（实测见过杂字符 "0"），会被当成第四条。
internal static class AppExecutionAlias
{
    private const uint IO_REPARSE_TAG_APPEXECLINK = 0x8000001B;
    private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
    private const uint FILE_READ_ATTRIBUTES = 0x00000080;
    private const uint FILE_SHARE_RW = 0x7;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const int MaxReparseData = 16 * 1024;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize,
                                               byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private static readonly IntPtr Invalid = new(-1);

    /// <summary>别名背后真实存在的 .exe 全路径；入参不是 App 执行别名 / 读不出 / 真文件已不在，一律返回 null。</summary>
    public static string? TryResolveRealExe(string path)
    {
        // 形态闸：别名是带重解析点属性的 0 字节文件。先把普通 exe / .lnk 挡在 DeviceIoControl 之外
        // （取图标每个格子都走，省一趟 ioctl）；元数据读不出就不猜。
        FileInfo fi;
        try { fi = new FileInfo(path); }
        catch { return null; }
        if (!fi.Exists || fi.Length != 0 || (fi.Attributes & FileAttributes.ReparsePoint) == 0) return null;

        var h = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING,
                            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (h == Invalid || h == IntPtr.Zero) return null;
        try
        {
            var buf = new byte[MaxReparseData];
            if (!DeviceIoControl(h, FSCTL_GET_REPARSE_POINT, IntPtr.Zero, 0, buf, (uint)buf.Length, out _, IntPtr.Zero))
                return null;
            // tag 不是 APPEXECLINK：符号链接 / 其他重解析点不归这里管（解析了也不是我们要的 exe）。
            if (BitConverter.ToUInt32(buf, 0) != IO_REPARSE_TAG_APPEXECLINK) return null;
            int dataLen = BitConverter.ToUInt16(buf, 4);
            var parts = ParseStrings(buf, dataLen);
            // 取最后一个 .exe：真路径在 [2]。优先要磁盘上还在的（包被卸载后字符串仍在、文件已没了），
            // 退而求其次也要把名字给调用方——它那边对取不到图标的路径本来就有回退。
            string? suffixOnly = null;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var s = parts[i];
                if (!s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                suffixOnly ??= s;
                if (File.Exists(s)) return s;
            }
            return suffixOnly;
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    /// <summary>纯解析（可测）：从重解析缓冲区取出那 N 个 UTF-16LE 串。布局见类注释。</summary>
    internal static List<string> ParseStrings(byte[] buf, int dataLen)
    {
        var list = new List<string>();
        if (buf == null || buf.Length < 12) return list;
        int count = BitConverter.ToInt32(buf, 8);
        if (count <= 0 || count > 16) return list;   // 不合理的串数：宁可不认，别按脏数据一路解码
        int p = 12;
        int end = 8 + Math.Min(dataLen, buf.Length - 8);
        for (int n = 0; n < count; n++)
        {
            int start = p;
            while (p + 1 < end && !(buf[p] == 0 && buf[p + 1] == 0)) p += 2;
            if (p + 1 >= end) break;                 // 数据被截断：已解出的几条照给
            list.Add(Encoding.Unicode.GetString(buf, start, p - start));
            p += 2;
        }
        return list;
    }
}
