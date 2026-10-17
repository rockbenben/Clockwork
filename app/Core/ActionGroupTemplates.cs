using Clockwork.I18n;
using System.Linq;

namespace Clockwork.Core;

// 常用动作组模板（旧 PS 版 Get-ActionGroupTemplates 的移植）。每次调用现生成 → 各自新 id，重复添加不撞 id。
// 进程名用全球通用的（Slack / Discord / msedge），不按语言本地化——添加后按自己的软件改进程名即可。
// 步骤不设 label：列表摘要由 StepDisplay 按当前语言自动生成；组名/消息文本经 resx 本地化。
//
// 收录标准：常用、且演示至少一样别处没演示的能力。曾经膨胀到 10 个又砍回来（投屏演示与会议重叠 2/3、
// 夜间护眼的每一步别处都有、截图标注撞系统热键）——模板列表是新用户对「动作组能干什么」的第一印象，
// 十个各有一手比十四个大同小异更有说服力。新模板先过这一关再进来。
//
// 「常用」这一条对后两个模板放宽了：逐行输入只对「粘贴被拦」的场景有用，本地源文件只对自建静态站
// 的人有用。放宽的依据不在使用面，而在**演示面**——它们演示的块循环、变量条件、交互输入、
// 站点映射表，各自在这个列表里再没有第二份，而标准的后半句（别处没演示的能力）正是为这种
// 「不是人人用，但只有它会让人看见某项能力」的位置准备的。真正按使用面就该砍掉的那些
// （与会议重叠、与睡前重叠）仍然砍着。
public static class ActionGroupTemplates
{
    public static readonly (string Key, ActionGroup Group)[] Templates = new[]
    {
        // 图标逐组指定：「运行动作」的格子没自己设图标时跟着组走（见 PanelLayout），而组没设就是
        // 那枚共用的「一叠动作」字形——八个模板摆到面板上会长成八格一模一样，认不出哪个是哪个。
        // 码位取的都是 Windows 自带字体里画得出来的形状（验收靠 --shots，见 IconLibrary 的注释）；
        // 不必每个都在选择器那张表里——首启预设的「显示桌面」用的 E770 也不在表里，同一个先例。
        // 专注：IM 用「最小化」而不是「关闭」——关掉会漏消息，现实中没人这么用。
        // 关通知排在最前：先把打断源掐掉，再收拾屏幕，顺序反了的话收拾到一半照样会被弹窗打断。
        // 它是有状态的开关（改注册表，不会自己恢复），所以「恢复正常」那个模板必须存在、且要配一条回来。
        ("focus", new ActionGroup { Name = Strings.Get("Tpl_Focus"), Icon = "E7ED", Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "notificationsOff" },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Slack" },
            new LaunchStep { Kind = "window", Action = "minimize", Process = "Discord" },
            new LaunchStep { Kind = "system", Command = "showDesktop" },
        } } ),
        // 会议：先清桌面（共享屏幕前别露隐私），再把音量设到能听清的档位。
        // 绝不能用 volume/mute——那静的是整机输出，等于把自己的耳朵关掉。
        // 也不再逐个 window/minimize 收 IM：showDesktop 本就把所有窗口（含 Slack/Discord）一并最小化，
        // 而 window/minimize 的实现是「先还原并置前台、等 ~360ms、再最小化」——既是重复劳动，
        // 又会在刚清干净的（很可能正在共享的）屏幕上，把聊天窗口重新亮出来小一秒。
        // 麦克风先静音：会前最容易出事的就是「以为没开麦」，这一步关的是系统默认录音设备本身，
        // 比任何会议软件自己的静音都彻底。进会后手动开麦即可——反过来（默认开着）风险大得多。
        ("meeting", new ActionGroup { Name = Strings.Get("Tpl_Meeting"), Icon = "E716", Steps = new()
        {
            new LaunchStep { Kind = "volume", Action = "micMute" },
            new LaunchStep { Kind = "system", Command = "notificationsOff" },
            new LaunchStep { Kind = "system", Command = "showDesktop" },
            new LaunchStep { Kind = "volume", Action = "set", Level = 70 },
        } } ),
        // 恢复正常：专注 / 会议关掉的东西，这里一次开回来。
        // 通知 / 麦克风静音都是有状态的开关——只给「关」不给「开」，用户第二天会以为通知坏了。
        // 亮度放最后且给 80 而不是 100：100 在多数笔记本上刺眼，80 是「回到正常工作亮度」
        // （模板里没有调暗的组，但亮度命令在，手动调暗过的人需要这个回程）。
        ("restore", new ActionGroup { Name = Strings.Get("Tpl_Restore"), Icon = "E895", Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "notificationsOn" },
            new LaunchStep { Kind = "volume", Action = "micUnmute" },
            new LaunchStep { Kind = "volume", Action = "unmute" },
            new LaunchStep { Kind = "system", Command = "brightness", Level = 80 },
        } } ),
        // 收工：确认闸门在最前（答「否」整组中止），锁屏必须在最后。
        ("endOfDay", new ActionGroup { Name = Strings.Get("Tpl_EndOfDay"), Icon = "E7C1", Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_EndOfDayMsg"), Confirm = true },
            new LaunchStep { Kind = "window", Action = "close", Process = "Slack" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Discord" },
            new LaunchStep { Kind = "system", Command = "clearClipboard" },
            new LaunchStep { Kind = "system", Command = "lockScreen" },
        } } ),
        // 睡前：锁屏 + 息屏都要——只息屏的话动下鼠标就亮、机器还在跑。
        ("bedtime", new ActionGroup { Name = Strings.Get("Tpl_Bedtime"), Icon = "E708", Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_BedtimeMsg"), Speak = true },
            new LaunchStep { Kind = "volume", Action = "mute" },
            new LaunchStep { Kind = "window", Action = "close", Process = "Slack" },
            // lockScreen 后多留 1.5 秒再息屏：LockWorkStation 是「发起后立即返回」的（换成原生调用之后
            // 依然如此，不是子进程的锅），返回时「已锁屏」远没完成，而锁屏过渡本身就是显示活动——
            // 紧接着 monitorOff 会黑一下又被唤醒。
            new LaunchStep { Kind = "system", Command = "lockScreen", DelayMs = 1500 },
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } } ),
        ("away", new ActionGroup { Name = Strings.Get("Tpl_Away"), Icon = "E72E", Steps = new()
        {
            new LaunchStep { Kind = "system", Command = "lockScreen", DelayMs = 1500 },   // 同「睡前」：等锁屏落定再息屏
            new LaunchStep { Kind = "system", Command = "monitorOff" },
        } } ),
        // 唯一演示「整组循环」的模板：45 分钟一轮、跑 8 轮 ≈ 覆盖一个下午。
        ("sedentary", new ActionGroup { Name = Strings.Get("Tpl_Sedentary"), Icon = "E916", Repeat = 8, RepeatDelayMs = 2700000, Steps = new()
        {
            new LaunchStep { Kind = "message", Message = Strings.Get("Tpl_SedentaryMsg") },
        } } ),
        // 唯一演示「步骤之间传值」的模板：问一句 → 拿答案去搜。两步，但它是整套变量机制
        // 在界面上**唯一**看得见的入口——「输出到变量」那一行不打开编辑器根本遇不到，
        // 而没人会去猜一个自己没见过的花括号语法。绑一条手势就是随手一划弹框搜东西。
        //
        // 变量名从文案表取，再拼进地址里：两处写死的话，翻译改了名字而地址没改，
        // 这个模板在那种语言下就静默失效——地址栏里留着一个换不掉的 {关键词}。
        ("search", SearchTemplate()),

        // 逐行输入：**唯一演示块循环的模板**，也是唯一一个交互式的。
        // 收录标准里的「至少一样别处没演示」它占了两样——块循环（久坐提醒演示的是
        // 整组 Repeat，那是另一回事：没有列表、没有变量、没有条件）和变量条件。
        //
        // 顺序是有讲究的：**「跳出」排在「发送文本」前面**。Esc 要停的是「这一行还没发出去」，
        // 反过来的话每按一次 Esc 都会先把那行敲进去，用户得按两次才能真的停。
        //
        // 循环变量用默认的 item / index：它们是 RunVars 内部的约定名，写死在模板里
        // 反而不如留空——留空由 RunEach 回落到同一个约定，两处不会分叉。
        ("typeLines", TypeLinesTemplate()),

        // 打开本地源文件：看自己博客上的文章、直接改本地那份 Markdown。
        // **两步，不再是一步。** 「读地址栏」和「网址映射」现在各是一个通用步骤，
        // 「看一眼线上文章、改它的源文件」这一整件事属于模板层，在这里拼出来。
        // 工具箱里只放原语（见 StepKindSections 与 StepRunner 里 siteMap 那段注释）。
        //
        // 它演示的两样别处都没有：UI Automation 读地址栏（读全局键态，不抢钩子链），
        // 以及「域名 → 本地路径」那张可在设置里改的站点映射表。
        //
        // **没配站点表时它会报错，这是有意的**：那句话（说清楚要先去设置里的「站点映射表」
        // 加一条）就是这个模板的第一课。用一个假装能用的模板把它藏起来，用户会以为功能坏了；
        // 而这里报错的那一刻正是他该知道站点表存在的时候。
        ("siteSource", SiteSourceTemplate()),
    };

    /// <summary>内置模板清单（一份全新副本 + 全新 Id，每次调用都不撞上一次那份）。</summary>
    public static List<ActionGroup> All() => Templates.Select(t => Fresh(t.Group)).ToList();

    /// <summary>按 key 取内置模板；没有（或 key 写错）返回 null。给的是全新副本，可直接存进配置。</summary>
    public static ActionGroup? BuiltIn(string key)
    {
        var hit = Templates.FirstOrDefault(t => t.Key == key);
        return hit.Group == null ? null : Fresh(hit.Group);
    }

    private static ActionGroup Fresh(ActionGroup src)
    {
        var g = ConfigStore.DeepClone(src);
        Reidentify(g);
        return g;
    }

    /// <summary>界面上「模板」那一份清单：用户改过的用改过的，没改过的用内置的。</summary>
    // 每次返回全新副本 + 全新的 Id。Templates 是静态只读数组，若把里面的实例直接交给调用方，
    // 编辑器改一处就把内置模板本身改了（下次再打开看到的是上一次编辑的残留）——
    // All() 一直以来「每次现生成」的承诺也是这么写的，有测试盯着。
    public static List<TemplateView> Resolve(RootConfig? cfg)
    {
        var overrides = cfg?.Templates ?? new List<TemplateOverride>();
        return Templates.Select(t =>
        {
            var o = overrides.FirstOrDefault(x => x.Key == t.Key);
            return new TemplateView(t.Key, Fresh(o?.Group ?? t.Group), o != null);
        }).ToList();
    }

    /// <summary>把一组连同它的每一步换成全新 Id，并把 ParentId 跟着改指过去。</summary>
    // **必须一起改**：ParentId 指的就是步骤 Id，只换被指的那一端的话，块会认不到自己的孩子
    // （界面上那些步骤会平铺掉，而跑起来它们还在块外）。
    private static void Reidentify(ActionGroup g)
    {
        g.Id = Guid.NewGuid().ToString();
        var map = new Dictionary<string, string>();
        foreach (var s in g.Steps)
        {
            var id = Guid.NewGuid().ToString();
            if (!string.IsNullOrEmpty(s.Id)) map[s.Id] = id;
            s.Id = id;
        }
        foreach (var s in g.Steps)
            if (!string.IsNullOrEmpty(s.ParentId) && map.TryGetValue(s.ParentId, out var to)) s.ParentId = to;
    }

    /// <summary>模板清单的一项：身份、内容，以及「这份是不是用户改过的」。</summary>
    public sealed record TemplateView(string Key, ActionGroup Group, bool Customized);

    // 逐行输入：列表 → 块循环 → 每轮等一个键 → 发一行；Esc 走的是「跳出」。
    private static ActionGroup TypeLinesTemplate()
    {
        var linesVar = Strings.Get("Tpl_LinesVar");
        var keyVar = Strings.Get("Tpl_KeyVar");

        // 块先建：子步骤的 ParentId 要指到它的 Id，而 Id 是它自己生成的。
        var loop = new LaunchStep { Kind = "each", Text = linesVar };

        return new ActionGroup
        {
            Name = Strings.Get("Tpl_TypeLines"),
            Icon = "E700",
            Steps = new()
            {
                new LaunchStep
                {
                    Kind = "list",
                    Message = Strings.Get("Tpl_TypeLinesAsk"),
                    OutputVar = linesVar,
                },
                loop,
                // 等 Insert 或 Esc。输出的是**键名**（Escape），不是 27——
                // 下一行的条件直接写它，用户不用去查虚拟键码。
                new LaunchStep
                {
                    Kind = "waitKey", ParentId = loop.Id,
                    Text = "Insert,Escape", OutputVar = keyVar,
                },
                // 条件挂在「跳出」这一步上，而不是单独加一个分支步骤：
                // 按 Insert 时这一条不成立、被跳过，接着执行发文本；
                // 按 Escape 时它成立，跳出本块（= 进入下一次迭代），发文本那一步被跳过。
                new LaunchStep
                {
                    Kind = "skip", ParentId = loop.Id, Action = "block",
                    IfVar = keyVar, IfVarOp = "eq", IfVarValue = "Escape",
                },
                // 目标进程留空 = 发给当前焦点窗口。用热键或手势触发时焦点还在你要敲的那个窗口；
                // 填了具体进程名反而更可能指错（该敲的窗口没在前台，它就被送到别处去了）。
                new LaunchStep
                {
                    Kind = "text", ParentId = loop.Id,
                    Text = "{item}", DelayMs = 60,
                },
            },
        };
    }

    // 打开本地源文件：看自己博客上的文章、直接改本地那份 Markdown。
    // **两步，不再是一步。** 「读地址栏」和「网址映射」现在各是一个通用步骤
    // （见 StepKindSections 与 StepRunner 里 siteMap 那段注释）：工具箱里只放原语，
    // 「看一眼线上文章、改它的源文件」这一整件事属于模板层，在这里拼出来。
    // 两步而不是一步的另一个好处：想映射一个手打的网址时，不必先跑一次读地址栏。
    private static ActionGroup SiteSourceTemplate()
    {
        var urlVar = Strings.Get("Tpl_UrlVar");
        return new ActionGroup
        {
            Name = Strings.Get("Tpl_SiteSource"),
            Icon = "E8F1",
            Steps = new()
            {
                new LaunchStep { Kind = "browserUrl", OutputVar = urlVar },
                new LaunchStep { Kind = "siteMap", Action = "open", Text = "{" + urlVar + "}" },
            },
        };
    }

    private static ActionGroup SearchTemplate()
    {
        var v = Strings.Get("Tpl_SearchVar");
        return new ActionGroup { Name = Strings.Get("Tpl_Search"), Icon = "E721", Steps = new()
        {
            new LaunchStep { Kind = "prompt", Message = Strings.Get("Tpl_SearchAsk"), OutputVar = v },
            new LaunchStep { Kind = "url", Target = string.Format(StepDisplay.DefaultSearchUrl, "{" + v + "}") },
        } };
    }
}
