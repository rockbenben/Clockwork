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
//   --selftest     要一扇真窗、因而进不了 app.Tests 的那几条检查（模态弹框那条链：
//                  问句弹框，以及手动重跑整份清单之前那道确认）。见 DevSelfTest.cs。
//   --trayshot <png>  把托盘菜单拍成一张 PNG（见 ProbeTrayMenu.cs）。这张菜单是本程序
//                  最常被看见的一屏，却不是 WPF 窗——--shots 那套离屏渲染罩不住它，
//                  于是它此前既没被拍过、也没被测过（字形撞车、区段标题不对齐都是这么漏的）。
//                  走的是屏幕上那一条真路径：屏幕左上角会闪一下菜单。
//   --shots <目录>  离屏渲染成 PNG 逐张比对：每个窗口的每个 Tab × 3 档宽度 × 5 档工作区高度 × 6 种语言，
//                  外加一轮浅色主题——量级是**万张**（具体张数随窗口与页签增减，不写死在这里：
//                  写死的那个数每次加窗口都会过期，而过期的数比没有数更坏）。
//                  全量太慢时可用环境变量 CW_SHOT_LANGS /
//                  CW_SHOT_WINDOWS（逗号分隔，窗口按名前缀）只渲子集，复验单条发现用。
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

    /// <summary>实现它的窗口**按自己的自然宽度**渲染，不吃截图矩阵的宽度档。</summary>
    // 矩阵对每一扇窗都设 w.Width = ww，而那一类窗的自然宽度正是它要展示的东西：
    // 面板管理器是 SizeToContent，宽度由最宽的那一行决定，被拉到 1040 之后
    // 「窗被那一行撑开、卡片两侧一片黑」就再也看不见了——它正是这么漏出矩阵的。
    // 与 BeginRenameForShots / SelectSideTabForShots 同一类：只有截图进得去的状态，
    // 就得给它一条静态入口，不然它等于没被看过。
    internal interface INaturalWidthShot { }

    // 推迟到消息循环转起来（ApplicationIdle）再跑，而不是在 OnStartup 里当场跑：
    // OnStartup 阶段 Application.Run 的循环还没起转，Show() 只把建 HWND/Loaded 的活排进队列，
    // 窗口 hwnd=0、ActualWidth=0（实测 84/84 全零）；就地 Dispatcher.Invoke 泵到 Loaded 档也救不回来。
    // OnStartup 返回、循环起转之后，Show() 才是教科书上的同步语义。
    private void RunDevCheck(string[] args)
        => Dispatcher.BeginInvoke(
            () =>
            {
                if (args.Contains("--hookprobe")) RunHookProbe();
                else if (args.Contains("--trayshot")) ProbeTrayMenuShot(TrayShotPath(args), args);
                else if (args.Contains("--screenshots")) RunScreenshots(args);
                else if (args.Contains("--shots")) RunShots(args);
                else if (args.Contains("--selftest")) RunSelfTest();
                else RunSmoke();
            },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);


    // ── --hookprobe：低级鼠标钩子的隔离探针 ──    //
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
            // 摘钩之前先把这一格抄下来（Dispose 不改它，但「摘钩前抄账」是本文件的既有规矩，
            // 见 GestureCoordinator.HealMouseHookIfDead 的注释：摘掉之后就再也问不到了）。
            var realUpstream = real.UpstreamMaxMs;
            var realCallbackMax = real.CallbackMaxMs;
            var realChainMax = real.ChainMaxMs;
            var realReplayMax = real.ReplayMaxMs;
            real.Dispose();
            var elevated = Native.Win32.IsElevated();
            var text =
                $"裸钩子={(hook == IntPtr.Zero ? "装不上" : "装上了")}  提权={elevated}\r\n" +
                $"12 秒里收到：总数={all}  移动={moves}  右键按下={rdown}  右键抬起={rup}  中键={mid}\r\n" +
                $"MouseHook 类：装上={realInstalled}  收到过输入={realBeat}  收到过右键={realRight}\r\n" +
                // 上游延迟：事件生成 → 我们回调收到，中间隔了多久的峰值。低级钩子按安装顺序串成链，
                // 排在我们前面的（Logi Options+ / PowerToys / 热键助手）拖多久，这一格就报多久。
                // 它是「光标能动但点不动」唯一能自证的量：光标位置不走钩子链，按钮事件必须走完链。
                $"上游延迟峰值={realUpstream}ms\r\n" +
                // 与上一格成对，但回答的是**相反**的问题：上面是别人堵了我们多久，这一格是我们自己
                // 离被摘还有多远（LowLevelHooksTimeout 是回调的预算，本机实测 300ms）。
                // 光看上游延迟分不出「链被堵」和「我们自己把钩子搞死」——而这两种病修法相反。
                $"回调耗时峰值={realCallbackMax}ms（预算 300ms，接近它就是在被摘的边缘）\r\n" +
                // 回调那段时间再劈两半，否则上面那一格只说「我们花了多久」，说不清是谁占的：
                // 链（CallNextHookEx，别人的钩子）与注入（我们自己的 SendInput）。
                // 链（CallNextHookEx，别人的钩子）单独一格：它大 = 链堵了，只能让路 / 抢链首。
                // 我们自己的注入从前挂在旁边（`我们的注入=`）；2026-10-06 把补发整体上移到补发线程
                // 之后，回调里只剩拖拽救援那一处同步注入，所以这里报的是**补发线程**的峰值——
                // 它与回调耗时不再是同一条账（见 MouseHook.ReplayMaxMs / ReplayQueued / ReplayDropped）。
                $"　其中 链={realChainMax}ms  补发线程注入峰值={realReplayMax}ms "
                + $"（排队 {real.ReplayQueued} 次，投不进去 {real.ReplayDropped} 次）\r\n" +
                // 同一刻在跑的那些「会装低级鼠标钩子」的程序（见 Core.HookSuspects）。
                // 上游延迟那一格说明链**有没有**被堵，这一格说明当时**谁在场**——
                // 两格合起来才是一句完整的话；只有前一格，结论停在「有人堵，但不知道去关谁」。
                // 名字里带「候选」是承重的：它证明的是在场，不是作案（理由写在 HookSuspects 里）。
                $"钩子链候选（此刻在跑）={HookSuspects.Describe(HookSuspects.RunningProcessNames())}\r\n" +
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

    // 焦点探针的地基：把键盘焦点放到某个元素上，并确认它**真的落上了**。
    // 少了这句断言，「聚焦图和不聚焦图一模一样」就有两种读法——控件没画焦点框，
    // 或者根本没聚焦成功。前者是要修的 bug，后者是探针自己坏了。
    private static void FocusOrThrow(System.Windows.IInputElement element, string what)
    {
        System.Windows.Input.Keyboard.Focus(element);
        if (!ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, element))
            throw new InvalidOperationException($"focus probe: keyboard focus never reached the {what}");
    }

    // ── 焦点可见性的成对判据（--smoke 跑，所以 CI 每次构建都过一遍）──────────────
    //
    // 强调色环是**纯样式属性**：谁重做一次 CheckBox 模板、把那个 ring 元素改个名，环就静默消失，
    // 而唯一受害者是键盘用户——他看不见焦点在哪，只能靠 Tab 数格子。截图矩阵里那五个探针只产出
    // 给人看的 PNG，而 CI 不跑 --shots，所以「补了环」这件事原本没有任何东西在守。
    //
    // 判据是**同一扇窗的成对像素差**：一次什么都不做，一次把键盘焦点放到目标控件上，两次的位图
    // 必须差出一片。基线用的是截图矩阵里已有的同名变体——两边都由 AllXamlWindows() 造，
    // 所以「探针和基线长得一样」不是这张表维护的巧合，而是同一个构造函数的结果。
    //
    // 名字写死在这儿：改了探针名而没改这张表，这里会直接抛，而不是静默少测一类控件。
    private static readonly (string Focused, string Baseline, string Control)[] FocusPairs =
    {
        ("FocusProbeChk", "GroupEditor", "CheckBox"),
        ("FocusProbeCombo", "StepEditor", "ComboBox"),
        ("FocusProbeTab", "MainWindow", "TabItem"),
        ("FocusProbeList", "GestureManager", "ListBoxItem"),
        ("FocusProbeCell", "MainWindow", "DataGridCell"),
        ("FocusProbeTopTab", "PanelManager", "PanelTopTab"),
        ("FocusProbePageTab", "PanelManager", "PanelPageTab"),
    };

    // 阈值是量出来的，不是拍的：五类控件补上环之后实测差 128 / 552 / 311 / 1226 / 1972 像素，
    // 而环坏掉的那一轮实测是**整 0**（不是「小一点」，是一模一样）。线就画在 0 与 128 之间，
    // 取 32：离抗锯齿抖动的量级足够远，又离最小的真环足够近。
    private const int FocusChangedPixelsMin = 32;

    private void CheckFocusRings()
    {
        var byName = AllXamlWindows().ToDictionary(v => v.Name, v => v.Make);
        // 一次跑完五类再报：第一条就抛的话，改一类要重跑一遍全矩阵才知道下一类坏没坏。
        var bad = new List<string>();
        foreach (var (focused, baseline, control) in FocusPairs)
        {
            if (!byName.TryGetValue(focused, out var makeFocused) || !byName.TryGetValue(baseline, out var makePlain))
            {
                bad.Add($"{control}：判据找不到变体 {focused} 或 {baseline}（改名请把这张表一起改，别让它静默少测一类控件）");
                continue;
            }
            var plain = RenderForDiff(makePlain);
            var hot = RenderForDiff(makeFocused);
            if (plain.W != hot.W || plain.H != hot.H)
            {
                bad.Add($"{control}：聚焦把窗口从 {plain.W}x{plain.H} 撑成 {hot.W}x{hot.H}，" +
                        "像素差对不齐——探针必须只动焦点，别动选中/展开（那会换掉整块内容）");
                continue;
            }
            int changed = CountDifferentPixels(plain.Pixels, hot.Pixels);
            if (changed < FocusChangedPixelsMin)
                bad.Add($"{control}：聚焦与不聚焦只差 {changed} 像素（下限 {FocusChangedPixelsMin}）——键盘用户看不见焦点在哪儿");
        }
        if (bad.Count > 0)
            throw new InvalidOperationException("焦点可见性判据失败：\n  " + string.Join("\n  ", bad));
    }

    private (int W, int H, byte[] Pixels) RenderForDiff(Func<Window> make)
    {
        var w = make();
        try
        {
            Park(w);
            w.Show();
            PumpToLoaded();
            w.UpdateLayout();
            int pw = (int)Math.Ceiling(w.ActualWidth), ph = (int)Math.Ceiling(w.ActualHeight);
            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(w);
            var px = new byte[pw * ph * 4];
            rtb.CopyPixels(px, pw * 4, 0);
            return (pw, ph, px);
        }
        finally { try { w.Close(); } catch { } }
    }

    // 逐字节比不成立：Pbgra32 里预乘的 alpha 会让「同一个不透明像素」在两次渲染间字节不同。
    // 比 RGB 三通道，且要求差得出来（>8/255）才算变——抗锯齿的轻微抖动不该被算成焦点环。
    private static int CountDifferentPixels(byte[] a, byte[] b)
    {
        int changed = 0;
        for (int i = 0; i + 3 < a.Length && i + 3 < b.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) > 8 || Math.Abs(a[i + 1] - b[i + 1]) > 8 || Math.Abs(a[i + 2] - b[i + 2]) > 8)
                changed++;
        return changed;
    }

    private static (string Name, Func<Window> Make)[] AllXamlWindows()
    {
        // **先把本进程的窗口幽灵化关掉，再谈笔迹窗。**
        //
        // 这张表里有一项（GestureTrail）造的是 GestureTrailWindow——**铺满整块屏、置顶**的
        // 透明覆盖窗，而它在这条路上会真的 Show() 到**活桌面**上（见 Views/GestureTrailWindow.cs
        // 的 ShowGated）。
        //
        // 幽灵化是进程级的单向开关：UI 线程一旦卡过 5 秒（HungWindowTimeout），DWM 就会在那个
        // 窗口的位置上盖一块**点得中**、且不继承 WS_EX_TRANSPARENT 的幽灵窗——于是整个桌面点不
        // 动，唯一的解法是杀掉 dwm.exe（见 Win32.DisableWindowGhosting 那段，以及用户报的原话
        // 「偶尔手势操作会造成无法点击，关闭桌面窗口管理器才正常」）。
        //
        // 生产路径早就防住了：Services/GestureCoordinator.cs 的 ApplyMouseHook 在建覆盖窗**之前**
        // 就翻了开关。**但开发期这条路不走 ApplyMouseHook**——App.OnStartup 见到
        // --smoke/--shots/--selftest 就 return 了，StartEngines 根本没跑。于是这里幽灵化是开着的，
        // 而它偏偏是唯一一条会把全屏覆盖窗摆到用户面前的路。
        //
        // 代价：进程级、单向、一次性（本进程从此不再有「未响应」外壳可拖可关）。对一个跑完就退的
        // 开发期进程来说等于没有代价——而省下的是「整个桌面点不动」。
        Native.Win32.DisableWindowGhosting();

        var groups = new[] { new ActionGroup() };

        // 面板管理器：默认配置只有一页两格，页眉、并排那三件要验的东西
        // 得靠再添一页格子更多的才拍得全。焦点探针必须与基线用同一份配置，
        // 否则成对像素差先被窗口尺寸差打断、根本量不到焦点环。
        PanelManagerWindow MakePanelManagerWithExtraPage()
        {
            var cfg = RootConfig.Default();
            var page = new PanelPage { Name = LongestOf("Tab_") };
            page.Steps.Add(new LaunchStep { Kind = "app", Label = "notepad", Target = @"C:\Windows\System32\notepad.exe" });
            page.Steps.Add(new LaunchStep { Kind = "system", Label = LongestOf("Sys_"), Command = "lockScreen" });
            page.Steps.Add(new LaunchStep { Kind = "volume", Label = LongestOf("Vol_"), VolumeAction = "mute" });
            cfg.PanelPages.Add(page);
            return new PanelManagerWindow(cfg, () => { });
        }

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
            // 端口页的空态叠行只在「筛到零行」那一刻出现，而机器上有没有端口拍不到第二次机会——
            // 拿一个永远搜不出结果的词填进搜索，任何一轮真机扫描落回后都是零行、分母不为零，
            // 提示这条路每轮矩阵都实实在在被拍到，而不是随端口有无闪断。
            ("MainWindowPortsHidden", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var w = new MainWindow(cfg, () => { });
                w.Tabs.SelectedItem = w.TabPorts;
                w.PSearch.Text = "zzz-no-such-port";
                return w;
            }),
            ("PanelManager", MakePanelManagerWithExtraPage),
            // 正好装满一屏（28 格）的样子：**下面那一块不该出现，卡片也不该多出一行**。
            // 这一档曾经正是那行假空位的现场——补位公式总多补一整行，
            // 于是「每屏 7 行 / 上带 3 行」写着下带 4，卡片却画着 5 行。
            ("PanelManagerExactFit", () =>
            {
                var cfg = RootConfig.Default();
                cfg.PanelPages.Clear();
                var full = new PanelPage { Name = LongestOf("Menu_") };
                for (int i = 0; i < 28; i++)
                    full.Steps.Add(new LaunchStep { Kind = "system", Label = LongestOf("Sys_"), Command = "showDesktop" });
                cfg.PanelPages.Add(full);
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 行数超过这块屏放得下的行数：面板管理器要**把实情说出来**（见 ScreenRowsNote）。
            // 设 8 + 8 行 = 16 行，任何常见屏幕都放不下，那句话必然出现——
            // 而这句话此前**一次都没被渲染过**（默认的 3 + 4 大多数屏幕放得下）。
            ("PanelManagerOverRows", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.PanelTopRows = 8;
                cfg.Settings.PanelBottomRows = 8;
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 一页装得比第一屏多的样子：预览里的第三块（落在第一屏之外的那些格子）
            // 与它前面那行「面板里往下滚才看得到」的说明。
            // **这一块此前一次都没被渲染过**——默认配置的页只有两三格，
            // 而「一屏之外还有东西」这个状态，只有格子多过一屏才存在。
            ("PanelManagerFullPage", () =>
            {
                var cfg = RootConfig.Default();
                cfg.PanelPages.Clear();
                var full = new PanelPage { Name = LongestOf("Menu_") };
                for (int i = 0; i < 40; i++)
                    full.Steps.Add(new LaunchStep { Kind = "system", Label = LongestOf("Sys_"), Command = "showDesktop" });
                cfg.PanelPages.Add(full);
                return new PanelManagerWindow(cfg, () => { });
            }),
            // 面板两条导航栏的焦点探针：它们的模板原先只有悬停，Tab 过去整行像素不动。
            ("FocusProbeTopTab", () =>
            {
                var w = MakePanelManagerWithExtraPage();
                w.Loaded += (_, _) =>
                {
                    var tab = w.TopTabBar.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault()
                        ?? throw new InvalidOperationException("focus probe: PanelManager 没有分类页签");
                    FocusOrThrow(tab, "PanelTopTab");
                };
                return w;
            }),
            ("FocusProbePageTab", () =>
            {
                var w = MakePanelManagerWithExtraPage();
                w.Loaded += (_, _) =>
                {
                    var tab = w.PageRail.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault()
                        ?? throw new InvalidOperationException("focus probe: PanelManager 没有页签");
                    FocusOrThrow(tab, "PanelPageTab");
                };
                return w;
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
            // 有点击统计的管理器：「清空点击统计」按钮与格子右下角的计数角标只在这一刻出现，
            // 而 2026-09-28 用户实报底部重叠正是这颗按钮和指路文案同列造成的——
            // 没有这一张，截图矩阵永远截不到那条有数据才现形的路。
            ("PanelManagerUsed", () =>
            {
                var cfg = RootConfig.Default();
                var usage = new Dictionary<string, PanelUsageStore.TileUsage>();
                foreach (var s in cfg.PanelPages.SelectMany(p => p.Steps).Take(4))
                    usage[s.Id] = new PanelUsageStore.TileUsage { Clicks = 17, LastUsedUnixMs = 1 };
                return new PanelManagerWindow(cfg, () => { }, usage, () => { });
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
            // 最常见的「加一个程序」：高级字段应当收起，只留名称+目标。
            ("StepEditorAppBasic", () => new StepEditorWindow(
                new LaunchStep { Kind = "app", Label = "记事本", Target = "notepad.exe" }, groups)),
            // 配过高级字段的同一型：折叠区应自动展开，已有配置不能被藏没。
            ("StepEditorAppAdvanced", () => new StepEditorWindow(
                new LaunchStep { Kind = "app", Label = "记事本", Target = "notepad.exe", Args = "--safe-mode", Elevated = true, Note = "示例" }, groups)),
            // 「发送按键」步骤：PanKeys 此前在截图矩阵里从未出现过，而它挂着全项目最长、
            // 拉丁键名最密的提示（Ed_KeysHint）——RTL 下最容易把这种混排打乱，不截等于没见过。
            ("StepEditorKeys", () => new StepEditorWindow(
                new LaunchStep { Kind = "keys", SendKey = "Ctrl+Alt+C" }, groups)),
            // 「运行动作组」步骤：组下拉与它下面那行内容预览只在这一种类型下出现，
            // 空步骤的编辑器截不到——而这正是「选择现有动作组」这条路的界面。
            // 「按键」与「路径」两型共用同一个 { } 变量按钮（RTL 下要 FlowDirection=LeftToRight 才不镜像成 } {），
            // 而 path 面板此前同样不在矩阵里——顺带把条件行（IfPathExists）也拍上。
            ("StepEditorPath", () => new StepEditorWindow(
                new LaunchStep { Kind = "path", Target = @"D:\Reports\daily.xlsx", IfPathExists = @"D:\Reports" }, groups)),
            ("StepEditorGroup", () =>
            {
                // 用默认配置里的真实组：harness 那个 groups 是个空名占位，截出来看不出选择器长什么样，
                // 更看不出下面那行内容预览有没有在工作。
                var real = RootConfig.Default().ActionGroups;
                return new StepEditorWindow(new LaunchStep { Kind = "group", GroupId = real[0].Id }, real);
            }),
            // 「运行启动项」步骤（面板语境）：目标下拉只在拿到清单时出现，不截一次等于没见过——
            // 与 StepEditorGroup 同一个理由（2026-09 新功能的界面从未被渲染过）。
            ("StepEditorRef", () =>
            {
                var items = RootConfig.Default().LaunchSteps;
                return new StepEditorWindow(new LaunchStep { Kind = "step", RefId = items[0].Id, Label = items[0].Label },
                                            new List<ActionGroup>(), items);
            }),
            // 条件区展开、且「仅 N 前」真的勾上：这一行的三个部件（复选框 + 时刻框 + 尾缀标签）
            // 是拼成句子的，各语言语序不同（中文「仅 [08:00] 前执行」、英文把整句放进复选框、
            // 韩文尾缀只有「실행」）。收起态与不勾的变体都拍不到这三个字，而它们正是逐语言复核
            // 改过的地方（ko 的两个复选框原本都只写「단」）。
            ("StepEditorCondition", () => new StepEditorWindow(
                new LaunchStep { Kind = "app", Target = "notepad.exe", OnlyBefore8 = true, BeforeHour = 8,
                                 Days = new List<int> { 1, 2, 3, 4, 5 } }, groups)),
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
            // HUD 两句：动作名小字与未匹配药丸。样式 accent 左条只在真窗里出现，
            // 不走一次渲染就没人见过它和笔迹是不是同一套语言。
            ("GestureTrailHint", () =>
            {
                var w = new GestureTrailWindow();
                w.ApplyLook("normal", "classic", false);
                var hit = new LaunchStep { Kind = "keys", Label = "Copy", Combo = "Ctrl+C" };
                foreach (var (x, y) in new[] { (300, 200), (330, 190), (360, 200), (390, 220), (420, 260) })
                    w.Point(x, y, armed: x == 420, hit: x == 420 ? hit : null);
                return w;
            }),
            ("GestureTrailNote", () =>
            {
                var w = new GestureTrailWindow();
                w.ApplyLook("normal", "classic", false);
                foreach (var (x, y) in new[] { (300, 200), (330, 190), (360, 200), (390, 220), (420, 260) })
                    w.Point(x, y);
                w.Note("↑↓", 60000);
                return w;
            }),
            // **五种笔迹样式并排**：这是「用户能挑什么」的那份目录的画出来的样子。
            // 单独给一格是因为——同一条线换个画法，五种摆在一起才比得出高低；
            // 而「比得出高低」这件事，截图是唯一说得清的判据（代码里只有层数与倍数，
            // 好不好看它不知道）。
            // 每种都画两遍：浅底上（描边那一层有没有起作用）与深底上（辉光那一层好不好看）。
            ("GestureTrailStyles", () =>
            {
                var w = new System.Windows.Window
                {
                    Width = 900,
                    Height = 300,
                    Background = System.Windows.Media.Brushes.Black,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                };
                var row = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                };
                foreach (var style in GestureTrailStyles.All)
                {
                    foreach (bool light in new[] { true, false })
                    {
                        var box = new System.Windows.Controls.Border
                        {
                            Width = 88,
                            Height = 132,
                            Margin = new System.Windows.Thickness(2),
                            Background = light
                                ? (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xEE, 0xE1))
                                : (System.Windows.Media.Brush)new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1E, 0x22)),
                            ClipToBounds = true,
                        };
                        var renderer = new GestureTrailRenderer(
                            style, GestureTrailWidths.Resolve("normal"),
                            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x75, 0x9C, 0xD7)),
                            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0xEF, 0xFF)),
                            fade: false);
                        for (int i = 0; i <= 40; i++)
                        {
                            double t = i / 40.0;
                            renderer.Add(new System.Windows.Point(10 + t * 68, 100 - System.Math.Sin(t * System.Math.PI) * 70));
                        }
                        box.Child = renderer.Surface;
                        row.Children.Add(box);
                    }
                }
                w.Content = row;
                return w;
            }),
            // 手势设置那扇窗：灵敏度、笔迹的样子、尾迹淡出、动作名、名单。
            // 它是手势管理器里那颗「手势设置…」按钮打开的那一扇——新界面从不出现在
            // 截图矩阵里，就等于没人见过它长什么样。
            ("GestureSettings", () => new GestureSettingsWindow(RootConfig.Default(), () => { })),
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
            ("StepEditorWaitClipboard", () => new StepEditorWindow(new LaunchStep { Kind = "waitClipboard", WaitSeconds = 5 }, groups)),
            // 「交互式流程」那批新面板。这几个的面板此前**一次都没被渲染过**，而这一档最容易出问题：
            // 面板本身就是它那一节的全部版面（没有「主行 + 副行」的层次可借），塌了只会表现为
            // 编辑器里一小块空白或一行挤在一起的字，而那只有看图才知道——Build 绿、smoke 绿都盖不住，
            // 因为 smoke 构造的是空步骤（Kind=""），那种情况下任何面板都不画。
            ("StepEditorList", () => new StepEditorWindow(
                new LaunchStep { Kind = "list", Message = LongestOf("Ed_"), OutputVar = "行" }, groups)),
            ("StepEditorJoin", () => new StepEditorWindow(
                new LaunchStep { Kind = "join", Text = "结果", Separator = ",", OutputVar = "串" }, groups)),
            // 「每个」是这个编辑器里最高的一档：三个输入框 + 一段说明 + 底下的「输出到变量」行。
            // 最高的一档恰恰最该拍——它塌了的时候界面看着还不像坏了，只是矮了一截。
            ("StepEditorEach", () => new StepEditorWindow(
                new LaunchStep { Kind = "each", Text = "行" }, groups)),
            ("StepEditorSiteMap", () => new StepEditorWindow(
                new LaunchStep { Kind = "siteMap", Target = "https://newzone.top/blog.html", SiteMapMode = "reveal" }, groups)),
            ("StepEditorWaitKey", () => new StepEditorWindow(
                new LaunchStep { Kind = "waitKey", Text = "Insert,Escape", WaitKeySeconds = 0, OutputVar = "按键" }, groups)),

            // ── 剩下那 9 种类型的编辑器面板，此前**一次都没有被渲染过** ──
            //
            // 补它们的理由和上面那批一样，但更硬：**这 9 种每一种在 StepEditorWindow 里都有
            // 自己的专属面板**（PanMouse / PanText / PanVolume / PanBrowserUrl / PanMessage /
            // PanReplace / PanUrlDecode / PanSplitLines），而每一种的版式都各不相同——
            // 一行、两行、带下拉、带开关、带联动行。「没有截图」在这种面板上不是「没验过」，
            // 是「面板塌了只有看图才知道」：Build 绿、smoke 绿都盖不住，因为 smoke 构造的是
            // Kind="" 的空步骤，那种情况下任何面板都不画。
            //
            // 样例值一律填**有内容的**，不留空：空面板和「这一格该有值却没画出来」在图上一样，
            // 只有填了值才分得开。占位符（{clipboard}）也照样填——它最容易被编辑器当普通文字吃掉，
            // 而那正是要拍的东西。
            //
            // 逐条说明为什么这一种值得单独占一档：
            //   mouse   一行：一个下拉 + 一句说明，那句说明是最长的 Ed_ 串之一。
            //   text    两行：正文 + 目标程序。
            //   volume  **联动**：音量档那一行只在动作是「设为」时才出现（VolLevelRow），
            //           是这个编辑器里少见的「一行会凭空多出来」的交互，必须拍。
            //   browserUrl  **没有自己的字段**：面板里只有一句说明，值全靠「输出到变量」。
            //           「一个只有说明、没有输入框的面板」塌了就是一片空白，最难自查。
            //   message 三段 + 联动：提示方式（弹窗/卡片）决定后面两行是否出现，另有朗读、
            //           自动关闭、确认后做什么三行——这是全编辑器最长的一档。
            //   replace / urlDecode / splitLines  这一族在 BlockOnlyKinds 里（只在复杂动作里出现），
            //           所以要传 insideGroup: true——否则拍出来的是「面板语境」的窄下拉，
            //           与用户实际在复杂动作里看到的不是同一个东西。
            //   delay   **没有专属面板**：它复用每一步都有的「运行后延时」框，运行期也是靠
            //           那个统一字段（StepRunner 的 case "delay"）。恰恰因为没有专属面板，
            //           这一档唯一能拍的就是「它确实没有多出来的东西」。
            ("StepEditorMouse", () => new StepEditorWindow(
                new LaunchStep { Kind = "mouse", MouseAction = "rightClick" }, groups)),
            ("StepEditorText", () => new StepEditorWindow(
                new LaunchStep { Kind = "text", Text = "看这个：{clipboard}", Process = "notepad" }, groups)),
            ("StepEditorVolume", () => new StepEditorWindow(
                new LaunchStep { Kind = "volume", VolumeAction = "set", VolumeLevel = 70 }, groups)),
            ("StepEditorBrowserUrl", () => new StepEditorWindow(
                new LaunchStep { Kind = "browserUrl", OutputVar = "网址" }, groups)),
            // OnYes.Type 的合法值只有 none / run / url（StepEditorWindow 里那张 FillCombo 是唯一出处）。
            // 这里取 url：一来「点了是之后打开某个地址」是这一族里最常被配的一种，
            // 二来只有非 none 才会把目标输入框那一行放出来（UpdateMessageRows 判的是 != "none"），
            // 于是这一档能同时拍到「下拉选中」与「目标框出现」两件事。
            ("StepEditorMessage", () => new StepEditorWindow(
                new LaunchStep
                {
                    Kind = "message", Message = "这批图导完了吗？", Speak = true, Confirm = true,
                    PopupSeconds = 8,
                    OnYes = new OnYes { Type = "url", Target = @"D:\Reports\done.txt" },
                }, groups)),
            ("StepEditorReplace", () => new StepEditorWindow(
                new LaunchStep { Kind = "replace", Text = "{clipboard}", Pattern = @"\s+", Replace = " ", OutputVar = "行" },
                groups, null, insideGroup: true)),
            ("StepEditorUrlDecode", () => new StepEditorWindow(
                new LaunchStep { Kind = "urlDecode", Text = "a%20b%2Bc%3Dd", OutputVar = "解码" },
                groups, null, insideGroup: true)),
            ("StepEditorSplitLines", () => new StepEditorWindow(
                new LaunchStep { Kind = "splitLines", Text = "{clipboard}", OutputVar = "行" },
                groups, null, insideGroup: true)),
            ("StepEditorDelay", () => new StepEditorWindow(
                new LaunchStep { Kind = "delay", DelayMs = 2000 }, groups)),

            ("StepEditorSkip", () => new StepEditorWindow(
                new LaunchStep { Kind = "skip", SkipMode = "block" }, groups)),
            ("StepEditorMath", () => new StepEditorWindow(
                new LaunchStep { Kind = "math", Text = "{index}+1", OutputVar = "n" }, groups)),
            ("StepEditorRegex", () => new StepEditorWindow(
                new LaunchStep { Kind = "regex", Text = "{关键词}", Pattern = "^(\\w+)", OutputVar = "词" }, groups)),
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
                new LaunchStep { Kind = "window", WindowAction = "restore" }, groups)),
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
            // ── 焦点探针（取证用）：把键盘焦点分别放到 CheckBox / ComboBox / TabItem /
            // ListBoxItem / DataGridCell 上，与不聚焦的同窗口变体做像素差。差不出来＝键盘用户
            // 看不见焦点在哪儿。每个探针都要求焦点**确实落到了**目标上（FocusOrThrow）——
            // 否则「没变化」可能只是没聚焦，这条负证据就不成立。
            ("FocusProbeChk", () =>
            {
                var w = new GroupEditorWindow(new ActionGroup(), groups, NoFunctionHotkeys);
                w.Loaded += (_, _) => FocusOrThrow(w.ShowInTrayChk, "CheckBox");
                return w;
            }),
            ("FocusProbeCombo", () =>
            {
                var w = new StepEditorWindow(new LaunchStep(), groups);
                w.Loaded += (_, _) => FocusOrThrow(w.KindCombo, "ComboBox");
                return w;
            }),
            ("FocusProbeTab", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var w = new MainWindow(cfg, () => { });
                w.Loaded += (_, _) =>
                {
                    // 聚焦**当前已选中的**页签：换成别的页签会连带换页，像素差里全是内容变化，
                    // 量不到「焦点框有没有画」。
                    if (w.Tabs.ItemContainerGenerator.ContainerFromIndex(w.Tabs.SelectedIndex) is System.Windows.Controls.TabItem ti)
                        FocusOrThrow(ti, "TabItem");
                };
                return w;
            }),
            ("FocusProbeList", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                // 与 GestureManager 变体同一份数据：不一致的话两边列表行数不同、窗口高度不同，
                // 像素差对不齐。
                cfg.Gestures.Add(new LaunchStep { Kind = "system", Command = "lockScreen", Gesture = "R3D" });
                if (cfg.ActionGroups.Count > 0)
                    cfg.Gestures.Add(new LaunchStep { Kind = "group", GroupId = cfg.ActionGroups[0].Id, Label = cfg.ActionGroups[0].Name, Gesture = "1L7" });
                var w = new GestureManagerWindow(cfg, () => { });
                w.Loaded += (_, _) =>
                {
                    // 只给焦点、不做选中：选中会让右栏换成该条目的编辑器，窗口高度变了，
                    // 像素差就没法和不聚焦的基线对齐了。
                    if (w.Bound.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.Controls.ListBoxItem lbi)
                        FocusOrThrow(lbi, "ListBoxItem");
                };
                return w;
            }),
            ("FocusProbeCell", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Settings.Language = Languages.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
                var w = new MainWindow(cfg, () => { });
                w.Loaded += (_, _) =>
                {
                    w.GridLaunch.Focus();
                    if (w.GridLaunch.Items.Count == 0) return;
                    var row = w.GridLaunch.ItemContainerGenerator.ContainerFromIndex(0) as System.Windows.Controls.DataGridRow;
                    var cell = row == null ? null : w.GridLaunch.Columns[1].GetCellContent(row)?.Parent as System.Windows.Controls.DataGridCell;
                    if (cell != null) FocusOrThrow(cell, "DataGridCell");
                };
                return w;
            }),
            ("ReminderEditor", () => new ReminderEditorWindow(new Reminder(), groups)),
            // 新增提醒的起手值：一小时后的“仅一次”，周期行显示 OnceRow 而不是 DaysRow。
            ("ReminderEditorNew", () => new ReminderEditorWindow(
                ReminderEditorWindow.NewReminderSeed(new DateTime(2026, 10, 25, 8, 0, 0)), groups)),
            // 展开「高级」的提醒编辑器：催促（HH:mm，…）/循环（HH:mm，…）/每 N 天锚点
            // (yyyy-MM-dd；…) 三条带日期的提示全藏在折叠的 Expander 里，收起态的矩阵从未渲染过它们。
            ("ReminderEditorAdv", () =>
            {
                var w = new ReminderEditorWindow(
                    new Reminder { RecurType = "everyNDays", RepeatMinutes = 10, IntervalMinutes = 30 }, groups);
                w.Loaded += (_, _) => w.AdvExp.IsExpanded = true;
                return w;
            }),
            // 「登录时机」这一档只有 Trigger=startup 才出现，而默认 Reminder 是 time——
            // 也就是说那个下拉在此之前一张图都没有。它的三个条目刚从「Before N:00」
            // 改成本地语言的「仅指定小时前」（最长的法语 32 字），没有这一变体就没人知道
            // 它装不装得下。
            ("ReminderEditorLogin", () => new ReminderEditorWindow(
                new Reminder { Trigger = "startup", StartupHourMode = "before", StartupWithinMinutes = 30 }, groups)),
            ("GroupEditor", () => new GroupEditorWindow(new ActionGroup(), groups, NoFunctionHotkeys)),
            // 三列编辑器里**有东西**的那一态。空组拍不出两件事：
            //   · 右侧变量面板是空的（它列的是各步骤的 OutputVar，空组一列都没有）；
            //   · 左侧工具箱与中间步骤表之间看不出层级——缩进、块循环、以及一条
            //     正在被选中的步骤。工具箱那一列本身在任何一态都在，但「变量/外观」
            //     与块缩进只有这一档拍得见。
            ("GroupEditorFilled", () => new GroupEditorWindow(new ActionGroup
            {
                Name = Strings.Get("Tpl_TypeLines"),
                // **按稳定 key 取，不是按本地化名。** 原来写的是
                // `All().First(g => g.Name == Strings.Get("Tpl_TypeLines"))`，于是：
                //   · Name 是 18 语本地化字符串，拿它当身份迟早对不上；
                //   · 更要命的是 Templates 是 **static readonly**，名字在类型首次初始化那一刻
                //     就按当时的文化冻结了，而截图循环每种语言都会切 CurrentUICulture——
                //     两边的语言一旦对不上，First() 直接抛 InvalidOperationException，
                //     整轮矩阵中途夭折（实测）。
                Steps = ActionGroupTemplates.BuiltIn("typeLines")?.Steps ?? new(),
            }, groups, NoFunctionHotkeys)),
            // 站点映射表：一条真站点（含端口的域名、两条改写规则）。空表只拍得到空态提示，
            // 而这一扇窗真正难排版的是右栏——四个字段加一张规则表，德语俄语下一列有多高。
            ("SiteMapEditor", () => new Views.SiteMapEditorWindow(new List<SiteEntry>
            {
                new SiteEntry
                {
                    Name = "LearnData",
                    Hosts = new() { "newzone.top", "www.newzone.top", "localhost:8080" },
                    SourceRoot = @"D:\Backup\Projects\LearnData\src",
                    IndexFile = "README.md",
                    PathRules = new()
                    {
                        new SitePathRule { Match = "^posts/", Replace = "_posts/" },
                        new SitePathRule { Match = "^blog\\.html$", Replace = "blog.md" },
                    },
                },
            })),
            // 模板管理器：十份模板，其中一份是**用户改过的**——「已修改」那颗角标与
            // 「恢复默认」那颗灰掉的按钮只有这一档拍得到。
            ("TemplateManager", () =>
            {
                var cfg = RootConfig.Default();
                cfg.Templates.Add(new TemplateOverride
                {
                    Key = "siteSource",
                    Group = ActionGroupTemplates.BuiltIn("siteSource")!,
                });
                return new Views.TemplateManagerWindow(cfg);
            }),
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
                cfg.Gestures.RemoveAll(s => !(s.Kind == "window" && s.WindowAction == "topmost"));
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
            // 删除确认那一档：按钮印的是动词而不是「是 / 否」，而动词比「是」长，
            // 两颗按钮加宽之后这张卡在最窄一档还站不站得住，只有画出来才知道。
            ("BrandDialogDelete", () => new BrandDialog(Strings.Get("Confirm_Title"),
                Strings.Lf("Confirm_DeleteItem", LongestOf("Kind_")), true, ToastLevel.Warn,
                Strings.Get("Btn_Delete"), Strings.Get("Confirm_Keep"))),
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
            // 动作多到装不下一屏时表圈右端才长出放大镜（见 BuildChrome 的阈值）。
            // 样例那 17 格过不了阈值，所以搜索这一角在截图里一直是空的——喂一份大的。
            ("QuickPanelSearchable", () => new QuickPanelWindow(BigPages(), SampleOps(), chromeOps: SampleWaist())),
            // 一页装不下一屏：格子往下排、在坑里滚，而不是被切成第二页。
            // 「切页」这件事被去掉之后，这一屏就是它唯一的验法。
            ("QuickPanelOverflow", () => new QuickPanelWindow(OverflowPages(), SampleOps(), chromeOps: SampleWaist())),
            // 搜索态本身：输入框 + 计数占满整幅宽度（它有自己的一条凹槽），结果铺进凹坑。
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
            // 手动重跑整份启动清单之前的那道确认框：它把「这一次会做哪几件事」摊开，
            // 而这一屏此前一次都没被渲染过——题干在十八种语言下长度差得很远，
            // 摊开的清单又有长有短、还带一条「另有 N 项」，只有画出来才看得见
            // 它会不会把那张卡撑破、按钮会不会掉出卡外。
            ("BrandDialogRerun", () =>
            {
                var cfg = RootConfig.Default();
                // 样例用真会跑的那几条（关窗口 / 发按键 / 置前）：默认清单那五条全是
                // 未勾选的样例，摊开是空的，而空的确认框验不到任何东西。
                cfg.LaunchSteps = new System.Collections.Generic.List<LaunchStep>
                {
                    new LaunchStep { Kind = "window", WindowAction = "close", Process = "PicGo.exe", Enabled = true },
                    new LaunchStep { Kind = "window", WindowAction = "close", Process = "QQ.exe", Enabled = true },
                    new LaunchStep { Kind = "window", WindowAction = "close", Process = "TIM.exe", Enabled = true },
                    new LaunchStep { Kind = "keys", Combo = "Win+6", Enabled = true },
                    new LaunchStep { Kind = "keys", Combo = "Alt+K", Enabled = true },
                    new LaunchStep { Kind = "window", WindowAction = "topmost", Process = "Weixin.exe", Enabled = true },
                    // 第七条故意长一点：清单里的条目有长有短，短的那几条永远验不到换行。
                    new LaunchStep { Kind = "app", Target = "C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal_8wekyb3d8bbwe\\wt.exe", Enabled = true },
                };
                var (total, lines, more) = LaunchPreview.Build(cfg, DateTime.Now);
                return new Views.BrandDialog(Strings.Get("Confirm_Title"), LaunchPreview.Message(lines, more),
                    true, Views.ToastLevel.Warn, Strings.Get("Tray_Rerun"), Strings.Get("Ed_Cancel"));
            }),
        };
    }

    // 样例页与自带操作。抽成方法而不是字段：每个档位要一份新的实例
    // （WPF 元素只能有一个父级，同一批 PanelTile 记录倒是可以共享，但页记录里带着回调，
    //  各档位共用一份也无妨——真正的原因是字段初始化时 Strings 还没定语言）。
    private static (IReadOnlyList<PanelTilePage> Pages, IReadOnlyList<PanelTile> Ops) PanelSample()
    {
        var pages = new[]
        {
                    // 20 格：默认版面（4 列 × 每屏 7 行 = 上 3 + 下 4）下上带正好铺满 12 格、
                    // 余 8 格落到下带，两条带和中间那道缝才都拍得到。少于 13 格截图里看不到分带。
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
                        // 补齐剩下六个类型的字形：这张合影就是 PanelGlyph 的逐格验收图，
                        // 漏一个类型，它挑错码位时就只有用户先看得见。
                        new PanelTile(Strings.Get("Kind_url"), PanelGlyph.ForKind("url"), () => { }),
                        new PanelTile(Strings.Get("Kind_path"), PanelGlyph.ForKind("path"), () => { }),
                        new PanelTile(Strings.Get("Kind_copySelection"), PanelGlyph.ForKind("copySelection"), () => { }),
                        new PanelTile(Strings.Get("Kind_waitClipboard"), PanelGlyph.ForKind("waitClipboard"), () => { }),
                        new PanelTile(Strings.Get("Kind_prompt"), PanelGlyph.ForKind("prompt"), () => { }),
                        new PanelTile(Strings.Get("Kind_choice"), PanelGlyph.ForKind("choice"), () => { }),
                    }, OnAdd: () => { }),
                    // 带 OnAdd：这一页能加东西，面板表圈右端那颗「+」才会亮。
                    // **没有虚位格了**——它曾经在格子阵末尾占一格位置，一页装满就变成多出来的
                    // 第 29 格（那一段经过见 PanelCoordinator.BuildPanelOps）。
                    new PanelTilePage(Strings.Get("Tab_Launch"), new[]
                    {
                        new PanelTile(LongestOf("Win_"), PanelGlyph.ForKind("mouse"), () => { }),
                        // 一枚品牌轮廓混在线描格子里：这张合影就是「彩色品牌图标和线描字形
                        // 并排站时齐不齐」的验收图（回退字形挂在第三个参数上，认不出名字时画它）。
                        new PanelTile("WeChat", new PanelIconSpec(PanelIconKind.Brand, "wechat", PanelGlyph.ForKind("app")), () => { }),
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
            pages.Add(new PanelTilePage(Strings.Get(title), tiles, null, side));
        }
        return pages;
    }

    // 十四条页签、同一叠：撑高面板的那种配置。
    // 分节名取自 StepKindSections 里真实在用的那几个（Menu_SecFlow 已随分节重构删掉，
    // 这里跟着换成了 Menu_SecCtrl —— 留着一条没人引用的文案键，早晚会有人照着它去改错地方）。
    private static IReadOnlyList<PanelTilePage> LongRailPages()
    {
        string[] keys = { "Tab_Launch", "Tab_Group", "Tab_Reminder", "Tab_Settings", "Tab_System", "Tab_Ports",
                          "Menu_SecUI", "Menu_SecClip", "Menu_SecText", "Menu_SecCtrl",
                          "Panel_Global", "Panel_Uncategorized", "Kind_app", "Kind_keys" };
        return keys.Select(k => new PanelTilePage(
            Strings.Get(k),
            Enumerable.Range(0, 4).Select(_ => new PanelTile(Strings.Get("Sys_showDesktop"),
                PanelIcon.Resolve(null, "system"), () => { })).ToList(),
            null, "")).ToList();
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

    // 一页装不下一屏的样例：40 格（默认版面一屏 28 格）。
    // 面板不再按容量把它切成两屏——格子排在下面、在坑里往下滚，
    // 而可见高度仍是设置里那个「每屏行数」（默认 7 行 = 上带 3 + 下带 4）。这一张是那件事唯一能验的图：
    // 切页的旧行为下这里该是「第 2 页」，现在该是同一页的第 4 行往下还有东西。
    private static IReadOnlyList<PanelTilePage> OverflowPages()
    {
        var tiles = new List<PanelTile>();
        for (int i = 0; i < 40; i++)
            tiles.Add(new PanelTile(LongestOf("Sys_", "Kind_"), PanelGlyph.ForKind("system"), () => { }));
        return new[] { new PanelTilePage(LongestOf("Tab_"), tiles, () => { }) };
    }

    // 表圈右端那几颗。截图里必须喂上，否则「找东西 / 管东西」那一角永远是空的。
    // 与 PanelCoordinator.BuildPanelWaistOps 一一对应。少喂一个的话，表圈右端在截图里就少一颗，
    // 而那正是要验的地方——它和「Esc 关闭」挤在同一行，多一颗少一颗的排布只有渲染看得见。
    private static IReadOnlyList<PanelTile> SampleWaist() => new[]
    {
        new PanelTile(Strings.Get("Gesture_Manager"), char.ConvertFromUtf32(0xE962), () => { }),
        // fillsEmptyPanel 要跟 PanelCoordinator.BuildPanelWaistOps 一致：空面板那颗行动按钮的显隐与文字全看它，
        // 这儿漏了标记，QuickPanelEmpty 那张图就会画出一块没有按钮的空面板——一张说谎的截图。
        new PanelTile(Strings.Get("Panel_Manager"), char.ConvertFromUtf32(0xE8A9), () => { }, fillsEmptyPanel: true),
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
            CheckFocusRings();
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
    // 顶层、各节子菜单和「常用」子菜单都要量：子菜单横着展开、不占顶层高度，
    // 但它自己一样会长过屏幕，而且那是加动作时唯一会变长的地方。
    //
    // 阈值 566 是渲染矩阵里这条菜单实际验证能站住的最小档（1366×768 @125% 的工作区减边距）；
    // 464 那档不作为必须装下的目标。子菜单与顶层同一档：父的都装不下时苛求子的没有意义。
    //
    // **必须量实际的菜单变体。** 面板/手势管理器的菜单会传 headerItems（「新建空白动作」），
    // 启动清单会传 firstOpenItem（「从开始菜单选择…」）且关掉 presets；只量不带参数的 Build
    // 说明不了真实菜单多出来的那一行。
    private void CheckStepMenuFits()
    {
        const double SmallestFittingWorkArea = 768 / 1.25 - 48;
        foreach (var culture in new string?[] { null, "de", "ru" })
        {
            Strings.ApplyCulture(culture);
            MeasureMenu(Views.StepMenu.Build((_, _) => { }), "step menu", culture, SmallestFittingWorkArea);
            var fromStart = new MenuItem { Header = Strings.Get("Menu_FromStartMenu") };
            MeasureMenu(Views.StepMenu.Build((_, _) => { }, firstOpenItem: fromStart, presets: false),
                        "startup step menu", culture, SmallestFittingWorkArea);
            var compound = new MenuItem { Header = Strings.Get("Panel_NewCompound") };
            MeasureMenu(Views.StepMenu.Build((_, _) => { }, headerItems: new[] { compound }, presets: true),
                        "step menu (header+presets)", culture, SmallestFittingWorkArea);
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
            // 子菜单与顶层同一档（566）：父的都装不下时苛求子的没有意义。
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
            // 定向重渲：全矩阵几千张，复验单条发现太慢。CW_SHOT_LANGS / CW_SHOT_WINDOWS
            // 给逗号分隔清单即只渲子集（窗口按名前缀匹配）。语言是**并集**：清单里能对上的
            // 用矩阵里的写法，对不上的（ja/ko 这类六档之外的高危语言）直接按给定 code 渲。
            // 早先的写法是「有匹配就丢掉没匹配的」，于是 de,ru,ar,hi 只出三档、hi 静默为零——
            // 少渲的语言不会报错，只会在报告里根本不出现。
            var langKeep = (Environment.GetEnvironmentVariable("CW_SHOT_LANGS") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (langKeep.Length > 0)
            {
                var picked = langs.Where(l => langKeep.Any(k => l.StartsWith(k, StringComparison.OrdinalIgnoreCase))).ToArray();
                var extra = langKeep.Where(k => !picked.Any(l => l.StartsWith(k, StringComparison.OrdinalIgnoreCase))).ToArray();
                langs = picked.Concat(extra).ToArray();
            }

            var winKeep = (Environment.GetEnvironmentVariable("CW_SHOT_WINDOWS") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var shotWindows = winKeep.Length == 0
                ? AllXamlWindows()
                : AllXamlWindows().Where(w => winKeep.Any(k => w.Name.StartsWith(k, StringComparison.OrdinalIgnoreCase))).ToArray();

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
                foreach (var (name, make) in shotWindows)
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
                foreach (var (name, make) in shotWindows)
                {
                    // 有些对话框自带宽度比矩阵最窄档还小（提醒编辑器 640 < 820）：矩阵测的
                    // 「最窄」其实比用户实际看到的更宽，窄态溢出永远抓不到。这类窗口按自身宽度补一档。
                    //
                    // **`make()` 不是纯构造，量完宽度必须把那扇窗关掉。** GestureTrail 那档的
                    // 5 个 Point() 里第 2 个就触发 ShowGated，把一扇满屏、置顶、点不着的覆盖窗
                    // 摆到了用户屏幕上；而覆盖窗不在这批截图的范围里，于是矩阵跑完照样「全绿」，
                    // 屏幕上却留着一扇窗——每种语言一扇（2026-10-17 报的就是这个：
                    // 一条蓝色 L 形轨迹赖在屏幕上不走，那是 (300,200)→(540,440) 那几个探针点）。
                    // 同一批 make() 的正式那一份在下面被 Close 掉（try 末尾一处、catch 里一处），
                    // 只有这次量宽度的临时那一份是漏的——它连 try 都没进。
                    var probe = make();
                    double native = probe.Width;
                    try { probe.Close(); } catch { }   // 关不掉也不能让整轮矩阵挂在这上面
                    var ws = native > 0 && native < widths[0] ? widths.Append(native).ToArray() : widths;
                    foreach (var wa in waHeights)
                        foreach (var ww in ws)
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
                                                                // 窗口自己的 MinWidth 优先，不提供它不允许的尺寸。
                                // 实现了 INaturalWidthShot 的窗口**跳过**这一句：那一类窗的自然宽度
                                // 本身就是它要展示的东西（面板管理器按内容定宽，SizeToContent 说了算），
                                // 被矩阵强行拉到 820/1040 之后，窗宽由最宽的那一行决定这件事就看不见了——
                                // 「卡片两侧一大片黑」正是这么漏出截图矩阵的。
                                if (w.MinWidth <= ww && w is not INaturalWidthShot) w.Width = ww;
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
