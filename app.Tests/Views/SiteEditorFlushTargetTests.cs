using System;
using System.IO;
using System.Linq;
using Xunit;

// **钉住「落账写的是界面上正装着的那一条」，不是「此刻选中的那一条」。**
//
// 事实经过（2026-10-17）：站点映射表的界面第一次渲染出来是一张**空白表**——
// 传进去的那个站点（名字 LearnData、三个域名、本地源码根、两条改写规则）一条不剩。
// 原因在 OnSelectionChanged 里的两行顺序：
//
//     FlushEditor();   // ← 目标取的是 Current
//     LoadEditor();
//
// 往空列表里加第一行时表格会**自动选中它**，于是 SelectionChanged 先于任何一次显式
// SelectedIndex 赋值就触发了。此刻 Current 已经是新那一行，而右边那一栏还是空的
// （LoadEditor 还没跑）——FlushEditor 于是拿空输入框把这条站点覆盖了一遍：
// 名字、域名、源码根、入口文件全被抹成空串，紧接着 LoadEditor 再把这条被抹空的
// 站点装回界面。站点一条没少，界面上却什么也没有，且不报任何错。
//
// **为什么它不是「无伤大雅」**：这三扇窗（站点表、模板管理器、动作编辑器）都在
// 「取消不留下半截改动」这条纪律上建了工作副本。用户打开表、看见空的、随手一点
// 就确定 —— 整张站点表被清空写回配置。没有任何一步会告诉他发生了什么。
//
// 钉的是**形状**不是实现细节：落账必须有一个「当前装着的」目标，且它不能是「当前选中的」。
public class SiteEditorFlushTargetTests
{
    private static string Src()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        var text = File.ReadAllText(Path.Combine(d!.FullName, "app", "Views", "SiteMapEditorWindow.xaml.cs"));
        // 注释先剥掉：本项目的注释全是 `//` 逐行写的，而解释性注释里往往会原样引用
        // 被断言的那几行——不剥的话，把代码删掉只留注释，断言照样是绿的（同 HookSuspectsTests）。
        return string.Join("\n", text.Split('\n').Where(l =>
        {
            var t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("/*");
        }));
    }

    private static string Body(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"找不到 {signature}");
        return src.Substring(at, 1400);
    }

    [Fact]
    public void The_editor_keeps_track_of_what_it_is_showing()
    {
        var src = Src();
        // 「界面上正装着的那一条」这个概念必须存在，而且是一个独立于 Current 的字段。
        Assert.Contains("private SiteEntry? _loaded;", src);
        // 装载时它取自当前选中项（也就是「换选中项 → 装新的一条」那一步）。
        var load = Body(src, "private void LoadEditor()");
        Assert.Contains("var s = Current;", load);
        Assert.Contains("_loaded = s;", load);
    }

    // 这条是真正的那条：FlushEditor 的目标只允许是 _loaded。
    // 写成 Current 就会在首次自动选中时把刚加载的站点覆盖成空白。
    [Fact]
    public void Flushing_writes_to_what_is_loaded_never_to_the_selection()
    {
        var flush = Body(Src(), "private void FlushEditor()");
        Assert.Contains("var s = _loaded;", flush);
        Assert.DoesNotContain("var s = Current;", flush);
        // 而且落账的入口要挡住「压根没装过任何一条」那次调用。
        Assert.Contains("if (s == null || _loading) return;", flush);
    }
}
