using Clockwork.I18n;

namespace Clockwork.Core;

// 「这次没跑」的原因 + 是否属正常配置状态（benign=true 走 Info 提示，false 走 Warn 提示）。
//
// **存的是文案键与参数，不是渲染好的句子。** 同一个原因要以两种语言出现：气泡跟界面语言，
// 而 clockwork.error.log 固定英文（那份是拿去贴 issue 的）。在这里就把它渲染定死，
// 另一种语言之后再也拿不回来——键一旦丢了，只剩一句已经翻好的话。谁要哪一种谁自己渲染。
//
// Arg 只有一个而不是 object[]：三种原因里只有「目标已禁用」带一个组名。用数组会让 record
// 的值相等退化成引用相等（数组不按内容比），而这个类型是当值用的。
public sealed record GroupSkip(string Key, string? Arg, bool Benign)
{
    /// <summary>按当前界面语言渲染（气泡用）。</summary>
    public string Text() => Arg == null ? Strings.Get(Key) : Strings.Lf(Key, Arg);

    /// <summary>固定英文渲染（错误日志用）。</summary>
    public string TextEn() => Strings.InEnglish(Text);
}

// 解析并分类嵌套组引用（RunGroupStep）的目标。Skip 为 null 表示目标可用（Group 非空，照常跑）；
// 否则 Group 为 null，Skip 带上具体原因与良性标记，调用方原样转发给 OnStepSkipped。
public sealed record GroupTarget(ActionGroup? Group, GroupSkip? Skip);

// 按 id 在动作组列表里解析出组；空 id / 未命中 / 空列表 → null。
// 启动步骤（group）与提醒（onYes/silentGroup）引用组时共用。
public static class ActionGroupResolver
{
    public static ActionGroup? Resolve(IEnumerable<ActionGroup>? groups, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || groups == null) return null;
        foreach (var g in groups) if (g != null && g.Id == id) return g;
        return null;
    }

    // 组内嵌套「动作组」步骤引用的目标解析 + 分类：目标缺失、目标已禁用、目标可用——三种结局的措辞与
    // 良性标记集中在此处一次判定，调用方（RunGroupStep）只管把 Skip 转发给 OnStepSkipped，不再自己
    // 决定「这算不算故障」。三种「这次没跑」的结局都不该经 OnStepError：那条通道套「异常：」措辞，会让
    // 「目标组被人手动禁用」这种正常配置状态读成故障。按启动清单（LaunchSequence）同款口径分 severity：
    //   目标不存在 → 坏配置，Warn（benign=false）
    //   目标已禁用 → 正常状态，Info（benign=true，与 LaunchSequence「已禁用，跳过」不算失败的判断一致）
    // StepEditorWindow 不校验组下拉，空 GroupId 存得下来，所以「找不到」是能走到的路径，不是理论情况。
    public static GroupTarget ResolveForRun(IEnumerable<ActionGroup>? groups, string id)
    {
        var target = Resolve(groups, id);
        if (target == null) return new GroupTarget(null, new GroupSkip("Skip_GroupNotFound", null, false));
        if (!target.Enabled) return new GroupTarget(null, new GroupSkip("Skip_GroupDisabled", target.Name, true));
        return new GroupTarget(target, null);
    }

    // 重入（环引用/已在运行）：RunGroup 返回 Skipped 后才知道，与 ResolveForRun 分开——
    // 这是跑之后才能判定的第三种「这次没跑」，不是解析阶段的结论。真问题（空转），不良性。
    public static GroupSkip Reentrant() => new("Skip_GroupReentrant", null, false);

    // 从 startId 出发沿 group 步骤引用走图，找「回到 startId」的环：找到返回组名路径（首尾同名），无环 null。
    // 只报含 startId 的环——别的环在保存那些组时自会被各自的校验拦下，这里报了反而指不到当前编辑对象。
    // onPath 挡「B↔C 互指」这类外部环导致的无限递归；菱形复用（两条路径引用同一组）合法放行。
    public static List<string>? FindCycle(IReadOnlyList<ActionGroup> groups, string startId)
    {
        var start = Resolve(groups, startId);
        if (start == null) return null;
        var path = new List<string>();
        var onPath = new HashSet<string>();

        List<string>? Dfs(ActionGroup g)
        {
            path.Add(g.Name);
            onPath.Add(g.Id);
            foreach (var s in g.Steps ?? new())
            {
                if (s?.Kind != "group" || string.IsNullOrWhiteSpace(s.GroupId)) continue;
                if (s.GroupId == startId) { path.Add(start.Name); var found = new List<string>(path); path.RemoveAt(path.Count - 1); return found; }
                if (onPath.Contains(s.GroupId)) continue;
                var ng = Resolve(groups, s.GroupId);
                if (ng == null) continue;
                var r = Dfs(ng);
                if (r != null) return r;
            }
            path.RemoveAt(path.Count - 1);
            onPath.Remove(g.Id);
            return null;
        }

        return Dfs(start);
    }
}
