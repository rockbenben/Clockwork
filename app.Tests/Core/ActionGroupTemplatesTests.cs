using Clockwork.Core;
using Xunit;

public class ActionGroupTemplatesTests
{
    // 数量是刻意钉死的：收录标准见 ActionGroupTemplates 头部注释（常用 + 演示别处没有的能力）。
    // 曾膨胀到 10 又砍回 7——这条红了说明有人加/删了模板，去核对那份标准，别顺手改数字了事。
    //
    // 从 8 到 10 的那两次：逐行输入（块循环 + 变量条件 + 交互输入，三样此前零曝光），
    // 打开本地源文件（站点映射表 + UIA 读地址栏）。都不是「人人都用」，但各自演示的东西
    // 整个列表里再没有第二份——这才是这条标准要的。
    [Fact]
    public void Every_template_has_a_name_steps_and_is_enabled()
    {
        var all = ActionGroupTemplates.All();
        Assert.Equal(10, all.Count);
        Assert.All(all, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
        Assert.All(all, g => Assert.NotEmpty(g.Steps));
        Assert.All(all, g => Assert.True(g.Enabled));
    }

    // 八个模板摆到面板上得是八个样子：「运行动作」的格子没自己设图标时跟着组走（见 PanelLayout），
    // 组不设就是八格共用同一枚「一叠动作」字形——那正是「看着一样但不知道哪个是哪个」的来源。
    [Fact]
    public void Every_template_carries_a_distinct_recognizable_icon()
    {
        var all = ActionGroupTemplates.All();
        var icons = all.Select(g => g.Icon).ToArray();
        Assert.All(icons, i => Assert.False(string.IsNullOrWhiteSpace(i), "有模板没设图标"));
        Assert.Equal(icons.Length, icons.Distinct().Count());
        // 「认得」= PanelIcon 把它当码位（或 brand:slug）解析成图标，而不是当文件路径去磁盘上找一趟。
        Assert.All(all, g =>
        {
            var spec = PanelIcon.Resolve(g.Icon, "group");
            Assert.True(spec.Kind is PanelIconKind.Glyph or PanelIconKind.Brand,
                        $"{g.Name} 的图标 {g.Icon} 解析不出图标");
        });
    }

    // 关通知 / 麦克风静音 / 扬声器静音都是有状态的开关：改完不会自己恢复。模板里每出现一个「关」，
    // 「恢复正常」里就必须有对应的「开」，否则用户第二天会以为通知坏了。
    // 这条测试就是那个出口的看门人——「恢复正常」被删或被改瘦，它立刻红。
    [Fact]
    public void Every_stateful_switch_turned_off_has_a_way_back()
    {
        var all = ActionGroupTemplates.All();
        var restore = all.Single(g => g.Name == Clockwork.I18n.Strings.Get("Tpl_Restore"));
        var offOn = new (Func<LaunchStep, bool> Off, Func<LaunchStep, bool> On, string What)[]
        {
            (s => s.Kind == "system" && s.Command == "notificationsOff", s => s.Kind == "system" && s.Command == "notificationsOn", "通知"),
            (s => s.Kind == "volume" && s.VolumeAction == "micMute", s => s.Kind == "volume" && s.VolumeAction == "micUnmute", "麦克风"),
            (s => s.Kind == "volume" && s.VolumeAction == "mute", s => s.Kind == "volume" && s.VolumeAction == "unmute", "扬声器"),
        };
        foreach (var (off, on, what) in offOn)
            if (all.SelectMany(g => g.Steps).Any(off))
                Assert.True(restore.Steps.Any(on), $"模板里有人关掉了{what}，「恢复正常」却没有把它开回来");
    }

    [Fact]
    public void Each_call_generates_fresh_ids()   // 重复添加同一模板不撞 id（运行态/引用都按 id 做键）
    {
        var a = ActionGroupTemplates.All();
        var b = ActionGroupTemplates.All();
        for (int i = 0; i < a.Count; i++) Assert.NotEqual(a[i].Id, b[i].Id);
        Assert.Equal(a.Count, a.Select(g => g.Id).Distinct().Count());   // 同一批内也不重复
    }

    [Fact]
    public void Steps_kinds_are_valid()   // 模板步骤类型必须是引擎认识的（防手滑写错 kind 静默不执行）
    {
        var known = new HashSet<string>(StepDisplay.StepKinds);
        foreach (var g in ActionGroupTemplates.All())
            foreach (var s in g.Steps)
                Assert.Contains(s.Kind, known);
    }

    // 回归：会议模板绝不能静音。volume/mute 静的是默认输出设备的主音量
    //（AudioController.SetMute → IAudioEndpointVolume.SetMute），开会静音＝听不到会议声音。
    // 这条曾经就是这么写的，别再改回去。
    [Fact]
    public void Meeting_template_sets_volume_instead_of_muting()
    {
        var meeting = ActionGroupTemplates.All()
            .Single(g => g.Name == Clockwork.I18n.Strings.Get("Tpl_Meeting"));

        Assert.DoesNotContain(meeting.Steps, s => s.Kind == "volume" && s.VolumeAction == "mute");
        Assert.Contains(meeting.Steps, s => s.Kind == "volume" && s.VolumeAction == "set");
    }

    // 搜索模板是唯一演示「步骤之间传值」的模板。**变量名与地址里那个花括号必须对得上**——
    // 两处分开写死的话，某种语言的译名一改，那个模板在那种语言下就静默失效：
    // 地址栏里留着一个换不掉的 {关键词}，而没有任何东西会报错。
    [Fact]
    public void Search_template_wires_its_variable_into_the_url()
    {
        var g = ActionGroupTemplates.All()
            .Single(x => x.Name == Clockwork.I18n.Strings.Get("Tpl_Search"));

        var ask = Assert.Single(g.Steps, s => s.Kind == "prompt");
        var open = Assert.Single(g.Steps, s => s.Kind == "url");
        Assert.False(string.IsNullOrWhiteSpace(ask.OutputVar));
        Assert.Contains("{" + ask.OutputVar + "}", open.Target);
        // 而且那个占位真的能被替换掉——名字里有空格之类的写法也得走得通（好几种语言的译名就是两个词）。
        var filled = StepPlaceholder.Apply(open.Target, null, urlEncode: true, Vars(ask.OutputVar, "猫"));
        Assert.DoesNotContain("{", filled);
    }

    private static RunVars Vars(string name, string value)
    {
        var v = new RunVars();
        v.Set(name, value);
        return v;
    }

    // 久坐模板是唯一演示「整组循环」的模板：没有它，Repeat/RepeatDelayMs 这对能力在模板里零曝光。
    [Fact]
    public void Sedentary_template_loops_the_whole_group()
    {
        var g = ActionGroupTemplates.All()
            .Single(x => x.Name == Clockwork.I18n.Strings.Get("Tpl_Sedentary"));

        Assert.True(g.Repeat > 1, "整组重复轮数应大于 1");
        Assert.True(g.RepeatDelayMs > 0, "每轮间隔应大于 0");
    }

    // 逐行输入是唯一演示块循环的模板，而它的正确性**全在接线上**：
    // 「等按键」产出变量、「跳出」那条条件读同一个变量，中间差一个字这个模板就退化成
    // 「按 Insert 把同一行再发一遍」——而它在列表上看着完全正常。
    //
    // 变量名从文案表取（与搜索模板同一惯例），所以这一条要防的不只是手滑，
    // 还有**某门语言改了译名而另一处没跟着改**。
    [Fact]
    public void Type_lines_template_wires_the_key_variable_into_its_condition()
    {
        var g = ActionGroupTemplates.All()
            .Single(x => x.Name == Clockwork.I18n.Strings.Get("Tpl_TypeLines"));

        var loop = Assert.Single(g.Steps, s => s.Kind == "each");
        Assert.False(string.IsNullOrWhiteSpace(loop.Text), "循环没说要遍历哪个列表");

        var wait = Assert.Single(g.Steps, s => s.Kind == "waitKey");
        Assert.False(string.IsNullOrWhiteSpace(wait.OutputVar), "等按键没产出变量");
        Assert.Contains("Insert", wait.Text);
        Assert.Contains("Escape", wait.Text);

        // 条件读的就是那个变量
        var skip = Assert.Single(g.Steps, s => s.Kind == "skip");
        Assert.Equal(wait.OutputVar, skip.IfVar);
        Assert.Equal("eq", skip.IfVarOp);
        Assert.Equal("Escape", skip.IfVarValue);

        // 「跳出」必须排在「发送文本」**前面**：Esc 停的是「这一行还没发出去」，
        // 反过来的话每按一次 Esc 都会先把那行敲进去，得按两次才真的停。
        var send = Assert.Single(g.Steps, s => s.Kind == "text");
        Assert.True(g.Steps.IndexOf(skip) < g.Steps.IndexOf(send),
                    "「跳出」排在「发送文本」之后：Esc 会先把这一行敲出去");

        // 三步都在块里，而不是散在外面
        foreach (var s in new[] { wait, skip, send })
            Assert.Equal(loop.Id, s.ParentId);
    }

    // 本地源文件模板是**两步**：读地址栏 → 映射并打开。
    //
    // 它此前只有一步，而那一步的网址框「留空 = 自动读地址栏」——一步干了两件事，
    // 而界面上看不出这一步会去读地址栏、也看不出它读的是哪个浏览器。
    // 拆开之后，工具箱里两个都是通用原语（想映射手打的网址不必先读一次地址栏），
    // 「看一眼线上文章、改它的源文件」这一整件事在模板层拼出来。
    //
    // 变量名两边必须对得上：browserUrl 写进去的那一格，siteMap 要真的引用它。
    // 两处分开写死的话，某种语言的译名一改，这个模板在那种语言下就静默失效。
    [Fact]
    public void Site_source_template_reads_the_address_bar_then_maps_it()
    {
        var g = ActionGroupTemplates.All()
            .Single(x => x.Name == Clockwork.I18n.Strings.Get("Tpl_SiteSource"));

        Assert.Equal(2, g.Steps.Count);

        var read = g.Steps[0];
        Assert.Equal("browserUrl", read.Kind);
        Assert.False(string.IsNullOrWhiteSpace(read.OutputVar), "读出来的地址要有落脚点");

        var map = g.Steps[1];
        Assert.Equal("siteMap", map.Kind);
        Assert.Equal("open", map.SiteMapMode);
        Assert.Equal("{" + read.OutputVar + "}", map.Text);
    }
}
