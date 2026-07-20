using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Clockwork.Core;
using Microsoft.Win32;

namespace Clockwork.Engine;

// 一条系统启动项。
public sealed class SystemStartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Type { get; set; } = "";        // Registry / StartupFolder / ScheduledTask
    public string Scope { get; set; } = "";        // User / Machine
    public bool Enabled { get; set; }
    public bool NeedsAdmin { get; set; }
    public string RegHive { get; set; } = "";
    public string RegRunKind { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string LnkPath { get; set; } = "";
    public string FolderKind { get; set; } = "";
    public string TaskName { get; set; } = "";
    public string TaskPath { get; set; } = "";
    public bool CanToggle { get; set; } = true;
    public string ReadOnlyNote { get; set; } = "";
}

// 系统启动项只读枚举/开关。注册表用 Microsoft.Win32.Registry；
// 计划任务用 COM Schedule.Service（含隐藏任务）；开关用 schtasks.exe（避开 CIM 卡顿）。全为真机交互、无单测（仅冒烟）。
public static class SystemStartupReader
{
    // hive 名→根键、StartupApproved 子键路径：各写一处。枚举/开关/删除共用，改动不必多处同步。
    private static RegistryKey HiveKey(string hive) => hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
    private static string ApprovedPath(string subKey) => $@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{subKey}";

    private static RegistryKey? OpenKey(string hive, string subPath)
    {
        try { return HiveKey(hive).OpenSubKey(subPath); } catch { return null; }
    }

    // 一次读出某 StartupApproved 子键的全部标志 → 值名 → 是否启用。缺记录=启用（调用方默认）。
    private static Dictionary<string, bool> GetApprovedMap(string hive, string subKey)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        using var key = OpenKey(hive, ApprovedPath(subKey));
        if (key == null) return map;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (key.GetValue(name) is byte[] blob) map[name] = StartupApproved.IsApprovedEnabled(blob);
        }
        return map;
    }

    // Run 键规格表：枚举（GetItems）与删除（RunKeyPath）共用——RunKind ↔ 实际路径只声明这一份，
    // 新增/修正条目两边自动同步，不会出现「列表里看得见、删除却找不到路径」的漂移。
    private static readonly (string Hive, string Path, string Scope, string RunKind, string Approved)[] RunSpecs =
    {
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "User", "Run", "Run"),
        ("HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "Machine", "Run", "Run"),
        ("HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Machine", "Run32", "Run32"),
    };

    public static List<SystemStartupItem> GetItems()
    {
        var items = new List<SystemStartupItem>();

        // 注册表 Run
        foreach (var spec in RunSpecs)
        {
            using var key = OpenKey(spec.Hive, spec.Path);
            if (key == null) continue;
            var approved = GetApprovedMap(spec.Hive, spec.Approved);
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                bool en = approved.TryGetValue(name, out var e) ? e : true;
                items.Add(new SystemStartupItem
                {
                    Name = name, Command = key.GetValue(name)?.ToString() ?? "", Type = "Registry", Scope = spec.Scope,
                    Enabled = en, NeedsAdmin = spec.Scope == "Machine", RegHive = spec.Hive, RegRunKind = spec.RunKind, ValueName = name,
                });
            }
        }

        // 启动文件夹
        foreach (var spec in new[]
        {
            (Hive: "HKCU", Dir: Environment.GetFolderPath(Environment.SpecialFolder.Startup), Scope: "User"),
            (Hive: "HKLM", Dir: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Scope: "Machine"),
        })
        {
            if (string.IsNullOrEmpty(spec.Dir) || !Directory.Exists(spec.Dir)) continue;
            var approved = GetApprovedMap(spec.Hive, "StartupFolder");
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(spec.Dir); } catch { continue; }
            foreach (var file in files)
            {
                var fn = Path.GetFileName(file);
                if (fn == "desktop.ini") continue;
                bool en = approved.TryGetValue(fn, out var e) ? e : true;
                items.Add(new SystemStartupItem
                {
                    Name = fn, Command = file, Type = "StartupFolder", Scope = spec.Scope, Enabled = en,
                    NeedsAdmin = spec.Scope == "Machine", RegHive = spec.Hive, ValueName = fn, LnkPath = file, FolderKind = "StartupFolder",
                });
            }
        }

        // 登录触发的计划任务（COM 枚举，含隐藏任务 GetTasks(1)）
        AddScheduledTasks(items);

        // GPO 策略 Run（只读）
        foreach (var spec in new[]
        {
            (Hive: "HKCU", Path: @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", Scope: "User"),
            (Hive: "HKLM", Path: @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", Scope: "Machine"),
        })
        {
            AddReadOnlyValues(items, spec.Hive, spec.Path, spec.Scope, "Note_Policy");
        }

        // RunOnce / RunOnceEx（只读）
        foreach (var spec in new[]
        {
            (Hive: "HKCU", Path: @"Software\Microsoft\Windows\CurrentVersion\RunOnce", Scope: "User"),
            (Hive: "HKCU", Path: @"Software\Microsoft\Windows\CurrentVersion\RunOnceEx", Scope: "User"),
            (Hive: "HKLM", Path: @"Software\Microsoft\Windows\CurrentVersion\RunOnce", Scope: "Machine"),
            (Hive: "HKLM", Path: @"Software\Microsoft\Windows\CurrentVersion\RunOnceEx", Scope: "Machine"),
            (Hive: "HKLM", Path: @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", Scope: "Machine"),
        })
        {
            AddReadOnlyValues(items, spec.Hive, spec.Path, spec.Scope, "Note_OneTime");
        }

        // Winlogon Shell / Userinit（系统关键，只读）
        using (var key = OpenKey("HKLM", @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon"))
        {
            if (key != null)
            {
                foreach (var v in new[] { "Shell", "Userinit" })
                {
                    var val = key.GetValue(v)?.ToString() ?? "";
                    if (val != "")
                        items.Add(new SystemStartupItem { Name = $"Winlogon {v}", Command = val, Type = "Registry", Scope = "Machine", Enabled = true, NeedsAdmin = true, ValueName = v, CanToggle = false, ReadOnlyNote = "Note_System" });
                }
            }
        }

        // Active Setup StubPath（只读）
        foreach (var basePath in new[] { @"Software\Microsoft\Active Setup\Installed Components", @"Software\WOW6432Node\Microsoft\Active Setup\Installed Components" })
        {
            using var baseKey = OpenKey("HKLM", basePath);
            if (baseKey == null) continue;
            foreach (var subName in baseKey.GetSubKeyNames())
            {
                try
                {
                    using var sub = baseKey.OpenSubKey(subName);
                    if (sub == null) continue;
                    var stub = sub.GetValue("StubPath")?.ToString();
                    if (string.IsNullOrWhiteSpace(stub)) continue;
                    var disp = sub.GetValue("")?.ToString();   // (default) 显示名
                    var nm = !string.IsNullOrEmpty(disp) ? disp : subName;
                    items.Add(new SystemStartupItem { Name = nm, Command = stub, Type = "Registry", Scope = "Machine", Enabled = true, NeedsAdmin = true, ValueName = subName, CanToggle = false, ReadOnlyNote = "Note_ActiveSetup" });
                }
                catch { continue; }
            }
        }

        return items;
    }

    private static void AddReadOnlyValues(List<SystemStartupItem> items, string hive, string path, string scope, string note)
    {
        using var key = OpenKey(hive, path);
        if (key == null) return;
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            items.Add(new SystemStartupItem
            {
                Name = name, Command = key.GetValue(name)?.ToString() ?? "", Type = "Registry", Scope = scope,
                Enabled = true, NeedsAdmin = scope == "Machine", ValueName = name, CanToggle = false, ReadOnlyNote = note,
            });
        }
    }

    private static void AddScheduledTasks(List<SystemStartupItem> items)
    {
        var svcType = Type.GetTypeFromProgID("Schedule.Service");
        if (svcType == null) return;
        dynamic? svc = null;
        try
        {
            svc = Activator.CreateInstance(svcType);
            svc!.Connect();
            var queue = new Queue<dynamic>();
            queue.Enqueue(svc.GetFolder("\\"));
            while (queue.Count > 0)
            {
                var folder = queue.Dequeue();
                // 每轮的 COM 对象(文件夹/任务集/单个任务)用完即释放：一次枚举会跨很多任务，
                // 不释放会在托盘长驻期间反复扫描时累积 RCW（虽最终由 GC 回收，及时释放更稳）。
                try
                {
                    try { foreach (var sub in folder.GetFolders(0)) queue.Enqueue(sub); } catch { }
                    dynamic? tasks = null;
                    try { tasks = folder.GetTasks(1); } catch { continue; }   // 1 = TASK_ENUM_HIDDEN
                    try
                    {
                        foreach (var task in tasks)
                        {
                            try
                            {
                                var def = task.Definition;
                                bool hasLogon = false;
                                foreach (var trg in def.Triggers) { if ((int)trg.Type == 9) { hasLogon = true; break; } }   // 9 = LOGON
                                if (!hasLogon) continue;
                                string cmd = "";
                                foreach (var act in def.Actions) { if ((int)act.Type == 0 && !string.IsNullOrEmpty((string)act.Path)) { cmd = (string)act.Path; break; } }   // 0 = EXEC
                                string scope = "User";
                                var prin = def.Principal;
                                string userId = "" + prin.UserId;
                                string groupId = "" + prin.GroupId;
                                if ((int)prin.RunLevel == 1 || Regex.IsMatch(userId, "SYSTEM|S-1-5-18|S-1-5-19|S-1-5-20") || Regex.IsMatch(groupId, "Administrators|S-1-5-32-544")) scope = "Machine";
                                string full = (string)task.Path;
                                string tname = (string)task.Name;
                                string tpath = full.Length > tname.Length ? full.Substring(0, full.Length - tname.Length) : "\\";
                                items.Add(new SystemStartupItem
                                {
                                    Name = tname, Command = cmd, Type = "ScheduledTask", Scope = scope, Enabled = (bool)task.Enabled,
                                    NeedsAdmin = true, TaskName = tname, TaskPath = tpath,
                                });
                            }
                            catch { }
                            finally { try { Marshal.ReleaseComObject((object)task); } catch { } }
                        }
                    }
                    finally { if (tasks != null) { try { Marshal.ReleaseComObject((object)tasks); } catch { } } }
                }
                finally { try { Marshal.ReleaseComObject((object)folder); } catch { } }
            }
        }
        catch { }
        finally { if (svc != null) { try { Marshal.ReleaseComObject(svc); } catch { } } }
    }

    private static void SetApprovedState(string hive, string subKey, string valueName, bool enable)
    {
        using var key = HiveKey(hive).CreateSubKey(ApprovedPath(subKey), true);
        key!.SetValue(valueName, StartupApproved.ApprovedBlob(enable), RegistryValueKind.Binary);
    }

    // 开关/删除共用的错误口径：唯一一份映射，新的「拒绝访问」形态只需在此补一处。
    // COM 的 E_ACCESSDENIED 按 HResult 判（与系统语言无关），文本正则只是最后一层兜底。
    private static string GuardAdminErrors(Func<string> body)
    {
        try { return body(); }
        catch (UnauthorizedAccessException) { return "NeedsAdmin"; }
        catch (System.Security.SecurityException) { return "NeedsAdmin"; }
        catch (Exception ex)
        {
            const int E_ACCESSDENIED = unchecked((int)0x80070005);
            if (ex.HResult == E_ACCESSDENIED || AdminError.IsAccessDenied(ex.Message)) return "NeedsAdmin";
            return "Error: " + ex.Message;
        }
    }

    // 计划任务的开关/删除改走 COM Schedule.Service（与枚举同通道），不再经 schtasks：
    // schtasks 的失败只有本地化文本可供分类，「是不是权限问题」在非中英文系统上只能靠猜——
    // 猜偏哪头都错（认不出的「拒绝访问」丢一键提权；非权限失败被拉去无效提权）。COM 的
    // E_ACCESSDENIED(0x80070005) 语义与系统语言无关，GuardAdminErrors 按 HResult 精确归类，
    // 任务不存在等其他失败则带真实 HRESULT 信息如实上报。
    private static void RunTaskOp(string taskPath, Action<dynamic> op)
    {
        var svcType = Type.GetTypeFromProgID("Schedule.Service") ?? throw new Exception("Schedule.Service unavailable");
        dynamic? svc = null;
        try
        {
            svc = Activator.CreateInstance(svcType);
            svc!.Connect();
            var p = taskPath.TrimEnd('\\');
            dynamic? folder = null;
            try
            {
                folder = svc.GetFolder(p == "" ? "\\" : p);
                op(folder);
            }
            finally { if (folder != null) { try { Marshal.ReleaseComObject((object)folder); } catch { } } }
        }
        finally { if (svc != null) { try { Marshal.ReleaseComObject(svc); } catch { } } }
    }

    // 开关某启动项。返回 Ok / NeedsAdmin / ReadOnly / Error:...。
    public static string SetItemEnabled(SystemStartupItem item, bool enable)
    {
        // 只读项(策略/RunOnce/Winlogon/ActiveSetup 等)一律拒改：它们 RegRunKind 为空，硬写会误建
        // StartupApproved 空子键+无效值。UI 复选框已按 CanEdit 禁用、接管也加了守卫，此处再兜一层源头防护。
        if (!item.CanToggle) return "ReadOnly";
        return GuardAdminErrors(() =>
        {
            switch (item.Type)
            {
                case "Registry": SetApprovedState(item.RegHive, item.RegRunKind, item.ValueName, enable); break;
                case "StartupFolder": SetApprovedState(item.RegHive, "StartupFolder", item.ValueName, enable); break;
                case "ScheduledTask":
                    RunTaskOp(item.TaskPath, folder =>
                    {
                        dynamic task = folder.GetTask(item.TaskName);
                        try { task.Enabled = enable; }
                        finally { try { Marshal.ReleaseComObject((object)task); } catch { } }
                    });
                    break;
            }
            return "Ok";
        });
    }

    // RegRunKind → Run 键实际路径（开关只写 StartupApproved，删除要动真正的 Run 值）。查 RunSpecs 单一来源。
    private static string? RunKeyPath(string hive, string runKind)
    {
        foreach (var s in RunSpecs)
            if (s.Hive == hive && s.RunKind == runKind) return s.Path;
        return null;
    }

    // 彻底删除某启动项（注册表值 / 启动文件夹快捷方式 / 计划任务）。返回 Ok / NeedsAdmin / ReadOnly / Error:...。
    // 错误映射与 SetItemEnabled 共用 GuardAdminErrors；只读项（策略/RunOnce/Winlogon/ActiveSetup 等）一律拒删。
    public static string DeleteItem(SystemStartupItem item)
    {
        if (!item.CanToggle) return "ReadOnly";
        return GuardAdminErrors(() =>
        {
            switch (item.Type)
            {
                case "Registry":
                    var path = RunKeyPath(item.RegHive, item.RegRunKind);
                    if (path == null) return "ReadOnly";
                    using (var key = HiveKey(item.RegHive).OpenSubKey(path, writable: true))
                        key?.DeleteValue(item.ValueName, throwOnMissingValue: false);
                    DeleteApprovedState(item.RegHive, item.RegRunKind, item.ValueName);
                    break;
                case "StartupFolder":
                    if (File.Exists(item.LnkPath))
                    {
                        // 只读属性会让 File.Delete 抛 UnauthorizedAccessException（与权限无关，提权也治不了），
                        // 若不先清位会被映射成 NeedsAdmin、引导用户绕一圈注定无效的 UAC → 先去只读再删。
                        var attrs = File.GetAttributes(item.LnkPath);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(item.LnkPath, attrs & ~FileAttributes.ReadOnly);
                        File.Delete(item.LnkPath);
                    }
                    DeleteApprovedState(item.RegHive, "StartupFolder", item.ValueName);
                    break;
                case "ScheduledTask":
                    RunTaskOp(item.TaskPath, folder => folder.DeleteTask(item.TaskName, 0));
                    break;
                default: return "ReadOnly";
            }
            return "Ok";
        });
    }

    // 主体已删后清掉对应 StartupApproved 记录（残留无害但脏）。失败不影响删除结果，静默。
    private static void DeleteApprovedState(string hive, string subKey, string valueName)
    {
        try
        {
            using var key = HiveKey(hive).OpenSubKey(ApprovedPath(subKey), writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch { }
    }

    // 据系统启动项构造「接管」用的 app 步骤（延迟 2000ms 体现接管价值）。
    public static LaunchStep ToImportedStep(SystemStartupItem item)
    {
        if (item.Type == "StartupFolder")
            return new LaunchStep { Kind = "app", Label = item.Name, Target = item.Command, Args = "", DelayMs = 2000, Enabled = true };
        var p = LaunchTarget.ParseCommandLine(item.Command);
        return new LaunchStep { Kind = "app", Label = item.Name, Target = p.Target, Args = p.Arguments, DelayMs = 2000, Enabled = true };
    }

    internal static (int Code, string Output) RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe", Arguments = args,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        // 并发读两条管道：串行 ReadToEnd(stdout) 再 ReadToEnd(stderr) 会在子进程先写满 stderr 缓冲(~4KB)
        // 时死锁（父等 stdout EOF、子阻塞在 stderr 写）。stdout 异步读、stderr 同步读即可避免。
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = p.StandardError.ReadToEnd();
        var o = outTask.GetAwaiter().GetResult() + err;
        p.WaitForExit();
        return (p.ExitCode, o);
    }
}
