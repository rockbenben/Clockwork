namespace Clockwork.Core;

// 注入类动作（发键/发文本）的结果：成功但无法证实接收（Unverified，日志标「~ 已发送（未校验）」）、
// 或失败带原因（Warning，日志标「⚠ …」）、或无输出（Empty，如空文本/急停静默）。
// 窗口动作(close/min/max/activate)不用它——那返回「操作了几个窗口」的整数，由引擎按动作类型解读。
public sealed class ActionResult
{
    public bool Unverified { get; init; }
    public string? Warning { get; init; }

    public static readonly ActionResult Empty = new();
    public static ActionResult Unver() => new() { Unverified = true };
    public static ActionResult Warn(string message) => new() { Warning = message };
}
