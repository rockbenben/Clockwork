using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 启动目标解析纯 helpers（命令行拆分 / 进程名推导 / 备用路径 / 自指判断）。
public static class LaunchTarget
{
    public sealed record CommandLine(string Target, string Arguments);

    // 拆 Run 键命令行 → Target/Arguments。首字符引号则取引号内为 Target；否则第一个空白前为 Target。
    public static CommandLine ParseCommandLine(string commandLine)
    {
        var s = commandLine ?? "";
        if (string.IsNullOrWhiteSpace(s)) return new CommandLine("", "");
        s = s.Trim();
        string target, rest;
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return new CommandLine(s.Trim('"'), "");
            target = s.Substring(1, end - 1);
            rest = s.Substring(end + 1).Trim();
        }
        else
        {
            int idx = s.IndexOfAny(new[] { ' ', '\t' });
            if (idx < 0) { target = s; rest = ""; }
            else { target = s.Substring(0, idx); rest = s.Substring(idx + 1).Trim(); }
        }
        return new CommandLine(target, rest);
    }

    // 同上，但对「不带引号」的命令行补一次探盘。注册表 Run 键和启动文件夹里的值经常不加引号，
    // 而路径带空格是常态，于是纯按空格切会把
    //     C:\Program Files\MS USB Display\WinUsbDisplay.exe
    // 切成 Target="C:\Program"。接管流程是「先禁用原启动项、再导入这条步骤」，所以截断的后果不是
    // 「少了个参数」，而是原程序从此彻底不再开机启动——两头落空。
    // Windows 自己解析这类值时是逐段延长着试（C:\Program.exe → C:\Program Files\MS.exe → …）直到命中
    // 真实存在的文件，这里补的就是同一套。ParseCommandLine 保持纯函数不动：它另有按「纯切分」语义使用的地方。
    // 只认 File.Exists 不认目录——否则 "C:\Program Files" 作为目录先命中，反而把路径切得更碎。
    public static CommandLine ParseCommandLineProbing(string commandLine)
    {
        var s = (commandLine ?? "").Trim();
        if (s == "" || s[0] == '"') return ParseCommandLine(s);   // 带引号：切分已无歧义
        // 从第一个空格处起逐段延长；i<0 那轮试的是「整串都是路径、没有参数」。
        // 第一轮的候选就等于 ParseCommandLine 的切法，所以不必再单独早退一次。
        for (int i = s.IndexOf(' '); ; i = s.IndexOf(' ', i + 1))
        {
            int cut = i < 0 ? s.Length : i;
            var cand = s.Substring(0, cut);
            var rest = cut < s.Length ? s.Substring(cut + 1).Trim() : "";
            if (File.Exists(cand)) return new CommandLine(cand, rest);
            if (File.Exists(cand + ".exe")) return new CommandLine(cand + ".exe", rest);
            if (i < 0) break;
        }
        return ParseCommandLine(s);   // 一个都没命中（裸程序名如 rundll32.exe、或文件已被删）：退回纯切分，照常尝试/报错
    }

    // 目标 → 进程名（不含扩展名），供「已运行则激活窗口」判断。网址/文档/脚本/快捷方式（进程名与目标名不一致）返回 ''。
    public static string TargetProcessName(string target)
    {
        var t = target ?? "";
        if (string.IsNullOrWhiteSpace(t)) return "";
        if (Regex.IsMatch(t, @"^\s*[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)) return "";   // 网址
        string leaf;
        try { leaf = Path.GetFileName(t); } catch { leaf = t; }
        string ext;
        try { ext = Path.GetExtension(leaf); } catch { ext = ""; }
        if (ext == "" || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(leaf);
        return "";   // .ps1/.bat/.lnk/文档 等：进程名无法可靠推导，交给手填
    }

    /// <summary>目标 → 给人看的名字（去路径、去扩展名）。步骤没起名时用它顶上。</summary>
    //
    // 与 TargetProcessName 是两件事，别合并：那个推的是**进程名**，用来判「已经在跑了吗」，
    // 所以对 .lnk / 文档 / 脚本一律返回空——推不准的宁可不说。而这里只是找一个能读的名字，
    // 没有「推错就出事」那一层，尽力而为即可。
    //
    // 为什么需要它：没起名的「运行程序」步骤在列表里显示的是整条 Target，而那一列是右侧截断的——
    //   C:\Users\Administrator\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\repo-radar.lnk
    // 截出来是「C:\Users\Administrator\AppData\Roaming\Microsoft\Wind…」，**能区分它的那一截恰好被切掉**，
    // 一屏开始菜单快捷方式长得一模一样。取叶子名就是 repo-radar，正是用户心里叫它的那个名字。
    //
    // 扩展名一律去掉：.lnk / .exe 是纯噪声，而文档的扩展名虽然有点信息，为它单开一张白名单
    // 不值得——真要看是什么文件，双击进编辑器，那儿有完整路径。
    // 网址原样返回：它本身就是可读的，砍成一个词反而把信息丢了。
    public static string DisplayName(string? target)
    {
        var t = NormalizeTarget(target);
        if (t.Length == 0) return "";
        // 协议判据要的是「冒号」而不是「://」：mailto:、ms-settings:、steam: 这些都没有斜杠，
        // 漏判的后果是拿路径规则去切它——实测 mailto:someone@example.com 被剥成
        // mailto:someone@example（".com" 被当成扩展名）。
        // 而 `+` 不是 `*` 是承重的：它要求冒号前至少两个字符，于是盘符 C:\ 只有一个字符、落不进这一档。
        if (Regex.IsMatch(t, @"^[a-z][a-z0-9+.-]+:", RegexOptions.IgnoreCase)) return t;
        try
        {
            // 去掉尾部分隔符，否则「打开文件夹 D:\Work\」取到的叶子是空串
            var trimmed = t.TrimEnd('\\', '/');
            if (trimmed.Length == 0) return t;
            var stem = Path.GetFileNameWithoutExtension(trimmed);
            if (stem.Length > 0) return stem;
            // 走到这儿只剩两种：盘根（D:\）、或整个名字都是扩展名（.gitignore）。
            // 前者带回原样，后者带回叶子——都比返回空串强，空串在列表里就是一行没有内容的行。
            var leaf = Path.GetFileName(trimmed);
            return leaf.Length > 0 ? leaf : t;
        }
        catch { return t; }   // 路径里有非法字符：原样给出去，让用户自己看出哪里写错了
    }

    // 目标框里粘进来的写法规范化。三件事都对应真实的粘贴来源，任何一条不做，路径「看着对」却会失配：
    //   去首尾空白 —— 从聊天窗口/文档里复制常带尾随空格或换行；
    //   去成对引号 —— 资源管理器 Shift+右键「复制文件地址」给的就是带引号的；
    //   展开 %VAR% —— %USERPROFILE%\x.ps1 这类写法 ShellExecute 不认，埋进 -File 更不认；
    // 只脱成对引号（不是 Trim('"')）：单边引号是用户写错了，原样留着让报错指出来，别猜。
    // 所有解析/启动路径都从这里过一遍——.ps1 的分支判定尤其吃这个：带引号的目标结尾不是 .ps1，
    // 会被当成普通文档交给文件关联，于是「运行脚本」变成「用记事本打开脚本」。
    public static string NormalizeTarget(string? target)
    {
        var t = (target ?? "").Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"') t = t.Substring(1, t.Length - 2).Trim();
        try { t = Environment.ExpandEnvironmentVariables(t); } catch { }
        return t;
    }

    // 备用路径解析：目标是完整路径且不存在时，返回 altTargets(每行一条) 里第一个存在的候选；都不存在则返回原目标。
    // 目标非完整路径(裸程序名/网址/文档关联)时原样返回。
    // 「存在」= 文件或目录（对齐旧 PS 版 Test-Path 语义）：打开文件夹的步骤（双机 D:\Work / E:\Work）目录候选也要能匹配。
    /// <param name="blocking">true = 真去问磁盘（启动前必须如此）；
    /// false = 走 <see cref="PathProbe"/> 的不阻塞版本，供**取图标**这条跑在 UI 线程上的路使用。</param>
    //
    // 分成两档不是洁癖：这个方法做的是 File.Exists / Directory.Exists，而取图标那条路
    // 全在 UI 线程上（面板每次呼出、管理器每次重画、三个编辑器的预览）。路径是用户填的，
    // 可以指向一个已断开的网络盘——那一次探测会卡满 SMB 超时，而 UI 线程同时是低级鼠标钩子的泵，
    // 占住超过 300ms 就会让 Windows 把钩子静默卸掉（IconLoader 为此早已改成一秒都不等）。
    // 真要启动程序时仍走 blocking：那发生在工作线程上，等得起，而且那时必须是真答案。
    public static string ResolveLaunchTarget(string target, string altTargets, bool blocking = true)
    {
        var t = NormalizeTarget(target);
        bool rooted;
        try { rooted = Path.IsPathRooted(t); } catch { rooted = false; }
        if (!rooted) return t;                       // 裸程序名/网址/文档：不动
        bool Exists(string p) => blocking ? PathExists(p) : PathProbe.ExistsOptimistic(p);
        if (Exists(t)) return t;                     // 主路径存在：用它
        foreach (var line in (altTargets ?? "").Split('\n'))
        {
            var c = NormalizeTarget(line);
            if (c != "" && Exists(c)) return c;      // 第一个存在的备用路径
        }
        return t;                                    // 都不存在：返回原目标（照常尝试/报错）
    }

    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    public const string PowerShellExe = "powershell.exe";   // Windows PowerShell 5.1，系统自带
    public const string PwshExe = "pwsh.exe";               // PowerShell 7+，需另装

    // 目标是否 PowerShell 脚本(.ps1)——须经 powershell.exe 运行；直接 ShellExecute 会按文件关联进编辑器而非执行。
    public static bool IsPowerShellScript(string? target)
        => !string.IsNullOrEmpty(target) && Regex.IsMatch(target, @"\.ps1$", RegexOptions.IgnoreCase);

    // 构造 powershell.exe 运行 .ps1 的参数串：-NoProfile -ExecutionPolicy Bypass -File "路径" [附加参数]。
    public static string PowerShellFileArgs(string target, string? extraArgs = null)
        => $"-NoProfile -ExecutionPolicy Bypass -File \"{target}\"" + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);

    // 脚本是否必须交给按 UTF-8 解码的解释器（PowerShell 7 的 pwsh.exe）。
    // Windows PowerShell 5.1 对「没有 BOM」的脚本一律按系统 ANSI 代码页解码——中文系统上是 GBK。
    // 于是现在最常见的存法（编辑器和 AI 生成的默认就是无 BOM UTF-8）只要带中文就会被解错：
    // 轻则字符串变乱码，重则乱码字节改变语法结构，整个文件在解析阶段就崩、脚本一行都执行不到。
    // 判定要求三条同时成立，缺一条都不能换解释器：
    //   无 BOM       —— 带 BOM 的文件 5.1 自己认得，换过去没意义；
    //   含非 ASCII   —— 纯 ASCII 在任何代码页下解出来都一样，5.1 本来就对；
    //   是合法 UTF-8 —— 老的 ANSI/GBK 脚本同样无 BOM 含非 ASCII，但它在 5.1 下本来就是对的，
    //                   交给按 UTF-8 解码的 pwsh 反而会变成乱码。这一条是防倒退的关键。
    public static bool NeedsUtf8PowerShell(string? scriptPath)
    {
        byte[] b;
        try { b = File.ReadAllBytes(scriptPath ?? ""); } catch { return false; }
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return false;   // UTF-8 BOM
        if (!b.Any(x => x >= 0x80)) return false;
        // GetCharCount 而非 GetString：要的只是「会不会抛」，解出整个文件的字符串随手丢掉是白白多分配一份等大内存。
        // UTF-16 文件不必单独判：其 BOM（FF FE / FE FF）永远不是合法 UTF-8 首字节，走到这里必抛，照样返回 false。
        try { _ = new UTF8Encoding(false, true).GetCharCount(b); return true; }   // 解得动 = 是 UTF-8 = 5.1 会解错
        catch { return false; }                                                  // 解不动 = ANSI/GBK 老脚本 = 5.1 才是对的
    }

    // pwsh.exe（PowerShell 7+）的完整路径；没装返回 null。整个进程只探一次。
    //
    // 标准安装位置排在 PATH 前面：装了 pwsh 的机器上头几条就命中，不必先把整条 PATH（典型 25~45 条）探完。
    // 但 PATH 这一段不能省——Clockwork 常由开机自启拉起，那时的环境块未必已经带上安装目录；
    // 反过来，PATH 里若有失效的映射盘或 UNC，File.Exists 会阻塞在 SMB 超时上（秒级到数十秒），
    // 而这正好发生在开机、网络栈还没起来的时候，所以更要靠前面的标准位置先命中、以及下面的结果缓存
    // ——否则开机清单里有 N 个无 BOM 中文脚本，就要把这套扫描连做 N 遍。
    // 用 Lazy 而不是「一个结果字段 + 一个探过了标志」：那两个字段是**双检失败**的经典形状。
    // 原来的写法先把 _pwshProbed 置真、再去跑上面说的那套可能阻塞几十秒的扫描，于是并发时：
    // A 进来置真、卡在 File.Exists 的 SMB 超时里；B 看见「探过了」拿走仍是 null 的 _pwsh，
    // 于是在一台**装了** pwsh 的机器上报「请安装 PowerShell 7 或给脚本加 BOM」。
    // 并发是常态而不是理论：两个动作组各自在自己的 Task 上跑（WindowManager 那边的注释也提到
    // 「两个动作组同时在跑」），各带一个无 BOM 的 .ps1 步骤就够了。
    // 两个普通字段也没有 volatile，写入顺序本身都不保证。
    //
    // ExecutionAndPublication：后到的线程**等**第一个探完再拿结果，而不是各探一遍——
    // 这正是原注释想要的「整个进程只探一次」，只有这个模式真的做到。
    private static readonly Lazy<string?> _pwsh =
        new(() => PwshCandidates().FirstOrDefault(File.Exists), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? FindPwsh() => _pwsh.Value;

    private static IEnumerable<string> PwshCandidates()
    {
        foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            string root;
            try { root = Environment.GetFolderPath(f); } catch { continue; }
            if (root == "") continue;
            yield return Path.Combine(root, "PowerShell", "7", PwshExe);
            yield return Path.Combine(root, "Microsoft", "WindowsApps", PwshExe);   // Store 版
        }
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "")
                          .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(d, PwshExe);
    }

    // 运行该 .ps1 该用哪个解释器。默认仍是 powershell.exe——不动任何本来就跑得通的脚本；
    // 只有 5.1 必然解码失败的那类（见 NeedsUtf8PowerShell）才要求 pwsh，没装则返回 null，
    // 由调用方给出「装 PowerShell 7 或给脚本加 BOM」的提示，而不是交给必然失败的 5.1 去撞一个没人看得懂的退出码。
    public static string? PowerShellExeFor(string? scriptPath)
        => NeedsUtf8PowerShell(scriptPath) ? FindPwsh() : PowerShellExe;

    // 目标路径是否就是 Clockwork 自身（防开机自启动循环）。规范化后大小写不敏感比较。
    public static bool IsSelfTarget(string target, IEnumerable<string> selfPaths)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string tf;
        try { tf = Path.GetFullPath(target); } catch { return false; }
        foreach (var sp in selfPaths)
        {
            if (string.IsNullOrWhiteSpace(sp)) continue;
            string sf;
            try { sf = Path.GetFullPath(sp); } catch { continue; }
            if (string.Equals(tf, sf, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
