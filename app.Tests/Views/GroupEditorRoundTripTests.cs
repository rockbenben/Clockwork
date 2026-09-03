using System.IO;
using System.Linq;
using Clockwork.Core;
using Xunit;

// 组编辑器点一次「确定」是**整份重建** ActionGroup（GroupEditorWindow.Ok_Click 里那个
// 对象初始化器），而它只有十几个具名字段。漏掉的那些在保存时就被抹掉了，
// 而且本窗口根本不显示它们——丢的那一下完全看不见。
//
// 实际发生过：PanelTab 和 PanelExpand 都不在那份初始化器里。于是在面板管理器里
// 把一页归进「写代码」，之后在动作组页点一次「确定」（什么都没改），那一页就掉回未分类；
// 若它是那一类的最后一页，整个分类连同顶栏那一格一起消失。
//
// 这条用反射钉住不变式本身，而不是钉某几个字段名：以后再往 ActionGroup 上加字段，
// 忘了在那份初始化器里带上它，这条就会红。
public class GroupEditorRoundTripTests
{
    // 与 StringKeyReferenceTests 同一个找法：从测试程序集往上走到含 app/Resources 的那一层。
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // Ok_Click 的初始化器实际带过去的字段。加了新字段却没加到这里，说明那个字段会被抹掉。
    private static readonly string[] Carried =
    {
        nameof(ActionGroup.Id), nameof(ActionGroup.Name), nameof(ActionGroup.Enabled),
        nameof(ActionGroup.Hotkey), nameof(ActionGroup.Gesture), nameof(ActionGroup.Repeat),
        nameof(ActionGroup.RepeatDelayMs), nameof(ActionGroup.ShowInTray), nameof(ActionGroup.ShowInPanel),
        nameof(ActionGroup.PanelForProcess), nameof(ActionGroup.PanelTab), nameof(ActionGroup.PanelExpand),
        nameof(ActionGroup.Icon), nameof(ActionGroup.Steps),
    };

    [Fact]
    public void Every_persisted_field_is_carried_across_an_edit()
    {
        var missing = typeof(ActionGroup).GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .Where(n => !Carried.Contains(n))
            .ToList();
        Assert.True(missing.Count == 0,
            "ActionGroup 上这些字段没有出现在 GroupEditorWindow.Ok_Click 的重建里，"
            + "点一次「确定」就会被抹掉：" + string.Join(", ", missing));
    }

    // 上面那份清单不是随手抄的：源码里那个初始化器必须真的写着它们。
    [Fact]
    public void The_list_matches_what_the_editor_actually_writes()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Views", "GroupEditorWindow.xaml.cs"));
        int i = src.IndexOf("var candidate = new ActionGroup");
        Assert.True(i > 0, "找不到 Ok_Click 里那个初始化器——它被改写了，这两条测试要跟着改。");
        var body = src.Substring(i, src.IndexOf("};", i) - i);
        foreach (var f in Carried)
            Assert.True(body.Contains(f + " ="), $"初始化器里没有 {f} =");
    }
}
