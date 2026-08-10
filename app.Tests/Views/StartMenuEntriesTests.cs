using System.IO;
using Clockwork.Views;
using Xunit;

// 「从开始菜单选择…」的扫描。碰真实文件系统，但只读、且对空环境自跳过。
public class StartMenuEntriesTests
{
    // 容错递归：逐目录枚举，读不了的跳过。用它算出「这台机器上到底有多少个 .lnk 是拿得到的」，
    // 作为 StartMenuEntries 的独立参照——不复用被测代码的任何枚举选项，否则同样的错会同时骗过两边。
    private static int ReachableShortcuts(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
        int n = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try { n += Directory.GetFiles(dir, "*.lnk").Length; } catch { }
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    // 跳过重解析点，与被测代码同口径：那些联结会把同一棵树数第二遍。
                    try { if (new DirectoryInfo(sub).LinkTarget != null) continue; } catch { continue; }
                    stack.Push(sub);
                }
            }
            catch { }
        }
        return n;
    }

    // 回归：两处开始菜单根目录下都有一个拒绝访问的旧版本地化联结（简中系统上叫「程序」）。
    // 曾经用 Directory.EnumerateFiles(..., SearchOption.AllDirectories) 扫，那个重载的「兼容」选项
    // IgnoreInaccessible=false，撞上它整根抛异常、被 catch 吞掉，于是三百多个快捷方式一个都没列出来，
    // 用户看到的是「没有在开始菜单里找到任何程序快捷方式」。
    // 只要这台机器上确实有拿得到的 .lnk，扫描结果就不能是空的。
    [Fact]
    public void Inaccessible_subfolders_do_not_wipe_out_the_whole_scan()
    {
        int reachable = ReachableShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu))
                      + ReachableShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        if (reachable == 0) return;   // 精简/容器环境里开始菜单可能真是空的，这时无从判断，跳过

        Assert.NotEmpty(Pickers.StartMenuEntries());
    }

    // 回归：AppsFolder 里混着开始菜单的 .url 条目，它们的「AUMID」本身就是一条网址。
    // 给网址套上 shell:AppsFolder\ 会做出一个打不开的目标，必须原样当网址用。
    [Fact]
    public void Url_entries_are_not_wrapped_in_the_appsfolder_prefix()
    {
        foreach (var (_, path) in Pickers.StartMenuEntries())
            if (path.StartsWith(Pickers.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                Assert.DoesNotContain("://", path.Substring(Pickers.AppsFolderPrefix.Length));
    }

    // 打包 / Store 应用（便笺、画图这一类）没有 .lnk，只扫开始菜单文件夹是永远看不到它们的。
    // 有 AppsFolder 就必须有对应条目——这是「从开始菜单选择」能不能覆盖用户看得见的全部应用的分界线。
    [Fact]
    public void Packaged_apps_without_a_shortcut_are_included()
    {
        int packaged = 0;
        try
        {
            var progId = Type.GetTypeFromProgID("Shell.Application");
            if (progId == null) return;   // 无 Shell COM 的环境（容器 / 受限令牌）：无从判断，跳过
            dynamic? shell = Activator.CreateInstance(progId);
            dynamic? folder = shell?.NameSpace("shell:AppsFolder");
            if (folder == null) return;
            foreach (dynamic item in folder.Items())
                try { if ((item.Path as string ?? "").Contains('!')) packaged++; } catch { }
        }
        catch { return; }
        if (packaged == 0) return;   // 这台机器真没有打包应用

        var fromAppsFolder = Pickers.StartMenuEntries()
            .Count(x => x.Path.StartsWith(Pickers.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.True(fromAppsFolder > 0, $"AppsFolder 里有 {packaged} 个打包应用，选择器却一个都没列出来");
    }

    // 两种合法目标：开始菜单里的 .lnk，以及打包 / Store 应用的 shell:AppsFolder\<AUMID>。
    // 后者没有文件形式，只能靠这个前缀交给 ShellExecute。
    [Fact]
    public void Entries_are_either_a_shortcut_or_an_appsfolder_target()
    {
        foreach (var (name, path) in Pickers.StartMenuEntries())
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.True(
                path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(Pickers.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)
                || path.Contains("://", StringComparison.Ordinal),   // AppsFolder 里混着的 .url 条目
                $"既不是 .lnk、AppsFolder 目标，也不是网址：{path}");
        }
    }

    // 显示名去重：同一个名字只留一条，否则清单里会出现两行一模一样的条目。
    [Fact]
    public void Display_names_are_unique()
    {
        var names = Pickers.StartMenuEntries().Select(x => x.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
