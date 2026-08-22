using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 读另一个进程的命令行与当前工作目录。
//
// 为什么不用 WMI（System.Management）：实测同一批 34 个占用端口的进程，两者读到命令行的
// 数量完全相同（各 13 个，逐个比对零分歧），但 WMI 要 413ms 而直读 PEB 只要 3ms——
// 138 倍差距，且 WMI 根本给不了 cwd。零依赖也顺带保住了（那个包会让单文件 exe 涨 40%）。
//
// 读不到的那 21 个是提权 / 受保护进程，两条路都读不到，不是这里的取舍造成的。
//
// 只支持 64 位目标进程：本程序自身就是 x64，而 Win11 上 32 位进程已属罕见；
// 真碰上就读不到，退回到只显示 exe 路径，不会出错。
// ponytail: 偏移写死 x64 的 RTL_USER_PROCESS_PARAMETERS 布局；要支持 WOW64 目标
// 得另走 NtWow64QueryInformationProcess64 一套，等真有人用 32 位 dev server 再说。
public static class ProcessInfo
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int cls, out PROCESS_BASIC_INFORMATION pbi, int len, out int ret);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1, PebBaseAddress, Reserved2_0, Reserved2_1, UniqueProcessId, Reserved3;
    }

    // x64 下的偏移。PEB.ProcessParameters 指向 RTL_USER_PROCESS_PARAMETERS，
    // 命令行与 cwd 都在那一个结构体里——所以一次打开进程能同时拿到两样。
    private const int PEB_ProcessParameters = 0x20;
    private const int RUPP_CurrentDirectory = 0x38;   // CURDIR.DosPath (UNICODE_STRING)
    private const int RUPP_CommandLine = 0x70;

    // 拿不到就返回空串（进程已退出 / 提权 / 32 位目标），调用方各自回退，不抛。
    public static (string CommandLine, string WorkingDir) Read(int pid)
    {
        if (pid <= 4) return ("", "");
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return ("", "");
        try
        {
            if (NtQueryInformationProcess(h, 0, out var pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0) return ("", "");
            var pp = ReadPtr(h, pbi.PebBaseAddress + PEB_ProcessParameters);
            if (pp == IntPtr.Zero) return ("", "");
            return (ReadUnicode(h, pp + RUPP_CommandLine), ReadUnicode(h, pp + RUPP_CurrentDirectory));
        }
        catch { return ("", ""); }
        finally { CloseHandle(h); }
    }

    private static IntPtr ReadPtr(IntPtr h, IntPtr addr)
    {
        var b = new byte[8];
        return ReadProcessMemory(h, addr, b, 8, out _) ? (IntPtr)BitConverter.ToInt64(b) : IntPtr.Zero;
    }

    // UNICODE_STRING: USHORT Length; USHORT MaximumLength; ULONG pad; PWSTR Buffer;
    // Length 是字节数不是字符数，且上限设一道闸：读到垃圾指针时别去申请几百 MB。
    private static string ReadUnicode(IntPtr h, IntPtr addr)
    {
        var head = new byte[16];
        if (!ReadProcessMemory(h, addr, head, 16, out _)) return "";
        int len = BitConverter.ToUInt16(head, 0);
        var buf = (IntPtr)BitConverter.ToInt64(head, 8);
        if (len == 0 || len > 32768 || buf == IntPtr.Zero) return "";
        var data = new byte[len];
        if (!ReadProcessMemory(h, buf, data, len, out _)) return "";
        return System.Text.Encoding.Unicode.GetString(data);
    }
}
