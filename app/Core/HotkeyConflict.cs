namespace Clockwork.Core;

/// <summary>一个组合键此刻归谁占着。返回占用方的名字，没人占则 null。</summary>
//
// 抽成纯函数不是为了复用（只有动作编辑器在用），是为了**能被测**：这段规则原先长在
// GroupEditorWindow.Ok_Click 里，而那是一扇 WPF 窗口，进不了 app.Tests。
// 它出过一次错——只比对了急停键，漏掉快捷面板键，于是给动作配上面板键的默认值
// （Ctrl+Alt+Space）会被安静收下，运行时那条动作注册失败，提示还把人指去查别的程序。
public static class HotkeyConflict
{
    /// <param name="selfId">正在编辑的那个动作，不和自己比。</param>
    /// <param name="functionHotkeys">本程序自己占着的功能键（急停 / 快捷面板…），键 → 属于谁。</param>
    //
    // **只算启用的动作**：运行期禁用的动作不注册热键，主动让出组合——用户禁用 A 正是为了把键腾给 B，
    // 在这儿反着拦就把那条路堵死了。**功能键则不分启停**，它们一直注册着。
    public static string? OwnerOf(string? hotkey, IReadOnlyList<ActionGroup>? groups, string? selfId,
                                  IReadOnlyList<(string Key, string Owner)>? functionHotkeys)
    {
        var key = (hotkey ?? "").Trim();
        if (key.Length == 0) return null;
        bool Same(string? a) => string.Equals((a ?? "").Trim(), key, StringComparison.OrdinalIgnoreCase);

        foreach (var g in groups ?? new List<ActionGroup>())
            if (g != null && g.Enabled && !string.Equals(g.Id, selfId, StringComparison.Ordinal) && Same(g.Hotkey))
                return g.Name;
        foreach (var (k, owner) in functionHotkeys ?? Array.Empty<(string, string)>())
            if (Same(k)) return owner;
        return null;
    }
}
