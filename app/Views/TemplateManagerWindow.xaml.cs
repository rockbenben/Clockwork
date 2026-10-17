using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
// UseWindowsForms 的全局 using 让 Brush 撞名，显式钉到 WPF（同 GestureTrailWindow 的惯例）。
using Brush = System.Windows.Media.Brush;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.Views;

// 动作组模板的管理界面（见 XAML 顶上的说明：模板此前只能「添加为新动作」，改不了模板本身）。
//
// 这一扇窗**只做决定，不做保存**：它把「用户想对哪个模板做什么」交回去，由 App 那头去
// 真的开动作编辑器 / 真的写配置。理由与 MainWindow 那些列表一样——配置读写只有一处。
public partial class TemplateManagerWindow : Window
{
    /// <summary>用户在模板管理器里点了什么。</summary>
    public enum TemplateAction
    {
        None,        // 关掉了
        Instantiate,  // 「添加为新动作」：把这份模板复制成一个普通动作
        Edit,        // 「修改模板」：要改的是模板本身，存进 RootConfig.Templates
        Reset,       // 「恢复默认」：删掉这份覆盖，回到内置的那份
    }

    /// <summary>一次选择的结果。</summary>
    public sealed class Pick
    {
        public TemplateAction Action { get; init; } = TemplateAction.None;
        public string Key { get; init; } = "";

        /// <summary>该模板的一份全新副本（Instantiate / Edit 用）。</summary>
        public ActionGroup? Group { get; init; }
    }

    private readonly ObservableCollection<Row> _rows = new();
    private readonly RootConfig _cfg;

    /// <summary>开一次管理器。返回 null 表示用户只是关掉了。</summary>
    public static Pick? Show(Window? owner, RootConfig cfg)
    {
        var dlg = new TemplateManagerWindow(cfg) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg._pick : null;
    }

    private Pick? _pick;

    // 公开给 DevChecks 建变体用（--shots 要拍这一扇窗）。
    public TemplateManagerWindow(RootConfig cfg)
    {
        InitializeComponent();
        Native.DarkWindow.Apply(this);
        WindowSizing.FitToWorkArea(this);
        _cfg = cfg;

        // 用户覆盖先算一遍：哪些模板被改过、按钮怎么亮，都取决于它。
        // 这一份每次调用都现算而不缓存——「修改模板」写完配置后主流程会再开一次管理器。
        foreach (var v in ActionGroupTemplates.Resolve(cfg)) _rows.Add(new Row(v));
        List.ItemsSource = _rows;
        List.SelectionChanged += (_, _) => SyncButtons();
        if (_rows.Count > 0) List.SelectedIndex = 0;
        SyncButtons();
    }

    private Row? Selected => List.SelectedIndex >= 0 && List.SelectedIndex < _rows.Count ? _rows[List.SelectedIndex] : null;

    private void SyncButtons()
    {
        var sel = Selected;
        AddBtn.IsEnabled = EditBtn.IsEnabled = sel != null;
        // 「恢复默认」只在**确实被改过**时可点：没改过时它什么也不做，
        // 而一颗亮着的「恢复默认」会让用户以为还有什么东西没恢复干净。
        ResetBtn.IsEnabled = sel is { Customized: true };
    }

    // 每次取一份新副本：动作编辑器改的是工作副本，用户点「取消」时原模板不该被动过。
    private ActionGroup? FreshCopy() => Selected is null ? null : ConfigStore.DeepClone(Selected.View.Group);

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        _pick = new Pick { Action = TemplateAction.Instantiate, Key = Selected.View.Key, Group = FreshCopy() };
        DialogResult = true;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        _pick = new Pick { Action = TemplateAction.Edit, Key = Selected.View.Key, Group = FreshCopy() };
        DialogResult = true;
    }

    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => Add_Click(sender, e);

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var sel = Selected;
        if (sel is not { Customized: true }) return;
        if (!BrandDialog.ConfirmDelete(this, sel.View.Group.Name)) return;
        _pick = new Pick { Action = TemplateAction.Reset, Key = sel.View.Key };
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class Row
    {
        public Row(ActionGroupTemplates.TemplateView view)
        {
            View = view;
            // 与面板上那一格是同一个画法（IconVisual → PanelIcon），不是各画一遍再指望它们一致。
            var brush = (Brush)System.Windows.Application.Current.TryFindResource("BrushAccentText");
            Icon = IconVisual.Make(PanelIcon.Resolve(view.Group.Icon, null), 17, brush);
        }

        public ActionGroupTemplates.TemplateView View { get; }
        public bool Customized => View.Customized;
        public string Name => View.Group.Name;

        /// <summary>图标元素（不是码位文本）。存的是码位，画出来才是它长得什么样。</summary>
        public FrameworkElement Icon { get; }

        /// <summary>副标题：几条步骤；被改过的那份多一句「已修改」。</summary>
        public string Detail => View.Customized
            ? Strings.Lf("TplMgr_DetailCustomized", View.Group.Steps.Count)
            : Strings.Lf("TplMgr_Detail", View.Group.Steps.Count);

        public string Badge => View.Customized ? Strings.Get("TplMgr_Badge") : "";
    }
}
