namespace Clockwork.Core;

/// <summary>
/// 把平铺的步骤表按 <see cref="LaunchStep.ParentId"/> 收成层级。跑块与显示层级都用它。
///
/// **为什么收一次、而不是每次要孩子时现扫**：组跑几十轮、每轮几十步，而每步都要问一次
/// 「我的孩子是谁」。现扫是 O(步数) 每问一次，整轮下来是平方级。收一次是 O(步数)。
///
/// **收的时候顺手处理三种坏数据**（都是手改 json 能造出来的，且都在运行期必然出事）：
///   · 指向一个不存在的块     → 当作顶层。留在原地不动的话它在运行期永远不会被执行到，
///                              界面上还显示得好好的。
///   · 指向自己 / 互指成环   → 取链条上第一个，按顶层处理。留成环的话递归执行会栈溢出——
///                              而栈溢出在动作组里表现为整个进程崩掉，配置里那一行完全看不出问题。
///   · 块的孩子不挨着       → 按它们在平铺表里的先后收进来。顺序由平铺位置决定是这个模型的代价，
///                              而「不挨着」通常是因为用户拖动时越过了别的步骤，不是配置错误。
/// </summary>
public sealed class StepTree
{
    /// <summary>「每个」循环里当前项的默认变量名。</summary>
    // 用 item/index 而不是 item1/item2 这种带序号的：序号每加一层就要改一遍所有引用，
    // 而嵌套里"外层 item"和"内层 item"本来就是同一个名字——靠 Scope 存档解决（见 RunEach）。
    public const string DefaultItemVar = "item";
    public const string DefaultIndexVar = "index";

    public IReadOnlyList<LaunchStep> Top { get; }
    private readonly Dictionary<string, List<LaunchStep>> _children;

    private StepTree(IReadOnlyList<LaunchStep> top, Dictionary<string, List<LaunchStep>> children)
    {
        Top = top;
        _children = children;
    }

    /// <summary>某个块的孩子（空 = 没有孩子，一个「每个」配一个空块就是跑零遍，如实什么都不做）。</summary>
    public IReadOnlyList<LaunchStep> ChildrenOf(string? blockId)
    {
        var id = (blockId ?? "").Trim();
        return id.Length > 0 && _children.TryGetValue(id, out var c) ? c : Array.Empty<LaunchStep>();
    }

    public static StepTree Build(IReadOnlyList<LaunchStep>? steps)
    {
        var all = steps?.Where(s => s != null).ToList() ?? new List<LaunchStep>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in all) if (!string.IsNullOrWhiteSpace(s.Id)) ids.Add(s.Id);

        var top = new List<LaunchStep>();
        var children = new Dictionary<string, List<LaunchStep>>(StringComparer.Ordinal);
        var placed = new HashSet<LaunchStep>();

        // 第一趟：能落到一个**真实存在**的块下面的，都归到那个块。坏父（不存在的 id / 空）留到顶层。
        foreach (var s in all)
        {
            var p = (s.ParentId ?? "").Trim();
            if (p.Length == 0 || s.Id == p || !ids.Contains(p))
            {
                top.Add(s);
                continue;
            }
            if (!children.TryGetValue(p, out var list)) children[p] = list = new List<LaunchStep>();
            list.Add(s);
            placed.Add(s);
        }

        // 第二趟：取环。父链上出现自己就说明成环了——把这一条提到顶层，它下面的东西随之回到它下面。
        // 链长上限取步骤总数：再长的链不可能不重复。
        var resolved = new Dictionary<string, LaunchStep>(StringComparer.Ordinal);
        foreach (var s in all) if (!string.IsNullOrWhiteSpace(s.Id)) resolved.TryAdd(s.Id, s);

        foreach (var kv in children.ToList())
        {
            foreach (var child in kv.Value.ToList())
            {
                var cur = child;
                int hops = 0;
                while (hops++ <= all.Count)
                {
                    var pid = (cur.ParentId ?? "").Trim();
                    if (pid.Length == 0 || pid == cur.Id) break;
                    if (!resolved.TryGetValue(pid, out var next) || next.Id == child.Id)
                    {
                        // 回到了起点（或指向链外的另一条）= 环。把 child 提回顶层。
                        top.Add(child);
                        children[kv.Key].Remove(child);
                        break;
                    }
                    cur = next;
                }
            }
        }

        // 第三趟：顶层里不该出现已经被收进块里的步骤（第二趟提上来的算例外，它们确实在顶层）。
        var topIds = new HashSet<string>(top.Select(s => s.Id ?? ""), StringComparer.Ordinal);
        foreach (var (bid, list) in children)
            foreach (var c in list) topIds.Add(c.Id ?? "");

        // 顶层保持平铺表原序：编辑器里拖动的顺序就是这个顺序，不能因为收树而打乱。
        return new StepTree(top, children);
    }

    /// <summary>
    /// 这一步在第几层（顶层 = 0）。编辑器用它算缩进。带环保护：真出现环时返回 0 而不是死循环。
    /// </summary>
    public int DepthOf(LaunchStep step, IReadOnlyDictionary<string, LaunchStep> byId)
    {
        int d = 0;
        var cur = step;
        while (d < 32)
        {
            var pid = (cur.ParentId ?? "").Trim();
            if (pid.Length == 0 || pid == cur.Id || !byId.TryGetValue(pid, out var p)) break;
            cur = p; d++;
        }
        return d;
    }
}
