using System.Runtime.InteropServices;
using System.Security;

namespace Clockwork.Engine;

// 登录自启注册：走 Task Scheduler 的 COM（Schedule.Service），与 SystemStartupReader 的枚举/开关同一通道。
// 任务直接指向 Clockwork.exe --boot；触发器无延迟，延时在进程内。
//
// 原先这三个操作是起 schtasks.exe 跑的，换掉的理由不是快，是**失败分不了类**：schtasks 只把错误
// 以当前系统语言的文本打回来，于是「这是不是权限问题」只能拿正则去猜，而那条正则只认中英文——
// 本程序支持 18 种语言，其余 16 种的用户勾「开机自启」时，「拒绝访问」认不出来，
// MainWindow 的一键 UAC 提权分支就永远不触发，只弹一句看不懂的原文错误。
// COM 的 E_ACCESSDENIED(0x80070005) 与系统语言无关，GuardAdminErrors 按 HResult 精确归类。
// SystemStartupReader 早就因为同一个理由把任务开关/删除迁到了 COM，这里是最后一处遗漏。
public static class Autostart
{
    public static string TaskName => "Clockwork";

    // ITaskFolder.RegisterTask / DeleteTask 的常量（taskschd.h）。
    private const int TaskCreateOrUpdate = 6;          // TASK_CREATE_OR_UPDATE
    private const int TaskLogonInteractiveToken = 3;   // TASK_LOGON_INTERACTIVE_TOKEN

    public static bool IsRegistered()
    {
        bool found = false;
        try
        {
            SystemStartupReader.RunTaskOp("\\", folder =>
            {
                dynamic? task = null;
                // 任务不存在时 GetTask 抛 COM 异常——那是「没注册」，不是故障，就地吞掉。
                try { task = folder.GetTask(TaskName); found = task != null; }
                catch { found = false; }
                finally { if (task != null) { try { Marshal.ReleaseComObject((object)task); } catch { } } }
            });
        }
        catch { return false; }   // 服务不可用等：当作未注册，别把设置页卡住
        return found;
    }

    // 注册「最高权限」登录任务（需管理员）。返回 Ok / NeedsAdmin / Error:...。
    public static string Register(string exePath)
    {
        var user = SecurityElement.Escape(Environment.UserDomainName + "\\" + Environment.UserName) ?? "";
        var cmd = SecurityElement.Escape(exePath) ?? "";
        // XML 原样保留（RegisterTask 直接收 XML 字符串），所以不再需要临时文件、也不用管 schtasks 要求的
        // UTF-16+BOM 落盘——字符串本身就是 UTF-16。
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
        return SystemStartupReader.GuardAdminErrors(() =>
        {
            SystemStartupReader.RunTaskOp("\\", folder =>
            {
                dynamic? task = null;
                try { task = folder.RegisterTask(TaskName, xml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null); }
                finally { if (task != null) { try { Marshal.ReleaseComObject((object)task); } catch { } } }
            });
            return "Ok";
        });
    }

    public static string Unregister()
    {
        if (!IsRegistered()) return "Ok";   // 幂等：本就没有=已是目标态
        return SystemStartupReader.GuardAdminErrors(() =>
        {
            SystemStartupReader.RunTaskOp("\\", folder => folder.DeleteTask(TaskName, 0));
            return "Ok";
        });
    }
}
