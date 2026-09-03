using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Clockwork.Core;
using Clockwork.Engine;
using Clockwork.I18n;
using Clockwork.Views;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，这几个名字两边都有（同别处那几行）。
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using SystemCommands = Clockwork.Engine.SystemCommands;

namespace Clockwork;

// --selftest：**要一扇真窗、因而进不了 app.Tests 的那几条检查**，在真实进程里自动跑一遍。
//
// 分工是清楚的：纯逻辑归 app.Tests（1500 多条），版面归 --shots，「每扇窗都打得开」归 --smoke。
// 剩下这一类没人管——问句弹框是**模态**的，从外面没有第二条路能驱动它（ShowDialog 一进去就不返回），
// 于是「打字 → 点确定 → 答案进变量 → 后面的步骤用得上」这条链一直只能靠人手点一遍。
// 而它恰恰是用户唯一真正会碰的那一段：占位替换、变量表、编排全都有测试，
// 唯独把它们连起来的那扇窗没有。这个开关把那一遍手点变成自动的。
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
        void Check(string what, bool ok, string got)
        {
            if (!ok) fails.Add($"{what}：{got}");
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
        }
        catch (Exception ex) { fails.Add("自查自己抛了：" + ex); }
        finally { BrandDialog.Rehearse = null; }

        File.WriteAllText(marker, fails.Count == 0
            ? "OK 7 checks"
            : $"FAIL {fails.Count}/7\r\n" + string.Join("\r\n", fails));
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
            // 走**真的** App.AskUser：提示语的占位替换、选项切分、跳 UI 线程都在那一层，
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
