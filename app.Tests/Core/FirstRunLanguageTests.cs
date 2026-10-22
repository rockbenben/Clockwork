using System.Globalization;
using System.IO;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// 首启那份带示例的配置：示例文案必须按**界面语言**写进去。
//
// 背景：示例文案是本地化的（resx 的 Smp_*），而中性 resx 就是中文源。
// 首启时配置文件还不存在，语言只能取「跟随系统」；写文件那一刻进程默认文化
// 还是系统那一门——不在 18 门支持列表里时（捷克语、希腊语、乌克兰语……），
// 界面按 Languages.ResolveFor 落到英文，文件里却是一屏中文。
// 用户第一眼看到的就是「英文界面 + 中文示例」，而且这份中文从此写死在配置里，
// 之后再改语言也救不回来——示例是**数据**，不是界面文案。
public class FirstRunLanguageTests
{
    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cw-firstrun-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    // 每一门支持的语言：首启文件里的示例都必须已经是那一门。
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("ar")]
    [InlineData("hi")]
    [InlineData("id")]
    [InlineData("vi")]
    [InlineData("th")]
    [InlineData("tr")]
    [InlineData("nl")]
    public void First_run_file_carries_the_samples_of_that_language(string code)
    {
        string dir = NewTempDir();
        try
        {
            InCulture(code, () =>
            {
                string path = Path.Combine(dir, "clockwork.settings.json");
                ConfigStore.WriteFirstRun(path, code);

                // WriteFirstRun 刚把文化设成了 code，所以这里的期望值就是那一门的译文。
                string wantLabel = Strings.Get("Smp_Mute");
                string wantMessage = Strings.Get("Smp_RemWater");

                var cfg = ConfigStore.Read(path);
                Assert.Equal(wantLabel, cfg.LaunchSteps[0].Label);
                Assert.Equal(wantMessage, cfg.Reminders[0].Message);
            });
        }
        finally { Cleanup(dir); }
    }

    /// <summary>在指定文化里做一件事，完了把文化还原回原样。</summary>
    //
    // 写首启配置要按那一门语言写示例，所以它会设文化——而文化是**进程级**的。
    // 本套件里有一批用例断言的是中文源（中性 resx 就是中文源），
    // 漏还原的话，跑在后面的那些用例会看到「荷兰语的界面」而红；
    // 用例顺序又不固定，于是变成偶发的假红——难查的那种。
    private static void InCulture(string code, Action act)
    {
        var prevUi = CultureInfo.CurrentUICulture;
        var prevDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            Strings.ApplyCulture(code);
            act();
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
            CultureInfo.DefaultThreadCurrentUICulture = prevDefault;
        }
    }

    // 上面那条纪律的看门人：切了文化就得还原，否则后面那批断言中文源的用例会红。
    [Fact]
    public void Writing_a_first_run_file_leaves_the_ambient_culture_alone()
    {
        var beforeUi = CultureInfo.CurrentUICulture;
        var beforeDefault = CultureInfo.DefaultThreadCurrentUICulture;
        string dir = NewTempDir();
        try
        {
            InCulture("en", () => ConfigStore.WriteFirstRun(Path.Combine(dir, "clockwork.settings.json"), "en"));
            Assert.Equal(beforeUi, CultureInfo.CurrentUICulture);
            Assert.Equal(beforeDefault, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally { Cleanup(dir); }
    }

    // 真正踩到的那条路：系统语言不在支持列表里。
    // 界面按 ResolveFor 落到英文，示例就必须是英文——不能是中性 resx 的中文。
    [Fact]
    public void An_unsupported_system_language_does_not_leak_chinese_samples()
    {
        string dir = NewTempDir();
        var prevUi = CultureInfo.CurrentUICulture;
        var prevDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            // 进程默认文化 = 系统显示语言。这一门不受支持 → 界面语言是英文。
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs-CZ");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

            string uiLanguage = Languages.ResolveFor(CultureInfo.GetCultureInfo("cs-CZ"));
            Assert.Equal("en", uiLanguage);

            string path = Path.Combine(dir, "clockwork.settings.json");
            ConfigStore.WriteFirstRun(path, uiLanguage);

            var cfg = ConfigStore.Read(path);
            Assert.Equal(Strings.InEnglish(() => Strings.Get("Smp_Mute")), cfg.LaunchSteps[0].Label);
            Assert.DoesNotContain('开', cfg.LaunchSteps[0].Label);
            Assert.DoesNotContain('开', cfg.Reminders[0].Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prevUi;
            CultureInfo.DefaultThreadCurrentUICulture = prevDefault;
            Cleanup(dir);
        }
    }

    // 首启用的语言要与界面随后会用的那一门一致：
    // ApplyUiCulture 用的是配置里的 language，首启时它被规范化成「跟随系统」的结果。
    [Fact]
    public void First_run_language_is_the_one_the_ui_will_use()
    {
        // Normalize("")（首启时配置里的 language 就是空串）与 ResolveForSystem() 同一条路。
        Assert.Equal(Languages.ResolveForSystem(), Languages.Normalize(""));
    }

    // 仓库里的示例配置（EnsureConfigFile 优先用它，而不是程序自己生成的那份）
    // 与出厂文案必须一字不差：差一个字它就认不出「这是出厂文本」，
    // 于是换了语言也换不掉——用户看到的就是一份与程序自己生成的样例对不上的配置。
    [Fact]
    public void The_shipped_example_config_carries_the_built_in_sample_text()
    {
        string root = RepoRoot();
        string path = Path.Combine(root, "clockwork.settings.example.json");
        Assert.True(File.Exists(path), $"找不到示例配置：{path}");

        var prev = CultureInfo.CurrentUICulture;
        var prevDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            // 示例文件是中文的，而中性 resx 正是中文源 —— 要比的就是那一门。
            Strings.ApplyCulture("zh-CN");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var launch = doc.RootElement.GetProperty("launchSteps");
            string[] labelKeys = { "Smp_Mute", "Smp_OpenApp", "Smp_OpenSite", "Smp_OpenChat", "Smp_ShowDesktop" };
            for (int i = 0; i < labelKeys.Length; i++)
                Assert.Equal(Strings.Get(labelKeys[i]), launch[i].GetProperty("label").GetString());

            var reminders = doc.RootElement.GetProperty("reminders");
            string[] messageKeys = { "Smp_RemWater", "Smp_RemWrapUp", "Smp_RemSleep", "Smp_RemBills" };
            for (int i = 0; i < messageKeys.Length; i++)
                Assert.Equal(Strings.Get(messageKeys[i]), reminders[i].GetProperty("message").GetString());

            // 预置的那两个动作组：名字也要是出厂那一份（模板的身份是 key，名字是 Tpl_* 的译文）。
            var groups = doc.RootElement.GetProperty("actionGroups");
            Assert.Equal(Strings.Get("Tpl_Away"), groups[0].GetProperty("name").GetString());
            Assert.Equal(Strings.Get("Tpl_EndOfDay"), groups[1].GetProperty("name").GetString());
            Assert.Equal(Strings.Get("Tpl_EndOfDayMsg"),
                groups[1].GetProperty("steps")[0].GetProperty("message").GetString());
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
            CultureInfo.DefaultThreadCurrentUICulture = prevDefault;
        }
    }

    // 仓库根：往上找到有 app/Resources 的那一层（与 RenderDefectRegressionTests 同一手法）。
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "app", "Resources"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }
}
