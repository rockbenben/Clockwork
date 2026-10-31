using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Services;
using Clockwork.Views;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这几个名字两边都有（同别处那几行）。
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using SystemCommands = Clockwork.Engine.SystemCommands;

namespace Clockwork;

// --selftest：**要一扇真窗、因而进不了 app.Tests 的那几条检查**，在真实进程里自动跑一遍。
//
// 分工是清楚的：纯逻辑归 app.Tests（两千多条），版面归 --shots，「每扇窗都打得开」归 --smoke。
// 剩下这一类没人管——**模态弹框**从外面没有第二条路能驱动它（ShowDialog 一进去就不返回），
// 于是「打字 → 点确定 → 答案进变量 → 后面的步骤用得上」这条链一直只能靠人手点一遍。
// 而它恰恰是用户唯一真正会碰的那一段：占位替换、变量表、编排全都有测试，
// 唯独把它们连起来的那扇窗没有。这个开关把那一遍手点变成自动的。
// 手动重跑整份清单之前那道确认也是这一类：它是唯一挡得住「重跑整份清单」的地方，
// 而它弹不弹得出来、点「否」之后闸放不放得回去，都只有真跑一遍才看得见。
//
// 驱动方式见 BrandDialog.Rehearse：弹框画出来之后按脚本操作它。等 700ms 再点是必须的——
// 那张卡有 600ms 的误触保护（它可能被一条热键或手势不请自来地弹出来），点早了不算数。
// 下面有一条专门验这个保护还在。
public partial class App
{
    private void RunSelfTest()
    {
        string marker = Path.Combine(Path.GetTempPath(), "clockwork-selftest.txt");
        var fails = new List<string>();
        var skips = new List<string>();
        int checks = 0;
        void Check(string what, bool ok, string got)
        {
            checks++;
            if (!ok) fails.Add($"{what}：{got}");
        }
        // **验不成的那一条**：环境不具备（如抢不到前台，没法往别的程序上注入键）。
        // 它不是失败——失败会让 CI 变红，而它压根不是程序的毛病；
        // 但也不能当成通过（那等于没验却说验过了），所以单开一档，写在账上。
        void Skip(string what, string why)
        {
            checks++;
            skips.Add($"{what}：{why}");
        }

        try
        {
            // ① 打一句进去、点确定 → 拿到那一句。
            var a = Ask("q", d => { d.AnswerBox.Text = "猫"; Click(d.YesBtn); });
            Check("用户输入·正常", a == "猫", $"得到 {Show(a)}");

            // ② 点取消 → **null，不是空串**。整套编排靠这一点区分「这组别做了」和「这项留空」。
            var b = Ask("q", d => Click(d.NoBtn), waitGuard: false);
            Check("用户输入·取消", b == null, $"得到 {Show(b)}");

            // ③ 空着点确定 → 空串。它不是取消：用户就是想留空这一项。
            var c = Ask("q", d => { d.AnswerBox.Text = ""; Click(d.YesBtn); }, initial: "预填");
            Check("用户输入·空答案", c == "", $"得到 {Show(c)}");

            // ④ 误触保护还在：600ms 内那一下不算数（这张卡可能被热键/手势不请自来地弹出来，
            //    而焦点默认就在确定上——在途的一次回车不该替用户把话答了）。
            //    先点一下、确认还开着，再等过保护点第二下。
            bool stillOpen = false;
            var d4 = Ask("q", d =>
            {
                Click(d.YesBtn);                                   // 早于 600ms，应当被吞掉
                After(d, 800, () => { stillOpen = d.IsVisible; d.AnswerBox.Text = "晚"; Click(d.YesBtn); });
            }, waitGuard: false);
            Check("用户输入·误触保护", stillOpen && d4 == "晚", $"保护住={stillOpen} 得到 {Show(d4)}");

            // ⑤ 选一项 → 拿到那一项（不是下标、不是别的那一项）。
            var opts = new[] { "上班", "下班", "午休" };
            var e = Pick("q", opts, d => { d.AnswerList.SelectedIndex = 1; Click(d.YesBtn); });
            Check("用户选择·正常", e == "下班", $"得到 {Show(e)}");

            // ⑥ 一项都没选就点确定 → **不放行**。放行等于把「我没选」和「我选了个空的」
            //    混成一件事，而后面的步骤拿那个空串去拼地址只会得到一个打不开的页面。
            bool blocked = false;
            var f = Pick("q", opts, d =>
            {
                d.AnswerList.SelectedIndex = -1;
                Click(d.YesBtn);                                   // 应当什么都不发生
                After(d, 300, () => { blocked = d.IsVisible; Click(d.NoBtn); });
            });
            Check("用户选择·没选中不放行", blocked && f == null, $"挡住={blocked} 得到 {Show(f)}");

            // ⑦ 端到端：问一句 → 答案进变量 → 后面那一步真的用上了它。
            //    走的是真的 ActionGroupRunner、真的 StepRunner、真的占位替换，
            //    只把落点换成剪贴板（而不是打开浏览器）——好让这一遍看得见结果又不打扰人。
            Check("端到端·答案流到下一步", EndToEnd(out var chain), chain);

            // ⑧ 手动重跑整份清单之前那道确认：它真的弹了吗？点「否」之后闸放回去了吗？
            //    闸卡在 1 的后果是「重跑清单」从此点不动——那种故障只有真跑一遍才看得见，
            //    而 app.Tests 罩不到模态框（问句那条链进不了测试，原因见文件头）。
            Check("重跑清单·确认与放行", RerunConfirm(out var relog), relog);

            // ⑨ 面板的按键：回车、空格、方向键、翻页键、Esc——**每一个都真按一遍**。
            //    那类缺陷代码里看不出来（case 都在、单测也绿，按下去没反应而已），
            //    所以只能真的按：投递 Windows 键消息走 WPF 输入管线，按完看效果。
            Check("面板按键", PanelKeyChecks.Run(out var keylog), keylog);

            // ⑩ 逐个功能：每一种步骤类型都过一遍（编辑器开不开得出来、引擎派不派发得了）。
            Check("逐功能自查", FuncSweep.Run(out var funclog), funclog);

            // ⑪ 面板之外的按键处理点：录键框 / 问句弹框 / 就地改名。
            Check("其它按键自查", KeySweep.Run(out var keysweep), keysweep);

            // ⑫ 面板的两条导航栏：顶栏点了另一个分类，左栏跟着换；两条栏各自写明
            //     自己装的是什么；管理器那颗「+」建出来的分类站得住。
            //     这一族缺陷单测罩不到（它们住在窗口的接线里，而单元测试进程里不能建
            //     WPF Application——见 DevCategoryNav 的文件头），所以在真进程里点一遍。
            Check("两条导航栏", CategoryNavChecks.Run(out var navlog), navlog);

            // ⑬ 发键：真的注入一次键，看**目标程序**收到的是不是用户写下的那个键。
            //     用户实报「发送 enter 还是无效」——修饰键还按着时，发出去的是
            //     Ctrl+Shift+Enter 而不是回车（机制与实测见 DevKeySend 的文件头）。
            //     这条只能在真进程里验：单测进程里不许往系统里注入（见 Win32.InTestHost），
            //     而「目标程序收到什么」本来就不是单测能问出来的问题。
            //     抢不到前台时它只能跳过（注入会打到用户正在用的程序上），不算失败。
            bool sendOk = KeySendChecks.Run(out var sendlog, out bool sendSkipped);
            if (sendSkipped) Skip("发键", sendlog);
            else Check("发键", sendOk, sendlog);
        }
        catch (Exception ex) { fails.Add("自查自己抛了：" + ex); }
        finally { BrandDialog.Rehearse = null; }

        File.WriteAllText(marker, fails.Count == 0
            ? $"OK {checks} checks"
                + (skips.Count == 0 ? "" : "\r\n（跳过 " + skips.Count + " 条：\r\n" + string.Join("\r\n", skips) + "）")
            : $"FAIL {fails.Count}/{checks}\r\n" + string.Join("\r\n", fails));
        Shutdown(fails.Count == 0 ? 0 : 1);
    }

    // 问一句，按脚本操作那扇框。waitGuard：等过 600ms 误触保护再跑脚本（点「确定」的都要等）。
    private static string? Ask(string q, Action<BrandDialog> script, string initial = "", bool waitGuard = true)
        => Drive(script, waitGuard, () => BrandDialog.Ask(null, "自查", q, initial));

    private static string? Pick(string q, IReadOnlyList<string> options, Action<BrandDialog> script, bool waitGuard = true)
        => Drive(script, waitGuard, () => BrandDialog.Pick(null, "自查", q, options));

    private static string? Drive(Action<BrandDialog> script, bool waitGuard, Func<string?> show)
    {
        BrandDialog.Rehearse = d =>
        {
            if (waitGuard) After(d, 700, () => script(d));
            else script(d);
        };
        try { return show(); }
        finally { BrandDialog.Rehearse = null; }
    }

    // 过 ms 毫秒之后做一件事。**必须走 DispatcherTimer 而不是 Sleep**：
    // 这会儿正卡在 ShowDialog 的模态消息循环里，睡的是那个循环本身——按钮永远收不到那一下点击。
    private static void After(DispatcherObject on, int ms, Action act)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, on.Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); act(); };
        t.Start();
    }

    // 点一个按钮。发的是真的 Click 路由事件，不是直接调处理器——误触保护、
    // 「没选中不放行」这些都长在处理器里，绕过去就等于没测。
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static string Show(string? s) => s == null ? "取消(null)" : $"「{s}」";

    // 手动重跑整份清单之前那道确认：弹出来了吗？点「否」之后还能再弹第二次吗？
    //
    // 第二次能再弹 = 闸确实放回去了。闸（_launchRunning）卡在 1 时，第二次调用会立刻返回、
    // 框不再出现——而那正是「重跑清单」从此点不动的故障（见 RunOrchestrator 里那处注释）。
    //
    // **用一份隔离的配置**：万一确认没弹出来（这正是要抓的回归），跑起来的也是这份只有一条
    // 延时的清单，而**不是用户真正的清单**——那里面有真的关窗口与发按键。
    // 日志也写到临时目录，不碰用户那份 clockwork.run.log。
    private bool RerunConfirm(out string log)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "clockwork-selftest-rerun");
        Directory.CreateDirectory(tmp);
        var cfg = RootConfig.Default();
        cfg.LaunchSteps = new List<LaunchStep> { new LaunchStep { Kind = "delay", DelayMs = 10, Enabled = true } };
        var runner = new RunOrchestrator(() => cfg, () => tmp, () => _exePath, new AppDiagnostics(() => tmp));

        int shown = 0;
        BrandDialog.Rehearse = d => { shown++; After(d, 700, () => Click(d.NoBtn)); };
        try
        {
            runner.RunLaunchAsync(false);
            runner.RunLaunchAsync(false);
        }
        finally { BrandDialog.Rehearse = null; }
        log = $"确认框弹了 {shown} 次（应为 2：一次证明弹得出来，一次证明闸放回去了）";
        return shown == 2;
    }

    // 一条真动作：用户输入 → 把答案写进剪贴板。跑完读剪贴板，看那一句有没有原样到达。
    // 用剪贴板当落点而不是「打开网址」：这一遍要能在 CI 上无人值守地跑，不能真弹出一个浏览器。
    private bool EndToEnd(out string log)
    {
        const string answer = "猫 狗";
        string? stepError = null;
        var ran = new List<string>();
        var vars = new RunVars();
        var g = new ActionGroup
        {
            Id = "selftest",
            Steps = new()
            {
                new LaunchStep { Kind = "prompt", Message = "问一句？", OutputVar = "关键词", DelayMs = 0 },
                new LaunchStep { Kind = "system", Command = "setClipboard", Text = "[{关键词}]", DelayMs = 0 },
            },
        };
        var selfPaths = new[] { _exePath };
        var deps = new GroupDeps
        {
            Vars = vars,
            // 走**真的** AskUser 链（App 门面 → RunOrchestrator）：提示语的占位替换、选项切分、跳 UI 线程都在那一层，
            // 绕开它就只测到了弹框自己。
            AskUser = s => Drive(d => After(d, 700, () => { d.AnswerBox.Text = answer; Click(d.YesBtn); }),
                                 waitGuard: false, () => AskUser(s, vars)),
            RunStep = s => { ran.Add(s.Kind); StepRunner.InvokeStepAction(s, _ => true, selfPaths, null, vars); },
            // 步骤抛了异常要说出来。不接的话，一次写剪贴板失败只会显示成「剪贴板=「」」——
            // 把「那一步炸了」说成了「值不对」，而这两件事该查的地方完全不同。
            OnStepError = (s, ex) => stepError = $"{s.Kind} 抛了 {ex.GetType().Name}: {ex.Message}",
        };
        ActionGroupRunner.RunGroup(g, deps);

        string clip = "";
        try { clip = SystemCommands.ClipboardText(); } catch { }
        log = $"变量={Show(vars.Get("关键词"))} 跑过的步骤=[{string.Join(",", ran)}] 剪贴板=「{clip}」"
              + (stepError == null ? "" : "  步骤异常：" + stepError);
        return vars.Get("关键词") == answer && clip == $"[{answer}]";
    }
}
