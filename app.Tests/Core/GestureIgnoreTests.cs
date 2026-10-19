using System.Collections.Generic;
using Clockwork.Core;
using Xunit;

// 「这些程序里根本不接管手势」这份名单的判据。
//
// 它从前不存在：要让出一个程序里的右键，唯一的办法是把**所有**程序里的手势一起关掉（总开关）。
// 而远程桌面、虚拟机、游戏、3D 视口这些地方程序自己就管右键，我们插一脚只会碍事。
// 别的工具（WGestures / StrokesPlus / Quicker）都有这一份「按程序排除」的名单，这里补上。
//
// 判据放在纯函数里（与 ShouldWatch、Match 同一层），钩子那边只负责把进程名问出来——
// 那边碰 Win32，断言够不着。
public class GestureIgnoreTests
{
    // 名单是空的 → 到处都接管。这是默认，也必须是从前唯一的行为。
    [Fact]
    public void An_empty_list_ignores_nothing()
    {
        Assert.False(GestureGate.IsIgnoredApp("chrome", new List<string>()));
        Assert.False(GestureGate.IsIgnoredApp("chrome", null));
    }

    [Fact]
    public void A_listed_app_is_ignored()
    {
        var list = new List<string> { "mstsc", "vmware" };
        Assert.True(GestureGate.IsIgnoredApp("mstsc", list));
        Assert.True(GestureGate.IsIgnoredApp("vmware", list));
        Assert.False(GestureGate.IsIgnoredApp("chrome", list));
    }

    // 进程名在 Windows 上不分大小写，而用户手打时大小写常常不一。
    [Fact]
    public void The_name_is_matched_without_regard_to_case()
    {
        var list = new List<string> { "Mstsc" };
        Assert.True(GestureGate.IsIgnoredApp("mstsc", list));
        Assert.True(GestureGate.IsIgnoredApp("MSTSC", list));
    }

    // 名单存的是**裸进程名**，与分应用绑定（ForProcess）同一套写法：
    // 「chrome」「chrome.exe」「C:\Program Files\Chrome\chrome.exe」三者等价。
    // 这一条不是洁癖——存成两种写法时，界面上看着名单里有它、却还在接管，那种症状极难查。
    [Fact]
    public void The_exe_suffix_and_the_path_do_not_matter()
    {
        var list = new List<string> { "chrome.exe" };
        Assert.True(GestureGate.IsIgnoredApp("chrome", list));
        Assert.True(GestureGate.IsIgnoredApp("Chrome.exe", list));

        var path = new List<string> { @"C:\Program Files\Google\Chrome\Application\chrome.exe" };
        Assert.True(GestureGate.IsIgnoredApp("chrome", path));
    }

    // 认不出名字时按「不在名单里」处理：反过来会让手势在**每一个**刚切过去的窗口里都失效，
    // 而名字解析走的是后台探针，未命中时只差几十毫秒。
    [Fact]
    public void An_unknown_process_is_not_ignored()
    {
        var list = new List<string> { "mstsc" };
        Assert.False(GestureGate.IsIgnoredApp("", list));
        Assert.False(GestureGate.IsIgnoredApp(null, list));
    }

    // 名单里的空行不是一条名单。
    [Fact]
    public void Blank_entries_in_the_list_are_not_entries()
    {
        var list = new List<string> { "", "   ", "mstsc" };
        Assert.True(GestureGate.IsIgnoredApp("mstsc", list));
        Assert.False(GestureGate.IsIgnoredApp("chrome", list));
    }

    // 手打的那一段文本 → 一份干净的名单：去空白、去路径、去 .exe、去重复。
    [Fact]
    public void A_typed_list_is_normalised_and_deduplicated()
    {
        var list = GestureGate.ParseIgnoreList(" mstsc \r\nChrome.exe\r\nchrome\r\n\r\n  vmware  ");
        Assert.Equal(new[] { "mstsc", "Chrome", "vmware" }, list);
    }

    [Fact]
    public void An_empty_typed_list_parses_to_nothing()
    {
        Assert.Empty(GestureGate.ParseIgnoreList(""));
        Assert.Empty(GestureGate.ParseIgnoreList("   \n\n  "));
        Assert.Empty(GestureGate.ParseIgnoreList(null));
    }
}
