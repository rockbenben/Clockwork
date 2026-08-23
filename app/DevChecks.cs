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
//   --shots <目录>  离屏渲染成 PNG 逐张比对：每个窗口的每个 Tab × 2 档宽度 × 3 档工作区高度 × 6 种语言。
//                  这些轴都是被真实缺陷逃掋后一条条加上去的：只截首页 → 其余五页从未被渲染；
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
//                  注意两个盲区：
//                    · MaxHeight 是 harness 无条件设的，「窗口自己忘了运行时封顶」这类问题截图里永远正常，
//                      只能靠 review 抓（检查点：每个 SizeToContent 窗口都要有 FitToWorkArea）。
//                    · 异步填充的列（如系统启动项页的扫描）拍到的是未完成态，那是时序不是缺陷。
// 两个开关都挂在单实例检查之前：托盘里正在用的实例照常工作，检查进程自己开自己关，互不打扰。
// 成败判定以 marker 文件为准，不靠退出码——PowerShell 读 GUI 进程 ExitCode 有已知读空坑，
// 且 marker 顺带证明真跑到了「所有窗口布局完、走到写文件」那一步。
public partial class App
{
    // 推迟到消息循环转起来（ApplicationIdle）再跑，而不是在 OnStartup 里当场跑：
    // OnStartup 阶段 Application.Run 的循环还没起转，Show() 只把建 HWND/Loaded 的活排进队列，
    // 窗口 hwnd=0、ActualWidth=0（实测 84/84 全零）；就地 Dispatcher.Invoke 泵到 Loaded 档也救不回来。
    // OnStartup 返回、循环起转之后，Show() 才是教科书上的同步语义。
    private void RunDevCheck(string[] args)
        => Dispatcher.BeginInvoke(
            () => { if (args.Contains("--shots")) RunShots(args); else RunSmoke(); },
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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
            ("StepEditor", () => new StepEditorWindow(new LaunchStep(), groups)),
            ("ReminderEditor", () => new ReminderEditorWindow(new Reminder(), groups)),
            ("GroupEditor", () => new GroupEditorWindow(new ActionGroup(), groups, "F9")),
            // 喂真实的最长文案，不再喂 "smoke"：这三个窗口的尺寸完全由文案长度决定，
            // 喂一个五字占位串等于根本没测它们——带三个变量的确认文案在窄窗口下什么样，
            // 一直是空白。长度按当前语言现算，不维护人工清单（哪一条最长逐语言不同）。
            ("ReminderPopup", () => new ReminderPopupWindow(LongestOf("Msg_", "Reminder_", "Confirm_"), confirm: true, autoDismissSeconds: 0)),
            ("Toast", () => new NotificationToast("Clockwork", LongestOf("SysMsg_", "Warn_", "Err_"), ToastLevel.Info, durationMs: 0)),
            ("BrandDialog", () => new BrandDialog(null, LongestOf("Confirm_"), confirm: true, ToastLevel.Info)),
        };
    }

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
            File.WriteAllText(marker, "OK");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(marker, ex.ToString()); } catch { }
            Shutdown(1);
        }
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
            var waHeights = new[] { 768 / 1.25 - 48, 1080 / 1.5 - 48, 1080.0 - 48 };
            // 高危语言：中文基线 / 英文 / 德语 / 西语 / 俄语 / 阿语（RTL）。
            // “德语最长”只是平均意义上成立，逐条看并不是：端口页那两个复选框
            // （Solo servidores de desarrollo / Mostrar todos los puertos）西语比德语长一截，
            // 俄语在另一批串上同理。只跑德语就会把这类裁切放过去（实测已发生过一次）。
            // RTL 不走 App 里的 OverrideMetadata（每类型只能调一次，逐语言循环会炸），逐窗口设 FlowDirection。
            var langs = new[] { "zh-CN", "en", "de", "es", "ru", "ar" };
            // 宽度两档：MinWidth(820) 是用户能拖到的最窄处，1040 是默认开窗宽。
            // 此前只变高度、宽度永远是 1040，于是「长译文顶出右缘」这类问题全靠人肉发现。
            var widths = new[] { 820.0, 1040.0 };

            int count = 0;
            var fails = new List<string>();
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
