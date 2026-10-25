using System;
using System.IO;
using System.Linq;
using Xunit;

// 摘钩那一行的 source= 是日志里唯一的分支点。
//
// 它把两件**表现完全一样**的事分开：自愈摘钩（真故障）与用户/开机摘钩（正常动作）。
// 分错了的后果是读日志的人去问用户「你当时点了吗」——而用户答不上来。
//
// 写死成 "manual" 的那版正是这么坏的：开机清单跑完后的自动抢链首
// （RehookMouseAfterBoot → RehookMouse("boot")）在摘钩那一行也被记成了 manual。
// 用户机器上的 clockwork.error.log 里，7 次开机重装全是
// 「mouse hook detached: source=manual」配「mouse hook reinstalled: source=boot」——
// 同一件事，两种说法。读日志的人于是以为用户在反复手动点「重新挂钩」，去查一个不存在的故障。
//
// 这里盯的是**调用形态**（跟 ReplayOffCallbackTests 同一手法）：盯字面量会被注释与死代码骗过。
public class HookDetachSourceTests
{
    private static string AppRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);   // 找不到就是布局变了，宁可红也别静默跳过
        return Path.Combine(d!.FullName, "app");
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppRoot() }.Concat(parts).ToArray()));

    // 去掉整行注释再断言：本项目的注释里常引用被断言的那几行原文，不剥的话会被自己的说明文字判红。
    private static string Code(string src)
        => string.Join("\n", src.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));

    // 取某个方法体：从它的签名到下一个同缩进的成员为止。
    private static string BodyOf(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"找不到 {signature}");
        int end = src.IndexOf("\n    /// <summary", start + signature.Length);
        if (end < 0) end = src.Length;
        return src.Substring(start, end - start);
    }

    // RehookMouse 必须把**调用方给的** source 传下去。
    [Fact]
    public void Rehook_passes_its_own_source_to_the_detach_line()
    {
        var body = Code(BodyOf(Source("Services", "GestureCoordinator.cs"),
                               "public void RehookMouse(string source = \"manual\")"));
        Assert.Contains("DisposeMouseHook(source)", body);
    }

    // 全仓再不许有人往 DisposeMouseHook 里塞字面量 "manual"：
    // 写死一次，那条唯一的分支点就又分不出「手动」与「开机自动」了。
    [Fact]
    public void No_call_site_labels_a_detach_as_manual_by_hand()
    {
        var hits = new System.Collections.Generic.List<string>();
        foreach (var f in Directory.EnumerateFiles(AppRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (var raw in File.ReadLines(f))
            {
                var t = raw.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;   // 注释不算
                if (t.Contains("DisposeMouseHook(\"manual\")"))
                    hits.Add($"{Path.GetFileName(f)}: {t.Trim()}");
            }
        }
        Assert.True(hits.Count == 0,
            "摘钩那一行的 source 必须由调用方给（manual / boot / heal），不能写死：\n"
            + string.Join("\n", hits));
    }

    // 三种 source 都得真的用得上：开机抢链首那条路传的是 "boot"。
    [Fact]
    public void The_boot_rehook_says_boot()
    {
        var src = Source("Services", "GestureCoordinator.cs");
        Assert.Contains("RehookMouse(\"boot\")", Code(src));
    }
}
