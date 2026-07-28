using System.Globalization;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 提醒时间文本的规整。
public static class DurationText
{
    // 把时间规整成规范 HH:mm，接受单数小时（"9:00"→"09:00"）。规整失败（空/非法）原样返回（trim 后）。
    public static string FormatTimeHHmm(string text)
    {
        var s = (text ?? "").Trim();
        if (s == "") return "";
        if (DateTime.TryParseExact(s, new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("HH:mm", CultureInfo.InvariantCulture);
        return s;
    }

    // 配置里的日期一律「公历 yyyy-MM-dd」，与用户的区域设置无关——格式化与解析都钉死 InvariantCulture。
    // 少钉一处就会出事：泰历(th-TH)/回历(ar-SA)/波斯历(fa-IR) 区域下不带 culture 的 ToString("yyyy-MM-dd")
    // 写出的是 2569 / 1448 / 1405 年，校验看着合法，引擎按公历解析后那条提醒永远不到期。
    // 与 FormatTimeHHmm 同样的理由集中在此一处：编辑器、日期选择器、引擎共用一份，不留手抄漂移的空间。
    public const string DatePattern = "yyyy-MM-dd";

    public static string FormatDate(DateTime date) => date.ToString(DatePattern, CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? text, out DateTime date)
        => DateTime.TryParseExact((text ?? "").Trim(), DatePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
