using System.IO;

namespace Clockwork.Core;

// 配置文件位置：**已经有配置的那一侧优先**（便携=exe 同目录，漫游=%APPDATA%\Clockwork\）；
// 两边都没有才按 exe 目录是否可写来挑，便携优先。详见 Resolve 上的注释——每次现场探测会让
// 「这次启动是否提权」决定用哪份配置，同一台机器上分裂成两份。
public static class ConfigPath
{
    public const string FileName = "clockwork.settings.json";

    public static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".cw_write_probe_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // 已经存在配置的那一侧优先；两边都没有才按可写性挑（首次启动）。
    //
    // 原来每次启动都现场探测 exe 目录可写性来决定用哪边，问题是探测结果会随**本次启动的权限**漂移：
    // 同一份装在 C:\Program Files 下的 exe，双击运行是非提权（清单写死 asInvoker）→ 目录不可写 → 用漫游；
    // 而开机自启的计划任务是 RunLevel=HighestAvailable（见 Autostart）→ 管理员账户下提权启动 → 目录可写
    // → 用便携。一漂就等于换了一份配置：用户看到的是「点了以管理员重开之后设置全空」，或者
    // 「开机自启跑的清单根本不是我配的」，而且此后两份各写各的、越差越远。非提权触发也有——
    // exe 放在启用了受控文件夹访问的目录、或临时只读的 U 盘/网络盘，探测失败一次就整体切走。
    // 文件在哪儿本身就是上一次的选择，比现场探针可靠得多，也不需要额外存一个"我用哪边"的状态。
    // isWritable：可写性探针，仅为可测而注入（真去写盘的那一下是不纯的部分，与 StepEnv 同思路）。
    // 生产调用一律省略。注意「目录只读属性」在 Windows 上并不阻止建文件，所以测试没法靠改属性造出不可写目录。
    public static string Resolve(string exeDir, string fileName = FileName, Func<string, bool>? isWritable = null)
    {
        var portable = Path.Combine(exeDir, fileName);
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clockwork");
        var roaming = Path.Combine(appDataDir, fileName);
        if (File.Exists(portable)) return portable;   // 便携优先：两边都有时按老约定以 exe 同目录为准
        if (File.Exists(roaming)) return roaming;
        if ((isWritable ?? IsWritable)(exeDir)) return portable;
        try { Directory.CreateDirectory(appDataDir); } catch { }
        return roaming;
    }
}
