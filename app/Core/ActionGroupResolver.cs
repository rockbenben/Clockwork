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
}
