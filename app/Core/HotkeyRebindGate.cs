namespace Clockwork.Core;

// 「动作组热键这次保存该不该全量重绑」的裁决。纯函数，不碰 Win32——与 HookWatch.ShouldHeal、
// GestureGate.ShouldWatch 同一套纪律：这句话决定何时可以省掉一整轮 UnregisterHotKey + RegisterHotKey，
// 而它要活在 HotkeyManager 里就断言不到（那儿既要窗口句柄又要真系统调用）。
//
// **为什么不是「签名没变就跳过」这么简单**：全量重绑同时兼着一条隐式的「重试失败注册」通道——
// 某个组的组合先前被别的程序占着（RegisterHotKey 失败），等对方放开后，本程序并没有事件可等，
// 全靠下一次保存无条件重建去把它抢回来。所以脏检查必须放过这一格：**只要还有失败项，就必须重绑**，
// 否则那句「解除占用后自动恢复」会悄悄死掉，而且没有任何测试盯得住。
//
// 另外三个前置：上一轮得真的成功绑过（prevSig 非空，否则启动首轮不能跳）、当前注册仍挂在窗口上
// （registeredCount>0：录键挂起会 UnregisterGroupHotkeys 清空，恢复时必须重建，不能被旧签名挡回去）、
// 且签名逐字相等（含组顺序：改动启用集或改键都会翻签名）。
public static class HotkeyRebindGate
{
    /// <summary>把「启用且绑了非空热键」的组集压成一个签名（含顺序）。配置里任何会影响绑定结果的改动都会翻它。</summary>
    public static string Signature(IEnumerable<(string Id, string Hotkey)> enabledBoundGroups)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in enabledBoundGroups) sb.Append(g.Id).Append('|').Append(g.Hotkey).Append('\n');
        return sb.ToString();
    }

    /// <summary>这一轮保存能不能整段跳过组热键重绑。</summary>
    public static bool ShouldSkip(string prevSig, string curSig, int failCount, int registeredCount)
        => prevSig.Length > 0 && prevSig == curSig && failCount == 0 && registeredCount > 0;
}
