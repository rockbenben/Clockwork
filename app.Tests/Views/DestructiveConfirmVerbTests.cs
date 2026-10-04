using System.IO;
using System.Linq;
using Xunit;

// **破坏性确认的按钮不许再印「是 / 否」。**
//
// 「是 / 否」把决定权推回给用户之前先要他做一遍阅读理解：红轨、Warn 语气、一句长问句，
// 然后两颗按钮叫「是」和「否」——要点哪个，得把题干再读一遍。动词（删除 / 留着）自己就说清了后果。
//
// 这条判据只在**系统发起的破坏性确认**上立案。提醒卡上那两颗「是 / 否」保留：
// 那里的问句是用户自己写的（「今天锻炼了吗？」），答案本来就是一一个是/否的问题，
// 换成动词反而是替用户说话。所以闸门数的是参数，不是文案——
// 一个 Warn 级 Confirm 必须自己把两颗按钮的标签传进来，编译器与这条测试一起拦着漏传。
public class DestructiveConfirmVerbTests
{
    private static IEnumerable<string> Sources()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "app", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        return Directory.EnumerateFiles(Path.Combine(root!.FullName, "app"), "*.cs", SearchOption.AllDirectories)
                        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    // 取出某个调用括号内的实参串（按引号与嵌套括号配平）。
    private static string? Args(string src, int openParen)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = openParen; i < src.Length; i++)
        {
            var c = src[i];
            if (inStr) { if (c == '"') inStr = false; else if (c == '\\') i++; continue; }
            if (c == '"') { inStr = true; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(openParen + 1, i - openParen - 1);
            }
        }
        return null;
    }

    private static int TopLevelCommas(string args)
    {
        int depth = 0, n = 0;
        bool inStr = false;
        for (int i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (inStr) { if (c == '"') inStr = false; else if (c == '\\') i++; continue; }
            if (c == '"') { inStr = true; continue; }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0) n++;
        }
        return n;
    }

    [Fact]
    public void Every_warn_level_confirm_passes_its_own_button_verbs()
    {
        var offenders = new List<string>();
        foreach (var file in Sources())
        {
            var src = File.ReadAllText(file);
            for (int i = src.IndexOf("Confirm(", StringComparison.Ordinal); i >= 0;
                 i = src.IndexOf("Confirm(", i + 1, StringComparison.Ordinal))
            {
                var args = Args(src, i + "Confirm".Length);
                if (args == null) continue;
                bool destructive = args.Contains("ToastLevel.Warn");
                // 6 个实参 = owner / title / message / level / yes / no。少传的就是还在吃默认「是 / 否」。
                if (destructive && TopLevelCommas(args) < 5)
                    offenders.Add($"{Path.GetFileName(file)}:{src.Substring(0, i).Count(ch => ch == '\n') + 1}: {args.Trim().Split('\n')[0][..Math.Min(90, args.Trim().Split('\n')[0].Length)]}");
            }
        }
        Assert.True(offenders.Count == 0,
            "破坏性确认（Warn 轨）必须自己传两颗按钮的动词，不能用默认的「是 / 否」：\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_delete_helper_is_the_single_place_that_wording_lives()
    {
        // ConfirmDelete 是删除确认的单一口径：它必须传动词，否则三张列表页 + 系统页 + 组编辑器
        // 会一起退回「是 / 否」而上面那条测试照样绿（它只看调用点参数）。
        var src = File.ReadAllText(Sources().First(f => f.EndsWith(Path.Combine("Views", "BrandDialog.xaml.cs"))));
        Assert.Contains("Strings.Get(\"Btn_Delete\"), Strings.Get(\"Confirm_Keep\")", src);
    }
}
