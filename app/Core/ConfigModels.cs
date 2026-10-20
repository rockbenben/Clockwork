using Clockwork.I18n;

namespace Clockwork.Core;

// 纯数据模型：LaunchStep / Reminder / ActionGroup / 默认配置。
// 不引用 WPF / Win32，可被 xUnit 直接测。属性名 PascalCase，经 System.Text.Json 的 CamelCase 策略映射到既有 JSON 键。

public sealed class OnYes
{
    public string Type { get; set; } = "none";
    public string Target { get; set; } = "";
}

public sealed class LaunchStep
{
    // 稳定身份：面板格子的点击统计按它做键（见 Core.PanelUsageStore）。改文案、改名、
    // 拖位、删了重建同名格子都不会串统计。与 Reminder.Id 同一个道理。
    // 只在新建步骤时发；盘上老配置没有这个键，由 ConfigStore.Normalize 的一次性迁移补，
    // 迁移门是 PanelSchema < 4——不能用「id 为空」当判据：这个初始值让缺键的步骤
    // 反序列化后带着一个全新的非空 Guid，空值检查永远不成立，id 就会每次重启都换一次。
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Enabled { get; set; } = true;
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
    // 默认 100ms：多数动作（开程序后发按键、切窗口后发文本）需要一点缓冲；0 会打空。
    // 只影响新建对象——盘上既有步骤都带显式 delayMs（JsonOptions 不忽略默认值），读回原样。
    public int DelayMs { get; set; } = 100;
    // app
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public bool Elevated { get; set; }
    // 默认开：「启动程序」步骤的常见意图是「让它到前面来」，不是「再开一个」——尤其开机清单里，
    // 手动开过的程序会被清单又开一份。目标是 URL/.lnk/.ps1/文档时 TargetProcessName 返回空串
    // （见 LaunchTarget），本选项自动空转、照常启动，故打开它对这些步骤无副作用。
    // 只影响新建步骤：盘上既有步骤都带显式 activateIfRunning（JsonOptions 不忽略默认值），读回原样。
    public bool ActivateIfRunning { get; set; } = true;
    public string ActivateProcess { get; set; } = "";
    public string WindowStyle { get; set; } = "";
    public string AltTargets { get; set; } = "";
    // keys
    public string Combo { get; set; } = "";
    // group（引用动作组 id）
    public string GroupId { get; set; } = "";
    // kind="step"（引用启动清单项）的目标 id。面板格子借此把清单里已有的条目摆成自己的一格：
    // 路径与参数只在清单里维护一份，点击统计也聚合到被引用条目上——消灭「同一个程序配三处、
    // 换路径漏两处」。只在面板语境下可创建（步骤编辑器的类型下拉仅在面板侧带这一项）。
    public string RefId { get; set; } = "";
    // 鼠标手势（方向串，如 "RD"）。只有 RootConfig.Gestures 里的步骤用得上它，空=没绑。
    // 与 ActionGroup.Hotkey 是同一位置的东西：触发方式挂在被触发的对象上。
    // 手势绑的是**步骤**而不是动作组——「粘贴」「最小化」这类动作只作为手势存在，本来就没有组；
    // 而「跑一整个组」用 group 类型的步骤指过去就是了，那本来就是那个步骤类型的用途
    //（与面板同一条规矩，见 ActionGroup.PanelExpand 上那段说明）。
    public string Gesture { get; set; } = "";
    // 分应用手势：只在指定进程（裸名/带.exe均可）上划该手势才触发。空=全局通用。
    // 匹配时优先命中当前鼠标所在窗口的进程，未命中时再回退到全局手势。
    public string ForProcess { get; set; } = "";
    // ── 每种类型自己的参数 ──
    //
    // 从前这些值挤在两格里：**Action**（音量/窗口/鼠标/跳过/站点映射五种类型共用）与
    // **Level**（音量百分比 / 等剪贴板秒数 / 亮度百分比 / 正则组号 / 等按键秒数，五种量纲共用）。
    //
    // 共用的代价是**换类型会串味**：一条音量（50%）改成「等剪贴板」，那个数就变成 50 秒——
    // 同一格里塞着两种量纲，而编辑器只有「每种类型各一个输入框」这一道挡。
    // 手改配置也一样：把 "kind" 从 volume 改成 waitClipboard，"level": 50 就成了 50 秒。
    //
    // 现在**每个类型认领自己那一格**。串味从根上没了：改类型时新类型那一格是它自己的值，
    // 而旧类型那一格里的值还留着（改回去还在）。
    //
    // **默认值与从前逐字相同**（字符串空、数值 50）：迁移要靠它做到「读回来一模一样」；
    // 而新建时的**意图值**（等剪贴板 5 秒、音量「设到几成」）仍然由编辑器给，见 StepEditorWindow.Create。
    //
    // volume
    public string VolumeAction { get; set; } = "";
    public int VolumeLevel { get; set; } = 50;
    // window
    public string WindowAction { get; set; } = "";
    // mouse
    public string MouseAction { get; set; } = "";
    // skip（跳过一整段 / 跳过一次运行）
    public string SkipMode { get; set; } = "";
    // siteMap（打开 / 定位 / 只产出不打开）
    public string SiteMapMode { get; set; } = "";
    // system（亮度，0-100）
    public int Brightness { get; set; } = 50;
    // waitClipboard（等多少秒，1-60）
    public int WaitSeconds { get; set; } = 50;
    // waitKey（等多少秒；0 = 不限时）
    public int WaitKeySeconds { get; set; } = 50;
    // regex（第几组）
    public int RegexGroup { get; set; } = 50;

    // ── 旧的两格：只给迁移读，迁完就空 ──
    //
    // **可空是这里的关键**：从前 Level 是个 int、默认 50，于是「配置里没写这一项」与
    // 「写了 0」读进来之后**分不开**——而这两件事必须分得开：前者是旧默认的 50，
    // 后者是用户明写的 0（音量 0%、等 0 秒）。迁移要按前者走 50、按后者走 0。
    // 改成可空之后，「没写」= null、「写了 0」= 0，一眼分得开。
    //
    // 迁完（ConfigStore 的一次性迁移，PanelSchema 4 → 5）它们被清成 null，
    // 于是**不会落盘**（序列化只写与新建实例不同的值，见 ConfigStore 那个跳过器），
    // 盘上从此只有新形状这一种。
    public string? Action { get; set; }
    public int? Level { get; set; }
    public bool OnlyBefore8 { get; set; }
    public int BeforeHour { get; set; } = 8;
    public int BeforeMinute { get; set; }   // 「仅 N 前」的分钟位：阈值=BeforeHour:BeforeMinute，支持任意时刻（不再只整点）
    // 「仅 N 点后」：与「仅 N 点前」对称，两者是 AND（同时开 = 交集，如 09:00 后且 18:00 前 = 上班时段）。
    // 有意不做「或」：18:00 后或 08:00 前这种跨午夜窗口写成两条步骤更好懂，不值得让一个复选框带两种语义。
    public bool OnlyAfter { get; set; }
    public int AfterHour { get; set; } = 18;
    public int AfterMinute { get; set; }
    // 仅在这些星期(ISO 1..7)开机启动；空=每天
    public List<int> Days { get; set; } = new();
    // 环境条件（都是「空=不限」）。星期/时刻只看钟表，这三条看的是机器此刻的状态：
    //   IfProcess + IfProcessMode —— 该进程在跑 / 没跑（进程名，裸名即可）
    //   IfPower                   —— "ac"=仅接电源、"battery"=仅用电池
    //   IfPathExists              —— 该文件 / 文件夹存在时才执行（U 盘挂上了没、报告导出了没）
    public string IfProcess { get; set; } = "";
    public string IfProcessMode { get; set; } = "";   // ""=不限 | running | notRunning
    public string IfPower { get; set; } = "";         // ""=不限 | ac | battery
    public string IfPathExists { get; set; } = "";

    // 变量条件：{名字} 与某个值比一比。不成立即跳过这一步——与上面四条环境条件同一个口径、同一个后果。
    //
    // **为什么单独三个字段而不是复用 IfProcessMode 那样的一个**：上面每条环境条件都有各自的取值框，
    // 而这一条的比较对象是一个用户自己起的变量名 + 一个随便什么内容的值，形状与它们都不同。
    // 硬塞进 IfProcess 那组字段会让那四个框里出现第五种含义，而它们分属不同页面、共用一套标签文案。
    //
    // IfVarOp 取值见 StepMath.Compare：eq/ne/contains/notcontains/gt/lt/ge/le/empty/notempty，
    // 外加三个只看列表的：listcount（列表非空）/ listcountgt / listcountlt。
    public string IfVar { get; set; } = "";
    public string IfVarOp { get; set; } = "";
    public string IfVarValue { get; set; } = "";

    /// <summary>
    /// 这一步属于哪个块。空 = 顶层。
    ///
    /// **用平铺 + 指向块，而不是把子步骤嵌进块自己的字段里。** 三条理由：
    ///   · 老配置没有这个键 → 全部为空 → 全部顶层，**天然向后兼容**，不需要迁移；
    ///   · 面板格子、拖拽排序、摘要渲染、搜索语料这一整套都已经是按「一串步骤」写的，
    ///     改成嵌套树要把每一处都重写一遍；
    ///   · 一条「把某步拖进块里」的编辑操作，在这个模型里就是改一个字符串字段——
    ///     而嵌套模型下那是整个子树的重写，中途失败就留下半棵子树。
    ///
    /// 代价：顺序由平铺位置决定（同一个块的孩子必须**挨在一起**），这条由 ConfigStore 规范化时收拾。
    /// </summary>
    public string ParentId { get; set; } = "";
    // 图标。空 = 自动（「运行程序」取目标自己的图标，其余按步骤类型用线描字形）。
    // 也可以填一张图片的路径、一个 exe（借它的图标）、四位十六进制的 MDL2 码位（如 E7C4），
    // 或一枚内置品牌轮廓（brand:wechat，见 Core.BrandIcons）。解析口径见 Core.PanelIcon。
    //
    // **谁读它**：面板格子、面板管理器、启动清单那一列、以及两处编辑器里的图标按钮。
    // 运行这条动作时不读它；手势表与定时任务表也**没有图标列**——在那儿设一个不会报错，
    // 但什么都不会变，别把它当成「设了就该在别处露出来」的字段。
    public string Icon { get; set; } = "";
    // window
    public string Process { get; set; } = "";
    public string SendKey { get; set; } = "{ENTER}";
    public int WaitForWindowSeconds { get; set; }
    public int PostWindowDelaySeconds { get; set; }
    // system
    public string Command { get; set; } = "";
    // message 步骤（动作组用）
    public string Message { get; set; } = "";
    public bool Speak { get; set; }
    public bool Confirm { get; set; }
    // message 步骤的呈现方式：""=沿用旧推导（Confirm/OnYes 决定弹是否框还是确定框），"card"=右下角卡片、不拦路。
    // 空是刻意的默认：盘上老步骤没有这个字段，读回即空 → 行为与加本字段之前逐字一致。
    public string Present { get; set; } = "";
    // 卡片形态的自动关闭秒数（0=常驻到点击），与提醒的 PopupTimeoutSeconds 同语义。
    // 默认 5 而非 0：动作组里的进度提示常驻会堆满右下角，而提醒那边 0=常驻是因为它是「必须被看到」的投递。
    public int PopupSeconds { get; set; } = 5;
    public OnYes OnYes { get; set; } = new();
    // text 步骤：往焦点窗口输入的字面文本
    public string Text { get; set; } = "";
    // 所有步骤通用：用途说明（仅列表显示用）
    public string Note { get; set; } = "";

    /// <summary>这一步产出的值写进哪个变量（留空 = 不产出）。后面的步骤用 <c>{名字}</c> 引用它。</summary>
    //
    // 只有会产出值的类型读它：用户输入 / 用户选择 / 获取选中的文字。
    // 骨架照着 Quicker——「能产出值的步骤有一个输出变量，后面的步骤在文本参数里引用它」，
    // 但没有它的类型系统：这里的变量一律是字符串（见 Core.RunVars 的说明）。
    //
    // 放在 LaunchStep 上而不是只放在那几个类型上：模型里所有类型共用一个类（Kind 分派），
    // 为三个类型另立子类只会把序列化和编辑器都变复杂，而多一个空字符串字段不值那个钱。
    public string OutputVar { get; set; } = "";
    // 所有步骤通用：连续执行次数（循环动作）；每次之间等 delayMs
    public int Repeat { get; set; } = 1;

    // 正则提取 / 替换两步用的正则。空 = 这一步没配正则，运行时如实报错而不是拿空正则去跑
    // （空正则会匹配每一处，产出「在每个字符之间插一条替换文本」这种东西，比报错更难收拾）。
    public string Pattern { get; set; } = "";
    // 替换文本。.NET 语法：$1…$9 是分组引用。空字符串是**合法值**（删掉匹配到的部分），
    // 与「没填」不同档，所以单独存而不是复用别的字段。
    public string Replace { get; set; } = "";
    // 「合」（列表 → 文本）用的分隔符。空 = 直接连在一起，不插任何东西。
    //
    // 单独开一个字段而不是复用 Replace / Pattern：那两个字段的名字已经各自绑死了一种
    // 含义（正则、替换文本），塞第三个含义进去，读代码的人得先翻定义才知道这里存的是什么。
    // 缺失即空串，老配置不受影响。
    public string Separator { get; set; } = "";
}

public sealed class Reminder
{
    // 稳定身份：计时器运行时状态按它做键，改文案/同名同时刻不串状态
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Enabled { get; set; } = true;
    public string Trigger { get; set; } = "time";
    public string Time { get; set; } = "09:00";
    public List<int> Days { get; set; } = new();
    public string Message { get; set; } = "";
    public bool Speak { get; set; }
    // 到点时先响一声系统提示音（卡片与弹窗两种形态都响；静默组不响——「静默」的字面意思）。
    // 默认关：升级不该让所有存量提醒突然开始出声。内置样例开着，新用户一上手就是有声的。
    // 卡片不抢焦点、不置顶，人在看别的屏幕就是错过——朗读能补，但那是为了"听清内容"，
    // 而这里要的只是"抬头看一眼"。用 SystemSounds 而非自带音频：不增体积，且跟随用户的系统声音方案。
    public bool Sound { get; set; }
    // 托盘「快速提醒」建出来的一次性条目：响完即从配置里删掉，而不是像普通「仅一次」那样取消勾选留行。
    // 不删的话，用几周就在定时任务列表里攒一堆"25 分钟到了"的死行，得手动清。
    public bool Temporary { get; set; }
    public OnYes OnYes { get; set; } = new();
    public int GraceMinutes { get; set; } = 5;
    // 错过必补：到点没弹(PC 休眠/关机/程序没跑)时，下次程序在跑且当天还没弹过就补弹一次，不受 grace 窗口上限约束。
    public bool CatchUpIfMissed { get; set; }
    public int DelaySeconds { get; set; }
    public int RandomDelaySeconds { get; set; }
    public int RepeatMinutes { get; set; }
    public string RepeatUntil { get; set; } = "";
    public string RecurType { get; set; } = "daily";
    public int IntervalDays { get; set; } = 1;
    public int MonthlyDay { get; set; } = 1;
    public string AnchorDate { get; set; } = "";
    public int PopupTimeoutSeconds { get; set; }
    public string StartupHourMode { get; set; } = "any";
    public int StartupHour { get; set; } = 9;
    // 「登录时」只认真正的开机时段：开机超过 N 分钟后再启动本程序不算登录（0=每次启动都算）
    public int StartupWithinMinutes { get; set; } = 10;
    // 非空=到点静默(不弹窗)运行该动作组
    public string SilentGroupId { get; set; } = "";
    // 循环运行：>0 则本条到点后每隔 N 分钟再跑一轮（确认不终止——与「催促」的区别），直到 intervalUntil（空=当天 23:59）。
    public int IntervalMinutes { get; set; }
    public string IntervalUntil { get; set; } = "";
    // 周期=once 时的目标日期（yyyy-MM-dd，空=今天）。触发完成后由 App 自动取消勾选（条目保留）。
    public string OnceDate { get; set; } = "";
    // 事件触发（Trigger ∈ ReminderEvent.All）专用参数：
    //   idle       —— 连续无键鼠操作满 IdleMinutes 分钟触发一次，人回来即复位（一次离开只触发一次）
    //   busy       —— 连续用满 BusyMinutes 分钟触发一次（久坐提醒），离开满一分钟即复位重新计时
    //   lowBattery —— 电量跌到 BatteryPercent 以下触发一次，充回阈值以上才复位
    // 其余事件（解锁/锁屏/唤醒/插拔电源/显示器变化/网络通断/U 盘插入）没有参数，三个字段留默认即可。
    public int IdleMinutes { get; set; } = 10;
    // 默认 30 分钟：久坐提醒的通行刻度（番茄钟 25、职业健康建议 30-45），比 IdleMinutes 的 10 分钟大得多——
    // 两者虽然读同一个空闲计数，问的却是相反的问题，默认值不该互相看齐。
    public int BusyMinutes { get; set; } = 30;
    public int BatteryPercent { get; set; } = 20;
}

/// <summary>一份被用户改过的内置模板。key 是 <see cref="ActionGroupTemplates.Templates"/> 里的那个。</summary>
// 只有「改过」才落盘：没改过的模板由代码提供，升级后自动跟着变（内置模板随版本加步骤时，
// 用户没动过的那份理应一起变）。改过的以这份为准，删掉这条即恢复默认。
public sealed class TemplateOverride
{
    public string Key { get; set; } = "";

    /// <summary>改后的内容。</summary>
    // 存的是整份动作组而不是「只存改动」：模板之间不共享步骤，差量算出来的代价
    // （版本一升级全对不上）远大于多存几行字。
    public ActionGroup Group { get; set; } = new();
}

public sealed class ActionGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Hotkey { get; set; } = "";   // 全局热键（如 "Ctrl+Alt+F"），空=不绑定；随时一键运行本组
    // 【历史字段】手势曾经绑在动作组上。后来发现手势不限于动作组——「粘贴」「最小化」这类动作
    // 只作为手势存在，根本没有对应的组——于是手势改绑**步骤**（RootConfig.Gestures），
    // 「跑一整个组」由 group 类型的步骤表达。运行期不再读它，只有 ConfigStore 的一次性迁移看一眼。
    // 保留属性是为了让老 json 里的 gesture 能被迁移读到；迁移会把它搬走并清空。别再给它加新用途。
    public string Gesture { get; set; } = "";
    public int Repeat { get; set; } = 1;       // 整组重复轮数（每次被触发时内部跑几轮）；与 group 引用步骤的 Repeat 相乘
    public int RepeatDelayMs { get; set; }     // 每轮之间间隔
    // 是否在托盘菜单里列一行。新建的组默认 false——组一多托盘就被撑成长条，而多数组是靠热键 / 提醒 /
    // 被别的组引用来触发的，不需要占一行；隐藏的组仍可从主窗口「运行」按钮跑，不会变成死组。
    // 可空是为了区分「老配置没有这个字段」与「显式关掉」：null 由 ConfigStore.Normalize 补成 true，
    // 否则升级后老用户托盘里的组会一起消失，看起来像功能坏了。
    public bool? ShowInTray { get; set; }
    // 本组是不是**面板的一页**。面板上只有这一种东西：一页 = 一个动作组，格子 = 组里的步骤。
    //
    // 想在页上放一个「跑整组」的按钮？加一个 group 类型的步骤指过去就是了——那本来就是那个
    // 步骤类型的用途。早先为此另立过一套（PanelExpand「整组一格」+ 一张自动拼出来的「动作组」页），
    // 结果是同一件事有两种表达，而那张自动页谁也改不了、排不了序。现在只剩这一个开关。
    //
    // 新建的组默认 false：一个刚建的组多半是给热键或定时任务用的，不该自己跑到面板上占一页。
    // 可空三态是历史包袱（null=老配置没这个字段），由 ConfigStore 的一次性迁移抹平。
    public bool? ShowInPanel { get; set; }
    // 【历史字段】早先用来区分「整组一格」与「展开成每步一格」。现在没有这个区分了，
    // 运行期不再读它——只有 ConfigStore 那次迁移看一眼，用来认出老配置里哪些组当初是"一格"。
    // 保留属性只为让老 json 原样往返（迁移要靠它判断），别再给它加新用途。
    public bool PanelExpand { get; set; }
    // 场景页：只在呼出面板时前台是这个程序，本组才出现在面板上（进程名，裸名即可，大小写不敏感）。
    // 空 = 全局，任何时候都在。
    //
    // 这是面板最值钱的一条规则：在 VS Code 里弹出来和在资源管理器里弹出来，看到的不该是同一批动作。
    // 与步骤上那个 IfProcess 条件是两回事，别混——
    //   IfProcess         判「那个程序此刻在不在跑」，管的是这一步**要不要执行**；
    //   PanelForProcess   判「你现在正用着哪个程序」，管的是这一组**要不要显示**。
    // 前者可以在后台成立（微信开着但你在写代码），后者只认前台那一个。
    public string PanelForProcess { get; set; } = "";

    /// <summary>这一页归哪一类（自由文本，空 = 未分类）。顶栏一格一类，点了只列这一类里的页。</summary>
    //
    // **两条栏是父子：上边选类，左边列这一类里的页。** 所以这里存的是分组名，
    // 而不是「摆哪边」——页只有一个落点（左栏），不存在「这一页在哪条栏上」这个问题。
    //
    // 分类没有自己的存储：它是从页上涌现出来的，用同一个名字的页就是同一类。
    // 于是「改分类名」是把那些页一起改掉，「解散一类」是把它们的这个字段清空——
    // 都不需要另一张表，也就不会出现「表里有一类，却一个页都不指向它」这种孤儿。
    //
    // 默认空串 = 未分类，它自己也在顶栏上占一格：不给它位置的话，没填分类的页无处可去。
    // 绝大多数人一直看到的就是这一格，而只有一类时那条栏整条不画。
    public string PanelTab { get; set; } = "";
    // 整组一格时用的图标。空 = 线描的「一叠动作」字形。同 LaunchStep.Icon 的写法：
    // 图片路径 / 借某个 exe 的图标 / 四位十六进制的 MDL2 码位。
    public string Icon { get; set; } = "";
    public List<LaunchStep> Steps { get; set; } = new();

    // 运行快照：浅拷贝步骤列表（步骤对象共享，字段级并发读写无害），后台枚举不受 UI 增删干扰。
    public ActionGroup SnapshotForRun() => new() { Id = Id, Name = Name, Enabled = Enabled, Hotkey = Hotkey, Repeat = Repeat, RepeatDelayMs = RepeatDelayMs, ShowInTray = ShowInTray, ShowInPanel = ShowInPanel, PanelExpand = PanelExpand, PanelForProcess = PanelForProcess, PanelTab = PanelTab, Icon = Icon, Steps = new List<LaunchStep>(Steps) };
}

/// <summary>快捷面板的一页。**页不是动作**——这是这一版拆开的那件事。</summary>
//
// 从前一页就是一个动作组：组身上挂着 ShowInPanel / PanelTab / PanelForProcess 一串字段，
// 「页本来就是组」。那个捷径已经付过好几次代价——面板的迁移、空名页签、组摘要里的特判，
// 每一次都是因为一个实体同时要当两样东西：**能被热键触发的一串操作**，和**面板上的一屏格子**。
// 这两件事没有共同的属性：动作要热键、要重复轮数、要能被提醒引用；页要分类、要场景、要排序。
//
// 拆开之后：
//   动作（ActionGroup）  = 名字 + 一串步骤 + 怎么触发（热键 / 托盘 / 被引用）
//   页（PanelPage）      = 名字 + 一屏格子 + 什么时候出现（分类 / 场景）
//
// **页自己持有格子（步骤），而不是「引用一串动作」。** 三个理由：
//   与现状 1:1 对应，迁移无损；
//   若格子必须是独立动作，「复制」「粘贴」这类微动作会灌满动作列表，比原来更糟；
//   想在页上放「跑一整个动作」照旧用 group 类型的步骤指过去——页可以混放内联格子和动作引用，
//   表达力严格大于「页 = 一串动作引用」那种设计。
public sealed class PanelPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    /// <summary>归哪一类（自由文本，空 = 未分类）。顶栏一格一类。</summary>
    // 分类没有自己的存储：它从页上涌现，同名即同类。于是改名 = 把那些页一起改，
    // 解散 = 把这个字段清空——不需要另一张表，也就不会有「表里有一类却没有页指向它」的孤儿。
    public string Tab { get; set; } = "";
    /// <summary>场景页：只在呼出面板时前台是这个程序，本页才出现。空 = 全局。</summary>
    // 面板最值钱的一条规则：在 VS Code 里弹出来和在资源管理器里弹出来，不该是同一批动作。
    // 与步骤上的 IfProcess 是两回事——那个判「程序在不在跑」，这个判「你正用着哪个」。
    public string ForProcess { get; set; } = "";
    /// <summary>页上的格子。每一格是一个步骤；要放「跑一整个动作」就用 group 类型的步骤。</summary>
    public List<LaunchStep> Steps { get; set; } = new();

    public PanelPage SnapshotForRun() => new()
    { Id = Id, Name = Name, Tab = Tab, ForProcess = ForProcess, Steps = new List<LaunchStep>(Steps) };
}

public sealed class AppSettings
{
    public int TickSeconds { get; set; } = 30;    public bool StartMinimized { get; set; }
    public bool StartupWaitForReady { get; set; }
    public int StartupDelaySeconds { get; set; } = 30;
    public string StopHotkey { get; set; } = "Ctrl+Alt+Q";
    // 快捷面板的呼出键。默认给一个，理由与急停键相同：不给默认值的功能等于藏起来了，
    // 而热键被占用时已有现成的处理（注册失败会点名 toast，见 RebindFunctionHotkey），代价可控。
    // 空=不绑定，面板仍可从托盘菜单或中键长按打开——所以「关掉这个键」不等于关掉这个功能。
    //
    // 为什么是 Ctrl+Alt+Space（候选都查过占用方，别再重挑一轮）：
    //   ✗ Alt+Space            —— Windows 的窗口系统菜单；且 PowerToys Run / uTools / Flow Launcher
    //                             全默认抢它，装了任一个就注册失败，而那正是最可能装本程序的一批人。
    //   ✗ Ctrl+Shift+Space     —— Visual Studio 的「参数信息」。全局热键会把它从 VS 手里抢走。
    //   ✗ Ctrl+Alt+A           —— QQ 截图。
    //   ✗ Ctrl+Alt+Z / +W      —— QQ/TIM 与微信的「激活窗口」默认键。
    //   ✗ Ctrl+Space / Shift+Space —— 输入法开关与全半角，中文用户天天在按。
    //   ✗ Ctrl+Alt+Q           —— 本程序自己的急停键。
    //   ✓ Ctrl+Alt+Space       —— 无已知占用方，且 Ctrl / Alt / 空格全在左手：右手可以一直不离开
    //                             鼠标，而面板恰恰弹在鼠标处。唯一瑕疵是欧洲键盘上 AltGr+Space
    //                             是不间断空格，属于非破坏性冲突。
    // 快捷面板的总开关。**默认开**——它一直就是开着的（热键有默认值），把既有用户升级成「关」
    // 会让一个天天在用的功能凭空消失，同 Sound 那条：升级不该改变既有行为。
    //
    // 为什么需要它：在此之前「不想用面板」只能靠**清空热键 + 不勾中键长按**间接达成，
    // 而那样托盘菜单里还留着入口，用户也没法确认自己到底关没关。
    // 手势那边一直有总开关（GesturesEnabled），面板这边没有，两个鼠标功能不对称。
    //
    // 关掉之后：热键不注册、中键钩子不装、托盘那一项不显示、TogglePanel 直接返回。
    // 面板页那些数据不动——关的是「用不用」，不是「删不删」，所以「管理面板…」照旧进得去。
    public bool PanelEnabled { get; set; } = true;

    public string PanelHotkey { get; set; } = "Ctrl+Alt+Space";

    // 一键直达热键（RunAny 式「选中即开」）：任意程序里选中文字，按一下自动识别
    // 网址 / 文件文件夹路径 / 磁力链接 / 注册表路径并直达。空串 = 不绑定。
    //
    // 默认 Ctrl+Oem3（Oem3 是 Esc 下方、数字 1 左边那个反引号 ` 键）。选它的账：
    //   · RunAny 自己默认就是裸 `——单键最快，但裸键必须**全局劫持**这个键，
    //     用户在任何程序里都打不出反引号，写代码/Markdown 时是灾难，故这里坚持要一个修饰键；
    //   · Ctrl+` 是 VS Code/终端「切换终端面板」的常用键，但那是**应用内**快捷键，
    //     全局注册只在那些窗口前台时盖过它，代价可接受（JetBrains 系的 Quick Documentation 同理）；
    //   · 不和本程序自己的急停 Ctrl+Alt+Q、面板 Ctrl+Alt+Space 撞车。
    public string QuickOpenHotkey { get; set; } = "Ctrl+Oem3";

    // 长按鼠标中键唤出快捷面板。**默认关**——它要装一个全局低级鼠标钩子，
    // 而那个钩子必须先吞掉每一次中键按下（按下的当口还不知道是长按还是普通点击），
    // 判定为短按后再补发一次真实中键。这套机制正常时无感，出错时的样子是「全系统中键失灵」。
    // 让所有人升级后默认承担这个风险是不对的（同 Sound 字段那条：升级不该改变既有行为）；
    // 想要的人在设置页勾一下即可，那时他知道自己开了什么。
    public bool PanelMiddleLongPress { get; set; }

    // 按住多久算长按（毫秒）。350 是手感与误触之间的常见折中：短于 ~250 会把稍慢的普通中键
    // 点击误判成长按，长于 ~500 按着会开始觉得"是不是没反应"。
    // 做成可配是因为这是个物理量——鼠标微动开关的手感、用户的按键习惯、远程桌面的输入延迟，
    // 都会让同一个数字在不同机器上感觉不同，留个旋钮比猜一个"正确值"实在。
    public int PanelLongPressMs { get; set; } = 350;

    // —— 面板外观（都在设置页可改；范围与合法值由 Core.PanelMetrics 统一把关）——
    // 每列格子数。4 列是照着「一屏放得下常用的一批、又不横跨半个屏幕」定的默认值。
    // 面板两条导航栏的开关。默认开，但**空的时候本来就不画**（只有一页就没有页签栏，
    // 只有一类就没有分类栏），所以这两个开关的实际用途是「我知道有好几页，但我就是不想看见那条栏」。
    public bool PanelLeftTabs { get; set; } = true;
    public bool PanelTopTabs { get; set; } = true;

    public int PanelColumns { get; set; } = 4;
    // 凹槽分上下两条带，各自的行数。两条带没有语义差别——它们是**版面节奏**：
    // 一整块 7 行的方格阵读起来是一堵墙，切成 3 + 4 之后眼睛有了落点，找格子快得多
    //（同键盘分区、仪表分区的道理）。
    // 上下加起来即一页的容量，装不下的翻到下一页。下带设 0 就退回单条带的老样子。
    public int PanelTopRows { get; set; } = 3;
    public int PanelBottomRows { get; set; } = 4;
    // 格子尺寸档位：compact | normal | roomy。用字符串而不是枚举，与本模型里 Kind / IfPower /
    // RecurType 同一口径——配置是人可以手改的 json，字符串在那里比数字可读得多。
    public string PanelTileSize { get; set; } = "normal";
    // 是否显示底部那排自带操作（重跑清单 / 停止 / 勿扰 / 打开窗口）。
    // 默认开：它们是「面板存在的第二个理由」，但把它关掉是合理偏好——热键和托盘都够得着。
    public bool PanelShowOps { get; set; } = true;
    // 只显示图标、不显示文字。格子会收成接近正方，一屏能放下的数量大幅增加。
    // 名字仍在 ToolTip 上，所以这不是「藏起信息」，是「把信息挪到需要时才看」。
    public bool PanelIconOnly { get; set; }

    /// <summary>本程序自己占着的全局功能键（键 → 属于谁），给动作编辑器查重用。</summary>
    // 只有一处清单，**加了新的功能键就往这儿添一行**：各调用点各抄一份的年代已经出过事
    // （面板键加进来的那一轮就漏抄过一处，于是面板与急停键可以撞在一起而没人拦）。
    // 放在这儿而不是某个窗口里，是因为查重需要它的不止一扇窗。
    public IReadOnlyList<(string Key, string Owner)> FunctionHotkeys() => new[]
    {
        (StopHotkey, Clockwork.I18n.Strings.Get("Settings_StopHotkey")),
        (PanelHotkey, Clockwork.I18n.Strings.Get("Settings_PanelHotkey")),
        (QuickOpenHotkey, Clockwork.I18n.Strings.Get("Settings_QuickOpenHotkey")),
    };

    /// <summary>面板数据模型的版本。迁移完成后写上，用来保证那次改写只发生一次。</summary>
    //
    // 为什么需要一个显式版本号，而不是靠"看字段长什么样"来判断：迁移会把老的「整组一格」组
    // 收进一张新页、并把它们自己从面板上摘下来。判据若写成「ShowInPanel 为真但不是页」，
    // 那么用户日后新建一个组并勾上「作为面板的一页」，就会再次落进同一个判据里被搬走一次。
    // 一次性的改写必须由一个一次性的标记来关门。
    /// <summary>鼠标手势的总开关。关掉即完全不监听右键。</summary>
    //
    // 与「每条手势自己的启用」是两件事：那个是「这一条灵不灵」，这个是
    // 「Clockwork 到底能不能碰右键」。有它是因为监听期间右键按住拖放用不了
    // （资源管理器里右键拖文件——见 GestureGate 头部那条 ponytail 注释）：
    // 临时要用那个手法时，翻一个开关，比把五条手势逐个取消勾选强。
    //
    // 默认 true：老配置里没有这个键，反序列化落到这个初始值上，行为与从前一致。
    public bool GesturesEnabled { get; set; } = true;
    // 鼠标手势灵敏度："low"（长笔画，防误触）| "normal"（默认 2.5% 屏宽）| "high"（短笔画，省力）。
    public string GestureSensitivity { get; set; } = "normal";
    // 鼠标手势轨迹宽度/显示方式："off"（不显笔迹）| "thin"（细）| "normal"（标准）| "thick"（粗）。
    public string GestureTrailWidth { get; set; } = "normal";

    /// <summary>这些程序里**根本不接管手势**：右键完全交给程序自己。</summary>
    //
    // 与「分应用绑定」（步骤上的 ForProcess）是一对，缺一不可：
    //   · ForProcess 说的是「在这个程序里，↑ 是另一件事」（同一个形状换个意思）；
    //   · 这一格说的是「在这个程序里，根本不要画」（整件事不成立）。
    // 后者从前没有，于是只有一条出路：把总开关关掉——而那是全局的，把其它程序里的手势也一起关了。
    //
    // **为什么这一格是常事而不是边角**：远程桌面、虚拟机、游戏、3D 视口、画图软件，
    // 这些地方程序自己就管右键（多义右键、右键旋转、右键拖拽），我们插一脚只会碍事。
    // 别的工具（WGestures / StrokesPlus / Quicker）都有这一份「按程序排除」的名单，
    // 而本项目的用户指南从前把这条写成了「人家的长处、我们的取舍」——那是一个可以补上的取舍。
    //
    // 存的是**裸进程名**（与 ForProcess 同一套写法，见 StepHelpers.ToProcessName）：
    // 「chrome」与「chrome.exe」等价，带路径也等价。判据在 GestureGate.IsIgnoredApp（纯函数、有测试）。
    public List<string> GestureIgnoreApps { get; set; } = new();

    /// <summary>画到一半认出某条手势时，笔迹旁边报一句「这将要做的事」。</summary>
    //
    // 为什么需要这一格：笔迹「亮了」只说明**认出形状了**，不说明**认出的是哪一个动作**。
    // 而手势是**分应用**的——同一个 ↑ 在浏览器里是「搜索」、在编辑器里是「复制」，
    // 于是「亮了」这件事本身是有歧义的：用户知道认出来了，却不知道松手会发生什么。
    // 一个不确定的反馈比没有反馈更糟：它让人以为已经知道会发生什么，于是不再确认。
    //
    // 默认开：分应用绑定一开，「亮了但不知道是哪件事」就从罕见变成常态。
    // 这一格是**可关的**：有人要那种极简的「只有一条线」的手感，翻掉它即可。
    public bool GestureShowActionLabel { get; set; } = true;

    public int PanelSchema { get; set; }
    public string Language { get; set; } = "";   // 空=跟随系统显示语言（App 启动时解析成具体 code 并落盘）

    /// <summary>界面主题："dark" / "light" / "system"（跟随 Windows 的应用主题）。
    /// 默认深色而不是跟随系统：这个程序原本只有深色一套，已经装着它的人不该某天开机发现界面变白了。
    /// 想跟随系统是一次明确的选择，不是默认。</summary>
    public string Theme { get; set; } = "dark";
    // 端口页「只看项目服务」的勾选状态。存进配置而不是像系统启动项页那个复选框一样只活在内存：
    // 那边是「偶尔看一眼只读项」，这边是「我就是个写代码的」——后者每次重启都要重新勾一遍就是磨人。
    // 默认开：这个页存在的理由就是找你自己起的服务；页面是跟功能一起新增的，不存在「升级后行为突变」的存量用户。
    public bool PortsDevOnly { get; set; } = true;
}

public sealed class RootConfig
{
    public List<LaunchStep> LaunchSteps { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public List<ActionGroup> ActionGroups { get; set; } = new();
    /// <summary>快捷面板的页。与动作分开存——理由见 PanelPage 的注释。</summary>
    // 由 PanelSchema 3 的一次性迁移从 ActionGroups 里搬过来（ConfigStore.MigratePanelPages）。
    public List<PanelPage> PanelPages { get; set; } = new();
    // 鼠标手势：一条手势 = 一个步骤，轨迹存在步骤的 Gesture 字段上。
    // 单独一份列表而不是散在动作组里，因为手势是全局的、少而互斥，而且多数动作根本不配手势。
    public List<LaunchStep> Gestures { get; set; } = new();

    /// <summary>站点映射表：域名 → 本地源码根 + 路径改写规则。供 siteMap 步骤使用。</summary>
    // 与 RunVars 分开存，理由见 SiteMap.cs 的注释：那张表要长期存在、要手改，
    // 而 RunVars 是一次运行内的变量，跑完即焚。
    public List<SiteEntry> Sites { get; set; } = new();

    /// <summary>用户改过的内置模板，按 <see cref="ActionGroupTemplates.Templates"/> 的 key 认领。</summary>
    // 内置模板本身在代码里（ActionGroupTemplates），这里只存**用户改过的那一份**：
    // 没改过的模板不占配置，升级带来新步骤时它会跟着变；改过的就以用户的为准。
    // 按 key 而不是按名字认领——名字是本地化的，同一个模板在 19 种语言下是 19 个字符串。
    public List<TemplateOverride> Templates { get; set; } = new();

    // 页上的格子要指回动作的 Id，所以动作得先建出来、再传给 DefaultPanelPages——
    // 两边各调一次 DefaultActionGroups() 的话，Id 是新 Guid，面板上两格全指向不存在的动作。
    public static RootConfig Default()
    {
        var actions = DefaultActionGroups();
        return new()
        {
            LaunchSteps = DefaultLaunchSteps(),
            Reminders = DefaultReminders(),
            // 首份配置直接写成当前模型，不留给迁移去改：写成老样子的话，每次读默认配置都会触发一次
            // 迁移改写，Read_clean_file_reports_no_normalization 会红——它红得对，说明首份配置不是规范形。
            // 版本号 5 = 「共用的 Action/Level 已经拆到各类型自己的格子里」（见 ConfigStore 那一趟迁移）。
            Settings = new AppSettings { PanelSchema = 5 },
            ActionGroups = actions,
            PanelPages = DefaultPanelPages(actions),
            Gestures = DefaultGestures(),
        };
    }

    // 示例手势。这一组照的是同类工具里已经通行的那套默认动作，一到两笔画得完：
    //
    //   ↑ 复制      ↓ 粘贴          ← 后退      → 前进
    //   ↙ 最小化    ↗ 最大化        ↖ 置顶      ↘ 关闭
    //   ↑↓ 搜索选中的文字           →↓ 到最底部
    //
    // **轴向四条给最常用的四件事**：复制粘贴是任何地方都用得上的，前进后退是浏览器里的；
    // 四条轴向也最好画（扇区各 56°，斜角只有 34°，见 GestureGate 的偏置）。
    //
    // **四个斜角凑齐窗口的四件事**，方向本身就是意思：↙ 往左下收起、↗ 往右上撑开、
    // ↖ 往左上钉住（置顶是开关，同一条手势管钉住和放开）、↘ 往右下扫走（关闭）。
    // 关闭放在斜角上是有意的：斜角扇区只有 34°，画不准就不会中——一个会关掉窗口的动作，
    // 「不容易误画」正是它该有的性质，而轴向那四条 56° 的宽容度该留给无害的操作。
    //
    // 剩下两条是两笔的：↑↓ 搜选中的词，→↓ 拐一下到最底部。
    //
    // **↑↓ 而不是 ∧（↗↘）。** 后者是原来的默认，实测它只在「两条腿都画在 40–60° 且画得干净」
    // 时才读作 ↗↘：人画尖角自然会画到 65–80°，那就落进 ↑ 和 ↓ 的扇区，稳定读成 ↑↓；
    // 稍微画弯一点，顶点还会再多切出一两个方向（实测 ↗↑↓↘）。斜角扇区只有 34°，
    // 两条腿的误差叠加还要过一个顶点，可用窗口窄得没剩多少。
    // 而 ↑↓ 在 80–90° 任意弓形下都稳——**把手本来就在画的那个形状认下来**，比逼手去画 45° 强。
    //
    // 这也是同类工具的通行做法：默认手势集清一色是轴向组合（WGestures 默认干脆只开四向，
    // 八向是选项）。斜角留给「我知道自己在干嘛」的人自己配。
    // 前缀撞车是这类两笔手势的固有性质：↑↓ 的第二笔太短会读成 ↑（复制）。
    // 选 ↑↓ 而不是 ↓↑ 正是为此——前缀落空时跑「复制」无害，跑「粘贴」会改掉你的内容。
    //
    // **默认启用**——与示例清单、示例提醒相反，那两处是不启用的。
    // 理由是这三样东西的性质不同：清单会去开程序、提醒会弹窗打断，都是「替你动了电脑」；
    // 而手势只在你主动画出一条轨迹时才发生，画之前它什么也不做。一个装完得先去
    // 逐条勾选才有反应的手势功能，多数人不会翻到那一屏，于是这个功能等于不存在。
    //
    // 代价必须说清楚：**只要有一条启用的手势，右键就被接管**——按下先被扣住，松手时再决定
    // 是补发普通右键还是执行手势；右键拖拽在监听期间不可用。
    // 出口是总开关（Settings.GesturesEnabled，手势管理器标题行右边那个勾）：
    // 要右键完全恢复原样时翻一下，比逐条取消勾选再逐条勾回来强。文档里这两条并排写着。
    //
    // 方向存成单字符（见 GestureGate）：L← U↑ R→ D↓，斜角借小键盘的 7↖ 9↗ 1↙ 3↘。
    // 动作直接取「常用操作」那张表（StepDisplay.CommonPresets 的同一批预设），
    // 于是手势样例与面板上那些格子说的是同一件事、用的是同一份文案。
    public static List<LaunchStep> DefaultGestures()
    {
        (string Path, string LabelKey, string Kind, string Arg)[] spec =
        {
            ("U",  "Cmd_copy",            "keys",   "Ctrl+C"),
            ("D",  "Cmd_paste",           "keys",   "Ctrl+V"),
            ("L",  "Cmd_back",            "keys",   "Alt+Left"),
            ("R",  "Cmd_forward",         "keys",   "Alt+Right"),
            ("1",  "Win_minimizeFg",      "window", "minimize"),
            ("9",  "Win_maximizeFg",      "window", "maximize"),
            ("7",  "Win_topmostFg",       "window", "topmost"),
            ("3",  "Win_closeFg",         "window", "close"),
            ("UD", "Sys_searchSelection", "system", "searchSelection"),
            ("RD", "Cmd_toBottom",        "keys",   "Ctrl+End"),
        };
        var list = new List<LaunchStep>();
        foreach (var (path, key, kind, arg) in spec)
        {
            var s = StepDisplay.MakePreset(kind, arg);
            s.Label = Strings.Get(key);
            s.Gesture = path;
            s.Enabled = true;
            list.Add(s);
        }
        return list;
    }

    // 供后台运行拍快照：浅拷贝各列表，枚举不再受 UI 线程增删的并发修改干扰（开机延迟期间增删步骤会
    // 让后台 foreach 抛 Collection was modified）。步骤/提醒对象本身共享——字段级并发读写无害。
    public RootConfig SnapshotForRun() => new()
    {
        LaunchSteps = new List<LaunchStep>(LaunchSteps),
        Reminders = new List<Reminder>(Reminders),
        Settings = Settings,
        ActionGroups = ActionGroups.Select(g => g.SnapshotForRun()).ToList(),
        Gestures = new List<LaunchStep>(Gestures),
        // 面板页也要拍。漏掉它的痕迹很明显：PanelPage.SnapshotForRun() 就是为这一行写的，
        // 而它一个调用方都没有。后果是快照上的 PanelPages 是 null（而不是空表），
        // 目前还没人读它，所以这是一个潜伏的 NRE——下一个在后台路径上想查面板格子的人会撞上。
        PanelPages = PanelPages.Select(pg => pg.SnapshotForRun()).ToList(),
    };

    // 首次使用的示例清单：按「一个真实的早晨」排序，而不是按功能覆盖率排列——
    // 先静音、开浏览器、开常用网站、把聊天软件最小化挂后台，最后 Win+D 清屏收尾。
    // 全部默认不勾选：样例是照着改的模板，不该在用户还没看过一眼时就替他动电脑。
    // 文案经 resx 本地化，与 ActionGroupTemplates 同口径；进程名/路径是全球通用的字面量，不本地化。
    public static List<LaunchStep> DefaultLaunchSteps() => new()
    {
        new LaunchStep { Kind = "volume", Label = Strings.Get("Smp_Mute"), VolumeAction = "mute", Enabled = false },
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenApp"), Target = "msedge.exe", Enabled = false },
        // 条件执行的演示放这条：「工作日才打开工作网站」是自证的，比原来的「仅 8 点前静音」好懂。
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenSite"), Target = "https://github.com", DelayMs = 800, Days = new() { 1, 2, 3, 4, 5 }, Enabled = false },
        // windowStyle 一步挂后台，比「开完再用窗口动作最小化」更贴近真实做法。
        new LaunchStep { Kind = "app", Label = Strings.Get("Smp_OpenChat"), Target = "Slack.exe", WindowStyle = "minimized", Enabled = false },
        // 放最后才成立：前面 4 步（1 步静音 + 3 步打开），这一下是清屏收尾。
        new LaunchStep { Kind = "keys", Label = Strings.Get("Smp_ShowDesktop"), Combo = "Win+D", Enabled = false },
    };

    // 通用示例提醒：工作日两条（补水 / 收工，后者带语音）、每天一条（睡前）、每月一条（账单）。
    // 每月那条是唯一演示「按月」周期的样例——原来三条全是每天/工作日。同样默认不启用。
    public static List<Reminder> DefaultReminders() => new()
    {
        // 样例一律开提示音：卡片不抢焦点、不置顶，静默弹出等于没弹——新用户第一次见到提醒就该听见它。
        // 模型默认仍是关（Reminder.Sound=false），升级的存量提醒不会突然集体出声。
        new Reminder { Time = "10:00", Days = new() { 1, 2, 3, 4, 5 }, Message = Strings.Get("Smp_RemWater"), Sound = true, Enabled = false },
        new Reminder { Time = "17:30", Days = new() { 1, 2, 3, 4, 5 }, Message = Strings.Get("Smp_RemWrapUp"), Speak = true, Sound = true, Enabled = false },
        new Reminder { Time = "23:00", Message = Strings.Get("Smp_RemSleep"), Sound = true, Enabled = false },
        new Reminder { Time = "09:00", RecurType = "monthly", MonthlyDay = 1, Message = Strings.Get("Smp_RemBills"), Sound = true, Enabled = false },
    };

    // 首次预置的动作组：直接从模板里挑，避免同一套步骤在两处各写一份、日后漂移。
    // 挑「离开一下」（零配置、任何机器都能跑）与「下班」（最有代表性，含确认闸门）。
    // 启用但不带热键——组不会自己跑，禁用只会让托盘里多两条灰的；而开箱占用全局组合键太越界。
    public static List<ActionGroup> DefaultActionGroups()
    {
        // 按 key 挑，不按 Name：Name 是 resx 里那个 Tpl_ 键的译文，按名字挑就得先把译文读出来，
        // 而一次改名或一次漏译都会让首启预置的动作**凭空消失**，用户还以为是自己删的。
        // key 是模板身份本身，与语言无关。
        var keys = new[] { "away", "endOfDay" };
        var picked = keys.Select(k => ActionGroupTemplates.BuiltIn(k))
                          .Where(g => g != null).Select(g => g!)
                          .ToList();
        // 显式进托盘：首启时没有任何提醒/热键指向它们，托盘和面板是仅有的顺手入口。
        // 显式写值（而非留 null 靠 Normalize 补）→ 首份配置就是规范形，读回不会再触发一次写盘
        //（Read_clean_file_reports_no_normalization 盯着这条不变量）。新加可空的迁移字段时，
        // 记得一并在这儿写上默认值，否则那条测试会红——它红得对，说明首份配置不是规范形。
        foreach (var g in picked) { g.ShowInTray = true; g.ShowInPanel = false; }
        return picked;
    }

    /// <summary>首份配置里的面板：一页「常用」，装的是**装完就能按**的那些东西。</summary>
    //
    // **不能让首启的面板是空的。** 呼出来一片空白时，「怎么往里放东西」在界面上没有一点痕迹，
    // 而这个功能的第一印象就是那一屏。
    //
    // 选格子只有两条标准：**装完就能按**（不需要先去配路径或进程名），
    // 以及**大部分人真的会按**。所以清一色是 Windows 自带的系统动作——换台机器照样能用。
    // 不放「打开某某软件」：那种格子在别人的机器上就是个死格。
    //
    // **不放通知开关（免打扰 / 恢复通知）**：它改注册表且不会自己恢复，新用户按一下之后
    // 可能好几天都不知道通知去哪了。开箱内容不该替人做这种会留下痕迹、又不提示的事。
    //
    // 「搜索选中的文字」放进来是因为它是这程序最不像别家的一件事，而且**它自己会说明自己**：
    // 选一段字、按一下，结果就在浏览器里——比任何一句说明都短。
    //
    // 顺序按「多久按一次」，不按功能分类：前四个是每天都会用到的，后面依次递减。
    // 末尾两格是示例动作的引用格——「运行动作」格子唯一的现成例子，
    // 也是面板与动作这两件事接起来的地方（改了动作里的步骤，这一格跟着变；抄一份就不会）。
    public static List<PanelPage> DefaultPanelPages(List<ActionGroup> actions)
    {
        // 不设 Label：标题由 StepDisplay 按当前语言现算（系统命令的短标签）。
        // 写死的话，用户日后在设置里换一种语言，这些格子还留着装机时那一种。
        //
        // 图标逐格指定：面板的前提是「图标一眼说明这是哪一类东西」，而系统命令这一整类
        // 共用一个齿轮（PanelIcon 按 Kind 取字形，看不见 Command）——不指的话首启那一页
        // 就是八个一模一样的齿轮，那句前提当场落空。
        var steps = new[]
        {
            ("lockScreen", "E72E"),        // Lock
            ("monitorOff", "E7F4"),        // 显示器
            ("showDesktop", "E770"),       // 桌面
            ("screenshot", "E123"),        // 框选
            ("clearClipboard", "E75C"),    // 清除
            ("taskManager", "E9D9"),       // 进程/性能
            ("emptyRecycleBin", "E74D"),   // 删除
            ("searchSelection", "E721"),   // 搜索
        }.Select(x => { var t = StepDisplay.MakePreset("system", x.Item1); t.Icon = x.Item2; return t; }).ToList();
        steps.AddRange(actions.Select(g => new LaunchStep
        { Kind = "group", GroupId = g.Id, Label = g.Name, Icon = g.Icon }));
        // 页名借「常用」那条现成文案（「新增 ▾」里那一节用的就是它），不为一个页名再造一条 18 语。
        return new List<PanelPage> { new() { Name = Strings.Get("Menu_SecCommon"), Steps = steps } };
    }
}
