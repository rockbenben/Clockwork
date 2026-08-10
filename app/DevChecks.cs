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
//   --smoke        构造并布局每一个 XAML 窗口后退出。XAML 是懒加载的——写错的窗口不打开就不报错，
//                  能一路带到发版；这个开关在 CI 里替用户把每扇窗都开一遍。
//   --shots <目录>  把每个窗口按三档工作区高度 × 高危语言离屏渲染成 PNG 逐张比对。
//                  德语最长、阿语 RTL、小屏封顶的问题，中文 + 常用尺寸下一张都看不见。
//                  注意它的盲区：MaxHeight 是 harness 无条件设的，「窗口自己忘了运行时封顶」这类
//                  问题截图里永远正常，只能靠 review 抓（检查点：每个 SizeToContent 窗口都要有 FitToWorkArea）。
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
            ("ReminderPopup", () => new ReminderPopupWindow("smoke", confirm: true, autoDismissSeconds: 0)),
            ("Toast", () => new NotificationToast("smoke", "smoke", ToastLevel.Info, durationMs: 0)),
            ("BrandDialog", () => new BrandDialog(null, "smoke", confirm: true, ToastLevel.Info)),
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
    private void PumpToLoaded()
        => Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

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
            // 高危语言：中文基线 / 英文 / 德语（最长翻译）/ 阿语（RTL）。
            // RTL 不走 App 里的 OverrideMetadata（每类型只能调一次，逐语言循环会炸），逐窗口设 FlowDirection。
            var langs = new[] { "zh-CN", "en", "de", "ar" };

            int count = 0;
            var fails = new List<string>();
            foreach (var lang in langs)
            {
                Strings.ApplyCulture(lang);
                foreach (var (name, make) in AllXamlWindows())
                    foreach (var wa in waHeights)
                    {
                        Window? w = null;
                        try
                        {
                        Park(w = make());
                        w.FlowDirection = Strings.IsRightToLeft
                            ? System.Windows.FlowDirection.RightToLeft
                            : System.Windows.FlowDirection.LeftToRight;
                        w.Show();
                        w.MaxHeight = wa;   // 必须 Show 之后设，否则被 FitToWorkArea 按真实显示器算的值覆盖
                        PumpToLoaded();
                        w.UpdateLayout();
                        var rtb = new RenderTargetBitmap(
                            (int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight),
                            96, 96, PixelFormats.Pbgra32);
                        rtb.Render(w);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(rtb));
                        using (var fs = File.Create(Path.Combine(dir, $"{name}@{lang}@{(int)wa}.png")))
                            enc.Save(fs);
                        w.Close();
                        count++;
                        }
                        catch (Exception ex)
                        {
                            string diag = w == null ? "ctor" :
                                $"aw={w.ActualWidth} vis={w.IsVisible} loaded={w.IsLoaded} visprop={w.Visibility} " +
                                $"hwnd={new System.Windows.Interop.WindowInteropHelper(w).Handle}";
                            fails.Add($"{name}@{lang}@{(int)wa} [{diag}] {ex.GetType().Name}: {ex.Message}");
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
