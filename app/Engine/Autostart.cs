using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Clockwork.Engine;

// 登录自启注册：用 schtasks.exe + XML（避开 CIM 卡顿）。
// 任务直接指向 Clockwork.exe --boot；触发器无延迟，延时在进程内。
public static class Autostart
{
    public static string TaskName => "Clockwork";

    private static bool IsAccessDenied(string s) => AdminError.IsAccessDenied(s);

    public static bool IsRegistered()
    {
        try { var (code, _) = SystemStartupReader.RunSchtasks($"/query /tn {TaskName}"); return code == 0; }
        catch { return false; }
    }

    // 注册「最高权限」登录任务（需管理员）。返回 Ok / NeedsAdmin / Error:...。
    public static string Register(string exePath)
    {
        var user = SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName) ?? "";
        var cmd = SecurityElement.Escape(exePath) ?? "";
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Clockwork 登录自启</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{cmd}</Command><Arguments>--boot</Arguments></Exec></Actions>
</Task>";
        var tmp = Path.Combine(Path.GetTempPath(), "shtask-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(tmp, xml, new UnicodeEncoding(false, true));   // UTF-16 + BOM，schtasks 要求
            var (code, output) = SystemStartupReader.RunSchtasks($"/create /tn {TaskName} /xml \"{tmp}\" /f");
            if (code == 0) return "Ok";
            return IsAccessDenied(output) ? "NeedsAdmin" : "Error: " + output.Trim();
        }
        catch (Exception ex) { return "Error: " + ex.Message; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public static string Unregister()
    {
        if (!IsRegistered()) return "Ok";   // 幂等：本就没有=已是目标态
        var (code, output) = SystemStartupReader.RunSchtasks($"/delete /tn {TaskName} /f");
        if (code == 0) return "Ok";
        return IsAccessDenied(output) ? "NeedsAdmin" : "Error: " + output.Trim();
    }
}
