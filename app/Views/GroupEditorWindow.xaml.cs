using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clockwork.Core;
using Clockwork.I18n;
using Clockwork.Native;
using Clockwork.ViewModels;
using static Clockwork.Views.EditorUi;
// UseWindowsForms 的全局 using 让 KeyEventArgs 撞名，显式钉到 WPF（同 GestureTrailWindow 的惯例）。
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Clockwork.Views;

// 动作组编辑器：名称 + 步骤列表（增▾/改/删/上/下），步骤复用 StepEditorWindow。
public partial class GroupEditorWindow : Window
{
    private readonly ActionGroup _original;
    private readonly IReadOnlyList<ActionGroup> _groups;
    // 本程序自己占着的功能键（键 → 它属于谁，查重时点名用）。**是一份清单而不是一个键**：
    // 此前它只装得下急停键，于是快捷面板键加进来的那一轮谁也没想起要更新这里。
    private readonly IReadOnlyList<(string Key, string Owner)> _functionHotkeys;
    private readonly ObservableCollection<StepRowVm> _rows = new();
    private string _groupIcon = "";

    public ActionGroup? Result { get; private set; }

    // 组内步骤可选类型：现含「group」——组可嵌套引用组（部分步骤循环 = 子组引用 ×N）。
    // 环引用由保存时 FindCycle DFS 拦（主防线），运行期重入集兜手改 json。
    // （工具箱的分节与清单在 StepDisplay.StepKindSections / StepKinds，那里是唯一定义；
    //   这里曾经另存一份 Kinds 字段，常态流程改动工具箱时被回退带走、它却没人用——
    //   两份并存的唯一效果是让人以为改一处就够。）

    public GroupEditorWindow(ActionGroup group, IReadOnlyList<ActionGroup> groups,
                             IReadOnlyList<(string Key, string Owner)> functionHotkeys)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _original = group;
        _groups = groups;
        _functionHotkeys = functionHotkeys;
        NameBox.Text = group.Name;
        GroupRepeatBox.Text = StepHelpers.ClampRepeat(group.Repeat).ToString();
        GroupRepeatDelayBox.Text = group.RepeatDelayMs.ToString();
        // 新建组 / 模板预填的组 ShowInTray 还是 null → 默认不勾（不进托盘）；已有组读盘时已被 Normalize 补过值。
        ShowInTrayChk.IsChecked = group.ShowInTray ?? false;
        _groupIcon = group.Icon ?? "";
        RefreshGroupIcon();
        _hotkey = group.Hotkey ?? "";
        // 全局热键「点击即录键」，与急停键/发送键统一走 KeyCaptureBox。只改工作副本 _hotkey，
        // 点「确定」才随 Result 落库——取消编辑不影响已有热键。
        KeyCaptureBox.Attach(HotkeyBox, HotkeyCapture.KeyCaptureMode.Hotkey, null,
            () => _hotkey, combo => _hotkey = combo);
        foreach (var s in group.Steps) _rows.Add(new StepRowVm(Clone(s), () => { }));
        BuildSectionBox();
        RebuildToolbox();
        RebuildRows();
        Steps.ItemsSource = _rows;
        // 空态跟着集合走，而不是跟着某一个按钮的点击走：加、删、复制、拖拽之后都可能是零行，
        // 逐处去记得设 Visibility 迟早漏一处（CollectionChanged 是这里唯一的真相来源）。
        _rows.CollectionChanged += (_, _) => SyncEmptyHint();
        SyncEmptyHint();
        DataGridReorder.Attach(Steps, (from, to) =>
        {
            if (from < 0 || from >= _rows.Count || to < 0 || to >= _rows.Count || from == to) return;
            var r = _rows[from];
            _rows.RemoveAt(from);
            _rows.Insert(to, r);
            // 拖完之后立刻重排：拖动只改了平铺位置，块与孩子的从属关系由 RebuildRows 重新算一遍。
            // 不重排的话，用户把某个子步骤拖到块外面，界面上它仍带着缩进——看着还在块里，跑起来却在块外。
            RebuildRows();
            Steps.SelectedIndex = IndexOfStep(r.Step);
        });
    }

    private string _hotkey = "";
    // 关窗恢复全局热键的兜底已由 KeyCaptureBox 统一负责（挂宿主窗口 Closed），此处不再各写一份。

    private int Sel => Steps.SelectedIndex;

    /// <summary>截图 harness 专用：把右栏切到某一页。</summary>
    // 存在的理由是 TabControl 的固有行为：**它只把选中页的内容交给键盘导航**，
    // 非选中页里的控件拿不到焦点（这也正是它该有的行为）。焦点环探针要验的是
    // 「那个 CheckBox 聚焦时有没有环」，所以探针得先翻到它所在的那一页——
    // 否则量到的是「一个拿不到焦点的控件」，等于什么都没验。
    internal void SelectSideTabForShots(int index) => SideTabs.SelectedIndex = index;

    private void BrowseGroupIcon_Click(object sender, RoutedEventArgs e)
    { if (IconPickerWindow.Pick(this, _groupIcon) is string s) { _groupIcon = s; RefreshGroupIcon(); } }

    private void RefreshGroupIcon()
    {
        IconVisual.Fill(GroupIconBtn, PanelIcon.Resolve(_groupIcon, null), 24,
                        (System.Windows.Media.Brush)FindResource("BrushPaper"));
        GroupIconBtn.ToolTip = _groupIcon.Length > 0 ? _groupIcon : Strings.Get("Icon_None");
        IconNameBox.Text = _groupIcon.Length > 0 ? _groupIcon : Strings.Get("Icon_None");
        // 「外观」那一页顶上那块大预览。字形放大到 44 是面板上那一枚的六倍——
        // 挑图标是看着挑的，而选择器那一格只有 46px，照着它挑等于凭记忆挑。
        if (IconPreview != null)
            IconVisual.Fill(IconPreview, PanelIcon.Resolve(_groupIcon, null), 44,
                            (System.Windows.Media.Brush)FindResource("BrushAccentText"));
    }

    private void SyncEmptyHint()
        => StepsEmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // _rows 是 ObservableCollection，而 FindIndex / RemoveAll 是 List<T> 的方法。
    // 改成 List 会失去变更通知（增删后界面不刷新），所以给两个小helper 而不是换集合类型。
    // 比的是**引用**不是 id：重排时每一行都是新的 StepRowVm，而 Step 本身还是同一个对象。
    private int IndexOfStep(LaunchStep s)
    {
        for (int i = 0; i < _rows.Count; i++) if (ReferenceEquals(_rows[i].Step, s)) return i;
        return -1;
    }

    private void RemoveSteps(IEnumerable<LaunchStep> doomed)
    {
        var set = new HashSet<LaunchStep>(doomed);
        for (int i = _rows.Count - 1; i >= 0; i--) if (set.Contains(_rows[i].Step)) _rows.RemoveAt(i);
    }

    // ---- 块（level）与层级 ----
    //
    // _rows 始终是**平铺**的一串，与 LaunchStep.ParentId 一样是「一串里带层级信息」。
    // 每次增删改之后都重排一次，让 (a) 行序符合块的阅读顺序、(b) 每行的 Depth 是当前值。
    //
    // **为什么每次都重排而不是只改受影响的几行**：一次操作最多动十几个行对象，
    // 而「谁是谁的孩子」这件事只有走一遍整棵树才知道。局部更新省下来的那点算力
    // 换来的是「某次拖拽之后缩进没更新」这类只在特定顺序下出现的错，而重排永远是收敛的。

    /// <summary>按块层级重排 _rows 并刷新每行的缩进。选中项尽量保持在原来那一条上。</summary>
    private void RebuildRows()
    {
        var keep = Sel >= 0 && Sel < _rows.Count ? _rows[Sel].Step : null;

        var steps = _rows.Select(r => r.Step).ToList();
        var ordered = new List<LaunchStep>();
        var tree = StepTree.Build(steps);
        var byId = steps.Where(s => !string.IsNullOrWhiteSpace(s.Id))
                        .GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var placed = new HashSet<LaunchStep>();
        var depth = new Dictionary<LaunchStep, int>();

        void Walk(IReadOnlyList<LaunchStep> list, int d)
        {
            foreach (var s in list)
            {
                if (!placed.Add(s)) continue;   // 环：已在别处放过，跳过（StepTree.Build 也做了同样的事）
                ordered.Add(s);
                depth[s] = d;
                if (StepDisplay.IsBlockKind(s.Kind)) Walk(tree.ChildrenOf(s.Id), d + 1);
            }
        }
        Walk(tree.Top, 0);

        // StepTree 认不出来（父 id 指向已删的步骤之类）的，补在末尾——它们仍在配置里，
        // 悄悄丢掉才是数据损失，而界面上排最后比排中间更容易看出「这一条不属于任何块」。
        foreach (var s in steps) if (placed.Add(s)) { ordered.Add(s); depth[s] = 0; }

        _rows.Clear();
        foreach (var s in ordered) _rows.Add(new StepRowVm(s, () => { }, depth.TryGetValue(s, out var d) ? d : 0));

        var idx = keep == null ? -1 : IndexOfStep(keep);
        if (idx >= 0) Steps.SelectedIndex = idx;
        // 变量面板跟着走：它列的就是这些步骤的 OutputVar，重排完的集合才是准的。
        RebuildVars();
    }

    /// <summary>把新步骤挂到当前选中的块下面（选中的不是块就挂顶层）。</summary>
    // 「选住一个循环，点新增，出来的东西就在循环里」——这是最顺的一条建块路径。
    // 比「先建循环、再把别的步骤拖进去」少一步，而拖拽跨层要额外做归属推算（DataGridReorder 只管上下）。
    private string? ParentIdForNewStep()
        => Sel >= 0 && Sel < _rows.Count && StepDisplay.IsBlockKind(_rows[Sel].Step.Kind)
            ? _rows[Sel].Step.Id
            : null;

    // 选组下拉排除本组：直环在挑选时就选不出来；间接环（A→B→A）由 Ok_Click 的 FindCycle 拦。
    private IReadOnlyList<ActionGroup> StepGroups => _groups.Where(g => g.Id != _original.Id).ToList();

    // ---- 模块工具箱（左侧那一列） ----
    //
    // 从「新增 ▾」一个下拉搬过来的。分节与 Quicker 的模块分类同源（StepDisplay.StepKindSections）。
    // 三条约定：
    //   · **「全部」里每种类型只出现一次**（按主节排）。分节表允许一个类型属于多节
    //     （同 Quicker 的「个别模块会属于多个分类」），而在「全部」视图里同一个类型
    //     出现两次只是看着像 bug。选定某一节时才按那一节的成员列——那才是多归属有意义的地方。
    //   · **搜索认标签也认类型 id**：界面上是译文，配置文件里是 kind，搜 `regex` 能搜到「正则提取」，
    //     搜「正则」也能。不做拼音首字母（Quicker 那样做要带一整张拼音表，
    //     而这个程序的中文标签本身短到可以直接输）。
    //   · **双击添加**，与 Quicker 一致（拖放另说，见 DevChecks 的说明：本程序步骤表只做了上下拖拽）。

    private const string PresetSection = "Menu_SecPreset";
    private const string AllSection = "";          // 「全部」
    private List<ToolItem> _toolItems = new();

    private static IEnumerable<string> AllKindsOrdered()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, kinds) in StepDisplay.StepKindSections)
            foreach (var k in kinds) if (seen.Add(k)) yield return k;
    }

    private void BuildSectionBox()
    {
        var box = new List<SectionItem>
        {
            new SectionItem(AllSection, Strings.Get("Tbx_All")),
            // 「常用预设」是**填好字段的成品**（复制 = Ctrl+C 那一类），与「常用」节里那些
            // 「先挑类型再自己配」不是一回事，两者同名会让人以为是一张表里的同一批。
            new SectionItem(PresetSection, Strings.Get("Menu_SecPreset")),
        };
        foreach (var (sec, _) in StepDisplay.StepKindSections) box.Add(new SectionItem(sec, Strings.Get(sec)));
        SectionBox.ItemsSource = box;
        SectionBox.SelectedIndex = 0;
    }

    private void RebuildToolbox()
    {
        var section = (SectionBox.SelectedItem as SectionItem)?.Key ?? AllSection;
        var q = SearchBox.Text.Trim();
        var items = new List<ToolItem>();

        if (section == AllSection || section == PresetSection)
            foreach (var (labelKey, kind, arg) in StepDisplay.CommonPresets)
                items.Add(new ToolItem(Strings.Get(labelKey), kind, StepDisplay.MakePreset(kind, arg)));

        if (section != PresetSection)
        {
            if (section == AllSection)
            {
                // 「全部」：每种类型一次，按主节顺序（节内不重复，跨节去重）。
                foreach (var k in AllKindsOrdered()) items.Add(new ToolItem(StepDisplay.StepKindLabel(k), k, null));
            }
            else
            {
                // 指定某一节：就列那一节的成员，此时重复是这一节的本意（用户是按这一节找的）。
                foreach (var (sec, kinds) in StepDisplay.StepKindSections)
                    if (sec == section)
                        foreach (var k in kinds) items.Add(new ToolItem(StepDisplay.StepKindLabel(k), k, null));
            }
        }

        if (q.Length > 0)
            items = items.Where(i => (i.Label ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || i.Kind.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _toolItems = items;
        Toolbox.ItemsSource = items;
        ToolboxEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildToolbox();
    private void SectionBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildToolbox();

    private void Toolbox_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Toolbox.SelectedItem is ToolItem it) AddStepOf(it);
    }

    private void Toolbox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        // 回车 = 添加。工具箱是常驻的，敲完关键词手还在键盘上，
        // 为了加一步去够一次鼠标是这个界面最贵的一次多余动作。
        if (e.Key == Key.Enter && Toolbox.SelectedItem is ToolItem it) { AddStepOf(it); e.Handled = true; }
    }

    private void AddStepOf(ToolItem it)
    {
        var step = StepEditorWindow.Edit(this, it.Seed, it.Kind, StepGroups, insideGroup: true);
        if (step == null) return;
        step.ParentId = ParentIdForNewStep() ?? "";
        int pos = StepHelpers.InsertPosition(Sel, _rows.Count);
        _rows.Insert(pos, new StepRowVm(step, () => { }));
        RebuildRows();
        Steps.SelectedIndex = IndexOfStep(step);
    }

    // ---- 变量面板（右侧那一列） ----
    //
    // 列的是「这一组里产出过的变量」，即各步骤 OutputVar 的并集，按出现顺序。
    // 它不新增任何能力——变量本来就在步骤的「输出到变量」那行里——只是把散在
    // N 个步骤对话框里的那 N 个名字收到一处，顺带标出它是谁产出的。
    // 双击复制 `{名字}`：写下一步的文本框时要打的花括号，抄一次比手敲一次准。

    private void RebuildVars()
    {
        var list = new List<VarItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in _rows)
        {
            var v = (r.Step.OutputVar ?? "").Trim();
            if (v.Length == 0 || !seen.Add(v)) continue;
            list.Add(new VarItem(v, r.KindLabel));
        }
        VarList.ItemsSource = list;
        VarsEmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VarList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (VarList.SelectedItem is not VarItem v) return;
        try { System.Windows.Clipboard.SetText("{" + v.Name + "}"); }
        catch { /* 剪贴板被别的进程占着就算了：这是便利功能，不该弹窗 */ }
    }

    // 分类项。**ToString 必须覆写成 Label**：下拉渲染的是 item 的 ToString，
    // 而 DisplayMemberPath 在本项目的 ComboBoxItem 模板（Theme.xaml 里那个裸 ContentPresenter）
    // 下吃不到——第一次渲染出来的是一行 "SectionItem { Key = , Label = 全部 }"。
    private sealed record SectionItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    // 图标走面板那张字形表（PanelGlyph.ForKind）：工具箱、菜单、面板格子是同一枚图标。
    private sealed record ToolItem(string Label, string Kind, LaunchStep? Seed)
    {
        public string Glyph => PanelGlyph.ForKind(Kind);
    }

    private sealed record VarItem(string Name, string KindLabel);

    private void SEdit_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var step = _rows[i].Step;
        var edited = StepEditorWindow.Edit(this, step, step.Kind, StepGroups, insideGroup: true);
        if (edited != null) { _rows[i] = new StepRowVm(edited, () => { }); Steps.SelectedIndex = i; }
    }

    private void SDel_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var target = _rows[i].Step;

        // 删一个块要连它的孩子一起删，且**先说清楚要删几条**。
        // 不说的话：块没了、孩子留下，而孩子的 ParentId 指向一个不存在的 id —— 运行期靠
        // StepTree 兜成顶层，于是它们继续跑，而且出现在块本该在的位置之外的地方。
        // 用户删「那个循环」时的本意是「那一整段都不要了」，只删外壳等于留了个空壳在原地跑里面的东西。
        var doomed = DescendantsOf(target).ToList();
        var label = doomed.Count > 0
            ? Strings.Lf("Confirm_DeleteBlockWithSteps", StepDisplay.StepListSummary(target), doomed.Count)
            : StepDisplay.StepListSummary(target);
        if (!BrandDialog.ConfirmDelete(this, label)) return;

        RemoveSteps(doomed);
        RebuildRows();
        if (_rows.Count > 0) Steps.SelectedIndex = Math.Min(i, _rows.Count - 1);
    }

    /// <summary>某个块的孩子（不含它自己）。</summary>
    private IEnumerable<LaunchStep> DescendantsOf(LaunchStep block)
    {
        var tree = StepTree.Build(_rows.Select(r => r.Step).ToList());
        var outp = new List<LaunchStep>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { block.Id ?? "" };
        void Walk(IReadOnlyList<LaunchStep> list)
        {
            foreach (var s in list)
            {
                if (!seen.Add(s.Id ?? "")) continue;
                outp.Add(s);
                Walk(tree.ChildrenOf(s.Id));
            }
        }
        Walk(tree.ChildrenOf(block.Id));
        return outp;
    }

    // 复制选中步骤：深拷贝插到选中之后（与主窗口三列表的「复制」同一条插入契约）。
    private void SCopy_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var src = _rows[i].Step;

        // 复制要连**整个子树**一起复制。只复制块外壳的话，副本是个空壳，而用户复制一个循环的
        // 意图是「再来一遍同样的事」——那个循环里当然有东西，不然谁会复制它。
        //
        // 父 id 必须**传递性**重映射：孙步骤的 ParentId 指的是它父亲的 id，而父亲也被换了新 id。
        // 只改第一层的话，复制出来的块里所有孙子立刻变成孤儿（StepTree 会把它们兜成顶层，
        // 于是它们出现在块外面继续跑——一次复制变成了两份都可能执行的步骤）。
        var family = new List<LaunchStep> { src };
        family.AddRange(DescendantsOf(src));

        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var clones = family.Select(CloneWithNewId).ToList();
        foreach (var c in clones) if (!string.IsNullOrWhiteSpace(c.Id)) idMap[c.Id] = c.Id;
        foreach (var c in clones)
        {
            var oldParent = c.ParentId ?? "";
            c.ParentId = idMap.TryGetValue(oldParent, out var nid) ? nid : "";
        }

        int pos = StepHelpers.InsertPosition(i, _rows.Count);
        foreach (var c in clones) _rows.Insert(pos++, new StepRowVm(c, () => { }));
        RebuildRows();
        Steps.SelectedIndex = IndexOfStep(clones[0]);
    }

    // 上移 / 下移 = **与上一个 / 下一个兄弟换位**，不是「整行往上挪一行」。
    //
    // 为什么不是后者：平铺表里「上一行」很可能正在另一个块内部（上一个兄弟的循环里的第一步），
    // 整行挪上去会把我的循环塞进它的循环里——而 ParentId 还指着原来的父亲，
    // 于是重排时这一段直接跑到别人的块底下。在没有嵌套的年代两种做法一模一样，
    // 有了块之后必须说清楚是哪一种。这里选换位：它在树上永远有个明确的含义，用户按一次就知道发生了什么。
    //
    // **整棵子树一起动**（含我自己的孩子）：只挪外壳的话，孩子留在原地，
    // 一段循环会被拆成前后两半，而两半都会跑。
    private void SUp_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i <= 0) return;
        var r = _rows[i];
        // 上一行比我还浅 = 我是父亲的第一个孩子，上面已经没有我的位置了。
        if (_rows[i - 1].Depth < r.Depth) return;

        // 上一行可能在我前面那个兄弟的块**内部**——要退到那个块的外面，才是我的上一位。
        int d = _rows[i - 1].Depth;
        int j = i - 1;
        while (j > 0 && _rows[j].Depth > d) j--;

        MoveSubtree(r.Step, i, j);
    }

    private void SDown_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var r = _rows[i];
        int span = 1 + DescendantsOf(r.Step).Count();
        if (i + span >= _rows.Count) return;

        // 下一行的深度与我相同 = 它是我的下一个兄弟，插到它整块之后。
        if (_rows[i + span].Depth != r.Depth)
        {
            // 下一行比我还深 = 我是这个块里的最后一个孩子，下面没有我的位置了。
            if (_rows[i + span].Depth > r.Depth) return;
            // 比我还浅 = 我是最后一个孩子。允许移到「父亲之后」（即离开这个块），
            // 否则一个块的最后一行永远动不了，而「把循环体里的东西挪到循环外面」是常见操作。
            return;
        }

        int d = r.Depth;
        int end = i + span;
        while (end < _rows.Count && _rows[end].Depth >= d) end++;
        MoveSubtree(r.Step, i, end);
    }

    /// <summary>把 <paramref name="step"/> 及其整棵子树，从 from 处挪到 to 处（to 会落在它后面）。</summary>
    private void MoveSubtree(LaunchStep step, int from, int to)
    {
        int span = 1 + DescendantsOf(step).Count();
        var block = _rows.Skip(from).Take(span).ToList();
        for (int k = 0; k < span; k++) _rows.RemoveAt(from);
        int at = to > from ? to - span : to;
        for (int k = 0; k < block.Count; k++) _rows.Insert(at + k, block[k]);
        RebuildRows();
        Steps.SelectedIndex = IndexOfStep(step);
    }

    // —— 试跑：跑的是编辑中（未保存）的内容，所见即所得 ——
    // 时间条件照常生效：周末试跑「仅工作日」的步骤会被跳过，这是真实语义，不为试跑放宽。
    private RunCancel? _tryRun;

    // 「运行这一步」命中 group 类型步骤时，跑的是已保存的目标组（见下）——这本质上也是一次
    // RunGroupAsync，同样要在关窗时收掉，不能让它变成孤儿。故意不复用 _tryRun：那个字段驱动的是
    // 「运行整组」按钮的文字/开关状态（Run ⇄ Stop），这里只是单步操作的副作用，不该让那颗按钮
    // 也跟着变成「停止」——两个闸各管各的运行，OnClosed 里一起收。
    //
    // 用列表而不是单个字段：两个引用步骤指向不同的组时，先后点「运行这一步」是两次都该跑的合法操作
    //（同一个组的重复触发另有 ActionGroupRunner._running 挡着，不必在这里再拦一道）。单字段会被后一次
    // 覆盖，且先跑完的那次回调会无条件清空字段，把仍在跑的后一次的句柄一起抹掉——关窗就收不掉它，
    // 正是本字段要防的孤儿运行。每次运行只摘掉自己那一个闸，互不干扰。
    // 三处读写（点击、onDone、OnClosed）都在 UI 线程上——onDone 由 RunGroupAsync 经 Dispatcher 派发
    //——故不加锁。
    private readonly List<RunCancel> _stepGroupRuns = new();

    private void SRunStep_Click(object sender, RoutedEventArgs e)
    {
        int i = Sel;
        if (i < 0 || i >= _rows.Count) return;
        var s = _rows[i].Step;
        if (s.Kind == "group")
        {
            // 引用步骤跑的是「已保存」的那份目标组——本编辑器里的未保存改动不属于它。
            var g = ActionGroupResolver.Resolve(_groups, s.GroupId);
            var app = App.Instance;
            if (g == null || app == null) return;
            // 闭包捕获的是局部变量本身：onDone 经 Dispatcher 派发，UI 线程要等本方法返回才轮得到它，
            // 所以回调真正执行时 cancel 早已赋值、也已入列（与 _tryRun 的赋值竞态同一条理由）。
            RunCancel? cancel = null;
            cancel = app.RunGroupAsync(g, this, () => { if (cancel != null) _stepGroupRuns.Remove(cancel); });
            _stepGroupRuns.Add(cancel);
            return;
        }
        App.Instance?.RunStepAsync(s, this);
    }

    private void SRunGroup_Click(object sender, RoutedEventArgs e)
    {
        // 已在试跑 → 本次点击是「停止」。
        if (_tryRun != null) { _tryRun.Request(); return; }
        var app = App.Instance;
        if (app == null) return;
        // 保留真实 Id：运行集（ActionGroupRunner._running）据此挡住「已被热键触发中又来试跑」与自引用，
        // 行为正确且零新代码。名称留空也无妨——试跑不落盘。
        var temp = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = true,
            Repeat = StepHelpers.ClampRepeat(ParseOr(GroupRepeatBox.Text, 1)),
            RepeatDelayMs = ParseOr(GroupRepeatDelayBox.Text, 0, min: 0),
            Steps = _rows.Select(r => r.Step).ToList(),
        };
        SRunGroup.Content = Strings.Get("Btn_StopRun");
        _tryRun = app.RunGroupAsync(temp, this, () =>
        {
            _tryRun = null;
            SRunGroup.Content = Strings.Get("Btn_RunGroup");
        });
    }

    // 关窗即停：这次试跑归本编辑器所有，不能在窗口没了之后还在偷偷跑（用户以为「取消」了一切）。
    // 所有闸都要收——「运行整组」的 _tryRun 与「运行这一步」命中嵌套组时的每一次运行是各自独立的，
    // 用户关窗时没有办法区分是哪一个还在跑，也不该被要求分清楚。
    // 遍历副本：Request 只置位、不会同步回调（onDone 经 Dispatcher 派发，本方法返回后才可能跑），
    // 复制是廉价的保险——免得日后有人把回调改成同步就地摘元素，把这里变成 InvalidOperationException。
    protected override void OnClosed(EventArgs e)
    {
        _tryRun?.Request();
        foreach (var c in _stepGroupRuns.ToList()) c.Request();
        base.OnClosed(e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) { BrandDialog.Warn(this, "Clockwork", Strings.Get("Val_GroupName")); return; }
        // 热键查重：与其它「启用」组、或本程序自己的功能键（急停 / 快捷面板）相同就地拦下
        //（等注册失败才报，用户可能早已关掉编辑器；而那句失败提示还会把人指去查别的程序）。
        // 只算启用组——运行时禁用组不注册、主动让出组合（用户禁用 A 正是为了把键腾给 B），此处不能反着拦。
        // 功能键则不分启停：它们一直注册着。
        if (!string.IsNullOrWhiteSpace(_hotkey))
        {
            var owner = HotkeyConflict.OwnerOf(_hotkey, _groups, _original.Id, _functionHotkeys);
            if (owner != null) { BrandDialog.Warn(this, "Clockwork", Strings.Lf("Val_HotkeyDup", _hotkey, owner)); return; }
        }
        var candidate = new ActionGroup
        {
            Id = _original.Id,
            Name = NameBox.Text.Trim(),
            Enabled = _original.Enabled,
            Hotkey = _hotkey,
            // 手势不在本窗口编辑（归「鼠标手势」管理器），但必须原样带过去——
            // 这里是整份重建 ActionGroup，漏一个字段就等于在保存时把它抹掉。
            Gesture = _original.Gesture,
            // Panel* 一串是历史字段（只有 ConfigStore 那次迁移读它们），仍原样带过去：
            // 这里是整份重建 ActionGroup，漏一个字段就等于在保存时把它抹掉，
            // 而一份还没迁移过就被编辑过的配置，迁移时会读到被抹平的值。
            ShowInPanel = _original.ShowInPanel,
            PanelTab = _original.PanelTab,
            PanelExpand = _original.PanelExpand,
            PanelForProcess = _original.PanelForProcess,
            Repeat = StepHelpers.ClampRepeat(ParseOr(GroupRepeatBox.Text, 1)),
            RepeatDelayMs = ParseOr(GroupRepeatDelayBox.Text, 0, min: 0),
            ShowInTray = ShowInTrayChk.IsChecked == true,
            Icon = _groupIcon.Trim(),
            Steps = _rows.Select(r => r.Step).ToList(),
        };
        // 环引用校验：候选列表 = 其余组 + 本组编辑结果（新建组即追加），从本组出发 DFS。
        // 编辑期是主防线——运行期重入集只会静默空转，用户会以为组坏了。
        var cycle = ActionGroupResolver.FindCycle(
            _groups.Where(g => g.Id != _original.Id).Append(candidate).ToList(), _original.Id);
        if (cycle != null)
        {
            // 环路径上可能有手改配置留下的空名组：逐段替空为占位符，别让消息渲成 "A →  → A" 指不出是谁。
            var path = string.Join(" → ", cycle.Select(n => string.IsNullOrWhiteSpace(n) ? Strings.Get("Ed_Group_None") : n));
            BrandDialog.Warn(this, "Clockwork", Strings.Lf("Val_GroupCycle", path));
            return;
        }
        Result = candidate;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // 步骤深拷贝（工作副本，取消即丢弃不动原组）。经 JSON 往返：LaunchStep 以后加字段自动带上，
    // 不再手抄 30 个字段——手抄漏一个，编辑组就悄悄丢那个字段的值。
    private static LaunchStep Clone(LaunchStep s)
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<LaunchStep>(
            System.Text.Json.JsonSerializer.Serialize(s, ConfigStore.JsonOptions), ConfigStore.JsonOptions)!;
        c.Days ??= new(); c.OnYes ??= new();   // 源对象字段为 null（手改配置）时补默认，与 ConfigStore.Read 同口径
        return c;
    }

    /// <summary>深拷贝并**换一个全新的 Id**。复制步骤专用。</summary>
    // 为什么要换：Id 是面板点击统计（Core.PanelUsageStore）的键，也是块与子步骤之间那根 ParentId 链的锚。
    // 复制出两条同 id 的记录，两边共用一份点击计数（改一个参数，另一边的统计也变），
    // 而更糟的是子步骤的 ParentId 指向谁变得不确定——复制出来的块可能认到原件的孩子上。
    private static LaunchStep CloneWithNewId(LaunchStep s)
    {
        var c = Clone(s);
        c.Id = Guid.NewGuid().ToString();
        return c;
    }

    public static ActionGroup? Edit(Window? owner, ActionGroup? group, IReadOnlyList<ActionGroup> groups,
                                   IReadOnlyList<(string Key, string Owner)> functionHotkeys)
    {
        var dlg = new GroupEditorWindow(group ?? new ActionGroup { Name = "" }, groups, functionHotkeys) { Owner = owner };
        // owner 为 null 时必须自己找位置、自己保证看得见：这两个入口都能从**面板**进来
        // （面板一格右键 → 编辑；主窗口收在托盘时 App 传的就是 null），而面板此刻已经自己关掉了。
        // 不兜底的话，WindowStartupLocation=CenterOwner 没有 owner 可居中、Topmost 也没置，
        // 于是模态开在任意位置、压在所有窗口后面 —— 用户看到面板消失、什么都没出现，
        // 而一个模态框正拦着后续操作。BrandDialog 与面板管理器都已经这么兜了（同款说明见那两处）。
        if (owner == null) { dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; dlg.Topmost = true; }
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
