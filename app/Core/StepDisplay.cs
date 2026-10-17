using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clockwork.I18n;

namespace Clockwork.Core;

// 步骤/系统命令/星期的显示文案。文案取自 resx（Strings.Get），随 UI 文化中/英切换。
public static class StepDisplay
{
    // 下拉顺序 = 这里的顺序：先「桌面上顺手做的事」，再显示器 / 通知 / 亮度这类环境开关，
    // 最后才是息屏 → 睡眠 → 休眠 → 注销 → 重启 → 关机这条越来越狠的下坡路（危险的排在末尾，不容易点错）。
    private static readonly string[] SysCmdIds =
    {
        "showDesktop", "lockScreen", "emptyRecycleBin", "openSettings", "screenshot", "clearClipboard",
        "setClipboard", "searchSelection", "quickOpen", "playSound", "taskManager",
        "displayInternal", "displayClone", "displayExtend", "displayExternal",
        "notificationsOff", "notificationsOn", "brightness",
        "monitorOff", "sleep", "hibernate", "signOut", "restart", "shutdown",
    };

    // 带参数的系统命令：摘要里要把参数一起写出来（否则清单上三条「设置剪贴板文本」长得一模一样）。
    public static bool SystemCommandTakesText(string id) => id is "setClipboard" or "searchSelection" or "playSound";

    // 摘要里要不要把那个参数写出来，取决于它是不是这一步「在做的事」本身。
    // 剪贴板文本是——不写出来的话，三条「设置剪贴板文本」在清单上长得一模一样；
    // 声音文件同理（「播放声音 ding.wav」）；
    // 搜索用的引擎地址不是——它是个设置，一串 https://… 塞进摘要只会把「搜索选中的文字」挤没。
    private static bool ArgIsTheSubject(string id) => id is "setClipboard" or "playSound";

    public static bool SystemCommandTakesLevel(string id) => id == "brightness";

    /// <summary>siteMap 这一步打开方式：open（默认）/ reveal（资源管理器里选中）/ none（只产出不打开）。</summary>
    // 单一出处：摘要（决定说「打开」还是「定位」）、编辑器下拉、运行时三处都读它。
    // 各写一份的话，改动一旦只落一处，清单上就会写着「定位」而实际打开了文件。
    public static string SiteMapActionOf(LaunchStep s)
    {
        var a = (s.Action ?? "").Trim().ToLowerInvariant();
        return a is "reveal" or "none" ? a : "open";
    }

    // 常见搜索引擎的地址模板，{0} 是被搜的词。**名字不进文案表**：Bing / Google / 百度 都是商标，
    // 各语言写法本来就一样（中文界面写 Bing 也没人不认识），翻译它们只会造出十八份要维护的同义词。
    //
    // 收这几个的理由是「它在某处是默认」：Google 全球、Bing 全球且国内可直连、百度国内、
    // DuckDuckGo 隐私向、Yandex 俄语区、Naver 韩语区——恰好覆盖本程序支持的语言里有自己搜索习惯的那几档。
    // 想要别的照样填得进来（自定义），所以这张表不必求全。
    public static readonly (string Name, string Url)[] SearchEngines =
    {
        ("Bing", "https://www.bing.com/search?q={0}"),
        ("Google", "https://www.google.com/search?q={0}"),
        ("百度", "https://www.baidu.com/s?wd={0}"),
        ("DuckDuckGo", "https://duckduckgo.com/?q={0}"),
        ("Yandex", "https://yandex.com/search/?text={0}"),
        ("Naver", "https://search.naver.com/search.naver?query={0}"),
    };

    /// <summary>搜索选中文字的默认地址；留空即用它。</summary>
    // 必应而不是别的：它在国内外都能直接打开，不需要用户先解决一次「打不开」才用得上这一步。
    public static string DefaultSearchUrl => SearchEngines[0].Url;

    /// <summary>这个地址是不是表里某个引擎；不是就返回 null（= 自定义）。</summary>
    public static string? SearchEngineOf(string? url)
    {
        var u = (url ?? "").Trim();
        foreach (var (name, tpl) in SearchEngines) if (tpl == u) return name;
        return null;
    }

    // 步骤类型 id 的规范顺序（步骤编辑器「类型」下拉用；标签一律经 StepKindLabel 本地化）。
    public static readonly string[] StepKinds =
        { "app", "url", "path", "siteMap", "keys", "mouse", "text", "volume", "window", "system", "group",
          "copySelection", "waitClipboard", "browserUrl", "prompt", "choice", "list", "delay", "message",
          "regex", "replace", "urlDecode", "join",
          "waitKey", "splitLines", "math", "skip", "each" };

    // 「引用启动清单项」**不在** StepKinds 里：它不是机制类型，而是面板专属的复用入口——
    // 执行分流只存在于面板格子的漏斗（PanelCoordinator.RunStep），组内/清单里的步骤到不了那里。
    // 所以类型下拉只在面板语境（编辑器拿到清单引用）时把它追加进去，其余语境连看见都看不见。
    public const string RefKind = "step";

    // 「新增」菜单与动作组工具箱的**分节索引**：按用户意图 / 操作对象分组，只此一份。
    //
    // **分法照着 Quicker 的模块分类来（getquicker.net/KC/Help「组合动作模块说明」），不是照着
    // "实现上像不像"。** 关键的三条与上一版不同：
    //
    //   1. **日常的原子操作聚在「常用」一节**：打开网址、运行程序、发送文本、获取选中的文字、
    //      用户输入、提示消息、等待时间——正是 Quicker「常用模块」那一节的东西。上一版把它们
    //      散进「打开 / 控制 / 取值」三节，于是「等两秒」和「取选中的文字」永远不在同一页。
    //   2. **其余各节按操作对象分**（界面交互 / 文本处理 / 剪贴板 / 文件与路径 / 系统与窗口 / 计算），
    //      名字就是 Quicker 的节名，不另发明。上一版的「取值」是自造的：Quicker 没有这一节，
    //      「获取当前浏览器网址」在它那儿属于 **Win系统操作**。
    //   3. **程序流控制里只剩结构**：每个 / 跳出。**「等待时间」不在里面**——它在 Quicker 的
    //      常用模块里，上一版把它塞进流程控制是错的（用户报过一次）。
    //
    // **这张表是索引，不是划分**：一个步骤类型可以出现在多节里（Quicker 官方原话：「个别模块会
    // 属于多个分类」）。所以「用户输入」既在「常用」也在「界面交互」，「获取选中的文字」既在
    // 「常用」也在「剪贴板」。对应的断言盯的是**每种类型至少出现在一节**，不再盯「恰好一次」。
    //
    // 「动作组」自成一节且排在最前：想找它的人不会去别的节里翻，而面板上的「运行动作」格子
    // 与启动清单的「选现有动作组」都靠它。
    public static readonly (string SectionKey, string[] Kinds)[] StepKindSections =
    {
        ("Menu_SecCommon", new[] { "url", "app", "text", "copySelection", "prompt", "choice", "list", "message", "delay", "waitClipboard" }),
        ("Menu_SecUI", new[] { "prompt", "choice", "list", "waitKey", "message" }),
        ("Menu_SecClip", new[] { "copySelection", "waitClipboard" }),
        ("Menu_SecText", new[] { "regex", "replace", "urlDecode", "splitLines", "join" }),
        // 「网址 → 本地文件」也在这一节：它产出的是一个本地路径，按对象分就该在这儿，
        // 而不是一个和「打开网址 / 打开文件夹」并排的「打开」原语（见 SiteSourceTemplate 的注释）。
        ("Menu_SecFile", new[] { "path", "siteMap" }),
        ("Menu_SecSys", new[] { "window", "keys", "mouse", "browserUrl", "system", "volume" }),
        ("Menu_SecCalc", new[] { "math" }),
        ("Menu_SecCtrl", new[] { "each", "skip" }),
        ("Tab_Group", new[] { "group" }),
    };

    /// <summary>一个步骤类型出现在哪几节，按 <see cref="StepKindSections"/> 的节序。</summary>
    // 工具箱的搜索结果要按节分组显示，而搜索框只有一份——分组这件事靠它。
    public static IEnumerable<string> SectionsOf(string kind)
        => StepKindSections.Where(s => s.Kinds.Contains(kind)).Select(s => s.SectionKey);

    /// <summary>这个类型归哪一节（「仅在一节出现时」的场合用；多节时取第一节，即最常用的那个）。</summary>
    public static string PrimarySectionOf(string kind) => SectionsOf(kind).DefaultIfEmpty("").First();

    // 「常用」——一份预填好的步骤。它们**没有任何新的执行路径**：每一条都只是既有类型
    // （keys / window / system）填好字段的样子，选完照样进同一个步骤编辑器，进去还能改。
    // 存在的理由只有一个：找得到。「复制」这一步的真身是「发送按键 Ctrl+C」，
    // 可你得先想到去「发送按键」里敲 Ctrl+C —— 而画手势的人心里想的词就是「复制」。
    //
    // 只收**手边这一刻**才成立的动作（复制什么？最小化哪个窗口？答案都是「你正看着的那个」），
    // 所以开机清单不显示这一节：开机时没人在选文字，也没有「当前窗口」。
    public static readonly (string LabelKey, string Kind, string Arg)[] CommonPresets =
    {
        ("Cmd_copy", "keys", "Ctrl+C"),
        ("Cmd_paste", "keys", "Ctrl+V"),
        ("Sys_searchSelection", "system", "searchSelection"),
        ("Cmd_back", "keys", "Alt+Left"),
        ("Cmd_forward", "keys", "Alt+Right"),
        // 媒体键与音量键**一直都能用**（KeysVk 走的是 Keys 枚举全表），只是你得先知道
        // 要往「发送按键」里敲 MediaPlayPause 这种词。摆到这儿来，它们才第一次被人看见。
        ("Cmd_playPause", "keys", "MediaPlayPause"),
        ("Cmd_prevTrack", "keys", "MediaPreviousTrack"),
        ("Cmd_nextTrack", "keys", "MediaNextTrack"),
        // 音量走按键而不是 volume 步骤：volume 只有「设为 N%」这种绝对值，
        // 而手势要的是「调高一点」——一次一格，正是这两个键的语义。
        ("Cmd_volUp", "keys", "VolumeUp"),
        ("Cmd_volDown", "keys", "VolumeDown"),
        ("Win_minimizeFg", "window", "minimize"),
        ("Win_maximizeFg", "window", "maximize"),
        ("Win_topmostFg", "window", "topmost"),
        ("Win_closeFg", "window", "close"),
        // 排在最后：它是这一列里唯一**不可撤销**的。清的是所有驱动器、不弹确认（SHERB_NOCONFIRMATION），
        // 与「关闭当前窗口」同理放在末位——最不该误点的排在最不容易误点的位置。
        ("Sys_emptyRecycleBin", "system", "emptyRecycleBin"),
    };

    /// <summary>把一条预设摊成真正的步骤。窗口类用 * 表示「当前窗口」（理由见 WindowManager.CurrentWindow）。</summary>
    public static LaunchStep MakePreset(string kind, string arg) => kind switch
    {
        "keys" => new LaunchStep { Kind = "keys", Combo = arg },
        "window" => new LaunchStep { Kind = "window", Action = arg, Process = CurrentWindowMark },
        _ => new LaunchStep { Kind = "system", Command = arg, Text = arg == "searchSelection" ? DefaultSearchUrl : "" },
    };

    // 窗口动作 id ↔ 文案键的唯一表。编辑器下拉与摘要都从这里取——与 MouseActions 同一条纪律：
    // 各写一份的话，加一个动作要改好几处，漏一处就是「下拉里选得到、跑起来说不认识这个动作」。
    // 顺序即下拉顺序：三个窗口状态（关/最小化/最大化）、置顶、再到需要抢前台的那两个，
    // 最后是「恢复活动窗口」——它是这张表里唯一**不选目标**的一条（目标就是你刚才在用的那个），
    // 摆在末尾，免得一个不需要填进程名的动作夹在一串需要填的中间。
    public static readonly (string Action, string LabelKey)[] WindowActions =
    {
        ("close", "Win_close"), ("minimize", "Win_minimize"), ("maximize", "Win_maximize"),
        ("topmost", "Win_topmost"), ("activate", "Win_activate"), ("sendkey", "Win_sendkey"),
        ("restore", "Win_restore"),
    };

    /// <summary>这个窗口动作要不要选一个目标进程。</summary>
    //
    // 只有「恢复活动窗口」不要：它的目标是**这次运行开始时的前台窗口**，
    // 那是运行时才知道的东西，配置期没有可填的名字。编辑器据此把进程名那几行整片收起来——
    // 留一个填了也不生效的框，比没有更糟。
    public static bool WindowActionNeedsTarget(string? action) => action != "restore";

    /// <summary>窗口动作的下拉项（文案已本地化）。</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> WindowActionMap()
        => WindowActions.Select(w => new KeyValuePair<string, string>(w.Action, Strings.Get(w.LabelKey))).ToList();

    // 鼠标步骤的动作 id ↔ 文案键 ↔ 伪键三者的唯一映射表。摘要、编辑器下拉、执行三处都从这里取——
    // 各写一份的话，加一个动作就得改三处，漏一处就是「下拉里选得到、跑起来没反应」这种最难查的错。
    // 缺省（手改 json 漏填 action、或旧配置）落到向下滚，与编辑器下拉的首项一致。
    public static readonly (string Action, string LabelKey, string Combo)[] MouseActions =
    {
        ("down", "Wheel_down", "WheelDown"), ("up", "Wheel_up", "WheelUp"),
        ("left", "Wheel_left", "WheelLeft"), ("right", "Wheel_right", "WheelRight"),
        ("leftClick", "Mouse_leftClick", "LeftClick"), ("doubleClick", "Mouse_doubleClick", "DoubleClick"),
        ("rightClick", "Mouse_rightClick", "RightClick"), ("middleClick", "Mouse_middleClick", "MiddleClick"),
        ("back", "Mouse_back", "MouseBack"), ("forward", "Mouse_forward", "MouseForward"),
    };

    private static (string Action, string LabelKey, string Combo) MouseAction(string? action)
    {
        foreach (var m in MouseActions) if (m.Action == action) return m;
        return MouseActions[0];
    }

    public static string MouseActionLabelKey(string? action) => MouseAction(action).LabelKey;
    public static string MouseActionCombo(string? action) => MouseAction(action).Combo;

    // 已知键则取译文，否则原样返回（未知 kind/command）。
    private static string OrRaw(string key, string raw)
    {
        var s = Strings.Get(key);
        return s == key ? raw : s;
    }

    public static string StepKindLabel(string kind) => OrRaw("Kind_" + kind, kind);

    /// <summary>这一步是不是一个「块」（有孩子、会被重复执行的那一种）。</summary>
    // 编辑器据此决定「新增」时新步骤挂进谁、删除时要不要连坐、步序重排时能不能跨层。
    // 目前只有「每个」。**单一定义在这**：编辑器与运行期各写一份的话，加第二种块时
    // 就会出现「编辑器认得它、runner 不认得它」或者反过来——而那种差异在界面上完全看不出来。
    public static bool IsBlockKind(string? kind) => kind == "each";

    /// <summary>动作组的内容摘要：前三步的动作串起来，多余的用省略号收尾。
    /// 动作组列表那一列、以及步骤编辑器里选组时的内容预览，用的都是它——
    /// 两处回答的是同一个问题「这个组里有什么」，口径必须一致，各写一份迟早漂移（曾经就是两份）。
    /// 用 StepSummary 而非 StepListSummary：后者会把「用途说明」当后缀拼进去，在窄处太长。</summary>
    public static string GroupSummary(ActionGroup? g)
    {
        if (g == null) return "";
        if (g.Steps.Count == 0) return Strings.Get("Group_Empty");
        var head = string.Join(" · ", g.Steps.Take(3).Select(StepSummary));
        return g.Steps.Count > 3 ? head + " …" : head;
    }

    /// <summary>把所有 group 步骤的 Label 重新对齐到目标组的当前名字，返回是否有改动。
    /// Label 对 group 步骤不是用户自己填的（编辑器保存时一律覆盖成组名），所以这里可以放心改写。
    /// 目标组已不存在的不动——那种 Label 是仅存的线索，抹掉只会让「这一步原本指向谁」彻底消失。</summary>
    public static bool SyncGroupStepLabels(IEnumerable<LaunchStep>? steps, IReadOnlyList<ActionGroup> groups)
    {
        if (steps == null) return false;
        bool touched = false;
        foreach (var s in steps)
        {
            if (s == null || s.Kind != "group") continue;
            var g = ActionGroupResolver.Resolve(groups, s.GroupId);
            if (g == null || s.Label == g.Name) continue;
            s.Label = g.Name;
            touched = true;
        }
        return touched;
    }

    // 有序系统命令表（编辑器下拉与摘要共用）：id 固定、标签本地化。
    public static IReadOnlyList<KeyValuePair<string, string>> SystemCommandMap()
        => SysCmdIds.Select(id => new KeyValuePair<string, string>(id, Strings.Get("Sys_" + id))).ToList();

    public static string SystemCommandLabel(string id) => OrRaw("Sys_" + id, id);

    // 系统命令的文案后面挂着一句括号解释——「锁屏（回来需输密码）」「休眠（存盘断电，开机恢复现场）」。
    // 那句解释在下拉里是必要的：锁屏 / 息屏 / 睡眠 / 休眠 四个词光看名字分不出差别，选错一次代价不小。
    // 但**当名字用**的时候它就是灾难：面板格子只有 88 宽，整句进去只剩「锁屏（回来...」，
    // 括号一开就没了下文，反倒比单写「锁屏」更难认。
    //
    // 所以下拉取长的，格子和摘要取短的。半角括号也要认——中文文案用（），其余语言用 ()。
    public static string SystemCommandShortLabel(string id) => StripTrailingAside(SystemCommandLabel(id));

    private static readonly System.Text.RegularExpressions.Regex TrailingAside =
        new(@"\s*[（(][^（()）]*[)）]\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>去掉结尾那一段括号里的补充说明；没有就原样返回。</summary>
    public static string StripTrailingAside(string label)
    {
        var trimmed = TrailingAside.Replace(label ?? "", "");
        // 整条都在括号里的话（某些语言可能这么译）就别删了——删完是空字符串，那比啰嗦糟得多。
        return trimmed.Length > 0 ? trimmed : (label ?? "");
    }

    // 星期集合 → 文案：空或全 7 天=每天；连着三天以上收成一段区间（周一–周五），
    // 剩下的照旧逐日列。
    // 区间只取两端 + 一个连接号（Days_RangeSep），不是「{0} 至 {1}」那种整句键：两端本来就是
    // 各语言自己的星期名。但那个号**必须**是每语言自己的——逐语言复核指出，韩语的区间记号是
    // 「~」、日语是「〜」，硬写一个 en dash 在这两种语言里看着就是机器拼的。
    // 阈值取 3：两天收成区间反而看不出是哪两天（「周六–周日」不如「周六、周日」直白）。
    public static string DaysLabel(IEnumerable<int>? days)
    {
        var d = (days ?? Enumerable.Empty<int>()).ToList();
        if (d.Count == 0 || d.Count == 7) return Strings.Get("Days_EveryDay");
        var on = d.Where(x => x >= 1 && x <= 7).OrderBy(x => x).ToList();
        if (on.Count == 0) return Strings.Get("Days_EveryDay");
        // 首尾都在、且中间无空档 = 一段连续的星期，才配得上区间。
        // 周日与周一在 1..7 里是两端而非相邻，这种环绕的两天照旧逐日列——
        // 「周一–周日」会把「只勾了周日和周一」说成「每天」，那是撒谎。
        bool contiguous = on.Count >= 3 && on.Last() - on.First() + 1 == on.Count;
        if (contiguous)
            return Strings.Get("Day_" + on.First()) + Strings.Get("Days_RangeSep") + Strings.Get("Day_" + on.Last());
        var sep = Strings.Get("Days_Sep");
        return string.Join(sep, on.Select(x => Strings.Get("Day_" + x)));
    }

    private static string NoNewline(string s) => Regex.Replace(s ?? "", @"\r?\n", " ");

    private static string WinActionLabel(string action)
    {
        foreach (var (a, key) in WindowActions) if (a == action) return Strings.Get(key);
        return action;   // 手改 json 填了个不认识的动作：原样显示，别装作认识它
    }

    /// <summary>进程名填这个即「当前窗口」。与 Native.WindowManager.CurrentWindow 是同一个值——
    /// Core 不引用 Native，所以这里留一份常量，并由测试钉住两边不会漂移。</summary>
    public const string CurrentWindowMark = "*";

    public static bool IsCurrentWindow(LaunchStep s) => (s.Process ?? "").Trim() == CurrentWindowMark;

    // 打「当前窗口」的窗口步骤。这三条不是「最小化窗口 + 当前窗口」拼出来的——
    // 拼出来是「最小化窗口 当前窗口」，两个「窗口」读着别扭，而这三句恰恰是菜单上要认的词。
    private static string WinCurrentLabel(string action) => action switch
    {
        "close" => Strings.Get("Win_closeFg"),
        "minimize" => Strings.Get("Win_minimizeFg"),
        "maximize" => Strings.Get("Win_maximizeFg"),
        "topmost" => Strings.Get("Win_topmostFg"),
        _ => $"{WinActionLabel(action)} {Strings.Get("Win_current")}",
    };

    /// <summary>窗口步骤的目标显示名（告警里用）。</summary>
    public static string WindowTarget(LaunchStep s)
        => IsCurrentWindow(s) ? Strings.Get("Win_current") : s.Process;

    public static string StepSummary(LaunchStep s) => StepBase(s) + DecorationSummary(s);

    /// <summary>给**搜索**用的完整语料：与 <see cref="StepSummary"/> 同源，但长路径 / 网址 / 长文本
    /// 不截断，并补上 app 步骤的目标原文与用途说明。</summary>
    //
    // 显示和检索是两种相反的要求，必须分开：ToolTip 上那条要在窄处可读，所以路径截到 48 字；
    // 但用户搜索时常常只记得路径尾巴那一段（…\Start Menu\Programs\Startup）、或者按目标文件名
    // 找一个起了中文名的步骤（名字叫「终端」、目标是 wt.exe）。截掉的部分永远搜不到。
    public static string StepSearchText(LaunchStep s)
    {
        var text = StepBase(s, full: true) + DecorationSummary(s);
        // app 分支有 Label 时摘要里只剩名字，没 Label 时也只有叶子名——完整目标路径在任何情况下
        // 都不进摘要，搜索却用得上（「wt」「WindowsApps」）。url / path 的目标已在不截断的主体里。
        if (s.Kind == "app")
        {
            var target = NoNewline(LaunchTarget.NormalizeTarget(s.Target));
            if (!string.IsNullOrWhiteSpace(target)) text += " " + target;
        }
        if (!string.IsNullOrWhiteSpace(s.Note)) text += " " + NoNewline(s.Note);
        return text;
    }

    /// <summary>格子/标题用的短文案：用户起的名字优先，没起名才用动作本身的描述，
    /// 且**不带**修饰段（×N、星期、条件后缀）。</summary>
    //
    // 与 StepSummary 分开是因为两者服务的宽度差一个量级：列表里一行有几百像素，
    // 把条件后缀写全是它的价值所在（「这一步为什么没跑」的唯一线索）；而快捷面板的格子只有 116 像素，
    // 同一串文字在那里会被裁成一句读不出意思的残句。
    // Label 在这里统一优先——StepBase 里只有 app / group 两种 kind 照顾了 Label，
    // 而用户给某一步起了名字，就是希望在能显示名字的地方看到它。
    public static string StepTitle(LaunchStep s)
        => !string.IsNullOrWhiteSpace(s.Label) ? s.Label : TileBase(s);

    // 格子标题与列表摘要的分工：**图标说动词，标题说对象**。
    // 列表里没有图标，所以摘要必须自带动词（「关闭窗口 Slack」）；格子上动词已经画在图标里了，
    // 再写一遍就把仅有的那点宽度花在了每一格都相同的字上——于是「关闭窗口 Slack」和
    // 「关闭窗口 Discord」双双截断成「关闭窗口 S…」「关闭窗口 D…」，两格看着一模一样。
    // 完整那句退到 ToolTip（面板与管理器都取 StepSummary），一个都没丢。
    private static string TileBase(LaunchStep s) => s.Kind switch
    {
        // 「当前窗口」那一档不走这条快路：Process 存的是哨兵值 "*"，
        // 原样返回就是拿一个星号当格子标题。落回 StepBase，它那边已经用 WinCurrentLabel
        // 给出「最小化当前窗口」这类真正的名字。（默认手势逃过一劫只是因为
        // RootConfig.DefaultGestures() 显式给每条赋了 Label；从面板 / 手势管理器里新加一个
        // 「常用 → 最小化当前窗口」则不会自动填 Label，那时就现形了。）
        "window" when !IsCurrentWindow(s) && !string.IsNullOrWhiteSpace(s.Process) => s.Process,
        _ => StepBase(s),
    };

    // **组合键原样写，裸键名换成名字。** 这不是两套口径，是同一条：组合键是个配方，
    // 「发送 Ctrl+C」一看就懂，换成「发送 复制」反而绕；而裸键名是个标识符——
    // 「发送 MediaPlayPause」在一处处本地化的界面里就是一串生硬的英文，
    // 中文界面下的用户扫过去只会卡一下。「常用」里已经给过它们名字，这里用回那个名字。
    // 判据用「有没有 +」而不是列一张白名单：有 + 的就是组合，没有的就是一个键的名字。
    private static string KeyComboLabel(string combo)
    {
        if (string.IsNullOrEmpty(combo) || combo.Contains('+')) return combo;
        foreach (var (labelKey, kind, arg) in CommonPresets)
            if (kind == "keys" && arg == combo) return Strings.Get(labelKey);
        return combo;
    }

    /// <summary>没填的那一格：统一写成「（未指定）」，绝不留空。</summary>
    //
    // **一行空白是这张列表最糟的一种显示。** 编辑器只校验两件窄事（发送键语法、自定义搜索地址），
    // 目标 / 组合键 / 进程名 / 消息全都可以留空就保存——「新增 → 运行程序 → 直接确定」就能造出一行。
    // 而空白行看起来是程序坏了，不是「这一步还没配」：用户既不知道它是什么类型（类型列有，但一行空文字
    // 会让人以为整行都失效了），也不知道该去补什么。写出「（未指定）」，至少它在说「你还没填」。
    //
    // group 那一档从来就是这么做的（Sum_Unset 原名 Sum_Group_None），这里只是把同一条规矩铺到其余类型。
    private static string Unset => Strings.Get("Sum_Unset");

    /// <summary>动词后面的那个宾语：空就换成占位，别留下「关闭窗口 」这种半句话。</summary>
    private static string Arg(string? v) => string.IsNullOrWhiteSpace(v) ? Unset : v!;

    private static string StepBase(LaunchStep s, bool full = false)
    {
        var text = StepBaseCore(s, full);
        // 兜底：任何一档算出空白都不许出去。上面各分支已尽量各自补齐，这一道拦的是
        // 「以后新加一种 kind 忘了补」以及未知 kind（手改坏了的 json、降级打开新版配置）。
        return string.IsNullOrWhiteSpace(text) ? Unset : text;
    }

    private static string StepBaseCore(LaunchStep s, bool full)
    {
        // 显示要短（Ellipsis 截断），搜索语料要全（原样保留）——同一个出口，由调用方选。
        string Clip(string v, int max) => full ? v : StepHelpers.Ellipsis(v, max);
        return s.Kind switch
        {
            // 没起名就用目标的叶子名，不用整条路径：那一列是右侧截断的，而开始菜单里那种
            // 一长串前缀相同的路径，被截掉的恰好是唯一能区分它们的那一截（见 LaunchTarget.DisplayName）。
            "app" => !string.IsNullOrEmpty(s.Label) ? s.Label : Arg(LaunchTarget.DisplayName(s.Target)),
            "keys" => Strings.Lf("Sum_SendKeys", Arg(KeyComboLabel(s.Combo))),
            // 次数由摘要共用的 ×N 修饰段（DecorationSummary）负责，这里只说动作，别把次数写两遍
            "mouse" => Strings.Get(MouseActionLabelKey(s.Action)),
            "volume" => s.Action switch
            {
                "mute" => Strings.Get("Vol_mute"), "unmute" => Strings.Get("Vol_unmute"), "set" => Strings.Lf("Vol_set", s.Level),
                "micMute" => Strings.Get("Vol_micMute"), "micUnmute" => Strings.Get("Vol_micUnmute"), _ => s.Action,
            },
            // 「恢复活动窗口」没有目标，摘要就是它自己那一句——拼一个空目标会得到「恢复活动窗口 （未设置）」。
            "window" => !WindowActionNeedsTarget(s.Action) ? WinActionLabel(s.Action)
                : IsCurrentWindow(s) ? WinCurrentLabel(s.Action) : $"{Arg(WinActionLabel(s.Action))} {Arg(s.Process)}",
            // 参数为空时退回纯标签：「播放声音」留空是**常态**（= 系统提示音），
            // 拼出来会是「播放声音：」后面跟一片空白，读着像是配漏了。
            "system" => ArgIsTheSubject(s.Command) && !string.IsNullOrWhiteSpace(s.Text)
                ? Strings.Lf("Sum_SysArg", SystemCommandShortLabel(s.Command), Clip(NoNewline(s.Text), 30))
                : SystemCommandTakesLevel(s.Command)
                    ? Strings.Lf("Sum_SysArg", SystemCommandShortLabel(s.Command), s.Level + "%")
                    : SystemCommandShortLabel(s.Command),
            "group" => Strings.Lf("Sum_RunGroup", !string.IsNullOrEmpty(s.Label) ? s.Label : (!string.IsNullOrEmpty(s.GroupId) ? s.GroupId : Strings.Get("Sum_Unset"))),
            // 引用清单项：与组引用同一条兜底链（名字 → id → 「未设置」）。格子提交时 Label 同步成
            // 被引用条目的名字，所以常态下这里显示的就是那个条目的名字。
            "step" => Strings.Lf("Sum_RunRef", !string.IsNullOrEmpty(s.Label) ? s.Label : (!string.IsNullOrEmpty(s.RefId) ? s.RefId : Strings.Get("Sum_Unset"))),
            // 问句本身就是这一步的身份（「关键词？」「去哪儿？」），类型名反而是次要的——
            // 三条「用户输入」摆在清单上，不写出问的是什么就完全分不开。
            // 后面缀上「→ 变量名」：这一步的**产出**去了哪儿，是读这条动作时第二要紧的事。
            "prompt" or "choice" or "list" => Strings.Lf("Sum_Ask", Arg(NoNewline(s.Message)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "delay" => s.DelayMs % 1000 == 0 ? Strings.Lf("Sum_Delay_Sec", s.DelayMs / 1000) : Strings.Lf("Sum_Delay_Ms", s.DelayMs),
            "message" => StepHelpers.MessageFormOf(s) == MessageForm.Card
                ? Strings.Lf("Sum_MsgCard", Arg(NoNewline(s.Message)))
                : Arg(NoNewline(s.Message)),
            "text" => Strings.Lf("Sum_Text", Arg(Clip(NoNewline(s.Text), 30))),
            // 网址原样显示（截断）。它本来就可读，砍成域名反而丢了「打开的到底是哪一页」。
            "url" => Strings.Lf("Sum_OpenUrl", Arg(Clip(NoNewline(s.Target), 48))),
            // 路径与网址同一条口径：原样显示（截断 + 规范化掉引号/环境变量）。
            // D:\Work 和 E:\Work 的区别全在前缀上，像 app 那样只取叶子名就分不开了。
            "path" => Strings.Lf("Sum_OpenPath", Arg(Clip(NoNewline(LaunchTarget.NormalizeTarget(s.Target)), 48))),
            "copySelection" => Strings.Get("Kind_copySelection"),
            "waitClipboard" => Strings.Lf("Sum_WaitClipboard", StepHelpers.ClampWaitSeconds(s.Level)),

            // ---- 产出值的那一族 ----
            // 摘要是「不写出来就答不了这一步在干什么」的那些字段：
            //   browserUrl 不写参数——它没有参数，产出的名字就在「→ 变量名」那一段里（同 prompt/choice 的口径）；
            //   siteMap 写「打开的是哪个文件」，砍成域名就答不了「我要改的那篇在不在」；
            //   三个文本步骤写**输入**，不写正则——正则可能有二三十字，摘要放不下，
            //     而「在处理哪段文字」比「用什么规则处理」更能让人认出这一步。

            "browserUrl" => Strings.Get("Kind_browserUrl")
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            // 三档都要各自的动词。原来只判 reveal，于是 none 落进「打开」那一支，
            // 清单上写着「打开源文件」而这一步根本不打开——用户照着点，什么也不会发生，
            // 而界面上看不出任何异常。三档说同一句话的话，这一档就白配了。
            "siteMap" => Strings.Lf(
                    SiteMapActionOf(s) switch
                    {
                        "reveal" => "Sum_LocateSource",
                        "none" => "Sum_SourcePath",
                        _ => "Sum_OpenSource",
                    },
                    Arg(string.IsNullOrWhiteSpace(s.Text) ? Strings.Get("Sum_CurrentUrl") : Clip(NoNewline(s.Text), 48)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            // urlDecode 同样要挂「→ 变量名」：它是一条产出值的步骤，
            // 摘要里不写出来，用户就不知道后面那一步该引用什么。
            "urlDecode" => Strings.Lf("Sum_UrlDecode", Arg(Clip(NoNewline(s.Text), 30)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "regex" => Strings.Lf("Sum_Regex", Arg(Clip(NoNewline(s.Text), 24)), Arg(Clip(NoNewline(s.Pattern), 24)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "replace" => Strings.Lf("Sum_Replace", Arg(Clip(NoNewline(s.Pattern), 20)), Arg(Clip(NoNewline(s.Replace), 20)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            // 「合」写的是「从哪个列表来」，不写分隔符：分隔符往往是个换行或逗号，
            // 印进摘要既占地方又读不出什么，而从哪个列表来才是这一步的身份。
            "join" => Strings.Lf("Sum_Join", Arg(string.IsNullOrWhiteSpace(s.Text) ? Strings.Get("Sum_Unset") : Clip(NoNewline(s.Text), 28)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),

            // ---- 交互式流程 ----
            // 摘要写「等的是哪个键 / 拆的是哪段 / 算的是什么」，不写死字段名：
            // 这几步的字段名（哪些键、哪段文本、哪个表达式）用户是照着框里那一栏理解的。

            "waitKey" => Strings.Lf("Sum_WaitKey", Arg(Clip(NoNewline(s.Text), 40)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "splitLines" => Strings.Lf("Sum_SplitLines", Arg(Clip(NoNewline(s.Text), 24)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "math" => Strings.Lf("Sum_Math", Arg(Clip(NoNewline(s.Text), 30)))
                + (string.IsNullOrWhiteSpace(s.OutputVar) ? "" : Strings.Lf("Sum_ToVar", s.OutputVar.Trim())),
            "skip" => Strings.Get((s.Action ?? "").Trim().ToLowerInvariant() == "run" ? "Sum_SkipRun" : "Sum_SkipBlock"),
            // 「每个」只写清单来源，**不写孩子有几步**：摘要这一格只拿到单条步骤，看不到兄弟列表，
            // 而硬要去数就得把整棵树传进 StepBaseCore 的签名——为了一个括注改一个被十几处调用的出口。
            // 孩子有几步在编辑器里是看得见的（缩进那一列就是），清单上要个数的地方用「运行整组」看日志。
            "each" => Strings.Lf("Sum_Each",
                    Arg(string.IsNullOrWhiteSpace(s.Text) ? Strings.Get("Sum_Unset") : Clip(NoNewline(s.Text), 28))),

            _ => s.Kind,
        };
    }

    // 摘要的修饰段（×重复 + 各条件后缀），与主体分开取。步骤编辑器「条件与重复」折叠条的标题
    // 直接用它：同一份判定、同一份文案，编辑时看到的与列表里看到的严格一致，不会两处各解释一遍。
    public static string DecorationSummary(LaunchStep s)
    {
        var result = "";
        int rep = StepHelpers.StepRepeat(s);
        if (rep > 1) result += $" ×{rep}";
        var dc = (s.Days ?? new()).Where(x => x >= 1 && x <= 7).ToList();
        if (dc.Count > 0 && dc.Count < 7) result += Strings.Lf("Sum_DaysSuffix", DaysLabel(dc));
        if (s.OnlyBefore8) result += Strings.Lf("Sum_Before", StepHelpers.BeforeTimeLabel(s));
        if (s.OnlyAfter) result += Strings.Lf("Sum_After", StepHelpers.AfterTimeLabel(s));
        // 环境条件也要写进摘要：条件是「这一步为什么没跑」的唯一线索，藏在编辑器里等于没有。
        // 判的必须是 **ToProcessName 之后**的名字，与 StepCondition 同一个值。
        // 曾经这里判原串、却拿规范串去格式化，于是 "C:\Program Files\App\" 这类值
        // （ToProcessName 返空）下，摘要会宣布一个引擎已经**不再执行**的条件，
        // 而且那个 {0} 是空的——读起来就是「（ 运行中）」。条件是「这一步为何没跑」的唯一线索，
        // 让它说谎比不说更差。
        var ifProc = StepHelpers.ToProcessName(s.IfProcess);
        if (!string.IsNullOrWhiteSpace(ifProc) && s.IfProcessMode is "running" or "notRunning")
            result += Strings.Lf(s.IfProcessMode == "running" ? "Sum_IfProcRunning" : "Sum_IfProcNot", ifProc);
        if (s.IfPower == "ac") result += Strings.Get("Sum_IfAc");
        else if (s.IfPower == "battery") result += Strings.Get("Sum_IfBattery");
        if (!string.IsNullOrWhiteSpace(s.IfPathExists)) result += Strings.Lf("Sum_IfPath", StepHelpers.Ellipsis(s.IfPathExists.Trim(), 24));
        return result;
    }

    // 列表显示用摘要：用途说明作后缀。
    public static string StepListSummary(LaunchStep s)
    {
        var result = StepSummary(s);
        if (!string.IsNullOrEmpty(s.Note)) result += Strings.Lf("Sum_DaysSuffix", s.Note);
        return result;
    }
}
