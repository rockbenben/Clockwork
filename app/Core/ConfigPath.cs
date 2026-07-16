using System.IO;

namespace Clockwork.Core;

// 配置文件位置：便携优先（exe 同目录），不可写时回退 %APPDATA%\Clockwork\。
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

    public static string Resolve(string exeDir, string fileName = FileName)
    {
        if (IsWritable(exeDir)) return Path.Combine(exeDir, fileName);
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clockwork");
        try { Directory.CreateDirectory(appData); } catch { }
        return Path.Combine(appData, fileName);
    }
}
