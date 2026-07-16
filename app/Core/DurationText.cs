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
}
