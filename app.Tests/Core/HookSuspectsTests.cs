using System;
using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 钩子链候选名单的守卫。这份测试盯三件事：
//
//   ① **判据**——子串匹配、按「家」归并、去重、排序，以及「一个都没有时写 none」。
//   ② **名单本身的不变量**——空 pattern 会匹配**每一个**进程名（于是那一格永远是一长串，
//      诊断信号直接退化成噪声）；重复 pattern 是抄名单时的复制粘贴残留。两条都没有症状，
//      只有断言能拦住。
//   ③ **接线与顺序**——两处调用点都真的用了它，且都在**摘钩之前**采。
//      这一格记的是「出事那一刻谁在跑」；采晚了就变成「查日志那一刻谁在跑」，
//      而用户往往是关掉某个工具之后才来看日志的——两者不是一回事，而代码里只是上下两行。
public class HookSuspectsTests
{
    // 本机 2026-09-18 真实的一份进程名快照（tasklist 抄下来的），原样留在这里当样本：
    // 它同时钉住「罗技那三个进程归成一家」「PowerToys 那一堆归成一家」和
    // 「GoodSync / explorer / Clockwork 这些不在名单里的不许被算进来」。
    private static readonly string[] ThisMachine =
    {
        "NutstoreDriverSvc", "logioptionsplus_updater", "logioptionsplus_agent",
        "logioptionsplus_logivoice", "logioptionsplus_appbroker",
        "LogiPluginService", "LogiPluginServiceExt",
        "PowerToys", "PowerToys.QuickAccess", "PowerToys.AlwaysOnTop", "PowerToys.ColorPickerUI",
        "PowerToys.CropAndLock", "PowerToys.FancyZones", "PowerToys.GrabAndMove",
        "PowerToys.KeyboardManager",
        "AutoHotkeyUX", "AutoHotkey64",
        "GoodSync", "NutstoreClient", "nutstore_watchdog", "Nutstore.WindowsHook",
        "snipaste", "explorer", "Clockwork", "svchost",
    };

    // 本机的那一份读数：五家在场，顺序是序数排序（大写在前）。
    // 这条断言就是日志里将来会看到的那一格，写死在这里是为了让「名单被改坏了」当场变红。
    [Fact]
    public void The_real_machine_reads_as_five_families()
        => Assert.Equal("AutoHotkey,LogiOptions+,Nutstore,PowerToys,Snipaste",
                        HookSuspects.Describe(ThisMachine));

    // 同一家的多个进程只留一条。逐进程报会把日志那一格撑成一条没人读得完的长串，
    // 而它们本来就是同一个开关——用户要关的是「PowerToys」，不是「PowerToys.GrabAndMove」。
    [Fact]
    public void One_family_appears_once_however_many_processes_it_has()
    {
        Assert.Equal(new[] { "LogiOptions+" },
            HookSuspects.Census(new[] { "logioptionsplus_agent", "LogiOptionsPlus_Updater", "LogiPluginService", "LogiPluginServiceExt" }));
        Assert.Equal(new[] { "PowerToys" },
            HookSuspects.Census(new[] { "PowerToys", "PowerToys.AlwaysOnTop", "PowerToys.FancyZones" }));
    }

    // 大小写不敏感：进程名的实际大小写在各版本间会变（logioptionsplus_agent 全小写、
    // LogiPluginService 驼峰），而名单是按小写写的。
    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal(new[] { "AutoHotkey" }, HookSuspects.Census(new[] { "AUTOHOTKEY64" }));
        Assert.Equal(new[] { "AutoHotkey" }, HookSuspects.Census(new[] { "autohotkeyux" }));
    }

    // **一个都没有时必须写 none，不能写空。** 那一格留空的话，读日志的人分不出
    // 「当时链上确实没别人」和「这一版还没采这一格」——而这两种情况该引出的结论完全相反。
    [Fact]
    public void An_empty_census_is_named_not_left_blank()
    {
        Assert.Equal("none", HookSuspects.Describe(Array.Empty<string>()));
        Assert.Equal("none", HookSuspects.Describe(new[] { "explorer", "svchost", "Clockwork" }));
        Assert.Empty(HookSuspects.Census(new[] { "explorer" }));
    }

    // null / 空串混进来不许把它变成「全部匹配」或抛出去：调用方那份名单来自系统枚举，
    // 里面混进空项是正常事，而这一格只是诊断信息，不值得为它中断自愈。
    [Fact]
    public void Blank_entries_are_skipped()
    {
        Assert.Equal(new[] { "Snipaste" }, HookSuspects.Census(new[] { "", null!, "snipaste" }));
    }

    // 序数排序（不是当前区域性排序）：日志那一格要在任何语言环境下都是同一个顺序，
    // 否则「同一台机器两次自愈的 suspects= 能不能直接比」就成了一件要靠眼睛的事。
    [Fact]
    public void The_order_is_ordinal_not_culture_dependent()
        => Assert.Equal("AutoHotkey,Snipaste", HookSuspects.Describe(new[] { "snipaste", "AutoHotkey64" }));

    // ── 名单本身的不变量 ──

    // **空 pattern 会匹配每一个进程名**，于是 Census 会把整份名单原样返回、Describe 永远是一长串。
    // 症状是「这一格看起来一直很热闹」，而它本该只在真出事时才有内容——没有任何东西会报错。
    [Fact]
    public void No_pattern_is_blank()
    {
        Assert.All(HookSuspects.KnownPatterns, p => Assert.False(string.IsNullOrWhiteSpace(p.Pattern),
            "空 pattern 会匹配所有进程名，那一格就再也不是信号了"));
        Assert.All(HookSuspects.KnownPatterns, p => Assert.False(string.IsNullOrWhiteSpace(p.Family)));
    }

    // 同一个 pattern 出现两次 = 抄名单时的复制粘贴残留。它不会改变结果（去重兜住了），
    // 所以只会在下次有人改名单时让人以为改过了——正好是这类清单最难发现的一种腐坏。
    [Fact]
    public void No_pattern_is_listed_twice()
    {
        var dup = HookSuspects.KnownPatterns
            .GroupBy(p => p.Pattern, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.Empty(dup);
    }

    // ── 接线 ──

    // 自愈那条路必须采这一格，且必须在**摘钩之前**采。摘钩之后才采的话，读到的仍然是同一份
    // 进程列表（进程不会因为我们摘钩就退出），但这条断言要拦的不是读不到，而是**顺序被挪了**：
    // 本文件既有的规矩是「摘钩前抄账」（见 HealMouseHookIfDead 里那几格 _prev*），
    // 新加的一格跟着挪到摘钩之后，就会在将来某次「采到的其实是重装之后的状态」上咬人。
    [Fact]
    public void The_heal_path_captures_the_census_before_disposing_the_hook()
    {
        var app = Code(AppSource());
        int heal = app.IndexOf("private void HealMouseHookIfDead");
        Assert.True(heal >= 0, "找不到 HealMouseHookIfDead：这段守卫要跟着它走");
        int capture = app.IndexOf("_prevSuspects = HookSuspects.Describe(", heal, StringComparison.Ordinal);
        int dispose = app.IndexOf("DisposeMouseHook(\"heal\")", heal, StringComparison.Ordinal);
        Assert.True(capture > heal, "自愈那条路没有采在场名单");
        Assert.True(dispose > capture, "在场名单必须在摘钩之前采——「出事那一刻谁在跑」与「重装之后谁在跑」不是一回事");
    }

    // 两处记账点都要填，少一处那一路的日志就永远是 `suspects=-`：
    // 手动「重新挂钩」那几下恰恰是用户排障时按的，最需要知道当时链上都有谁。
    [Fact]
    public void Both_accounting_sites_capture_the_census()
    {
        var app = Code(AppSource());
        Assert.Equal(2, app.Split("_prevSuspects = HookSuspects.Describe(").Length - 1);
    }

    // 日志行真的把它写出来了，而且用的是 `suspects=` 这个名字——它是「候选」不是「元凶」，
    // 名字写错会让人把在场当成作案（理由见 Core.HookSuspects 的类注释）。
    [Fact]
    public void The_reinstall_line_carries_the_census()
    {
        var app = Code(AppSource());
        Assert.Contains("$\"suspects={_prevSuspects ?? \"-\"}\"", app);
        Assert.Contains("_prevSuspects = null;", app);   // 只用一次，别污染下一条非自愈的记录
    }

    // 探针也要报：它是用户**主动**去问「这台机器上钩子链里都有谁」的唯一入口，
    // 而上游延迟那一格单独看只能得出「有人堵」，得配上这一格才知道去关谁。
    [Fact]
    public void The_hook_probe_reports_the_census()
    {
        var dev = Code(DevChecksSource());
        Assert.Contains("钩子链候选（此刻在跑）", dev);
        Assert.Contains("HookSuspects.Describe(HookSuspects.RunningProcessNames())", dev);
    }

    // ── helpers ──

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string AppSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "App.xaml.cs"));

    private static string DevChecksSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "app", "DevChecks.cs"));

    // 注释先剥掉再断言：本项目的注释全是 `//` 逐行写的（没有块注释），而解释性注释里
    // 往往会原样引用被断言的那段代码——不剥的话，把代码删掉、只留注释，断言照样是绿的。
    private static string Code(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
           {
               var t = l.TrimStart();
               return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
           }));
}
