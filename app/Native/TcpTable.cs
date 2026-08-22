using System.Net;
using System.Runtime.InteropServices;

namespace Clockwork.Native;

// 处于 LISTENING 的 TCP 端口 → 占用它的进程 PID。
//
// 走 iphlpapi 的 GetExtendedTcpTable，不 shell 出 netstat -ano：省掉每次刷新一次进程创建
// （还得 CreateNoWindow 压住黑框），也不必解析随系统语言而变的文本输出。.NET 自带的
// IPGlobalProperties.GetActiveTcpListeners() 更省事但只给端口不给 PID，拿不到进程名也杀不掉。
//
// IPv4 / IPv6 两张表都读：Next.js 默认绑 ::，Vite 绑 127.0.0.1，只读一张会漏掉一半的服务。
// 同一个服务常在两张表里各出现一次（Node 绑 localhost 会同时监听 127.0.0.1 和 ::1），
// 合并成一行是调用方 PortReader.Merge 的事。
public static class TcpTable
{
    private const int AF_INET = 2, AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const int NO_ERROR = 0, ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int cls, int reserved);

    // MIB_TCPROW_OWNER_PID
    [StructLayout(LayoutKind.Sequential)]
    private struct Row4
    {
        public uint State, Addr, Port, RemoteAddr, RemotePort, Pid;
    }

    // MIB_TCP6ROW_OWNER_PID
    [StructLayout(LayoutKind.Sequential)]
    private struct Row6
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Addr;
        public uint ScopeId, Port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId, RemotePort, State, Pid;
    }

    // 监听中的 (绑定地址, 端口, PID)。任一族查询失败就跳过那一族，不让整页空掉。
    public static List<(string Address, int Port, int Pid)> Listeners()
    {
        var rows = new List<(string, int, int)>();
        Read(AF_INET, rows, (Row4 r) => (new IPAddress(r.Addr).ToString(), HostPort(r.Port), (int)r.Pid));
        Read(AF_INET6, rows, (Row6 r) => (new IPAddress(r.Addr, r.ScopeId).ToString(), HostPort(r.Port), (int)r.Pid));
        return rows;
    }

    // dwLocalPort 的低两字节是网络字节序（大端），直接当整数读会得到 47115 这种数字。
    private static int HostPort(uint netOrder) => (int)(((netOrder & 0xFF) << 8) | ((netOrder >> 8) & 0xFF));

    private static void Read<T>(int af, List<(string, int, int)> into, Func<T, (string, int, int)> project) where T : struct
    {
        int size = 0;
        uint rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0);
        // 表为空时首次调用直接返回 NO_ERROR 且 size=0，没有第二次调用的必要。
        if (rc != ERROR_INSUFFICIENT_BUFFER || size <= 0) return;

        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            rc = GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (rc != NO_ERROR) return;   // 两次调用之间表变长了；下次刷新自会补上，不值得重试循环
            int n = Marshal.ReadInt32(buf);
            int stride = Marshal.SizeOf<T>();
            // 表结构是 { DWORD dwNumEntries; ROW table[]; }，行的对齐要求是 4，故数组从偏移 4 开始。
            for (int i = 0; i < n; i++)
            {
                var row = Marshal.PtrToStructure<T>(buf + 4 + i * stride);
                into.Add(project(row));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
