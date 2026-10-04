using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Clockwork.I18n;
using Xunit;

// **定宽下拉装不装得下它自己的候选项——量出来，别等截图发现。**
//
// 触发这件事的是一张实拍：德语 „Keine Einschränkung“ 在 150px 的框里被切成
// „Keine Einschränkun“——最后一个字母没了。那不是"看起来挤"，是**读到一个不存在的词**。
// 截图只拍得到被选中的那一条，没被选中的候选项一张图都不会露脸；而译文只会越写越长。
//
// 判据 = 读 XAML 里的 Width + 用 WPF 自己的排版量一次，不需要跑起来：
//   可用宽 = Width − 40（模板里选中区 Margin="10,0,28,0"，再加 1px 边框×2）
//   候选宽 = FormattedText 在该语言文本上的实际宽度（字号取 FsBody=12）
// 弹层不参与：它的 Border 用 MinWidth，会自己长到够宽，列表侧本来就不会裁。
//
// 真装不下的（系统命令下拉：泰语那条带括号解释的要 800px）进 Trims 名单并写明理由——
// 那种场合选中框走省略号、点开看全文，是设计而不是事故。名单里的框若哪天其实装得下了，
// 另一条用例会红，逼着名单跟着代码走，而不是攒成一堆没人敢删的例外。
public class ComboFitTests
{
    private const int Chrome = 40;
    private const double Body = 12;

    private sealed record Box(string File, string Name, string[] Keys);

    // 候选项清单对应代码里的 FillCombo 调用点；改了下拉要同步这里。
    // 只列**候选项来自 resx** 的框。装动作组名 / 启动项名的那几个（GroupCombo、SilentCombo、
    // OnYesGroupCombo、RefCombo）内容长度由用户输入决定，天生没有"够宽"的数——
    // 它们靠选中区的省略号兜，不归这条测试管。
    private static readonly Box[] Boxes =
    {
        new("StepEditorWindow.xaml", "KindCombo", KeySet("Kind_")),
        new("StepEditorWindow.xaml", "MouseActCombo", KeySet("Mouse_", "Wheel_")),
        new("StepEditorWindow.xaml", "WinActionCombo", KeySet("Win_")),
        new("StepEditorWindow.xaml", "SysCmdCombo", KeySet("Sys_")),
        new("StepEditorWindow.xaml", "SysEngineCombo", new[] { "Ed_SearchCustom" }),
        new("StepEditorWindow.xaml", "PresentCombo", new[] { "Present_Dialog", "Present_Card" }),
        new("StepEditorWindow.xaml", "WinStyleCombo", new[] { "WinStyle_Default", "WinStyle_Minimized", "WinStyle_Maximized", "WinStyle_Hidden" }),
        new("StepEditorWindow.xaml", "VolActionCombo", new[] { "Vol_mute", "Vol_unmute", "Ed_Vol_Set", "Vol_micMute", "Vol_micUnmute" }),
        new("StepEditorWindow.xaml", "IfProcModeCombo", new[] { "Ed_Cond_Any", "Ed_IfProc_Running", "Ed_IfProc_Not" }),
        new("StepEditorWindow.xaml", "IfPowerCombo", new[] { "Ed_Cond_Any", "Ed_IfPower_Ac", "Ed_IfPower_Battery" }),
        new("StepEditorWindow.xaml", "OnYesTypeCombo", new[] { "Ed_OnYes_None", "Ed_OnYes_Run", "Ed_OnYes_Url" }),
        new("ReminderEditorWindow.xaml", "TrigCombo", KeySet("Ed_Trig_")),
        new("ReminderEditorWindow.xaml", "SModeCombo", new[] { "Ed_SMode_Any", "Ed_SMode_Before", "Ed_SMode_After" }),
        new("ReminderEditorWindow.xaml", "RecurCombo", new[] { "Ed_Rec_Daily", "Ed_Rec_EveryN", "Ed_Rec_Monthly", "Ed_Rec_Once" }),
        new("ReminderEditorWindow.xaml", "SoundCombo", new[] { "Snd_None", "Snd_Sound", "Snd_Speak", "Snd_Both" }),
        new("ReminderEditorWindow.xaml", "OnYesTypeCombo", new[] { "Ed_OnYes_None", "Ed_OnYes_Run", "Ed_OnYes_Url", "Ed_OnYes_Group" }),
    };

    // 装不下、按省略号处理的框：值 = 撑爆它的那条键。
    private static readonly Dictionary<string, string> Trims = new()
    {
        // 系统命令每条都带着括号解释（「锁屏（回来需输密码）」），那半句在下拉里是必要的
        // ——四个"关/睡/眠/息"光看名字分不出差别。法语那条整句要 740px，缩不进任何框。
        ["StepEditorWindow.xaml:SysCmdCombo"] = "Sys_quickOpen",
        // 系统命令每条都带着括号解释（「锁屏（回来需输密码）」），那半句在下拉里是必要的
        // ——四个"关/睡/眠/息"光看名字分不出差别。泰语那条整句要 800px，缩不进任何框。
    };

    private static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string ResxPath(string code)
        => Path.Combine(Root(), "app", "Resources",
            code.Length == 0 ? "Strings.resx" : "Strings." + code + ".resx");

    // 直接读 resx，不走 ResourceManager：这条测试判的是"写下来的字有多宽"，
    // 顺带就不依赖文化切换（测试并行时改 CurrentUICulture 会互相踩）。
    private static Dictionary<string, string> Values(string code)
    {
        var doc = XDocument.Load(ResxPath(code));
        var d = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var e in doc.Descendants("data"))
            d[e.Attribute("name")?.Value ?? ""] = e.Element("value")?.Value ?? "";
        return d;
    }

    // 键名清单：中性文件里同一前缀的全部键（Kind_ / Win_ / Sys_ …）。
    private static string[] KeySet(params string[] prefixes)
        => Values("").Keys
            .Where(k => prefixes.Any(p => k.StartsWith(p, System.StringComparison.Ordinal)))
            .OrderBy(k => k, System.StringComparer.Ordinal).ToArray();

    private static double WidthOf(string file, string name)
    {
        // 只看这个元素自己的属性：一行里常常先摆着标签的 Width="108"，
        // 拿整行的第一个 Width 去读，量到的是标签列宽（第一版就这么读错过一次）。
        var text = File.ReadAllText(Path.Combine(Root(), "app", "Views", file));
        var m = Regex.Match(text, "x:Name=\"" + name + "\"[^>]*?Width=\"([0-9.]+)\"");
        if (!m.Success) m = Regex.Match(text, "Width=\"([0-9.]+)\"[^>]*?x:Name=\"" + name + "\"");
        Assert.True(m.Success, $"{file} 里读不到 {name} 的 Width（改名了？），这条测试的名单要更新");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static readonly System.Windows.Media.Typeface Face = new(
        new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static double TextWidth(string text, double size)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return new FormattedText(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            Face, size, System.Windows.Media.Brushes.White, pixelsPerDip: 1.0).WidthIncludingTrailingWhitespace;
    }

    private static (double Width, string Who) WorstOf(Box box)
    {
        double worst = 0;
        var who = "";
        foreach (var lang in Languages.All)
        {
            var code = lang.Code == "zh-CN" ? "" : lang.Code;
            var values = Values(code);
            foreach (var key in box.Keys)
            {
                if (!values.TryGetValue(key, out var text)) continue;
                var w = TextWidth(text, Body);
                if (w > worst) { worst = w; who = lang.Code + "/" + key; }
            }
        }
        return (worst, who);
    }

    public static IEnumerable<object[]> EveryBox() => Enumerable.Range(0, Boxes.Length).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(EveryBox))]
    public void Every_combo_fits_its_longest_item_in_every_locale(int index)
    {
        var box = Boxes[index];
        var id = box.File + ":" + box.Name;
        if (Trims.ContainsKey(id)) return;
        var usable = WidthOf(box.File, box.Name) - Chrome;
        var (worst, who) = WorstOf(box);
        Assert.True(worst <= usable,
            $"{id} 宽 {usable + Chrome:0}，可用 {usable:0}px，而 {who} 需要 {worst:0}px——选中它就会被裁。" +
            "加宽、缩短那条候选，或者（真的装不下时）进 Trims 名单并写清为什么。");
    }

    // 名单防腐：列进来的必须**确实**还装不下，而且撑爆它的那条键还在。
    [Fact]
    public void Every_trimmed_box_really_cannot_fit()
    {
        Assert.True(Trims.Count > 0, "名单空了就把这套豁免逻辑一起删掉");
        foreach (var entry in Trims)
        {
            var parts = entry.Key.Split(':');
            var box = Boxes.FirstOrDefault(b => b.File == parts[0] && b.Name == parts[1]);
            Assert.True(box != null, $"{entry.Key} 已经不是一个下拉了，Trims 里该删掉");
            var usable = WidthOf(box!.File, box.Name) - Chrome;
            var (worst, who) = WorstOf(box);
            Assert.True(worst > usable,
                $"{entry.Key} 现在最坏只要 {worst:0}px（可用 {usable:0}），装得下了——从 Trims 拿出去");
            Assert.Contains(entry.Value, who, System.StringComparison.Ordinal);
        }
    }

    // 扫描本身得真的读到东西：路径写错、控件改名都会让上面几条静默变绿。
    [Fact]
    public void The_scan_actually_reads_widths_and_values()
    {
        Assert.True(Boxes.Length >= 12, "名单只剩 " + Boxes.Length + " 个框，大概是读法坏了");
        foreach (var b in Boxes) Assert.True(WidthOf(b.File, b.Name) > 50);
        Assert.True(WorstOf(Boxes[0]).Width > 20, "一个宽度都没量到，字体或 resx 路径坏了");
    }
}
