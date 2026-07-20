using System.Collections.ObjectModel;
using Clockwork.Core;

namespace Clockwork.ViewModels;

// 列表行的公共契约：编辑后刷新显示。
public interface IRowVm { void Refresh(); }

// 三个列表页 VM 的非泛型基：仅暴露与类型无关的选中索引，便于 MainWindow 用一个 helper 统一
// "变更后把选中回推到 DataGrid"，取代此前 launch 用 helper、reminder/group 各内联的不一致写法。
public abstract class ListVmBase
{
    public int SelectedIndex { get; set; } = -1;
}

// 三个列表页(启动清单 / 提醒 / 动作组)的公共增删改逻辑。此前三份 ViewModel 的
// Add/DeleteSelected/RefreshSelected/ReplaceSelected/SelectedIndex/Rows 几乎逐字相同——统一到此。
// 子类只提供：底层模型集合、行工厂，以及可选的「替换时 id 处理」(OnReplacing)。
public abstract class ListVm<TModel, TRow> : ListVmBase where TModel : class where TRow : IRowVm
{
    protected readonly RootConfig Config;
    protected readonly Action Save;
    protected readonly IList<TModel> Models;   // = config 里对应的那份 List，增删即改配置
    private readonly Func<TModel, TRow> _makeRow;

    public ObservableCollection<TRow> Rows { get; } = new();

    protected ListVm(RootConfig config, IList<TModel> models, Func<TModel, TRow> makeRow, Action save)
    {
        Config = config; Models = models; Save = save; _makeRow = makeRow;
        foreach (var m in models) Rows.Add(makeRow(m));
    }

    // 在选中项之后插入（无选中/越界→追加）。返回落点。
    public int Add(TModel m)
    {
        int pos = StepHelpers.InsertPosition(SelectedIndex, Rows.Count);
        Models.Insert(pos, m);
        Rows.Insert(pos, _makeRow(m));
        SelectedIndex = pos;
        Save();
        return pos;
    }

    public void DeleteSelected()
    {
        int i = SelectedIndex;
        if (i < 0 || i >= Rows.Count) return;
        Models.RemoveAt(i);
        Rows.RemoveAt(i);
        SelectedIndex = Math.Min(i, Rows.Count - 1);
        Save();
    }

    // 编辑后刷新选中行显示并存盘。
    public void RefreshSelected()
    {
        int i = SelectedIndex;
        if (i < 0 || i >= Rows.Count) return;
        Rows[i].Refresh();
        Save();
    }

    // 按条件批量移除（模型与行同序同删），只用基类状态故放在此。
    // save:false 供与其他改动合并存盘的调用方（如删除动作组的联动清理，随后 DeleteSelected 会整体落盘），
    // 避免一次操作写两次盘；默认 true 与本类其他改动方法同契约。
    public void RemoveWhere(Func<TModel, bool> pred, bool save = true)
    {
        bool removed = false;
        for (int i = Models.Count - 1; i >= 0; i--)
            if (pred(Models[i])) { Models.RemoveAt(i); Rows.RemoveAt(i); removed = true; }
        SelectedIndex = Math.Min(SelectedIndex, Rows.Count - 1);
        if (removed && save) Save();
    }

    // 用编辑器返回的新模型替换选中项。id 处理交子类。
    public void ReplaceSelected(TModel m)
    {
        int i = SelectedIndex;
        if (i < 0 || i >= Rows.Count) return;
        OnReplacing(m, Models[i]);
        Models[i] = m;
        Rows[i] = _makeRow(m);
        Save();
    }

    // 替换前的 id 处理钩子：Reminder 换新 id（重置运行态）、Group 保留旧 id（被引用）、Launch 无 id。
    protected virtual void OnReplacing(TModel newModel, TModel oldModel) { }

    // 复制选中项：深克隆插到选中之后。与 Reminder/Group 各写一份的旧写法统一到此（同 OnReplacing 的钩子模式）。
    public void DuplicateSelected()
    {
        var sel = Selected;
        if (sel == null) return;
        var clone = ConfigStore.DeepClone(sel);
        OnDuplicating(clone);
        Add(clone);
    }

    // 克隆后的调整钩子：Reminder/Group 均须换新 id，Group 另加名称后缀并清热键。
    protected virtual void OnDuplicating(TModel clone) { }

    // 上/下移：三个列表页共用。
    public void MoveUp() => Move(-1);
    public void MoveDown() => Move(1);

    private void Move(int dir)
    {
        int i = SelectedIndex, j = i + dir;
        if (i < 0 || i >= Rows.Count || j < 0 || j >= Rows.Count) return;
        (Models[i], Models[j]) = (Models[j], Models[i]);
        Rows.Move(i, j);
        SelectedIndex = j;
        Save();
    }

    protected TModel? Selected => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Models[SelectedIndex] : null;
}
