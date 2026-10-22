using System.Globalization;
using System.IO;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// 出厂示例文本要跟着界面语言走：
// 换过语言、导入过别处的配置、首启时系统语言不在支持列表里——
// 三条路都会让配置里留着一屏别的语言的示例，而那些示例正是用户用来搞懂这个程序的东西。
//
// 判据：**没被改过的**出厂文本换成当前语言的那一份，**被改过的一个字不动**。
public class SampleTextTests
{
    private static CultureInfo? _prevUi;
    private static CultureInfo? _prevDefault;

    private static void UseCulture(string code)
    {
        _prevUi = CultureInfo.CurrentUICulture;
        _prevDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Strings.ApplyCulture(code);
    }

    // **两个都要还原**：Strings.ApplyCulture 同时改线程文化与进程默认文化，
    // 只还原前者的话，后面换线程跑的用例会带着上一门语言——于是偶发的假红。
    private static void Restore()
    {
        var zh = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentUICulture = _prevUi ?? zh;
        CultureInfo.DefaultThreadCurrentUICulture = _prevDefault ?? zh;
        _prevUi = null;
        _prevDefault = null;
    }

    // 这一类要切文化（切文化是进程级的）。切完必须还原 —— 后面那批断言中文源的用例
    // 会看到别的语言而红，而用例顺序不固定，于是变成偶发的假红。
    [Fact]
    public void Switching_culture_here_leaves_the_ambient_culture_alone()
    {
        var beforeUi = CultureInfo.CurrentUICulture;
        var beforeDefault = CultureInfo.DefaultThreadCurrentUICulture;
        UseCulture("en");
        Restore();
        Assert.Equal(beforeUi, CultureInfo.CurrentUICulture);
        Assert.Equal(beforeDefault, CultureInfo.DefaultThreadCurrentUICulture);
    }

    [Fact]
    public void Untouched_samples_follow_the_ui_language()
    {
        try
        {
            // 先在中文下造一份出厂配置（示例、示例提醒、预置动作组都在里面）。
            UseCulture("zh-CN");
            var cfg = RootConfig.Default();
            string zhLabel = cfg.LaunchSteps[1].Label;
            string zhMessage = cfg.Reminders[0].Message;
            string zhGroup = cfg.ActionGroups[0].Name;

            // 换语言：界面变英文，配置还留在中文。
            UseCulture("en");
            Assert.True(SampleText.Relocalize(cfg));

            Assert.Equal(Strings.Get("Smp_OpenApp"), cfg.LaunchSteps[1].Label);
            Assert.Equal(Strings.Get("Smp_RemWater"), cfg.Reminders[0].Message);
            Assert.Equal(Strings.Get("Tpl_Away"), cfg.ActionGroups[0].Name);
            Assert.NotEqual(zhLabel, cfg.LaunchSteps[1].Label);
            Assert.NotEqual(zhMessage, cfg.Reminders[0].Message);
            Assert.NotEqual(zhGroup, cfg.ActionGroups[0].Name);
        }
        finally { Restore(); }
    }

    // 用户自己写下的字一个字都不动——这是这条修复的安全底线。
    [Fact]
    public void Edited_samples_are_left_alone()
    {
        try
        {
            UseCulture("zh-CN");
            var cfg = RootConfig.Default();
            cfg.LaunchSteps[1].Label = "我自己的浏览器";
            cfg.Reminders[0].Message = "我自己的一条提醒";
            cfg.ActionGroups[0].Name = "我的动作";

            UseCulture("en");
            SampleText.Relocalize(cfg);

            Assert.Equal("我自己的浏览器", cfg.LaunchSteps[1].Label);
            Assert.Equal("我自己的一条提醒", cfg.Reminders[0].Message);
            Assert.Equal("我的动作", cfg.ActionGroups[0].Name);
        }
        finally { Restore(); }
    }

    // 已经是当前语言的那一份就不要再动（每次启动都写一遍盘是白白的 IO）。
    [Fact]
    public void Samples_already_in_the_ui_language_are_not_rewritten()
    {
        try
        {
            UseCulture("en");
            var cfg = RootConfig.Default();
            Assert.False(SampleText.Relocalize(cfg));
        }
        finally { Restore(); }
    }

    // 手势与面板格子里存的是组名的缓存副本：组名换了，这些也得跟着换。
    [Fact]
    public void Cached_group_names_follow_the_renamed_group()
    {
        try
        {
            UseCulture("zh-CN");
            var cfg = RootConfig.Default();
            var group = cfg.ActionGroups[0];
            var tile = cfg.PanelPages[0].Steps.First(s => s.Kind == "group");
            tile.Label = group.Name;

            UseCulture("en");
            SampleText.Relocalize(cfg);

            Assert.Equal(Strings.Get("Tpl_Away"), group.Name);
            Assert.Equal(Strings.Get("Tpl_Away"), tile.Label);
        }
        finally { Restore(); }
    }

    // 这一门缺某条文案时，不能把用户看到的那段话换成键名本身。
    [Fact]
    public void A_missing_translation_never_replaces_text_with_the_key()
    {
        try
        {
            UseCulture("en");
            var cfg = RootConfig.Default();
            // 造一条「不是任何一门语言的出厂字」的文本：它必须原样留下。
            const string mine = "my own label";
            cfg.LaunchSteps[0].Label = mine;
            SampleText.Relocalize(cfg);
            Assert.Equal(mine, cfg.LaunchSteps[0].Label);
        }
        finally { Restore(); }
    }
}
