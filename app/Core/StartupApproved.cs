namespace Clockwork.Core;

// StartupApproved 位标志（Run 键/启动文件夹的启用状态存于 Explorer\StartupApproved\* 的二进制值）。
public static class StartupApproved
{
    // 缺记录/空 = 启用；否则首字节最低位=1 表示禁用。
    public static bool IsApprovedEnabled(byte[]? blob)
    {
        if (blob == null || blob.Length == 0) return true;
        return (blob[0] & 0x01) == 0;
    }

    public static byte[] ApprovedBlob(bool enable)
        => enable
            ? new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
            : new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
}
