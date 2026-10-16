using System.Windows;
using Clockwork.I18n;

namespace Clockwork.Views;

// 品牌化模态对话框，替代原生 MessageBox。confirm=true → 是/否(返回 true=是)；否则 → 确定(返回 true=已确认)。
public partial class BrandDialog : Window
{
    public bool Result { get; private set; }

    /// <summary>问句形态下用户给的答案；取消或不是问句形态则为 null。</summary>
    public string? Answer { get; private set; }

    // 误触保护，同提醒弹窗：本框可能由后台动作组/破坏性命令确认突然弹出并夺走焦点，
    // 且默认焦点落在「是」。弹出后极短一段时间内只挡「是」，Esc/否/确定不受影响。
    // _shownAt 构造即上膛：等 ContentRendered 才上膛的话，激活到渲染之间的在途按键正落在无保护的「是」上。
    private const int GuardMs = 600;
    private readonly long _shownAt = Environment.TickCount64;
    private bool Guarded => Environment.TickCount64 - _shownAt < GuardMs;

    public BrandDialog(string? title, string message, bool confirm, ToastLevel level,
                       string? yesLabel = null, string? noLabel = null)
    {
        InitializeComponent();
        WindowSizing.FitToWorkArea(this);   // 崩溃提示的载体：消息长度不可控，小屏上也不能把是/否挤下屏
        var t = string.IsNullOrEmpty(title) ? "Clockwork" : title!;
        Title = t;   // 无可见标题栏，仅用于 alt-tab/辅助功能
        Eyebrow.Text = t;
        MsgText.Text = message ?? "";
        var accent = (System.Windows.Media.Brush)FindResource(level == ToastLevel.Warn ? "BrushDanger" : "BrushAccent");
        Rail.Background = accent;
        // 眉标平时退到钢灰（强调色交给轨），只有警示才让它跟着变 clay——升级信号要看得出来，不能人人都亮。
        Eyebrow.Foreground = level == ToastLevel.Warn ? accent : (System.Windows.Media.Brush)FindResource("BrushMuted");
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Result = false; DialogResult = true; } };   // 无关闭按钮 → Esc 取消
        YesBtn.Content = yesLabel ?? Strings.Get("Reminder_Popup_Yes");
        NoBtn.Content = noLabel ?? Strings.Get("Reminder_Popup_No");
        OkBtn.Content = Strings.Get("Reminder_Popup_Ok");
        YesBtn.Visibility = NoBtn.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        OkBtn.Visibility = confirm ? Visibility.Collapsed : Visibility.Visible;
        // 破坏性确认（删除 / 关机 / 覆盖导入）反转轻重：把「是」做成面板上最亮的强调色主按钮，等于让危险选项
        // 当视觉焦点——眼睛先被它拽走，键盘焦点却在「否」上，两边打架。这里让安全的一侧当主按钮，
        // 「是」退成 clay 文字＋clay 描边：看得见、够得着，但不再是这块面板上最响的东西。
        if (confirm && level == ToastLevel.Warn)
        {
            YesBtn.Style = (Style)FindResource("DangerButton");
            YesBtn.BorderBrush = accent;   // DangerButton 本身无描边(列表内联用)，对话框里给回一圈，仍读作按钮
            NoBtn.Style = (Style)FindResource("PrimaryButton");
        }
        // 初始焦点=「是」/「确定」：回车=确认是既有肌肉记忆，不改（误触由 600ms 保护挡，且从构造起算无空窗）。
        // **问句形态会再挂一个 ContentRendered 把焦点抢到输入框/列表上**（Ask / Pick）——
        // 后挂的后跑，所以那两档最终落在答案上。别把这里改成一次性赋值或调换挂载顺序。
        ContentRendered += (_, _) => (confirm ? YesBtn : OkBtn).Focus();
    }

    private void Yes_Click(object sender, RoutedEventArgs e) { if (Guarded) return; Confirm(); }
    private void No_Click(object sender, RoutedEventArgs e) { Result = false; DialogResult = true; }
    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    // 「确认」这一下：问句形态下还要收下答案。三个按钮里有两个走这条（是 / 确定），
    // 列表双击也走它——各写一份的话，双击那条早晚会漏掉收答案这一步。
    private void Confirm()
    {
        // 没选中任何一项时不放行：让一个「用户选择」返回空串，等于把「我没选」和
        // 「我选了个空的」混成一件事，而后面的步骤拿这个空串去拼地址，只会得到一个打不开的页面。
        if (AnswerBox.Visibility == Visibility.Visible) Answer = AnswerBox.Text;
        else if (AnswerList.Visibility == Visibility.Visible)
        {
            if (AnswerList.SelectedItem is not string pick) return;
            Answer = pick;
        }
        Result = true;
        DialogResult = true;
    }

    /// <summary>问一句，收一段文字。返回 null = 用户取消（Esc / 关闭）。</summary>
    //
    // 复用「确定/取消」那一档而不是「是/否」：这张卡此刻问的不是一个判断题。
    // 取消**不是**空串——调用方据此中止整组（同 message 答「否」），两者必须分得开。
    public static string? Ask(Window? owner, string? title, string message, string? initial)
    {
        var dlg = Make(owner, title, message);
        dlg.AnswerBox.Text = initial ?? "";
        dlg.AnswerBox.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(dlg.AnswerBox, message ?? "");
        // 焦点落在输入框而不是「确定」：打开就能直接打字，这是这张卡此刻唯一的用途。
        // 全选那一下是给「预填了默认值」准备的——想换直接敲，想用就回车。
        //
        // **而「想用就回车」得先有个默认按钮。** 焦点在单行 TextBox 上时它自己不处理回车，
        // 窗口里又没有任何 IsDefault 按钮，于是那一下按键直接被丢掉——这张卡挂在那儿，
        // 而它是模态的：一个手势或热键跑的动作组会就此停住等人，而急停键解不开模态。
        // 三个编辑器窗口都显式设了 IsDefault，只有这张后来加的问答卡漏了。
        // 设在 Make 里（本方法与 Pick 共用它），因为那里才知道哪颗是可见的。
        dlg.ContentRendered += (_, _) => { dlg.AnswerBox.Focus(); dlg.AnswerBox.SelectAll(); };
        return Run(dlg);
    }

    /// <summary>问一句，从给定的几项里收一个。返回 null = 用户取消。</summary>
    public static string? Pick(Window? owner, string? title, string message, IReadOnlyList<string> options)
    {
        var dlg = Make(owner, title, message);
        foreach (var o in options) dlg.AnswerList.Items.Add(o);
        dlg.AnswerList.SelectedIndex = 0;   // 预选第一项：多数时候第一项就是要的，回车即走
        dlg.AnswerList.Visibility = Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(dlg.AnswerList, message ?? "");
        // 双击一项 = 选它并确定。列表里双击即确认是所有选择框的共同约定，也是人第一下会做的。
        dlg.AnswerList.MouseDoubleClick += (_, _) => dlg.Confirm();
        // 同 Ask：焦点在列表上，回车得有个默认按钮接着，否则上面那句「回车即走」是空话。（在 Make 里设。）
        dlg.ContentRendered += (_, _) => dlg.AnswerList.Focus();
        return Run(dlg);
    }

    /// <summary>问一句，收**多行**文字。返回 null = 用户取消（Esc / 关闭）。</summary>
    ///
    // 与 Ask 共用同一个输入框，只是把它变成多行。
    //
    // 为什么要这一档：列表是这个程序里唯一**不能凭空从文本得到**的形状——文本由
    // prompt / 浏览器地址 / 正则 / 替换…十来种方式产出，列表此前只有「把一段多行文本拆开」
    // 这一条降级路径，而多行文本又没有一步能造（prompt 是单行框）。于是循环功能在
    // 界面上第一步就走不出去：用户得先手改配置才凑得出多行文本。
    //
    // **不新开一个 TextBox**：答案收取在 Confirm 里按「哪个框可见」分派（见那里），
    // 多开一个就多一条分支；而这一档与 Ask 的差别只有 AcceptsReturn 和高度，
    // 那两个属性在切换前设回去即可，行为与 Ask 逐字一致。
    public static string? AskMulti(Window? owner, string? title, string message, string? initial)
    {
        var dlg = Make(owner, title, message);
        dlg.AnswerBox.Text = initial ?? "";
        dlg.AnswerBox.Visibility = Visibility.Visible;
        // 多行框的回车是「换一行」而不是「确定」——所以 IsDefault 必须摘掉，
        // 否则回车会被 OkBtn 接走，用户永远打不出第二行。
        // 那这一档怎么提交？Ctrl+Enter（AnswerBox 上显式挂一个回车处理）。见下面。
        dlg.YesBtn.IsDefault = false;
        dlg.AnswerBox.AcceptsReturn = true;
        dlg.AnswerBox.Height = 132;
        System.Windows.Automation.AutomationProperties.SetName(dlg.AnswerBox, message ?? "");

        dlg.AnswerBox.PreviewKeyDown += (_, e) =>
        {
            // Ctrl+Enter = 确定。这是所有多行输入框的共同约定（VS Code、记事本、
            // 浏览器地址栏的编辑框都是它），而单靠回车的话这一档没法提交——
            // 回车已经被「换行」占用了。
            if (e.Key == System.Windows.Input.Key.Enter
                && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                dlg.Confirm();
            }
        };
        dlg.ContentRendered += (_, _) => { dlg.AnswerBox.Focus(); dlg.AnswerBox.SelectAll(); };
        return Run(dlg);
    }

    // 问句形态的两处共用：建卡 + 摆位 + 跑模态。摆位规则与 Show 那条一字不差
    //（owner 在前台就认它当父，否则 Topmost 自保），理由见 Show 的注释。
    private static BrandDialog Make(Window? owner, string? title, string message)
    {
        // **两颗按钮，不是一颗。** 问句形态必须有一个看得见的「取消」——
        // 只留 Esc 的话，一个不请自来（热键 / 手势触发）的框对用户就是「只能答，不能不答」，
        // 而取消在这里是有意义的一档（它中止整组，见 ActionGroupRunner 那个分支）。
        //
        // 借 confirm 那一档的两颗按钮再改文案，而不是把「取消」塞进单按钮那一档：
        // 顺序因此与本程序其余对话框一致（肯定的在前，同步骤编辑器的「确定 / 取消」）。
        var dlg = new BrandDialog(title, message, confirm: true, ToastLevel.Info);
        dlg.YesBtn.Content = Strings.Get("Ed_Ok");
        dlg.NoBtn.Content = Strings.Get("Ed_Cancel");
        // **IsDefault 要设在看得见的那颗上。** 问句形态走的是 confirm 那一档，
        // 构造函数里 `OkBtn.Visibility = confirm ? Collapsed : Visible`，所以这张卡上
        // OkBtn 是折叠的——而 AccessKeyManager 只把回车发给 IsEnabled && IsVisible 的目标。
        // 曾经两处都把 IsDefault 设在 OkBtn 上，于是它们那两段「回车即走」的注释
        // 全是空话：焦点在单行 TextBox / 列表上，它们自己不处理回车，而那一下又没人接。
        // --selftest 抳不到它：DevSelfTest 每一档都是 Click(d.YesBtn)，从没按过回车。
        dlg.YesBtn.IsDefault = true;
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.Topmost = true;
            DialogForeground.Arm(dlg);   // 不请自来（热键/手势/提醒）：Topmost 管看得见，前台得另抢
        }
        return dlg;
    }

    /// <summary>自查专用（--selftest）：弹框显示出来之后调一次，用来按脚本操作它。</summary>
    //
    // 问句弹框是模态的，而模态窗没有第二条路可以从外面驱动——ShowDialog 一进去就不返回了。
    // 留这个钩子是为了让「打字 → 点确定 → 拿到答案」这条链能**自动**跑一遍：
    // 它是这套东西里唯一测不到的一段（其余都在 app.Tests 里），而它恰恰是用户唯一会碰的那一段。
    // 同 PanelManagerWindow.BeginRenameForShots：只在自查开关下被赋值，跑完即清。
    internal static Action<BrandDialog>? Rehearse;

    private static string? Run(BrandDialog dlg)
    {
        // ContentRendered 而不是 Loaded：脚本要点按钮，而按钮得先真的画出来才收得到点击。
        if (Rehearse is { } script) dlg.ContentRendered += (_, _) => script(dlg);
        dlg.ShowDialog();
        return dlg.Result ? dlg.Answer : null;
    }

    // 仅「确定」：信息/警示。返回 true=已确认。
    public static void Info(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Info);
    public static void Warn(Window? owner, string? title, string message) => Show(owner, title, message, false, ToastLevel.Warn);
    // 「是/否」确认。返回 true=是。level 决定强调轨颜色（破坏性操作传 Warn）。
    // yesLabel / noLabel 是给破坏性问句准备的：按钮印「是 / 否」时，用户必须回头把题干再读一遍
    // 才知道自己点下去的是哪一个——而那一遍恰恰是在他最不想读的时候读的。传动词（删除 / 留着），
    // 两颗按钮各自说清后果。不传就是普通是非题，照旧。
    public static bool Confirm(Window? owner, string? title, string message, ToastLevel level = ToastLevel.Info,
                               string? yesLabel = null, string? noLabel = null)
        => Show(owner, title, message, true, level, yesLabel, noLabel);

    // 删除确认的唯一口径（标题/文案键/Warn 红轨/两颗按钮的动词）：主窗口三列表、系统启动项、
    // 组编辑器、面板管理器共用，契约变更只改这一处。
    public static bool ConfirmDelete(Window? owner, string label)
        => Confirm(owner, Strings.Get("Confirm_Title"), Strings.Lf("Confirm_DeleteItem", label), ToastLevel.Warn,
                   Strings.Get("Btn_Delete"), Strings.Get("Confirm_Keep"));

    public static bool Show(Window? owner, string? title, string message, bool confirm, ToastLevel level,
                            string? yesLabel = null, string? noLabel = null)
    {
        var dlg = new BrandDialog(title, message, confirm, level, yesLabel, noLabel);
        // owner=用户此刻正在交互的那个窗（主窗/编辑器）→ 认它为父：CenterOwner 居中，关窗后焦点也回它。
        // owner=null → 本框不请自来（开机清单、托盘重跑、热键、提醒触发的动作组、崩溃兜底）：
        // 刻意不退回主窗当 owner——Win32 会把整条 owner 链一起提到前台，用户在别的应用里干活时
        // 主界面会跟着被拽出来（同 ReminderPopupWindow.Show 的说明）。可见性改由 Topmost 保证：
        // 没有它，无主的框会藏在用户当前窗口后面，用户看着「卡住了」而实际是有个框在等他。
        if (owner != null && owner.IsVisible) { try { dlg.Owner = owner; } catch { } }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.Topmost = true;
            DialogForeground.Arm(dlg);   // 同 Make：无主即不请自来，键盘焦点不能留在背后的程序上
        }
        dlg.ShowDialog();
        return dlg.Result;
    }
}
