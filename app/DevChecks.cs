using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Views;

namespace Clockwork;

// 开发期自查开关，常驻主干（探针类的量完就删，不进这里）：
//   --smoke        构造并布局每一个 XAML 窗口（并逐个选一遍它的每个 Tab）后退出。
//                  XAML 是懒加载的，而 WPF 又只实例化当前选中页——写错的窗口/标签页不打开就不报错，
//                  能一路带到发版；这个开关在 CI 里替用户把每扇窗、每一页都开一遍。
//   --selftest     要一扇真窗、因而进不了 app.Tests 的那几条检查（问句弹框那条链）。见 DevSelfTest.cs。
//   --shots <目录>  离屏渲染成 PNG 逐张比对：每个窗口的每个 Tab × 3 档宽度 × 5 档工作区高度 × 6 种语言，
//                  外加一轮浅色主题——实测约 6200 张。
//                  这些轴都是被真实缺陷逃过之后一条条加上去的：只截首页 → 其余五页从未被渲染；
//                  宽度恒为 1040 → 窗口拖到最窄时的裁切从未被看见；只跑 zh/en/de/ar → 西语在某些串上比德语更长。
//                  「工作区高度」是模拟出来的可用高度，不等于窗口高度：最高那一档（1032）下窗口并不会真长到
//                  1032，它被窗口自带的 Height 封住（主窗口 720、动作组编辑器 640），实测就是这两个数。
//                  也就是说那一档测的是「工作区比窗口还宽裕」，而不是一个更高的窗口——想看更高窗口下的版面，
//                  得先改窗口的 Height，只加高工作区没用。
//                  这 6 种语言不等于「每条串的最长译文」——它们只是整体上最容易出事的一组。
//                  想验某一条具体文案的最坏情况，先按长度把 18 种语言排一遍，再临时把下面那个 langs
//                  数组换成排在前面的几种、跑一次、看完还原（这是探针，别留在主干里）。
//                  已这样验过一次：Ports_Count 最长的是土耳其语（46 字，中性 25 字），
//                  比矩阵里最长的德语还长 24%，实测 820 宽下仍单行放得下——计数在 Auto 列里，
//                  它先拿够宽度，被压缩的是旁边那个星号列的说明。法语（页脚最长）同样正常。
//                  注意三个盲区，前两个是**结构性**的——再多拍几千张也堵不上：
//                    · MaxHeight 是 harness 无条件设的，「窗口自己忘了运行时封顶」这类问题截图里永远正常。
//                      这一条已经不靠 review：app.Tests/Views/SizeToContentTests.cs 扫源码就能判，
//                      要求每个 SizeToContent 窗口既调了 FitToWorkArea、又在 <Window> 自己那个标签上
//                      留了 ≤392 的兜底 MaxHeight（=最紧那一档 464 减 72 边距；探测失败时生效的就是它，写大了等于没写）。
//                    · 条件性 UI 的显隐由**数据**决定，而矩阵只铺「窗口 × 语言 × 尺寸 × 主题」：
//                      只在某个下拉选到冷门那一档时才出现的字段行，可能一张图都没有。
//                      同样落成了测试：WrappingHintTests 要求 Wrap 的 TextBlock 在无限宽容器里
//                      必须自带 Width 或 MaxWidth，否则那句 Wrap 是空话。
//                    · 异步填充的列（如系统启动项页的扫描）拍到的是未完成态，那是时序不是缺陷。
// 两个开关都挂在单实例检查之前：托盘里正在用的实例照常工作，检查进程自己开自己关，互不打扰。
// 成败判定以 marker 文件为准，不靠退出码——PowerShell 读 GUI 进程 ExitCode 有已知读空坑，
// 且 marker 顺带证明真跑到了「所有窗口布局完、走到写文件」那一步。
public partial class App
{
    // 截图/冒烟用：本程序占着的功能键清单留空——这些变体只验版面，不验查重。
    private static readonly (string Key, string Owner)[] NoFunctionHotkeys = System.Array.Empty<(string, string)>();

    // 推迟到消息循环转起来（ApplicationIdle）再跑，而不是在 OnStartup 里当场跑：
    // OnStartup 阶段 Application.Run 的循环还没起转，Show() 只把建 HWND/Loaded 的活排进队列，
    // 窗口 hwnd=0、ActualWidth=0（实测 84/84 全零）；就地 Dispatcher.Invoke 泵到 Loaded 档也救不回来。
    // OnStartup 返回、循环起转之后，Show() 才是教科书上的同步语义。
    private void RunDevCheck(string[] args)
        => Dispatcher.BeginInvoke(
            () =>
            {
                if (args.Contains("--hookprobe")) RunHookProbe();
                else if (args.Contains("--screenshots")) RunScreenshots(args);
                else if (args.Contains("--shots")) RunShots(args);
                else if (args.Contains("--selftest")) RunSelfTest();
                else RunSmoke();
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);


    // ── --hookprobe：低级鼠标钩子的隔离探针 ──
    //
    // 装一个裸的 WH_MOUSE_LL，数 12 秒，把收到的事件数写进 %TEMP%\clockwork-hookprobe.txt 然后退出。
    // 裸钩子那一半不读配置、不看总开关、没有心跳没有自愈、不建轨迹窗——就一句 SetWindowsHookEx 加一个计数器。
    //
    // **但它不只装裸钩子。** 同一进程、同一 12 秒里还并排装一个真正的 MouseHook（见下面
    // 那段）——只有把两者摆在完全相同的条件下比，「是环境不通、还是我们这个类有问题」才分得开。
    // （这一句曾经写成「只有一句 SetWindowsHookEx」，而那是并排那一半加进来之前的事了。）
    //
    // 存在的理由：程序正常那条路上挂着太多环节，任一环出问题都表现为「按了没反应」，
    // 于是「钩子到底收不收得到事件」这个最底层的问题反而查不出来。剥到只剩一句系统调用之后，
    // 它就有了一个不掺杂本程序任何逻辑的答案：
    //   总数 > 0 → 这台机器、这个进程上下文里，钩子是通的；问题在程序里。
    //   总数 = 0 → 钩子压根收不到；问题在环境（权限、别的钩子不放行、安全软件），与程序无关。
    // 右键那一格单独数：只有它是 0 而移动不是 0，才说明右键被谁单独截走了。
    private void RunHookProbe()
    {
        string marker = Path.Combine(Path.GetTempPath(), "clockwork-hookprobe.txt");
        int all = 0, moves = 0, rdown = 0, rup = 0, mid = 0;
        // 吞掉按下之后，系统还认不认为右键按着？
        //
        // 真实的手势路径里，OnRightDown 一律返回 Swallow，钩子随即 return 1——那条消息就此被丢弃，
        // **不再进入系统的输入处理**。而紧接着每一次移动，代码都会用 GetAsyncKeyState(VK_RBUTTON)
        // 问一句「手还按着吗」，答「没有」就整笔作废。若吞掉按下会让那一问答「没按」，
        // 手势就永远在第一次移动时被自己掐死——而且外表一切正常。
        // 这两个计数器就是为这一问准备的：吞掉之后，随后的移动里它答「按着」多少次、「没按」多少次。
        int heldYes = 0, heldNo = 0;
        bool swallowed = false;
        IntPtr hook = IntPtr.Zero;
        Native.MouseHook.RawProc proc = (code, w, l) =>
        {
            if (code >= 0)
            {
                all++;
                int m = w.ToInt32();
                if (m == 0x0200)
                {
                    moves++;
                    if (swallowed) { if (Native.Win32.RightButtonDown()) heldYes++; else heldNo++; }
                }
                else if (m == 0x0204)
                {
                    rdown++;
                    // 照真实路径那样吞掉它，好让这一测与生产代码处在同一个状态里。
                    swallowed = true;
                    return (IntPtr)1;
                }
                else if (m == 0x0205) { rup++; swallowed = false; }
                else if (m is 0x0207 or 0x0208) mid++;
            }
            return Native.MouseHook.CallNext(hook, code, w, l);
        };
        hook = Native.MouseHook.InstallRaw(proc);

        // **同一进程、同一 12 秒里，再装一个真正的 MouseHook。**
        // 只有把两者摆在完全相同的条件下比，「是环境不通、还是我们这个类有问题」才分得开——
        // 分别跑两次、隔着几分钟、还夹着别的程序，那种比较什么都证明不了。
        //
        // **gesture 必须传 null。** 带上判定器的话，GestureGate.OnRightDown 一律返回 Swallow，
        // 而这个钩子装得比裸钩子晚、在链里排它前面——于是它把每一次右键按下都吃掉，
        // 裸计数器永远数到 0，探针就会「测出」一个根本不存在的「右键被上游截走」。
        // 这不是假设：这个探针最早的两版就是这么写的，据此得出的结论全是错的。
        // 传 null 之后它照旧记录收到过哪些消息（_beatRDown 写在 _gesture == null 那条早退之前），
        // 只是不再插手，裸计数器量到的才是这台机器上右键的真实去向。
        var real = new Native.MouseHook(350, () => { }, a => Dispatcher.BeginInvoke(a), gesture: null);
        bool realInstalled = real.Install();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Native.MouseHook.UninstallRaw(hook);
            var realBeat = real.EverBeat;
            var realRight = real.EverRightBeat;
            real.Dispose();
            var elevated = Native.Win32.IsElevated();
            var text =
                $"裸钩子={(hook == IntPtr.Zero ? "装不上" : "装上了")}  提权={elevated}\r\n" +
                $"12 秒里收到：总数={all}  移动={moves}  右键按下={rdown}  右键抬起={rup}  中键={mid}\r\n" +
                $"MouseHook 类：装上={realInstalled}  收到过输入={realBeat}  收到过右键={realRight}\r\n" +
                $"吞掉按下后 GetAsyncKeyState 说右键：按着={heldYes} 次  没按={heldNo} 次\r\n" +
                (heldNo > 0 && heldYes == 0
                    ? "结论：**吞掉按下之后系统就不认为右键按着了**。手势路径每次移动都拿这一问当判据，"
                      + "答「没按」就整笔作废 —— 第一次移动就把自己掐死，一个采样点都攒不下。这就是手势没反应的原因。\r\n"
                    : all == 0
                    ? "结论：连裸钩子都收不到 —— 环境问题（权限 / 别的钩子不放行 / 安全软件），与本程序无关。\r\n"
                    : !realBeat
                        ? "结论：裸钩子收得到而 MouseHook 类收不到 —— 问题在我们这个类里。\r\n"
                        : rdown == 0
                            ? "结论：两者都收得到移动。右键那一格为 0；若这 12 秒里确实按过右键，说明右键在到达钩子链之前被截走了。\r\n"
                            : "结论：两者都通，连右键一起。那么「手势没反应」的原因在钩子之上（判定、匹配、或执行那一段）。\r\n");
            try { File.WriteAllText(marker, text); } catch { }
            Shutdown();
        };
        timer.Start();
        // 提示写在前面：探针要人动鼠标才有数据，而它只跑 12 秒。
        try { File.WriteAllText(marker, "正在采集…请在 12 秒内移动鼠标、并按几次右键。\r\n"); } catch { }
    }

    // 每个 XAML 窗口一个工厂。构造参数给最小样例对象——够 InitializeComponent + 布局走完就行，
    // 不读用户配置（RootConfig.Default() 是全新对象，save 是 no-op，冒烟进程绝不碰真数据）。
    // 当前语言下、键名以这些前缀开头的最长一条文案（已填好占位符）。
    // 截图要测的是「最坏情况下版面碎不碎」，那就得把最坏情况喂进去。
    private static string LongestOf(params string[] prefixes)
    {
        var rm = new System.Resources.ResourceManager("Clockwork.Resources.Strings", typeof(Strings).Assembly);
        var set = rm.GetResourceSet(System.Globalization.CultureInfo.CurrentUICulture, true, true);
        string best = "";
        if (set != null)
            foreach (System.Collections.DictionaryEntry e in set)
                if (e.Key is string k && e.Value is string v && prefixes.Any(k.StartsWith) && v.Length > best.Length)
                    best = v;
        // 占位符填上真实长度的样本值，否则 {0} 只算三个字符，测不出实际宽度。
        var args = new object[] { "logioptionsplus_agent", 30844, 58000, @"C:\Program Files\Some Vendor\App\launcher.exe" };
        try { return string.Format(best, args); } catch { return best; }
    }

    // 「运行程序」步骤的自动取图标：与真实路径走同一条解析（Core.PanelIcon），
    // 所以截图验的是生产代码本身，不是 harness 里另写的一份近似逻辑。
    private static PanelIconSpec AutoIcon(string target) => PanelIcon.Resolve(null, "app", target);

    private static (string Name, Func<Window> Make)[] AllXamlWindows()
    {
        var groups = new[] { new ActionGroup() };
        return new (string, Func<Window>)[]
        {
            ("MainWindow", () =>
            {
                var cfg = RootConfig.Default();
                // 必须复刻正常启动的约定：App 在建任何窗口之前会把 Language 的默认值 ""（跟随系统）
                // 解析成具体 code 落盘。样例配置若留着 ""，MainWindow 语言下拉的初始赋值会被
                // Lang_Changed 当成「用户切了语言」→ RelaunchForLanguage() 把 Shutdown 排进队列，
                // 之后第一次泵消息就整个进程静默退出（实测：84 张截图全零、第二轮起建窗即抛
                // 「应用程序对象正在关闭」，肇事栈是从 ShutdownStarted 里抓出来的）。
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                return new MainWindow(cfg, () => { });
            }),
            // 面板管理器：默认配置只有一页两格，页眉、空位、并排那三件要验的东西
            // 得靠再添一页格子更多的才拍得全。
            ("PanelManager", () =>
            {
                var cfg = RootConfig.Default();
                var page = new PanelPage { Name = LongestOf("Tab_") };
                page.Steps.Add(new LaunchStep { Kind = "app", Label = "notepad", Target = @"C:\Windows\System32\notepad.exe" });
                page.Steps.Add(new LaunchStep { Kind = "system", Label = LongestOf("Sys_"), Command = "lockScreen" });
                page.Steps.Add(new LaunchStep { Kind = "volume", Label = LongestOf("Vol_"), Action = "mute" });
                cfg.PanelPages.Add(page);
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 「只显示图标」档：这一档的 LabelFont 是 0，而 WPF 的 FontSize 不接受 0 ——
            // 曾经在这里当场崩（「"0" 不是属性 "FontSize" 的有效值」）。
            // 单元测试只能钉住「这一档会给 0」，钉不住「这一档画得出来」，那要靠真渲染一次。
            ("PanelManagerIconOnly", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                cfg.Settings.PanelIconOnly = true;
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 紧凑 + 只显示图标：格子最小的那一档，两行标签的空间彻底没有。
            ("PanelManagerTiny", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                cfg.Settings.PanelIconOnly = true;
                cfg.Settings.PanelTileSize = "compact";
                return new PanelManagerWindow(cfg, () => { });
            }),
            ("StepEditor", () => new StepEditorWindow(new LaunchStep(), groups)),
            // 「运行动作组」步骤：组下拉与它下面那行内容预览只在这一种类型下出现，
            // 空步骤的编辑器截不到——而这正是「选择现有动作组」这条路的界面。
            ("StepEditorGroup", () =>
            {
                // 用默认配置里的真实组：harness 那个 groups 是个空名占位，截出来看不出选择器长什么样，
                // 更看不出下面那行内容预览有没有在工作。
                var real = RootConfig.Default().ActionGroups;
                return new StepEditorWindow(new LaunchStep { Kind = "group", GroupId = real[0].Id }, real);
            }),
            // 画手势时屏幕上那条笔迹。它是**唯一一扇没有任何常规界面**的窗口——
            // 透明、点不着、只有一条线——所以更需要被画出来一次：它的三条硬约束
            //（不抢前台、点得穿、不在钩子里画）都不会以「长得不对」的形式暴露，
            // 但「线画歪了 / 缩放算错了 / 根本没显示」会，而那只有渲染看得见。
            ("GestureTrail", () =>
            {
                var w = new GestureTrailWindow();
                foreach (var (x, y) in new[] { (300, 200), (420, 200), (540, 200), (540, 320), (540, 440) })
                    w.Point(x, y);
                return w;
            }),
            // 刚「添加面板页」之后的样子：一张**空卡**。这是新用户最先遇到的一屏，
            // 而它此前从未被画出来过——空页曾经根本不产出（见 PanelLayout.keepEmpty）。
            ("PanelManagerEmptyPage", () =>
            {
                var cfg = RootConfig.Default();
                cfg.PanelPages.Add(new PanelPage { Id = "blank", Name = "" });
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 就地改名那一态：新建一页之后光标就在标题里。这一态没有任何静态入口能到达，
            // 不单列变体就永远没被画出来过——而它正是「添加面板页」之后你看到的那一屏。
            ("PanelManagerRenaming", () =>
            {
                var cfg = RootConfig.Default();
                var w = new PanelManagerWindow(cfg, () => { });
                w.BeginRenameForShots(cfg.PanelPages.First());
                return w;
            }),
            // 顶栏有**两个分类**、左栏有**两页**的样子。默认配置全在「未分类」里，
            // 于是顶栏只画得出那一格加一颗「+」，多分类下的排布永远拍不到。
            //
            // 顺带把第一页绑给一个程序：页眉上那颗「只在哪个程序上出现」的按钮
            // 默认是淡着的「任何程序」，绑上之后才显示进程名——两种样子得各拍一次。
            // 挑列表里的第一个来绑：管理器不重排（allContexts 下完全跟随列表顺序），
            // 所以它就是打开时停的那一页，也就是页眉被画出来的那一页。
            ("PanelManagerTabSides", () =>
            {
                var cfg = RootConfig.Default();
                // 再添一页，好让**每一类里都有两页**：一类只有一页时左栏整条不画，
                // 那这张截图就只验到了顶栏，而它要验的正是两条栏同时在的样子。
                var extra = new PanelPage { Name = LongestOf("Tab_") };
                extra.Steps.Add(new LaunchStep { Kind = "system", Command = "lockScreen" });
                cfg.PanelPages.Add(extra);
                cfg.PanelPages.Add(new PanelPage { Name = LongestOf("Sys_") });
                int i = 0;
                foreach (var p in cfg.PanelPages) p.Tab = i++ < 2 ? "Tab_Group" : "";
                cfg.PanelPages[0].ForProcess = "Code.exe";
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 「常用」里那两条会改变编辑器版式的预设。空步骤截不到它们：
            // 窗口那一节多了一句「留空 = 当前窗口」，系统那一节的文本行标签会随命令换成「搜索地址」
            // 并多出一行说明——两处都是只在这一种取值下才存在的界面，不单列变体就永远没被画出来过。
            ("StepEditorWindowCurrent", () => new StepEditorWindow(
                StepDisplay.MakePreset("window", "minimize"), groups)),
            ("StepEditorSearch", () => new StepEditorWindow(
                StepDisplay.MakePreset("system", "searchSelection"), groups)),
            // 自定义搜索引擎：选了现成引擎时地址框是藏起来的，只有这一档会露出地址 + 那行说明。
            // 也就是说 StepEditorSearch 那个变体**永远拍不到**这两行，得单列一个。
            ("StepEditorSearchCustom", () => new StepEditorWindow(
                new LaunchStep { Kind = "system", Command = "searchSelection", Text = "https://example.com/find?q={0}" }, groups)),
            // 这一轮新增的三档：打开网址（地址框 + 占位说明）、获取选中的文字（只有一行说明）、
            // 等剪贴板变化（一个秒数框）。三者的版面互不相同，各拍一张才看得见有没有塌。
            ("StepEditorUrl", () => new StepEditorWindow(
                new LaunchStep { Kind = "url", Target = "https://www.bing.com/search?q={clipboard}" }, groups)),
            ("StepEditorCopySelection", () => new StepEditorWindow(new LaunchStep { Kind = "copySelection" }, groups)),
            ("StepEditorWaitClipboard", () => new StepEditorWindow(new LaunchStep { Kind = "waitClipboard", Level = 5 }, groups)),
            // 两个问句类型：一个是「提示语 + 默认值」两行，一个是「提示语 + 多行选项 + 说明」三段，
            // 而两者下面都还挂着共用的「输出到变量」那一行。三段拼起来是这个编辑器里最高的一档面板。
            ("StepEditorPrompt", () => new StepEditorWindow(
                new LaunchStep { Kind = "prompt", Message = LongestOf("Ed_"), Text = "默认", OutputVar = "关键词" }, groups)),
            ("StepEditorChoice", () => new StepEditorWindow(
                new LaunchStep { Kind = "choice", Message = LongestOf("Ed_"), Text = "上班\n下班\n午休", OutputVar = "去哪儿" }, groups)),
            // 这一轮的两个 Windows 操作，两处版面都是**减法**，只有截图看得出减对了没有：
            //   回到刚才那个窗口 —— 进程名、两句说明、等窗口出现四行整片收起，换上它自己那一句。
            //     一个只剩下拉和一句说明的面板，最容易在某种语言下塌成一条缝。
            //   播放声音 —— 与「搜索地址」共用同一个输入框，标签换成「声音文件」、
            //     说明行换成「留空 = 系统提示音」，高度从三行收成一行。
            ("StepEditorRestore", () => new StepEditorWindow(
                new LaunchStep { Kind = "window", Action = "restore" }, groups)),
            ("StepEditorPlaySound", () => new StepEditorWindow(
                new LaunchStep { Kind = "system", Command = "playSound" }, groups)),
            // 置顶：这一档比别的窗口动作多一行「它是开关」的说明，只有这一种取值下才存在。
            ("StepEditorTopmost", () => new StepEditorWindow(
                StepDisplay.MakePreset("window", "topmost"), groups)),
            // 目标为空（上一版的「（无）」存的就是这个值）：下拉必须停在「（无）」上，
            // 而不是落到第一个真实动作组——那会让「打开看一眼再确定」悄悄改掉这一步。
            ("StepEditorGroupNone", () => new StepEditorWindow(
                new LaunchStep { Kind = "group", GroupId = "" }, RootConfig.Default().ActionGroups)),
            // 目标组已被删除：单列一项、不静默改指。这一态没有截图就没人会发现它长什么样。
            ("StepEditorGroupMissing", () => new StepEditorWindow(
                new LaunchStep { Kind = "group", GroupId = "已删除的组", Label = "早就删了的组" },
                RootConfig.Default().ActionGroups)),
            // ── 交互态 ──
            // 到这一轮为止，所有截图拍的都是**静止态**：没有选中行、没有焦点、没有悬停。
            // 而配色一换，最先坏掉的恰恰是这些——实测就在这一轮抓到了：主按钮的悬停色
            // 原来借的是「亮一档的黄铜」，那个键改名成警告色之后，鼠标一移上去按钮变成琥珀黄。
            // 悬停/按下拿不到（IsMouseOver 是只读的，没法在离屏窗口上造出来），
            // 但**选中**和**键盘焦点**是能设的，那就把这两样拍下来。
            // 「一列全勾上」是真实用户最常见的状态（谁会把启动项全关掉），
            // 可样例数据是未勾选的，于是**勾选态从来没被截图覆盖过**——
            // 用户在自己的配置里看到那一列强调色方块，而我一次都没见过。
            ("MainWindowChecked", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                foreach (var st in cfg.LaunchSteps) st.Enabled = true;
                foreach (var r in cfg.Reminders) r.Enabled = true;
                // 设置页的开关也一并打开：勾选态在这里才有文字标签作伴，
                // 和列表里的裸勾选框是两种读法，都得看得见。
                cfg.Settings.StartMinimized = true;
                cfg.Settings.StartupWaitForReady = true;
                cfg.Settings.PanelMiddleLongPress = true;
                cfg.Settings.PanelIconOnly = true;
                return new MainWindow(cfg, () => { });
            }),
// 设置页的下半截。主窗口高度封顶 720，而设置页有八节——**底下六节从来没进过任何截图**
            // （面板、鼠标手势、关于全在折线以下），裁切和文案溢出在那儿发生了也没人看得见。
            // 滚到底再拍，把这块盲区盖上。
            ("MainWindowSettingsBottom", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var w = new MainWindow(cfg, () => { });
                w.Loaded += (_, _) =>
                {
                    w.Tabs.SelectedIndex = w.Tabs.Items.Count - 1;   // 设置是最后一页
                    w.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        w.SettingsScroll.UpdateLayout();
                        w.SettingsScroll.ScrollToEnd();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                };
                return w;
            }),
                        ("MainWindowPicked", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var w = new MainWindow(cfg, () => { });
                // Loaded 而不是构造里：行是 Show 之后才具现的，构造时选不中。
                w.Loaded += (_, _) =>
                {
                    if (w.GridLaunch.Items.Count > 0) w.GridLaunch.SelectedIndex = 0;
                    w.GridLaunch.Focus();
                };
                return w;
            }),
            ("GroupEditorFocused", () =>
            {
                var w = new GroupEditorWindow(new ActionGroup(), groups, NoFunctionHotkeys);
                w.Loaded += (_, _) => System.Windows.Input.Keyboard.Focus(w.NameBox);
                return w;
            }),
            ("ReminderEditor", () => new ReminderEditorWindow(new Reminder(), groups)),
            ("GroupEditor", () => new GroupEditorWindow(new ActionGroup(), groups, NoFunctionHotkeys)),
// 手势管理器：带两条已绑手势进去，才截得到箭头与摘要。
            // 一条 group（跑整组）、一条 system（只作为手势存在的动作）——这两种正是这个界面要能同时装下的。
            // 斜角箭头 ↖↗↙↘ 有没有字形只能靠渲染验（同 MDL2 码位——缺字形不抛异常，只画成空心方块）。
            ("GestureManager", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                cfg.Gestures.Add(new LaunchStep { Kind = "system", Command = "lockScreen", Gesture = "R3D" });
                if (cfg.ActionGroups.Count > 0)
                    cfg.Gestures.Add(new LaunchStep { Kind = "group", GroupId = cfg.ActionGroups[0].Id, Label = cfg.ActionGroups[0].Name, Gesture = "1L7" });
                return new GestureManagerWindow(cfg, () => { });
            }),
            // 空态：得**自己清空**。默认配置现在带 5 条示例手势（RootConfig.DefaultGestures），
            // 直接喂 Default() 的话这张图里是那 5 条，空态从此拍不到——而空态正是新用户
            // 删光样例之后看到的那一屏，也是「怎么加第一条」那句话唯一的载体。
            ("GestureManagerEmpty", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Gestures.Clear();
                return new GestureManagerWindow(cfg, () => { });
            }),
            // 「置顶」那一档：右栏比别的动作多一行「它是开关」的说明，只有这一种取值下才存在
            //（同 StepEditorTopmost 的理由）。得让它落在第一行——列表默认选中第一条，
            // 不选中就永远拍不到那行字。
            ("GestureManagerTopmost", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Gestures.RemoveAll(s => !(s.Kind == "window" && s.Action == "topmost"));
                return new GestureManagerWindow(cfg, () => { });
            }),
            // 图标选择器：这个窗口本身就是内置图标库的验收图。挑错的 MDL2 码位不会报错，
            // 只会画成一个空心方框——只有把库整个铺出来渲染一次，才看得见有没有坏的。
            ("IconPicker", () => new IconPickerWindow("")),
            // 喂真实的最长文案，不再喂 "smoke"：这三个窗口的尺寸完全由文案长度决定，
            // 喂一个五字占位串等于根本没测它们——带三个变量的确认文案在窄窗口下什么样，
            // 一直是空白。长度按当前语言现算，不维护人工清单（哪一条最长逐语言不同）。
            ("ReminderPopup", () => new ReminderPopupWindow(LongestOf("Msg_", "Reminder_", "Confirm_"), confirm: true, autoDismissSeconds: 0)),
            ("Toast", () => new NotificationToast("Clockwork", LongestOf("SysMsg_", "Warn_", "Err_"), ToastLevel.Info, durationMs: 0)),
            ("BrandDialog", () => new BrandDialog(null, LongestOf("Confirm_"), confirm: true, ToastLevel.Info)),
            // 问句形态的同一张卡：多一个输入框 / 多一个列表。两者都只由 Ask / Pick 这两个
            // 静态入口到达（它们自己就跑模态），harness 到不了那条路，所以在这儿把那两行直接摆出来。
            // 要验的是**这张卡长高之后还站得住**：眉标 + 一段可能很长的问句 + 答案 + 两个按钮，
            // 在 464 那一档和阿拉伯语下最容易挤。
            ("BrandDialogAsk", () =>
            {
                var d = new BrandDialog(Strings.Get("Ask_Title"), LongestOf("Ed_"), true, ToastLevel.Info);
                d.YesBtn.Content = Strings.Get("Ed_Ok");
                d.NoBtn.Content = Strings.Get("Ed_Cancel");
                d.AnswerBox.Text = LongestOf("Kind_");
                d.AnswerBox.Visibility = System.Windows.Visibility.Visible;
                return d;
            }),
            ("BrandDialogPick", () =>
            {
                var d = new BrandDialog(Strings.Get("Ask_Title"), LongestOf("Ed_"), true, ToastLevel.Info);
                d.YesBtn.Content = Strings.Get("Ed_Ok");
                d.NoBtn.Content = Strings.Get("Ed_Cancel");
                foreach (var o in new[] { LongestOf("Kind_"), LongestOf("Sys_"), LongestOf("Win_") }) d.AnswerList.Items.Add(o);
                d.AnswerList.SelectedIndex = 0;
                d.AnswerList.Visibility = System.Windows.Visibility.Visible;
                return d;
            }),
            // 快捷面板：格子宽度是写死的，而标签是用户的组名或托盘文案——最长的那条译文
            // （土耳其语「Çalışan Eylemleri Durdur」之类）在 116 宽的格子里换不换得下，
            // 只有把真实最长文案喂进去才看得见。三个格子够撑出一整行，跨行的排布也就一并测到了。
            // 两片都要喂：分隔线与「自带操作」那一片只有在 ops 非空时才画得出来，
            // 只传上面一片的话，截图里永远看不到面板真正的样子。
            // 首页喂满 6 格（跨两行，验列宽与行距），其中一格停用（验压暗）；夹板窄条 4 条正好一行。
            // 给三页：只有多页时翻页刻度、页名才画得出来，单页拍出来看不到签名件。
            // 图标逐个换不同的 kind：挑错的 MDL2 码位不会报错，只会渲染成空心方框——
            // 这几张截图是唯一能发现它的地方。
            ("QuickPanel", () => new QuickPanelWindow(SamplePages(), SampleOps(), null, SampleWaist())),
            // ── 面板的可配置外观 ──
            // 尺寸档位、只显示图标、列数、单条带都是设置页里点得到的状态，可它们从来没被渲染过。
            // 只拍默认那一档，等于把「用户改了设置之后长什么样」整块留白：
            // 紧凑档文字会不会挤成一团、只显示图标时格子是不是收成方的、8 列下面板有多宽、
            // 下带设 0 时上面那个坑会不会孤零零——每一条都只有画出来才知道。
            // 内容与默认那张完全一样，所以两张摆在一起，差别只剩外观本身。
            ("QuickPanelCompact", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 4, TileSize: "compact"))),
            ("QuickPanelRoomy", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 4, TileSize: "roomy"))),
            ("QuickPanelIconOnly", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 4, TileSize: "normal", IconOnly: true))),
            ("QuickPanelCol3", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 3, TopRows: 2, BottomRows: 3))),
            ("QuickPanelCol8", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 8, TopRows: 1, BottomRows: 2))),
            // 单条带：下带设 0，退回一整块方格阵。上面那个坑得独自撑住场面。
            ("QuickPanelOneBand", () => new QuickPanelWindow(SamplePages(), SampleOps(), chromeOps: SampleWaist(), look: new PanelLook(Columns: 4, TopRows: 7, BottomRows: 0))),
            // 关掉自带操作那一排：夹板下沿少了一片，凹坑会不会贴着边。
            ("QuickPanelNoOps", () => new QuickPanelWindow(SamplePages(), null, chromeOps: SampleWaist(), look: new PanelLook(Columns: 4, ShowOps: false))),
            // ── 两条导航栏 ──
            // 它们**空着就不画**，所以默认那几张里一条也看不见——不单列变体的话，
            // 这个功能画得对不对永远没人看得到。这里喂一份真有分类、也真有多页的样例：
            // 上面一条分类栏（三叠，其中一叠没名字→「其他」），左边一条页签栏。
            ("QuickPanelRails", () => new QuickPanelWindow(RailPages(), SampleOps(), chromeOps: SampleWaist())),
            // 只有页签、没有分类：这是「分了好几页但没归类」的常态，顶部那条该整条不出现。
            ("QuickPanelPageRailOnly", () => new QuickPanelWindow(
                RailPages().Select(p => p with { Tab = "" }).ToList(), SampleOps(), chromeOps: SampleWaist())),
            // 十几条页签：参考里那种密度才是这个功能真正要面对的场面，而它是唯一会**把面板撑高**的
            // 内容——不封住上限的话，页一多面板就比屏幕还高，而这个窗口没有标题栏，那时连关都不好关。
            ("QuickPanelLongRail", () => new QuickPanelWindow(LongRailPages(), SampleOps(), chromeOps: SampleWaist())),
            // 两条栏都关掉：版面必须与从前逐像素相同（那是「默认不打扰」的底线）。
            ("QuickPanelRailsOff", () => new QuickPanelWindow(RailPages(), SampleOps(), chromeOps: SampleWaist(),
                look: new PanelLook(LeftTabs: false, TopTabs: false))),
            // 动作多到装不下一屏时腰栏才长出放大镜（见 BuildWaist 的阈值）。
            // 样例那 17 格过不了阈值，所以搜索这一角在截图里一直是空的——喂一份大的。
            ("QuickPanelSearchable", () => new QuickPanelWindow(BigPages(), SampleOps(), chromeOps: SampleWaist())),
            // 搜索态本身：输入框 + 计数占满整条腰栏，结果铺进凹坑。
            // 没有任何静态入口能到达这个状态，只能让它自己进去一次。
            ("QuickPanelSearching", () =>
            {
                var w = new QuickPanelWindow(BigPages(), SampleOps(), chromeOps: SampleWaist());
                w.StartSearch();
                return w;
            }),
            // 打了字、有结果：空查询只能验版面，验不到匹配与排序对不对。
            // 种子取自真实标签的头一个字，所以六种语言下都必然命中——
            // 写死一个「搜」之类的词，在英语下会拍出一张空结果，验不到想验的东西。
            ("QuickPanelSearchHit", () =>
            {
                var pages = BigPages();
                var w = new QuickPanelWindow(pages, SampleOps(), chromeOps: SampleWaist());
                var seed = pages[0].Tiles[0].Label;
                w.StartSearch(seed.Length > 0 ? seed[..1] : "");
                return w;
            }),
            // 打了字、没结果。空状态是最该给方向的一屏，而它也只有真搜一次才到得了。
            ("QuickPanelSearchMiss", () =>
            {
                var w = new QuickPanelWindow(BigPages(), SampleOps(), chromeOps: SampleWaist());
                w.StartSearch("zzzz");
                return w;
            }),
            // 空面板：一页都没有。空状态是最该给方向的一屏，而它一次都没拍过。
            ("QuickPanelEmpty", () => new QuickPanelWindow(System.Array.Empty<PanelTilePage>(), SampleOps(), null, SampleWaist())),
        };
    }

    // 样例页与自带操作。抽成方法而不是字段：每个档位要一份新的实例
    // （WPF 元素只能有一个父级，同一批 PanelTile 记录倒是可以共享，但页记录里带着回调，
    //  各档位共用一份也无妨——真正的原因是字段初始化时 Strings 还没定语言）。
    private static (IReadOnlyList<PanelTilePage> Pages, IReadOnlyList<PanelTile> Ops) PanelSample()
    {
        var pages = new[]
        {
                    // 15 格：默认版面（4 列 × 上 3 行 + 下 4 行）下上带正好铺满 12 格、
                    // 余 3 格落到下带，两条带和中间那条发丝线才都拍得到。少于 13 格截图里看不到分带。
                    new PanelTilePage(Strings.Get("Tab_Group"), new[]
                    {
                        new PanelTile(LongestOf("Tray_"), PanelGlyph.Group, () => { }),
                        new PanelTile(LongestOf("Panel_"), PanelGlyph.ForKind("app"), () => { }),
                        new PanelTile(LongestOf("Kind_"), PanelGlyph.ForKind("keys"), () => { }),
                        new PanelTile(LongestOf("Sys_"), PanelGlyph.ForKind("system"), () => { }),
                        new PanelTile(LongestOf("Vol_"), PanelGlyph.ForKind("volume"), () => { }),
                        new PanelTile(LongestOf("Tab_"), PanelGlyph.ForKind("window"), () => { }, false),
                        new PanelTile(Strings.Get("Kind_mouse"), PanelGlyph.ForKind("mouse"), () => { }),
                        new PanelTile(Strings.Get("Kind_text"), PanelGlyph.ForKind("text"), () => { }),
                        new PanelTile(Strings.Get("Kind_delay"), PanelGlyph.ForKind("delay"), () => { }),
                        // 真实程序图标：面板好不好看几乎全押在这上面，而「取图标」这条路只有
                        // 渲染出来才验得到——码位挑错只是画成方框，图标取不到则是整格退回线描，
                        // 两种都不会报错。挑 System32 下必然存在的几个，任何 Windows 上都拍得出。
                        new PanelTile("notepad", AutoIcon(@"C:\Windows\System32\notepad.exe"), () => { }),
                        new PanelTile("explorer", AutoIcon(@"C:\Windows\explorer.exe"), () => { }),
                        new PanelTile("mspaint", AutoIcon(@"C:\Windows\System32\mspaint.exe"), () => { }),
                        new PanelTile("cmd", AutoIcon(@"C:\Windows\System32\cmd.exe"), () => { }),
                        // 指向一个不存在的文件：必须优雅退回线描，而不是空一格。
                        new PanelTile("missing", AutoIcon(@"C:\nope\gone.exe"), () => { }),
                        new PanelTile(Strings.Get("Kind_keys"), PanelGlyph.ForKind("keys"), () => { }),
                    }, OnAdd: () => { }),
                    // 带 OnAdd：末尾那个半透明的「新增」虚位只有在这一页可加东西时才画得出来。
                    new PanelTilePage(Strings.Get("Tab_Launch"), new[]
                    {
                        new PanelTile(LongestOf("Win_"), PanelGlyph.ForKind("mouse"), () => { }),
                    }),
                    new PanelTilePage(Strings.Get("Tab_Reminder"), new[]
                    {
                        new PanelTile(LongestOf("Adv_"), PanelGlyph.ForKind("message"), () => { }),
                    }),
        };
        var ops = new[]
        {
                    new PanelTile(Strings.Get("Tray_Rerun"), TrayGlyph.Rerun, () => { }),
                    new PanelTile(Strings.Get("Tray_Stop"), TrayGlyph.Stop, () => { }),
                    new PanelTile(Strings.Get("Tray_DndMenu"), TrayGlyph.Dnd, () => { }),
                    new PanelTile(Strings.Get("Tray_Show"), TrayGlyph.Window, () => { }),
        };
        return (pages, ops);
    }

    private static IReadOnlyList<PanelTilePage> SamplePages() => PanelSample().Pages;

    private static IReadOnlyList<PanelTile> SampleOps() => PanelSample().Ops;

    // 一份「两条栏都有东西」的样例：两页挑到顶部、四页留在左边。
    // 页名取真实文案键，好让页签上的截断与最长语言一并拍到。
    // 分两类：上边那条分类栏得有两格才画得出来，而左栏画的是**当前这一类**里的页——
    // 全归一类的话，这两条栏各自的取舍都拍不到。
    private static IReadOnlyList<PanelTilePage> RailPages()
    {
        (string Title, string Side)[] spec =
        {
            ("Tab_Launch", "Tab_Group"), ("Tab_Group", "Tab_Group"),
            ("Tab_Reminder", ""), ("Tab_Settings", ""), ("Tab_System", ""), ("Tab_Ports", ""),
        };
        var pages = new List<PanelTilePage>();
        foreach (var (title, side) in spec)
        {
            var tiles = new List<PanelTile>();
            for (int i = 0; i < 6; i++)
                tiles.Add(new PanelTile(Strings.Get("Sys_showDesktop"), PanelIcon.Resolve(null, "system"), () => { }));
            pages.Add(new PanelTilePage(Strings.Get(title), tiles, null, side, 0));
        }
        return pages;
    }

    // 十四条页签、同一叠：撑高面板的那种配置。
    private static IReadOnlyList<PanelTilePage> LongRailPages()
    {
        string[] keys = { "Tab_Launch", "Tab_Group", "Tab_Reminder", "Tab_Settings", "Tab_System", "Tab_Ports",
                          "Menu_SecOpen", "Menu_SecControl", "Menu_SecSystem", "Menu_SecFlow",
                          "Panel_Global", "Panel_Uncategorized", "Kind_app", "Kind_keys" };
        return keys.Select(k => new PanelTilePage(
            Strings.Get(k),
            Enumerable.Range(0, 4).Select(_ => new PanelTile(Strings.Get("Sys_showDesktop"),
                PanelIcon.Resolve(null, "system"), () => { })).ToList(),
            null, "", 0)).ToList();
    }

    // 一份「动作多到要搜索」的样例：四页各 24 格，共 96。
    // 96 既过了「装不装搜索」的阈值（一屏 28），也超过搜索结果的上限（两屏 56）——
    // 于是截断时那个「56/96」的读数也拍得到。48 那一版永远拍不到截断。
    // 标签取各前缀里最长的那条，好让搜索结果里的换行/省略号也一并拍到。
    private static IReadOnlyList<PanelTilePage> BigPages()
    {
        string[] prefixes = { "Sys_", "Vol_", "Win_", "Kind_", "Tray_", "Panel_" };
        var pages = new List<PanelTilePage>();
        for (int p = 0; p < 4; p++)
        {
            var tiles = new List<PanelTile>();
            for (int i = 0; i < 24; i++)
                tiles.Add(new PanelTile(LongestOf(prefixes[(p * 24 + i) % prefixes.Length]),
                                        PanelGlyph.ForKind(StepDisplay.StepKinds[(p * 24 + i) % StepDisplay.StepKinds.Length]),
                                        () => { }));
            pages.Add(new PanelTilePage(LongestOf("Tab_"), tiles));
        }
        return pages;
    }

    // 表圈右端那几颗。截图里必须喂上，否则「找东西 / 管东西」那一角永远是空的。
    // 与 App.BuildPanelWaistOps 一一对应。少喂一个的话，表圈右端在截图里就少一颗，
    // 而那正是要验的地方——它和「Esc 关闭」挤在同一行，多一颗少一颗的排布只有渲染看得见。
    private static IReadOnlyList<PanelTile> SampleWaist() => new[]
    {
        new PanelTile(Strings.Get("Gesture_Manager"), char.ConvertFromUtf32(0xE962), () => { }),
        // fillsEmptyPanel 要跟 App.BuildPanelWaistOps 一致：空面板那颗行动按钮的显隐与文字全看它，
        // 这儿漏了标记，QuickPanelEmpty 那张图就会画出一块没有按钮的空面板——一张说谎的截图。
        new PanelTile(Strings.Get("Panel_Manager"), char.ConvertFromUtf32(0xE713), () => { }, fillsEmptyPanel: true),
    };

    // 挪出屏幕再显示：不居中、不抢焦点、不进任务栏视野。
    private static void Park(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -32000; w.Top = -32000;
        w.ShowActivated = false;
    }

    // 把队列泵到 Loaded 档：Show() 排进队列的 Loaded 级收尾活（模板实例化等）当场做完再量尺寸。
    // 截图文件名里的 Tab 标识。只留英数：标题本身是翻译过的，中文/阿语下会落成空串，
    // 此时靠前面的序号区分；英德下则能直接从文件名看出是哪一页。
    private static string TabSlug(TabControl tabs)
    {
        string header = (tabs.SelectedItem as System.Windows.Controls.TabItem)?.Header as string ?? "";
        var keep = new string(header.Where(ch => ch < 128 && (char.IsLetterOrDigit(ch))).ToArray());
        return keep.Length == 0 ? "" : "-" + keep;
    }

    private void PumpToLoaded()
        => Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

    // 比 PumpToLoaded 再低两档。DataGrid 的列宽分配（Pixel / Star 实际取值）发生在
    // Loaded 之后的异步一轮里：只泵到 Loaded 就截图，每一列都停在自己的 MinWidth 上
    // （220px 的列量出来是 20，星号列也是 20），拍出一张看上去版式崩了的假图。
    // 初始选中的那个 Tab 在 Show 期间已经 settle，所以这个坑在只截首页的年代从未暴露。
    private void PumpToIdle()
        => Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    private void RunSmoke()
    {
        string marker = Path.Combine(Path.GetTempPath(), "clockwork-smoke.txt");
        try
        {
            Strings.ApplyCulture(null);
            foreach (var (name, make) in AllXamlWindows())
            {
                var w = make();
                Park(w);
                w.Show();
                PumpToLoaded();
                w.UpdateLayout();
                // 不只「没抛」：窗口必须真的布局出来了。不查这个的话，smoke 在「Show 静默没生效」
                // 的环境里照样绿灯（上面 PumpToLoaded 注释里的 84/84 全零正是这么漏掉的）。
                if (w.ActualWidth <= 0 || w.ActualHeight <= 0)
                    throw new InvalidOperationException($"{name}: laid out to zero size");
                VisitAllTabs(w);
                w.Close();
            }
            CheckStepMenuFits();
            File.WriteAllText(marker, "OK");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(marker, ex.ToString()); } catch { }
            Shutdown(1);
        }
    }

    // 「新增 ▾」那份菜单一直没人量过——它是唯一一处**会随文案表变长**的界面：
    // 每加一条常用动作就多一行，而菜单不像窗口，长过屏幕不会报错，只会悄悄长出上下滚动箭头。
    // 那时用户看到的是一份「最后几项要滚才看得见」的清单，而最常用的往往正好排在末尾。
    //
    // 两份都要量：顶层那份，和「常用」子菜单。子菜单横着展开、不占顶层高度，
    // 但它自己一样会长过屏幕，而且那是加动作时唯一会变长的地方。
    //
    // 阈值是量出来的，不是拍的。顶层：光是类型清单就已经 524px 高
    // （行高与文案无关，德语俄语量出来一模一样，长的只会是行数），加上「常用」那一行约 561px。
    // 渲染矩阵里最紧的一档工作区是 464（1366×768 @150%），顶层菜单在那一档**本来就装不下**——
    // 这是既有状况，不是这次改出来的。所以顶层按 566（1366×768 @125%，小本的出厂设置）要求，
    // 只剩一行的余量：谁再往顶层加一行这里就会红，那时该想的是「收进子菜单」而不是抬阈值。
    // 子菜单与顶层同一档（理由在方法体里那三行：父的都装不下，苛求子的没有意义）。
    private void CheckStepMenuFits()
    {
        const double SmallestFittingWorkArea = 768 / 1.25 - 48;
        foreach (var culture in new string?[] { null, "de", "ru" })
        {
            Strings.ApplyCulture(culture);
            MeasureMenu(Views.StepMenu.Build((_, _) => { }), "step menu", culture, SmallestFittingWorkArea);
            // 每一节的子菜单单独量：它们横着展开、不占顶层的高度，但自己一样会长过屏幕，
            // 而现在类型全在这些子菜单里——顶层量得再好也说明不了它们装得下。
            foreach (var (sectionKey, _) in StepDisplay.StepKindSections)
            {
                var sub = new ContextMenu();
                foreach (var it in Views.StepMenu.SectionItems(sectionKey, (_, _) => { })) sub.Items.Add(it);
                MeasureMenu(sub, "section " + sectionKey, culture, SmallestFittingWorkArea);
            }
            // 「常用」子菜单同理。
            var probe = new ContextMenu();
            foreach (var it in Views.StepMenu.PresetItems((_, _) => { })) probe.Items.Add(it);
            // 阈值与顶层菜单同一档（566），不是更严的 464。此前按 464 要求是**不自洽的**：
            // 顶层菜单本身就 524px 高，在 464 那一档从来装不下——却要求它的子菜单在那儿装得下。
            // 父的都装不下，苛求子的没有意义。566 是这条菜单路径实际站得住的那一档。
            MeasureMenu(probe, "common submenu", culture, SmallestFittingWorkArea);
        }
        Strings.ApplyCulture(null);
    }

    private void MeasureMenu(ContextMenu menu, string what, string? culture, double limit)
    {
            // ContextMenu 不进可视树就没有尺寸：挂到一个停在屏外的窗口上开一下，量完就关。
            var host = new Window { Width = 200, Height = 100, ContextMenu = menu };
            Park(host);
            host.Show();
            menu.PlacementTarget = host;
            menu.IsOpen = true;
            PumpToIdle();
            menu.UpdateLayout();
            double h = menu.ActualHeight;
            // 收尾要收干净。ContextMenu 的 popup 是一扇**独立的顶层窗口**，不在 host 的可视树里：
            // 只置 IsOpen=false 就撒手，那扇窗会一直挂着，进程到最后退不掉（实测：冒烟写完 OK 就再也不返回）。
            // 断开引用之后各泵一轮，让 popup 与 host 的 HWND 都真的销毁。
            menu.IsOpen = false;
            PumpToIdle();
            menu.PlacementTarget = null;
            host.ContextMenu = null;
            host.Close();
            PumpToIdle();
            if (h <= 0) throw new InvalidOperationException($"{what} laid out to zero height");
            if (h > limit)
                throw new InvalidOperationException(
                    $"{what} ({culture ?? "zh"}) is {h:F0}px tall, over the {limit:F0}px work area");
    }

    // WPF 的 TabControl 只实例化当前选中页：只 Show 一下，非首页的几个 Tab 其实一行都没跑过，
    // 里面写错的 StaticResource / 绑定名能一路带到发版——正是这个开关要替用户堆掉的那类问题。
    // 逐个选一遍，把每页都 realize 出来；选中事件里的懒加载（扫自启项 / 扫端口）一并跑到。
    private void VisitAllTabs(Window w)
    {
        if (w.FindName("Tabs") is not TabControl tabs) return;
        object? original = tabs.SelectedItem;
        foreach (var item in tabs.Items)
        {
            tabs.SelectedItem = item;
            PumpToIdle();
            w.UpdateLayout();
        }
        tabs.SelectedItem = original;
        PumpToIdle();
    }

    // README 里那三张截图。单独一个模式而不是从 --shots 的几千张里挑：
    // README 要的是**固定的三种语言、固定的一个尺寸、固定的一页**，与那套「验裁切」的矩阵目标不同；
    // 而且繁体中文根本不在那套矩阵的语言里（那边挑的是最容易出事的六种，不是最多人看的三种）。
    // 截图每次界面一改就得重出，所以它得是一条能重跑的命令，而不是某次手工摆拍。
    private void RunScreenshots(string[] args)
    {
        int i = Array.IndexOf(args, "--screenshots");
        string dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--")
            ? args[i + 1]
            : Path.Combine(Path.GetTempPath(), "clockwork-screenshots");
        Directory.CreateDirectory(dir);

        // 与 README 里现有那三张同名，直接覆盖 assets/ 即可。
        foreach (var (lang, file) in new[] { ("en", "screenshot.png"), ("zh-CN", "screenshot.zh.png"), ("zh-TW", "screenshot.zh-Hant.png") })
        {
            Strings.ApplyCulture(lang);
            App.ApplyTheme("dark");
            var cfg = RootConfig.Default();
            cfg.Settings.Language = lang;
            // 示例步骤全部勾上：README 的图要展示「配好之后长什么样」，
            // 而首次载入的示例刻意全不勾（不勾就什么都不会跑），照原样拍出来是一列空框。
            foreach (var st in cfg.LaunchSteps) st.Enabled = true;
            var w = new MainWindow(cfg, () => { });
            try
            {
                Park(w);
                w.Show();
                w.Width = 1457;
                w.MaxHeight = 771;
                PumpToIdle();
                w.UpdateLayout();
                int pw = (int)Math.Ceiling(w.ActualWidth), ph = (int)Math.Ceiling(w.ActualHeight);
                var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(w);

                // **必须压成不透明**：Ceiling 会多出一圈窗口没画到的像素（实测右 6px、下 15px），
                // 它们在 Pbgra32 里是全透明的。README 图在 GitHub 浅色主题下一渲染，
                // 那圈透明就成了白边——看起来像截图带了个白框。
                // 垫一层画布色再叠上去，比事后裁掉更稳：裁掉要猜边到底有多宽。
                var bg = new SolidColorBrush(System.Windows.Application.Current.Resources["Ink"] is System.Windows.Media.Color ink
                                             ? ink : System.Windows.Media.Colors.Black);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(bg, null, new Rect(0, 0, pw, ph));
                    dc.DrawImage(rtb, new Rect(0, 0, pw, ph));
                }
                var flat = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                flat.Render(dv);

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(flat));
                using var fs = File.Create(Path.Combine(dir, file));
                enc.Save(fs);
            }
            finally { try { w.Close(); } catch { } }
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "clockwork-shots.txt"), "OK");
        Shutdown(0);
    }

    private void RunShots(string[] args)
    {
        string marker = Path.Combine(Path.GetTempPath(), "clockwork-shots.txt");
        try
        {
            // --shots 后面跟目录；没给就落到临时目录。
            int i = Array.IndexOf(args, "--shots");
            string dir = i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--")
                ? args[i + 1]
                : Path.Combine(Path.GetTempPath(), "clockwork-shots");
            Directory.CreateDirectory(dir);

            // 工作区高度（DIP）= 物理高 / 缩放 - 任务栏（Win11 约 48 DIP）。
            // 第一档就是最容易出事的 1366×768 @125%。
            // 模拟的可用高度 = 屏高 / 缩放 − 任务栏。每一档都对应一台真机上会出现的配置：
            //   464  1366×768 @150%   —— 小本上把缩放调到 150 的人不少，这是**最紧的一档**
            //   566  1366×768 @125%   —— 小本的出厂设置
            //   672  1920×1080 @150%  —— 最常见的一档
            //  1032  1920×1080 @100%
            //  2112  3840×2160 @100%  —— 4K 直出，验的是「窗口会不会被拉得空空荡荡」
            // 之前最小只到 566，于是 464 那一档下会不会把「确定 / 取消」挤出屏幕，从来没人看过。
            var waHeights = new[] { 768 / 1.5 - 48, 768 / 1.25 - 48, 1080 / 1.5 - 48, 1080.0 - 48, 2160.0 - 48 };
            // 高危语言：中文基线 / 英文 / 德语 / 西语 / 俄语 / 阿语（RTL）。
            // “德语最长”只是平均意义上成立，逐条看并不是：端口页那两个复选框
            // （Solo servidores de desarrollo / Mostrar todos los puertos）西语比德语长一截，
            // 俄语在另一批串上同理。只跑德语就会把这类裁切放过去（实测已发生过一次）。
            // RTL 不走 App 里的 OverrideMetadata（每类型只能调一次，逐语言循环会炸），逐窗口设 FlowDirection。
            var langs = new[] { "zh-CN", "en", "de", "es", "ru", "ar" };
            // 宽度三档：820 是用户能拖到的最窄处（MinWidth），1040 是默认开窗宽，
            // 1600 验的是拉宽之后版面会不会散架（星号列吃掉全部余量、按钮飘到天边）。
            var widths = new[] { 820.0, 1040.0, 1600.0 };

            int count = 0;
            var fails = new List<string>();

            // ── 浅色主题：单尺寸、全语言、全窗口 ──
            // 版面不随主题变，变的只有颜色，所以浅色不必把 15 组尺寸再跑一遍——
            // 那只会多出两千张只有色号不同的图。要验的是「这个色在这套皮下还读得出来吗」，
            // 一个尺寸足够；尺寸相关的裁切由深色那一轮负责。
            foreach (var lang in langs)
            {
                Strings.ApplyCulture(lang);
                App.ApplyTheme("light");
                foreach (var (name, make) in AllXamlWindows())
                {
                    Window? w = null;
                    string shot = $"{name}@{lang}@light";
                    try
                    {
                        Park(w = make());
                        w.FlowDirection = Strings.IsRightToLeft
                            ? System.Windows.FlowDirection.RightToLeft
                            : System.Windows.FlowDirection.LeftToRight;
                        w.Show();
                        w.MaxHeight = 1032;
                        if (w.MinWidth <= 1040) w.Width = 1040;
                        PumpToIdle();
                        w.UpdateLayout();
                        // 有 Tab 的窗口只拍首页：浅色这一轮验的是配色，不是逐页版面。
                        var rtb = new RenderTargetBitmap(
                            (int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight),
                            96, 96, PixelFormats.Pbgra32);
                        rtb.Render(w);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = File.Create(Path.Combine(dir, shot + ".png"))) enc.Save(fs);
                        count++;
                    }
                    catch (Exception ex) { fails.Add($"{shot}: {ex.Message}"); }
                    finally { try { w?.Close(); } catch { } }
                }
            }
            App.ApplyTheme("dark");

            foreach (var lang in langs)
            {
                Strings.ApplyCulture(lang);
                foreach (var (name, make) in AllXamlWindows())
                    foreach (var wa in waHeights)
                        foreach (var ww in widths)
                        {
                            Window? w = null;
                            string shot = $"{name}@{lang}@{(int)ww}x{(int)wa}";
                            try
                            {
                                Park(w = make());
                                w.FlowDirection = Strings.IsRightToLeft
                                    ? System.Windows.FlowDirection.RightToLeft
                                    : System.Windows.FlowDirection.LeftToRight;
                                w.Show();
                                w.MaxHeight = wa;   // 必须 Show 之后设，否则被 FitToWorkArea 按真实显示器算的值覆盖
                                if (w.MinWidth <= ww) w.Width = ww;   // 窗口自己的 MinWidth 优先，不提供它不允许的尺寸
                                PumpToIdle();
                                w.UpdateLayout();

                                // 一个窗口可能有多页（MainWindow 的 6 个 Tab）。WPF 只实例化当前选中页，
                                // 不逐个选一遍，截出来的图里有 5/6 的界面从没被渲染过。
                                var tabs = w.FindName("Tabs") as TabControl;
                                int pages = tabs?.Items.Count ?? 1;
                                for (int p = 0; p < pages; p++)
                                {
                                    string suffix = "";
                                    if (tabs != null)
                                    {
                                        tabs.SelectedIndex = p;
                                        PumpToIdle();
                                        w.UpdateLayout();
                                        suffix = $"-{p + 1}{TabSlug(tabs)}";
                                    }
                                    var rtb = new RenderTargetBitmap(
                                        (int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight),
                                        96, 96, PixelFormats.Pbgra32);
                                    rtb.Render(w);
                                    var enc = new PngBitmapEncoder();
                                    enc.Frames.Add(BitmapFrame.Create(rtb));
                                    using (var fs = File.Create(Path.Combine(dir, $"{name}{suffix}@{lang}@{(int)ww}x{(int)wa}.png")))
                                        enc.Save(fs);
                                    count++;
                                }
                                w.Close();
                            }
                            catch (Exception ex)
                            {
                                string diag = w == null ? "ctor" :
                                    $"aw={w.ActualWidth} vis={w.IsVisible} loaded={w.IsLoaded} visprop={w.Visibility} " +
                                    $"hwnd={new System.Windows.Interop.WindowInteropHelper(w).Handle}";
                                fails.Add($"{shot} [{diag}] {ex.GetType().Name}: {ex.Message}");
                                try { w?.Close(); } catch { }
                            }
                        }
            }
            File.WriteAllText(marker, fails.Count == 0
                ? $"OK {count} shots -> {dir}"
                : $"FAIL {fails.Count}/{count + fails.Count}\r\n" + string.Join("\r\n", fails));
            Shutdown(fails.Count == 0 ? 0 : 1);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(marker, ex.ToString()); } catch { }
            Shutdown(1);
        }
    }
}
