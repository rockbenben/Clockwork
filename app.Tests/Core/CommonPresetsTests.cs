using System.Linq;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Xunit;

// 「常用」那一节里的每一条都是预填好的步骤。它们的错法很特别：**编辑器里看着完全正常**
// （「发送按键 Ctrl+C」白纸黑字），点了却没反应——因为组合键名拼错了、或系统命令 id 不在表里。
// 这类错在界面上没有任何征兆，只有真按下去才知道，所以这份表必须在构建期就被钉住。
public class CommonPresetsTests
{
    [Fact]
    public void Every_preset_makes_a_step_of_its_own_kind()
    {
        foreach (var (_, kind, arg) in StepDisplay.CommonPresets)
            Assert.Equal(kind, StepDisplay.MakePreset(kind, arg).Kind);
    }

    // 按键类预设的组合键必须真能解析出一个虚拟键码。写错一个字母（"Ctrl+Copy"、"Alt+ Left"）
    // 得到的是一条能存、能显示、一按就告警「无法识别的键」的步骤。
    [Fact]
    public void Every_key_preset_resolves_to_a_real_key()
    {
        foreach (var (labelKey, kind, arg) in StepDisplay.CommonPresets.Where(p => p.Kind == "keys"))
        {
            var p = KeyCombo.ParseCombo(StepDisplay.MakePreset(kind, arg).Combo);
            Assert.False(string.IsNullOrWhiteSpace(p.Key), $"{labelKey}（{arg}）没有主键");
            Assert.True(KeyInput.KeysVk(p.Key!) != 0, $"{labelKey}（{arg}）的主键 {p.Key} 解析不出虚拟键码");
        }
    }

    // 窗口类预设走的是「进程名留空 = 当前窗口」那条路。Process 一旦被填上，
    // 这几条就变成「最小化某个固定程序」——菜单上写着「当前窗口」，做的却是别的事。
    [Fact]
    public void Window_presets_target_the_current_window()
    {
        foreach (var (labelKey, kind, arg) in StepDisplay.CommonPresets.Where(p => p.Kind == "window"))
        {
            var s = StepDisplay.MakePreset(kind, arg);
            Assert.Equal(StepDisplay.CurrentWindowMark, s.Process);
            Assert.Contains(arg, StepDisplay.WindowActions.Select(w => w.Action));
        }
    }

    // 「下拉里选得到，跑起来说不认识这个动作」——窗口动作散在展示表和执行器两处，
    // 只加一处就会造出这种步骤：存得下、显示正常、一运行报「未知的窗口动作」。
    // 拿一个不存在的进程名去跑：合法动作会走到「没找到窗口」，非法动作在那之前就被判掉，
    // 两者分得干干净净，而且都不需要真的有窗口。
    [Fact]
    public void Every_window_action_in_the_dropdown_is_executable()
    {
        foreach (var (action, _) in StepDisplay.WindowActions)
        {
            if (action == "sendkey") continue;   // 那一支要等窗口出现，默认 8 秒，不适合放进单测
            Assert.NotEqual(WindowOutcome.UnknownAction,
                WindowManager.WindowAction("__clockwork_no_such_process__", action));
        }
    }

    // 置顶必须是**开关**：分成置顶/取消两条，等于让用户为同一件事配两条手势。
    // 这里只钉「它是被当成一个合法动作接住的」，真正的翻转要有窗口才验得了（渲染/冒烟都盖不到）。
    [Fact]
    public void Topmost_is_one_action_not_two()
    {
        Assert.Contains("topmost", StepDisplay.WindowActions.Select(w => w.Action));
        Assert.DoesNotContain("untopmost", StepDisplay.WindowActions.Select(w => w.Action));
    }

    // 系统命令类预设的 id 必须在系统命令表里。不在表里的话下拉框选不中它，
    // 编辑器一打开就把它悄悄改成表里的第一条（清空回收站），保存后动作彻底变了。
    [Fact]
    public void System_presets_are_in_the_command_list()
    {
        var ids = StepDisplay.SystemCommandMap().Select(kv => kv.Key).ToList();
        foreach (var (_, kind, arg) in StepDisplay.CommonPresets.Where(p => p.Kind == "system"))
            Assert.Contains(arg, ids);
    }

    // 菜单上那一行字。文案键缺失时 Strings.Get 原样返回键名——菜单里就会出现一行「Cmd_copy」。
    [Fact]
    public void Every_preset_label_is_translated()
    {
        foreach (var (labelKey, _, _) in StepDisplay.CommonPresets)
            Assert.NotEqual(labelKey, Strings.Get(labelKey));
        Assert.NotEqual("Menu_SecCommon", Strings.Get("Menu_SecCommon"));
    }

    // 搜索预设要带着默认地址进编辑器：地址框空着的话，用户看到的是一个必填却没说要填什么的框。
    [Fact]
    public void Search_preset_carries_the_default_url()
    {
        var s = StepDisplay.MakePreset("system", "searchSelection");
        Assert.Equal(StepDisplay.DefaultSearchUrl, s.Text);
        Assert.Contains("{0}", StepDisplay.DefaultSearchUrl);
    }

    // 进程名留空的窗口步骤，摘要必须是「最小化当前窗口」这一句完整的话。
    // 走老路（动词 + 进程名）拼出来的是「最小化窗口 」——一个尾随空格结尾的残句。
    [Fact]
    public void Current_window_steps_read_as_one_sentence()
    {
        foreach (var action in new[] { "minimize", "maximize", "close" })
        {
            var sum = StepDisplay.StepSummary(StepDisplay.MakePreset("window", action));
            Assert.Equal(sum.Trim(), sum);
            Assert.DoesNotContain(Strings.Get("Win_" + action), sum);   // 不是「最小化窗口 X」那种拼法
            Assert.Equal(Strings.Get("Win_" + action + "Fg"), sum);
        }
    }

    // 搜索词是从剪贴板里拿的一整段文字，不是一个词。原样拼进地址会得到一串 %0A%20%20，
    // 搜出来的结果和你选的那句话对不上；而 Ctrl+A 之后顺手画一个手势会拼出浏览器打不开的长地址。
    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("第一行\n第二行", "第一行 第二行")]
    [InlineData("a\r\n\tb", "a b")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Search_query_is_one_tidy_line(string? raw, string want)
        => Assert.Equal(want, Clockwork.Engine.SystemCommands.SearchQuery(raw));

    [Fact]
    public void Search_query_is_capped()
    {
        var q = Clockwork.Engine.SystemCommands.SearchQuery(new string('x', 5000));
        Assert.Equal(200, q.Length);
    }

    // 摘要里的两条口径：组合键原样、裸键名换成名字。写反了任何一边都难看——
    // 「发送 复制」是绕，「发送 MediaPlayPause」是在中文界面里蹦出一串英文标识符。
    [Fact]
    public void Combos_stay_literal_but_bare_key_names_get_a_name()
    {
        var copy = StepDisplay.StepSummary(StepDisplay.MakePreset("keys", "Ctrl+C"));
        Assert.Contains("Ctrl+C", copy);
        Assert.DoesNotContain(Strings.Get("Cmd_copy"), copy);

        var play = StepDisplay.StepSummary(StepDisplay.MakePreset("keys", "MediaPlayPause"));
        Assert.DoesNotContain("MediaPlayPause", play);
        Assert.Contains(Strings.Get("Cmd_playPause"), play);
    }

    // 所有裸键名的预设都得有名字可换。将来再加一条（比如 F5）忘了配文案，这里就会红。
    [Fact]
    public void No_preset_leaks_a_raw_key_identifier()
    {
        foreach (var (labelKey, kind, arg) in StepDisplay.CommonPresets)
        {
            if (kind != "keys" || arg.Contains('+')) continue;
            var sum = StepDisplay.StepSummary(StepDisplay.MakePreset(kind, arg));
            Assert.DoesNotContain(arg, sum);
            Assert.Contains(Strings.Get(labelKey), sum);
        }
    }

    // 引擎表：地址模板少了 {0}，那一条就是「打开引擎首页」而不是搜索——
    // 界面上看不出任何异常，点下去只是跳到 google.com，用户会以为是没取到选中的字。
    [Fact]
    public void Every_search_engine_takes_the_query()
    {
        foreach (var (name, url) in StepDisplay.SearchEngines)
        {
            Assert.Contains("{0}", url);
            Assert.StartsWith("https://", url);   // 明文 http 会把用户搜的东西暴露在链路上
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
        Assert.Equal(StepDisplay.SearchEngines.Length,
                     StepDisplay.SearchEngines.Select(e => e.Url).Distinct().Count());
    }

    // 盘上存的是**地址**，不是引擎 id：编辑器靠反查决定下拉停在哪一项。
    // 反查错了的表现是「上次选的 Google，再打开变回了必应」，而配置其实没坏。
    [Fact]
    public void An_engine_url_maps_back_to_its_engine()
    {
        foreach (var (name, url) in StepDisplay.SearchEngines)
            Assert.Equal(name, StepDisplay.SearchEngineOf(url));
        Assert.Null(StepDisplay.SearchEngineOf("https://example.com/?q={0}"));   // 表外的 = 自定义
        Assert.Null(StepDisplay.SearchEngineOf(""));
    }

    // 默认那一条必须真的在表里，否则新建的搜索步骤一打开就落到「自定义…」。
    [Fact]
    public void The_default_url_is_one_of_the_engines()
        => Assert.NotNull(StepDisplay.SearchEngineOf(StepDisplay.DefaultSearchUrl));

    // 「当前窗口」= 恰好一个窗口，或一个也没有。钉的是那个灾难性的退化：
    // 若它落回 WindowsForProcess()，返回的可能是**一大把**窗口——
    // 那时「最小化当前窗口」会把桌面上的窗口全部最小化，而且看着像是「生效了」。
    [Fact]
    public void The_current_window_is_never_every_window()
        => Assert.True(WindowManager.Handles(StepDisplay.CurrentWindowMark).Length <= 1);

    // **空进程名必须一个窗口都匹配不上。** 这不是「自然如此」而要显式挡住：
    // GetProcessesByName("") 会匹配上名字读不出来的那些进程，本机实测由此得到 19 个真实窗口，
    // 于是盘上一条 {"kind":"window","action":"close","process":""}（旧编辑器不拦空进程名，
    // 这条路真实可达）会去关掉那 19 个窗口——而它看起来只是「有个字段没填」。
    [Fact]
    public void A_blank_process_still_matches_nothing()
    {
        Assert.Empty(WindowManager.Handles(""));
        Assert.Empty(WindowManager.Handles("   "));
        Assert.False(WindowManager.IsCurrentWindow(""));
    }

    // 两个常量必须是同一个字符：Core 不引用 Native，所以「当前窗口」在两边各存了一份，
    // 漂移的表现是界面写着「当前窗口」而执行器按进程名去找一个叫 * 的程序。
    [Fact]
    public void The_current_window_mark_agrees_across_layers()
        => Assert.Equal(WindowManager.CurrentWindow, StepDisplay.CurrentWindowMark);

    // 「当前窗口」不开放 sendkey / activate：sendkey 的安全性建立在「抢到前台、稍后再复核
    // 焦点没被偷走」上，而当前窗口的复核恒为真——那串按键（常常是密码）会打进任何一个
    // 碰巧抢走焦点的窗口。activate 对当前窗口则本就是空操作。
    [Theory]
    [InlineData("sendkey")]
    [InlineData("activate")]
    public void The_current_window_refuses_focus_stealing_actions(string op)
        // **NotForCurrentWindow 而不是 UnknownAction。** 这两个动作名都是合法的，
        // 拒的是「当前窗口」这个目标；共用 UnknownAction 会让提示变成「不认识的窗口动作：activate」
        // ——指着一个完全合法的动作说不认识，而该换的是目标或步骤类型。
        // 这个组合在编辑器里点得出来（窗口动作下拉 + 「当前窗口」目标），不是理论情况。
        => Assert.Equal(WindowOutcome.NotForCurrentWindow,
                        WindowManager.WindowAction(StepDisplay.CurrentWindowMark, op));

    // 带参数的系统命令有两条，但只有剪贴板文本该出现在摘要里：
    // 搜索的引擎地址是设置，一串 https://… 会把「搜索选中的文字」整句挤出可见宽度。
    [Fact]
    public void Search_url_stays_out_of_the_summary()
    {
        var s = StepDisplay.MakePreset("system", "searchSelection");
        Assert.True(StepDisplay.SystemCommandTakesText("searchSelection"));   // 编辑器仍要露出那个框
        Assert.DoesNotContain("http", StepDisplay.StepSummary(s));
        Assert.Contains(Strings.Get("Sys_searchSelection"), StepDisplay.StepSummary(s));
    }
}
