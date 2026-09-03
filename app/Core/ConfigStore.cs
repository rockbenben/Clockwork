using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

using Clockwork.I18n;

namespace Clockwork.Core;

// 配置读写（原子写 + 缺失容错）。
// 原子写：先写同目录临时文件、再原子替换——直接写目标是非原子的，写到一半崩溃会截断配置、下次读失败落回默认、全部设置静默丢失。
// 不做 launchItems/specialSteps 旧格式迁移（按 spec：项目未发布、不考虑旧版兼容）。
public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,       // C# LaunchSteps ↔ json launchSteps
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文/+ 不转义成 \uXXXX，保持可读 JSON
    };

    // 深拷贝：序列化再反序列化。复制动作组/提醒共用一处，与读写走同一套 JsonOptions（含未来的 [JsonIgnore] 等约定），
    // 避免各 VM 各写一份、日后改克隆策略时漏改其一。
    public static T DeepClone<T>(T obj) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, JsonOptions), JsonOptions)!;

    public static void Write(RootConfig config, string path)
        => WriteTextAtomic(path, JsonSerializer.Serialize(config, JsonOptions), attempts: 5, delayMs: 100, throwOnFail: true);

    // 单次原子写（不睡眠、失败返回 false 不抛）：给不能在调用线程阻塞重试的场景（如 UI 线程上的状态存盘快路径）。
    public static bool TryWriteTextAtomic(string path, string text, int attempts = 1, int delayMs = 0)
        => WriteTextAtomic(path, text, attempts, delayMs, throwOnFail: false);

    // 原子写文本的唯一实现：Write（配置）与 ReminderStateStore（运行态）共用，写策略只此一份。
    // 整个「写临时 + 替换」都在重试循环内：临时与目标都可能被瞬时占用（OneDrive/索引/杀软持句柄，文件常在 Documents 下）；
    // 且 File.Replace 出错时已消耗 tmp，只有每轮重写临时文件，下次重试才有源可用、不退化成 FileNotFound 误报。
    private static bool WriteTextAtomic(string path, string text, int attempts, int delayMs, bool throwOnFail)
    {
        var tmp = path + ".tmp";
        var enc = new UTF8Encoding(false); // 无 BOM
        for (int i = 0; ; i++)
        {
            try
            {
                File.WriteAllText(tmp, text, enc);
                if (File.Exists(path)) File.Replace(tmp, path, null); // 第三参 null=不留备份
                else File.Move(tmp, path);
                return true;
            }
            catch
            {
                // 重试耗尽（持久占用）→ 清掉本轮临时文件（尽力）；目标文件保持原样、绝不损坏。
                if (i >= attempts - 1) { try { File.Delete(tmp); } catch { } if (throwOnFail) throw; return false; }
                if (delayMs > 0) Thread.Sleep(delayMs);
            }
        }
    }

    public static RootConfig Read(string path) => Read(path, out _);

    public static RootConfig Read(string path, out bool normalized) => Read(path, out normalized, out _);

    // normalized：本次读入是否做了「重启后有影响」的规范化（剔 null 元素 / 补生或重发 id）。
    // 为 true 时调用方应把规范化结果写回文件——尤其重发的提醒 id 若不落盘，每次启动都换新 id、
    // 运行态（今天已弹/稍后）永远接不上，被去重那条提醒会每次重启都重弹。
    //
    // unreadable：文件存在但读不出来（JSON 语法错 / 上次断电留下的半截文件 / 编码坏）。
    // 调用方拿到 true 时**绝不能把返回的配置写回原路径**——这里返回的默认配置与用户的内容毫无关系，
    // 写回去就是把一份还能手工修好的配置抹掉。「文件不存在」不算 unreadable：那是首次启动，写默认是对的。
    public static RootConfig Read(string path, out bool normalized, out bool unreadable)
    {
        normalized = false;
        unreadable = false;
        if (!File.Exists(path)) return RootConfig.Default();
        RootConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<RootConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            unreadable = true;
            return RootConfig.Default(); // 解析失败落回默认（不损坏、不崩溃）
        }
        if (cfg is null) { unreadable = true; return RootConfig.Default(); }   // json 字面量 "null"
        normalized = Normalize(cfg);
        return cfg;
    }

    // 反序列化后的规范化管线：启动读取与配置导入共用——「什么算合法配置」只定义这一份，
    // 导入落盘的就是规范形，不把修补推迟到下次启动。返回是否做了「重启后有影响」的修补。
    public static bool Normalize(RootConfig cfg)
    {
        bool normalized = false;
        // 缺集合容错：任一为 null（json 显式 null）用默认补齐，避免下游 NRE。
        var def = RootConfig.Default();
        cfg.LaunchSteps = OrDefault(cfg.LaunchSteps, def.LaunchSteps);
        cfg.Reminders = OrDefault(cfg.Reminders, def.Reminders);
        cfg.Settings = OrDefault(cfg.Settings, def.Settings);
        cfg.ActionGroups = OrDefault(cfg.ActionGroups, def.ActionGroups);
        // PanelPages 也在这里补，**不能留给下面的 FillPanelPageDefaults**。
        // 那个方法末尾确实有一句 `cfg.PanelPages ??= new()`，但它排在 MigratePanelPages 之后，
        // 而迁移里就有 `cfg.PanelPages.Add(page)`——于是 "panelPages": null + panelSchema<3 +
        // 任意一个 showInPanel:true 的组，启动时在 Add 那一行 NRE。
        // 而 Normalize 是在 Read 的 try/catch **之外**调的（见上面第 87 行附近），
        // 一崩就正好违背这一段自己写的承诺：解析失败落回默认，绝不崩。导入配置走同一条路。
        // 这里是 ??= new() 而不是 OrDefault(..., def.PanelPages)：只把守卫**提前**，不改它做什么。
        // 原来那句（在 FillPanelPageDefaults 末尾）补的就是空表；换成补出厂默认页的话，
        // 任何写着 "panelPages": null 的配置会突然长出一整页出厂格子——那是另一个改动，不是修崩。
        cfg.PanelPages ??= new();
        // 剔除数组里的 null 元素（手改出 "reminders":[null] 之类）：反序列化会留下 null 引用，
        // 下面按元素解引用即 NRE，而这些兜底在 try/catch 之外，一崩就违背「解析失败落默认、绝不崩」。
        normalized |= cfg.LaunchSteps.RemoveAll(x => x is null) > 0;
        normalized |= cfg.Reminders.RemoveAll(x => x is null) > 0;
        normalized |= cfg.ActionGroups.RemoveAll(x => x is null) > 0;
        // "panelPages":[null] 同样解引用即崩：PanelManagerWindow 那边是 p.Tab（删分类时数页数），
        // PanelLayout.BuildContentPages 自己防了，管理器和编辑器没防。
        normalized |= cfg.PanelPages.RemoveAll(x => x is null) > 0;
        // 页里那张步骤表也要在**任何遍历之前**补齐：手改出 "steps":null 的话，
        // 下面 SyncGroupStepLabels 那一趟、以及 PanelManagerWindow 里数格子的几处都会 NRE。
        // （OnYes 那一层留到迁移之后再补——迁移会往 PanelPages 里搬进新的页，见下面。）
        foreach (var p in cfg.PanelPages)
        {
            p.Steps ??= new();
            normalized |= p.Steps.RemoveAll(s => s is null) > 0;
        }
        // 补生缺失/空白 id：运行态与组引用都按 id 做键，json 里写成 "id":"" 会让多条共用一份状态、
        // 或组引用失效。默认值仅在 json 省略 id 时生效，显式空串需在此规范化。
        // 提醒 id 还要去重：运行态按 id 做键，重复 id（复制粘贴/手改出）会让两条共用一份状态、当天一条被另一条压制。
        // （reminder id 不被任何配置引用，可安全重发；组 id 被 groupId/silentGroupId/onYes 引用，只补空白不去重。）
        var seenReminderIds = new HashSet<string>();
        foreach (var r in cfg.Reminders)
            if (string.IsNullOrWhiteSpace(r.Id) || !seenReminderIds.Add(r.Id))
            {
                seenReminderIds.Add(r.Id = Guid.NewGuid().ToString());
                normalized = true;
            }
        foreach (var g in cfg.ActionGroups) if (string.IsNullOrWhiteSpace(g.Id)) { g.Id = Guid.NewGuid().ToString(); normalized = true; }
        // 「在托盘显示」的迁移：本字段之前建的组读进来是 null，一律补 true 保持升级前的托盘外观
        //（新建的组由编辑器给 false）。不补的话老用户升级后托盘里的动作组会全部消失，像功能坏了。
        foreach (var g in cfg.ActionGroups) if (g.ShowInTray is null) { g.ShowInTray = true; normalized = true; }
        // 「作为面板的一页」的默认值与托盘**相反**：null 一律补 false（编辑器同样默认不勾）。
        // 托盘补 true 是为了保住升级前的外观；而面板页是这一版才有的东西，没有「升级前的外观」可保，
        // 且一页 = 一整个组，默认把每个组都变成一页只会让面板一开就是十几页翻不完。
        // 这段注释原本写的是「面板也补 true、编辑器默认勾上」——与代码完全相反（见 FillPanelPageDefaults
        // 与 GroupEditorWindow 的 `?? false`）。而 PanelManagerWindow 曾按那句注释写成 `!= false`
        // （把 null 当成「在面板上」），与唯一真相 PanelLayout 的 `!= true` 正好判反。
        // 【一次性迁移】PanelTab 一度存的是「页签摆哪边」，只有 "" 和 "top" 两个值。
        // 现在它存的是**分类名**，那个 "top" 会原样变成一个名叫「top」的分类——
        // 一个用户从没起过的名字，却在顶栏上占一格。清成未分类。
        //
        // **必须有 schema 门。** 原来这里写的是「清空本身就是已迁移，不必设计数器」——
        // 那句话在字段只有两个取值时成立，而现在它是自由文本：一个真叫「top」的分类
        // （英文用户起这个名字再自然不过）会被每一次读配置清掉并写回盘，没有提示也找不回来，
        // 而且每次启动都再来一遍。迁移只在旧 schema 上跑一次，之后 "top" 就只是一个普通名字。
        if (cfg.Settings.PanelSchema < 2)
        {
            foreach (var g in cfg.ActionGroups)
                if (string.Equals((g.PanelTab ?? "").Trim(), "top", StringComparison.OrdinalIgnoreCase))
                { g.PanelTab = ""; normalized = true; }
        }
        // 其余归一化紧随其后（下面那些与面板无关）。
        // 嵌套引用容错：json 显式写 "onYes":null / "steps":null 会覆盖模型初始化器，下游（编辑器读 .OnYes.Type、遍历 Steps）会 NRE。
        foreach (var s in cfg.LaunchSteps) { s.OnYes ??= new(); normalized |= NormalizeOnYes(s.OnYes); }
        // json 显式 "repeatUntil":null 会覆盖模型的 "" 默认；UpdateAfterFire 直接 Regex.IsMatch(它) 会 NPE 崩，补回空串。
        foreach (var r in cfg.Reminders) { r.OnYes ??= new(); r.RepeatUntil ??= ""; r.IntervalUntil ??= ""; r.OnceDate ??= ""; normalized |= NormalizeOnYes(r.OnYes); }
        cfg.Gestures ??= new();

        normalized |= cfg.Gestures.RemoveAll(s => s is null) > 0;
        foreach (var st in cfg.Gestures)
        {
            st.OnYes ??= new(); normalized |= NormalizeOnYes(st.OnYes);
            // 手改 json 塞进来的坏轨迹在这里洗成规范形，别让钩子对着垃圾比对。
            //
            // **但洗成空的那一类不写回盘。** Normalize 遇到任何一个不认识的字符就整串拒收
            //（一个 tab、一个换行、"R-D" 里的连字符都算），返回空——那是「这串不合法」的**判定**，
            // 不是「它应该变成空」的修正。写回去等于替用户删掉他手写的绑定：没有提示，也找不回来。
            // 留着原样是安全的，**但这件事靠 GestureGate.ShouldWatch 一起兑现**：
            // 它判的也是 Normalize 之后的串，所以这种洗不动的绑定不会让钩子装上。
            // 它曾经判的是原串，于是这里写的「只是不生效而已」并不成立：钩子照装，
            // 每一次右键按下先被吞再补发，而那条手势永远比不上。
            // 纯空白另说——那本来就该洗成空，否则它会让钩子为一条根本不存在的手势装上。
            var gz = GestureGate.Normalize(st.Gesture);
            if (gz != (st.Gesture ?? "") && (gz.Length > 0 || string.IsNullOrWhiteSpace(st.Gesture)))
            { st.Gesture = gz; normalized = true; }
        }
        // group 步骤的 Label 与目标组重新对齐。Label 是组名的**缓存副本**，只在编辑那一步写入，
        // 于是改一次组名，启动清单/其它组/手势里所有引用它的行都会一直显示旧名——
        // 而那正是用户用来找回这一步的名字。放在这里而不是改名那一处：这里能一次walk到全部四份清单，
        // 顺带把老配置里早已漂移的也自愈掉。
        var live = cfg.ActionGroups;
        normalized |= StepDisplay.SyncGroupStepLabels(cfg.LaunchSteps, live);
        normalized |= StepDisplay.SyncGroupStepLabels(cfg.Gestures, live);
        foreach (var g in live) normalized |= StepDisplay.SyncGroupStepLabels(g.Steps, live);
        // **面板页是第四份**（这一版才从 ActionGroups 里分出来的），上面那句「全部三份」当时是对的。
        // 漏掉它的后果与漏掉别的一样、但更常见：DefaultPanelPages 和 MigratePanelPages 都把
        // Label = g.Name 当缓存写进格子里，于是改一次动作名，面板上那个格子永远显示旧名。
        foreach (var p in cfg.PanelPages) normalized |= StepDisplay.SyncGroupStepLabels(p.Steps, live);

        // 【一次性迁移】手势曾经绑在动作组上。搬进 cfg.Gestures，用 group 步骤表达「跑一整个组」——
        // 语义一模一样，而用户从此还能把手势绑到不属于任何组的单个动作上。
        // 不设 schema 计数器：组上的 gesture 字段被清空本身就是「已迁移」的标记，搬完不会再触发。
        foreach (var g in cfg.ActionGroups)
        {
            var moved = GestureGate.Normalize(g.Gesture);
            // 解析不出来就**原样留着**，不清空。清空会被 App.LoadConfig 写回盘：
            // 用户手写的 "R-D" 这类绑定就此消失，没有提示、没有 .bad 备份、也找不回来。
            // 上面那段（cfg.Gestures）为此专门拒绝写回，SilentDataLossTests 用同一个 "R-D" 钉着它——
            // 只是从没覆盖到这条迁移路径。留着一个跑不起来的字段，好过替用户删掉他写的东西。
            if (moved.Length == 0) continue;
            cfg.Gestures.Add(new LaunchStep { Kind = "group", GroupId = g.Id, Label = g.Name, Gesture = moved });
            g.Gesture = "";
            normalized = true;
        }


        // 【一次性迁移】面板页从动作里独立出来（PanelSchema 2 → 3）。
        //
        // **必须排在上面那段手势迁移之后。** 手势迁移要读 `ActionGroup.Gesture`，而这一段会把
        // 「只当页用」的组整个删掉——顺序反过来的话，一个既是面板页、又带着旧手势绑定的组
        // 会先被删掉，那条手势就永远丢了，而且悄无声息。实测：GestureBindingTests 当场变红，
        // 是它把这个顺序问题指出来的。
        //
        // 也必须排在 FillPanelPageDefaults 之前——那个方法会把 PanelSchema 抬到 3，
        // 抬完之后这道门就关了。账交给调用方（App.LoadConfig）报给用户看。
        LastMigrationLog = MigratePanelPages(cfg);
        if (LastMigrationLog.Count > 0) normalized = true;
        // 抬版本号必须是**最后一步**：它一旦把 PanelSchema 写到 3，上面那道门就关了。
        // 排在前面的话，这次启动的迁移根本不会跑，而版本号却已经说"迁过了"——数据就此卡在半路。
        normalized |= FillPanelPageDefaults(cfg);
        foreach (var g in cfg.ActionGroups) { g.Steps ??= new(); normalized |= g.Steps.RemoveAll(s => s is null) > 0; foreach (var s in g.Steps) { s.OnYes ??= new(); normalized |= NormalizeOnYes(s.OnYes); }
 }
        // 面板页里的步骤同样要补 OnYes。**必须排在 MigratePanelPages 之后**：迁移会把组里的步骤
        // 整批搬进 PanelPages 并把那个组从 ActionGroups 里删掉，于是上面那一趟（遍历 ActionGroups）
        // 再也碰不到它们——一个 {"kind":"message","onYes":null} 的步骤就这样带着 null 活下来，
        // 等用户一打开步骤编辑器（它读 step.OnYes.Type）当场 NRE。
        // 上面那批空表/空元素的守卫排在迁移之前，因为迁移之前就有人遍历它；这一层没人早用，放这儿。
        foreach (var p in cfg.PanelPages)
            foreach (var s in p.Steps) { s.OnYes ??= new(); normalized |= NormalizeOnYes(s.OnYes); }
        return normalized;
    }

    /// <summary>把「作为面板的一页」这个字段补齐：没表过态（null）的一律当作不是页。</summary>
    //
    // **这里曾经有一段「老面板模型」的迁移，已经删掉——因为那个老模型从未发布过。**
    // 它假定老配置里存在「整组一格的组 + 程序自动拼出来的动作组页」，于是把
    //   ShowInPanel != false && !PanelExpand
    // 的组全部扫进一张新造的页。可这三个字段都是随快捷面板一起新增的（`git show <上个发行版>` 里
    // 一个都没有），所以任何真实的老配置读进来都是 ShowInPanel == null、PanelExpand == false ——
    // **恰好 100% 命中那个判据**。结果不是「把老样子翻译过来」，而是凭空造出一个用户从没建过的组，
    // 里面用 group 步骤引用了他的每一个动作组，还因为 ShowInTray 补 true 而出现在托盘菜单里。
    //
    // 它没被测试发现，是因为测试夹具里每个组都显式写了 ShowInPanel = true，
    // 而真实配置里那个键压根不存在。夹具不等于现实。
    //
    // 现在只做一件诚实的事：null → false。升级上来的用户面板是空的，
    // 空态会告诉他怎么建第一页 —— 这比替他造一页他没要过的东西好。
    /// <summary>一次性迁移：把面板页从动作里搬出来（PanelSchema 2 → 3）。返回动过的账，没动就是空表。</summary>
    //
    // 从前一页就是一个动作组（组身上挂着 ShowInPanel / PanelTab / PanelForProcess）。
    // 拆开的理由见 PanelPage 的注释；这里只管把存量配置无损地搬过去。
    //
    // **三种情形，规则不同**——判据是「这个组除了当页，还被当动作用吗」：
    //
    //   1) 只当页用（没热键、没人引用）→ 步骤整批搬进新页，组删掉。沿用原 id，便于事后追溯。
    //   2) 既当页又当动作（有热键或被提醒/步骤引用）→ **动作留下**（清掉面板字段），
    //      另建一页放一个「运行动作」引用格。页从 N 格变成 1 格是有意的：
    //      热键和引用证明它被当成一串序列在用，而新模型逼它二选一，那就选动作——
    //      丢一页格子可以再摆，丢一个被引用的动作会让提醒和别的步骤一起断。
    //   3) 页上挂着托盘项 → 托盘项去掉。页不是可运行的序列，托盘菜单里点它没有意义。
    //
    // 账要如实记下来并报给用户：这是一次**结构性**改写，静默完成的话，
    // 用户下次打开发现动作列表少了三个、托盘少了两行，只会以为程序把配置弄坏了。
    public static List<string> MigratePanelPages(RootConfig cfg)
    {
        var log = new List<string>();
        if (cfg.Settings.PanelSchema >= 3) return log;

        // 「被引用」= 提醒的静默动作 / 提醒的「点是后」/ 任何 group 类型的步骤指向它。
        // 手势也走 group 步骤（RootConfig.Gestures 里那些），所以一并扫到。
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in cfg.Reminders ?? new())
        {
            if (r == null) continue;
            if (!string.IsNullOrWhiteSpace(r.SilentGroupId)) referenced.Add(r.SilentGroupId);
            if (r.OnYes?.Type == "group" && !string.IsNullOrWhiteSpace(r.OnYes.Target)) referenced.Add(r.OnYes.Target);
        }
        // **必须容忍 null 元素。** 这一段跑在「剔掉列表里的 null」之前（那一步在下面，
        // 且要等到迁移完成、动作列表定型之后才做），而 json 里显式写 "steps":[null] 是能读进来的。
        // 不设防的话，一个手写坏了的配置会让迁移在启动时 NRE ——而它恰恰是最需要迁移别出事的一次。
        // 实测：ConfigStoreTests.Read_null_array_elements_are_dropped_not_crashed 当场变红。
        IEnumerable<LaunchStep> AllSteps()
        {
            foreach (var s in cfg.LaunchSteps ?? new()) if (s != null) yield return s;
            foreach (var s in cfg.Gestures ?? new()) if (s != null) yield return s;
            foreach (var g in cfg.ActionGroups)
                foreach (var s in g.Steps ?? new()) if (s != null) yield return s;
            // **第四份清单：已有的面板页。** 漏掉它的后果不是少算一个引用，而是**删掉一个组**：
            // 下面那个循环拿 `referenced` 判「这个组除了上面板还有别的用处吗」，
            // 答「没有」就走 else 分支、`cfg.ActionGroups.Remove(g)`，而 LoadConfig 会把规范化后的
            // 配置写回磁盘——于是只被面板格子引用的组永久消失，幸存的那个格子从此
            // 解析成 Skip_GroupNotFound，而迁移日志里一字不提。
            // MainWindow.GDel_Click 那边删组时数的正是同一四份（它的注释还写着面板页是
            // 「最容易撞上的一份」）——两处必须同口径，否则删的时候拦着、迁移的时候静默删掉。
            // 循环里会往 cfg.PanelPages 里新增页，但 `referenced` 在那之前就已枚举完了，
            // 这里读到的只会是迁移前本就存在的页——正是该算的那些。
            foreach (var pg in cfg.PanelPages ?? new())
                foreach (var s in pg?.Steps ?? new()) if (s != null) yield return s;
        }
        foreach (var s in AllSteps())
            if (s.Kind == "group" && !string.IsNullOrWhiteSpace(s.GroupId)) referenced.Add(s.GroupId);

        foreach (var g in cfg.ActionGroups.Where(g => g.ShowInPanel == true).ToList())
        {
            bool alsoAnAction = !string.IsNullOrWhiteSpace(g.Hotkey) || referenced.Contains(g.Id);
            var page = new PanelPage
            {
                Id = alsoAnAction ? Guid.NewGuid().ToString() : g.Id,
                Name = g.Name,
                Tab = (g.PanelTab ?? "").Trim(),
                ForProcess = g.PanelForProcess ?? "",
            };
            if (alsoAnAction)
            {
                // 页上放一个引用格，指回那个留下来的动作。
                page.Steps.Add(new LaunchStep { Kind = "group", GroupId = g.Id, Label = g.Name, Icon = g.Icon });
                g.ShowInPanel = false; g.PanelTab = ""; g.PanelForProcess = ""; g.PanelExpand = false;
                log.Add(Strings.Lf("Mig_PageAndAction", g.Name));
            }
            else
            {
                page.Steps = (g.Steps ?? new()).Where(x => x != null).ToList();
                cfg.ActionGroups.Remove(g);
                log.Add(Strings.Lf("Mig_PageMoved", g.Name, page.Steps.Count));
            }
            // 托盘项去掉：页不是可运行的序列，托盘菜单里点它没有意义。
            if (g.ShowInTray == true && !alsoAnAction) log.Add(Strings.Lf("Mig_PageTrayDropped", g.Name));
            cfg.PanelPages.Add(page);
        }
        return log;
    }

    /// <summary>最近一次读配置时，面板页迁移动过的账。空 = 没迁移。由 App 读走并报给用户。</summary>
    // 静态而不是返回值：Normalize 的返回值是「要不要写回盘」那个布尔，已经被好几处调用方用着，
    // 为一次性迁移改它的签名不划算。这份账只在启动那一次有值。
    public static List<string> LastMigrationLog { get; private set; } = new();

    private static bool FillPanelPageDefaults(RootConfig cfg)
    {
        bool touched = false;
        foreach (var g in cfg.ActionGroups)
            if (g.ShowInPanel is null) { g.ShowInPanel = false; touched = true; }
        // 版本号留着：它是下一次真有东西要迁移时的锚点，而且已经写进了所有在用的配置。
        // 2 = 「PanelTab 从『摆哪边』改成分类名」那一版。抬到 2 之后上面那段迁移不再跑，
        // "top" 从此只是一个普通的分类名。
        if (cfg.Settings.PanelSchema < 3) { cfg.Settings.PanelSchema = 3; touched = true; }
        cfg.PanelPages ??= new();
        return touched;
    }

    private static T OrDefault<T>(T? value, T fallback) where T : class => value ?? fallback;

    // 「点是后」空组引用规范化：旧版编辑器允许存下 type="group" 而目标为空（下拉留在「（无）」），
    // 点「是」什么都不做。读入时归一成 none 并触发写回，运行期不必再对这种残留误报「组被删」。
    private static bool NormalizeOnYes(OnYes y)
    {
        if (y.Type != "group" || !string.IsNullOrWhiteSpace(y.Target)) return false;
        y.Type = "none";
        return true;
    }
}
