namespace Clockwork.Core;

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
