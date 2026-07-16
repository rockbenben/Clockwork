using System.Globalization;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 提醒调度的纯决策逻辑。不掷随机、不弹窗——'arm' 交上层据 base+延迟算 pendingFireAt。
public static class ReminderEngine
{
    public const int MaxRepeats = 20;

    // HH:mm 校验（编辑器与 repeatUntil 判定共用一份，避免两处手抄漂移）。宽松输入先经 DurationText.FormatTimeHHmm 规整。
    public const string HhmmPattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    // 今天是否落在提醒周期上。daily=星期过滤(空=每天)；everyNDays=从 anchorDate 取模(防漂移)；monthly=每月第N天(夹月末)。
    public static bool IsRecurrenceDueToday(Reminder r, DateTime today)
    {
        switch (r.RecurType)
        {
            case "everyNDays":
                int n = r.IntervalDays < 1 ? 1 : r.IntervalDays;
                if (string.IsNullOrWhiteSpace(r.AnchorDate)) return true;
                if (!DateTime.TryParseExact(r.AnchorDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var anchor)) return true;
                anchor = anchor.Date;
                if (today.Date < anchor) return false;
                return ((today.Date - anchor).Days % n) == 0;
            case "monthly":
                int d = r.MonthlyDay; if (d < 1) d = 1; if (d > 31) d = 31;
                int eff = Math.Min(d, DateTime.DaysInMonth(today.Year, today.Month));
                return today.Day == eff;
            default:
                var days = r.Days ?? new();
                if (days.Count == 0) return true;
                return days.Contains(StepCondition.IsoDayOfWeek(today));
        }
    }

    // 登录时刻小时是否满足提醒的 startup 限制。before=登录小时<阈值; after=登录小时>=阈值; 其它=不限。
    public static bool IsStartupHourOk(Reminder r, DateTime startTime)
    {
        var mode = r.StartupHourMode;
        if (mode != "before" && mode != "after") return true;
        int loginHour = startTime.Hour;
        return mode == "before" ? loginHour < r.StartupHour : loginHour >= r.StartupHour;
    }

    // 弹窗有效自动关闭秒数：显式 popupTimeoutSeconds>0 优先；否则重复型默认 60s；否则 0(永不自动关)。
    public static int PopupTimeoutSeconds(Reminder r)
    {
        // 封顶 24h：下游把秒 *1000 / TimeSpan.FromSeconds 喂给 DispatcherTimer，无上限的大值会越界溢出/抛异常。
        if (r.PopupTimeoutSeconds > 0) return Math.Min(r.PopupTimeoutSeconds, 86_400);
        if (r.RepeatMinutes > 0) return 60;
        return 0;
    }

    // 触发判定纯函数。原地改 st，返回 action ∈ none|arm|fire 与 base（arm 时非空）。
    // uptimeMinutes：程序启动那一刻的系统开机分钟数（-1=未知则不做开机时段门控）。
    // existedAtStartup：本提醒在程序启动时就已在配置里（true）还是本次运行中途才新建（false）——只有前者才允许「错过必补」，
    // 避免"到点后才新建一条早时刻提醒"被立刻补弹；而启动时就存在、因休眠/关机错过的会照常补。
    public static ReminderDecision Decide(Reminder r, DateTime now, DateTime startTime, ReminderState st, int uptimeMinutes = -1, bool existedAtStartup = true)
    {
        if (!r.Enabled) return new("none", null);

        // 稍后(snooze)：一次性、显式请求，优先于周期门——跨午夜落到非周期日也照发一次，到点即清。
        if (st.SnoozeUntil is DateTime snooze)
        {
            // 过期的稍后(早于今天，多为跨日停机后从盘载入的旧 snooze)：丢弃不补，继续走正常判定，不在开机时突然弹一条几天前的。
            if (snooze.Date < now.Date) { st.SnoozeUntil = null; }
            else if (now >= snooze) { st.SnoozeUntil = null; return new("fire", null); }
            else return new("none", null);
        }

        // 重复到点优先。在途重复是"已在有效周期日触发过"的延续，像 snooze 一样跨周期日也把窗口跑完——
        // 否则 23:50→次日00:30 这类跨午夜重复会在午夜被下面的周期过滤清掉（对限定星期的提醒尤甚）。
        // 受 repeatUntil 截止 + MaxRepeats 约束，有界，不会漂到别的周期。故放在周期过滤之前。
        if (st.NextRepeatAt is DateTime nr)
        {
            if (now >= nr) { st.NextRepeatAt = null; return new("fire", null); }
            return new("none", null);
        }

        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // 已 arm，等延迟到点。与 snooze/repeat 同理放在周期过滤之前：arm 只发生在有效周期日，
        // 延迟把到点推过午夜（如周五 23:58 + 随机延迟）不该被次日的周期过滤抹掉——那是已到期、已武装的一次触发。
        // 有意不设「过期跨日丢弃」守卫（曾加过又撤销）：pending 是会话态不落盘，过期只发生在常驻+休眠场景，
        // 唤醒后晚发一次即旧版行为；丢弃反而制造三种静默丢失——「登录时」提醒会陷入武装/丢弃死循环
        // （base 固定为启动时刻、StartupHandled 只在触发时置位）、23:55 武装后合盖跨午夜整周丢失、
        // 多天错峰延时被一再顺延永不触发。
        if (st.PendingFireAt is DateTime pf)
        {
            if (now >= pf)
            {
                st.PendingFireAt = null; st.LastFiredDate = today;
                if (r.Trigger == "startup") st.StartupHandled = true;
                return new("fire", null);
            }
            return new("none", null);
        }

        // 周期过滤。走到这里 pending/repeat/snooze 都已在上面处理并返回，无需再清。
        if (!IsRecurrenceDueToday(r, now)) return new("none", null);

        // 3) 首发判定
        if (r.Trigger == "startup")
        {
            // 「登录时」只认真正的开机时段：开机超过 startupWithinMinutes 分钟后再启动本程序不算登录。0=不限；uptime<0 不门控。
            int limit = r.StartupWithinMinutes;
            if (limit > 0 && uptimeMinutes >= 0 && uptimeMinutes > limit)
            {
                st.StartupHandled = true;   // 本次运行不再反复判定
                return new("none", null);
            }
            if (!st.StartupHandled && now >= startTime && IsStartupHourOk(r, startTime)) return new("arm", startTime);
            return new("none", null);
        }

        if (st.LastFiredDate == today) return new("none", null);
        // time 可能来自手改 json：单位数小时（"9:00"）宽容接受，其余非法格式按 none（该条不触发，其余提醒不受牵连）。
        if (!DateTime.TryParseExact($"{today} {r.Time}", new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var baseTime))
            return new("none", null);
        // 取整到分钟比较：now 带秒/毫秒，否则 grace=0 永远不等于整分的 base → 永不触发。
        var nowMin = now.Date.AddHours(now.Hour).AddMinutes(now.Minute);
        if (nowMin < baseTime) return new("none", null);   // 还没到点
        // 错过必补：到点后不设窗口上限补弹——覆盖休眠/关机/程序没跑而错过的（回来照弹）。
        // 仅限"启动时就存在"的提醒(existedAtStartup)，排除"到点后才新建"的，免得刚建一条 09:00 的下午就突然弹。
        // 靠持久化的 LastFiredDate 判"当天没弹过"，故重启不会重复弹。
        if (r.CatchUpIfMissed && existedAtStartup) return new("arm", baseTime);
        // 否则只在 [base, base+grace] 窗口内弹，过了就算错过。
        int grace = r.GraceMinutes < 0 ? 0 : r.GraceMinutes;
        if (nowMin <= baseTime.AddMinutes(grace)) return new("arm", baseTime);
        return new("none", null);
    }

    // 弹窗后推进周期重复状态。确认(yes/no/ok)=停；未确认('')按 repeatMinutes 排下次，受 repeatUntil 截止与 MaxRepeats 约束。
    // 「稍后」由 Snooze 单独处理，不经此。
    public static ReminderState UpdateAfterFire(Reminder r, DateTime now, string result, ReminderState st)
    {
        if (result is "yes" or "no" or "ok") { st.NextRepeatAt = null; st.RepeatCount = 0; return st; }

        int rep = r.RepeatMinutes;
        if (rep <= 0) { st.NextRepeatAt = null; return st; }

        int count = st.RepeatCount + 1;
        if (count >= MaxRepeats) { st.NextRepeatAt = null; st.RepeatCount = 0; return st; }

        var next = now.AddMinutes(rep);
        // 两个时刻都先规整：手改 json 的 "9:30" 会过不了严格 HH:mm 校验、整个截止判定被静默跳过；
        // "9:00" 的序数比较会把 "10:30"<"9:00" 误判成跨午夜、催促窗被错误顺延一天。
        var untilStr = DurationText.FormatTimeHHmm(r.RepeatUntil ?? "");
        if (Regex.IsMatch(untilStr, HhmmPattern))
        {
            var until = DateTime.ParseExact($"{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {untilStr}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            // 仅当 repeatUntil 时刻早于提醒自身触发时刻（窗口真跨午夜，如 23:50→00:30）才把截止顺延到次日。
            // 若 repeatUntil 只是"今天已过"（如触发被延时推过当天截止），仍按原样停——不误判为次日、避免刷屏。
            if (until < now && string.CompareOrdinal(untilStr, DurationText.FormatTimeHHmm(r.Time)) < 0) until = until.AddDays(1);
            if (next > until) { st.NextRepeatAt = null; st.RepeatCount = 0; return st; }
        }
        st.RepeatCount = count;
        st.NextRepeatAt = next;
        return st;
    }

    // 用户「稍后」N 分钟：钉一次性 snoozeUntil（独立于周期），清掉进行中的周期重复。N<1 视作默认 10 分钟。
    // 保留 repeatCount：snooze 后续上重复仍受 MaxRepeats 约束。
    public static ReminderState Snooze(ReminderState st, DateTime now, int minutes)
    {
        if (minutes < 1) minutes = 10;
        st.NextRepeatAt = null;
        st.SnoozeUntil = now.AddMinutes(minutes);
        return st;
    }
}

// 触发决策结果：action ∈ none|arm|fire；base 在 arm 时为触发基准时刻，供上层据以算 pendingFireAt（含随机延迟）。
public sealed record ReminderDecision(string Action, DateTime? Base);
