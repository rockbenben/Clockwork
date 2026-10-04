using System;
using System.IO;

namespace Clockwork.Core;

// 操作中心（不是我们的 toast 窗口，是 Win11 那个通知中心分组头）只认磁盘路径，不认 exe 内嵌资源；
// 而单文件发布时 exe 旁边没有 assets\logo.ico，所以内嵌图标要先解压到
// %LOCALAPPDATA%\Clockwork\logo.ico，再把那个路径注册给 AUMID（见 App.RegisterAumidBranding）。
//
// 缓存判据是**内容一致**，不是「文件在且非空」。老判据在标识换过一次之后就会咬人：
// 每台升过级的机器上那个文件早就存在，于是永远不重写，操作中心继续显示旧图标——
// 注册表里的 IconUri 一个字没变，变的本该是文件本身。用户看到的症状是「更新之后图标还是老的」，
// 而且无论重启多少次都不会好。
public static class BrandIconCache
{
    // 只在内容真的不同时写。写不动（旧文件正被 shell 占用等）就把现有那份照用——
    // 品牌图标不值得把启动崩掉，也不值得把异常抛到注册流程外面。
    public static string Refresh(string path, byte[] icon)
    {
        try
        {
            if (!IsCurrent(path, icon))
                File.WriteAllBytes(path, icon);
        }
        catch { }
        return path;
    }

    private static bool IsCurrent(string path, byte[] icon)
    {
        try
        {
            var fi = new FileInfo(path);
            // 先比长度再比内容：绝大多数启动是「已经一致」，长度不等时省掉一次 47KB 读
            return fi.Exists && fi.Length == icon.Length
                && File.ReadAllBytes(path).AsSpan().SequenceEqual(icon);
        }
        catch { return false; }
    }
}
