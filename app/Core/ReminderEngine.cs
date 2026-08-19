using System.Globalization;
using System.Text.RegularExpressions;

namespace Clockwork.Core;

// 提醒调度的纯决策逻辑。不掷随机、不弹窗——'arm' 交上层据 base+延迟算 pendingFireAt。
public static class ReminderEngine
{
    public const int MaxRepeats = 20;

    // 无人应答的弹窗兜底。弹窗是模态的，其嵌套消息循环期间 _reminderTickBusy 会挡掉所有其他提醒——
    // 一个永不自动关的弹窗等于把整个提醒引擎冻结到有人点为止。故弹窗一律有超时：
    // 用户没设「自动关闭」的，挂满 UnattendedPopupSeconds 收起；没配重复催促的提醒，超时视作
    // 「稍后 UnattendedSnoozeMinutes 分钟」自动重发——SnoozeUntil 落盘、重启不丢、跨天由 Decide 丢弃
    // （「错过必补」的提醒例外：跨天补发一次）、提醒被删后由孤儿清理回收。
    // 绝不把无人应答记成已处理/未确认然后静默丢弃。
    // 但重发也不是无限的：连续 MaxAutoSnoozes 轮没人理，就不再弹模态窗，改由 App 挂一张常驻卡片
    // 等人回来（见 AutoSnooze）——「不静默丢弃」要的是提醒仍在，不是非得一直抢焦点。
    // 配了重复催促的提醒超时仍走 UpdateAfterFire 按用户节奏续催（受 repeatUntil/MaxRepeats 约束）。见 App.FireReminder。
    public const int UnattendedPopupSeconds = 60;
    public const int UnattendedSnoozeMinutes = 10;

    // 连续无人应答的自动稍后上限。闹钟类系统的同款边界（iOS 闹钟响 ~15 分钟即停、AOSP 时钟默认 10 分钟静音）：
    // 无人应答说明人不在，对没人看的屏幕反复弹模态窗没有意义。
    // 到顶后由 App 降级成常驻卡片——不再抢焦点，但提醒仍挂在屏上等人回来，投递保证不丢。
    //
    // 这是**轮数**上限，不是时长上限：默认（自动关闭兜底 60 秒）下 6 轮 × 10 分钟稍后 ≈ 1 小时，
    // 但把「自动关闭」设成 1800 秒的话，每轮还要先挂 30 分钟模态窗，6 轮就是 4 小时——而且那 30 分钟里
    // _reminderTickBusy 一直占着，其余提醒全部排队。真要按墙钟封顶得再落一个「本串起始时刻」，
    // 会话态还是耐久态又是一串取舍；配长自动关闭本来就是少数派，先按轮数封，文档如实写清两种口径。
    public const int MaxAutoSnoozes = 6;

    // HH:mm 校验（编辑器与 repeatUntil 判定共用一份，避免两处手抄漂移）。宽松输入先经 DurationText.FormatTimeHHmm 规整。
    public const string HhmmPattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    // 「哪一天」的统一键。LastFiredDate / SkippedDate / PendingForDate 三者都是它，且都靠 == 比较，
    // 其中 SkippedDate 还要跨进程比（落盘的值 vs 运行时现算的值）——格式各处手抄一份，
    // 哪天有人写成 "yyyy/MM/dd" 或漏了 InvariantCulture，跳过就会在某些区域设置下静默失效。只此一份。
    public static string DateKey(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // 今天是否落在提醒周期上。daily=星期过滤(空=每天)；everyNDays=从 anchorDate 取模(防漂移)；monthly=每月第N天(夹月末)。
    public static bool IsRecurrenceDueToday(Reminder r, DateTime today)
    {
        switch (r.RecurType)
        {
            case "everyNDays":
                int n = r.IntervalDays < 1 ? 1 : r.IntervalDays;
                // 空/非法 anchor=每天都在周期上（宽容兜底）。按时间触发的提醒经编辑器保存时留空已被落成今天，
                // 故走到这条兜底的是手改的 json、以及「登录时/事件」身上残留的 everyNDays（那两种本就不看周期）。
                if (string.IsNullOrWhiteSpace(r.AnchorDate)) return true;
                if (!DateTime.TryParseExact(r.AnchorDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var anchor)) return true;
                anchor = anchor.Date;
                if (today.Date < anchor) return false;
                return ((today.Date - anchor).Days % n) == 0;
            case "monthly":
                int d = r.MonthlyDay; if (d < 1) d = 1; if (d > 31) d = 31;
                int eff = Math.Min(d, DateTime.DaysInMonth(today.Year, today.Month));
                return today.Day == eff;
            case "once":
                // 空/非法日期按「今天」——新建未填日期的 once 应当天生效，而不是永不触发。
                if (string.IsNullOrWhiteSpace(r.OnceDate)) return true;
                if (!DateTime.TryParseExact(r.OnceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var od)) return true;
                return today.Date == od.Date;
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

        // 手动「今天不再提醒」：挡在所有分支之前——稍后 / 催促 / 循环 / 已武装的延迟都算"今天的事"，
        // 用户说的是这一整天不想被这条打扰，不是只掐掉下一次。在途链已在 SkipToday 里一并清干净，
        // 这里只需拦住当天剩余的重新排程。明天此值不再等于今天，一切照常恢复。
        // today 在此提前算出：下面首发判定也要用同一份，两处各格式化一次既浪费也让日期格式有两份要同步。
        var today = DateKey(now);
        if (st.SkippedDate == today) return new("none", null);

        // 稍后(snooze)：一次性、显式请求，优先于周期门——跨午夜落到非周期日也照发一次，到点即清。
        if (st.SnoozeUntil is DateTime snooze)
        {
            // 过期的稍后(早于今天，多为跨日停机后从盘载入的旧 snooze)：默认丢弃不补，继续走正常判定，
            // 不在开机时突然弹一条几天前的。「错过必补」例外——挂着的稍后（含无人应答的自动稍后）
            // 就是一次没送达的投递，睡眠/关机跨了天不该把它无声吞掉；该开关正是用户对「错过怎么办」
            // 的表态，补发一次。（App 启动时的陈旧稍后清理对这类提醒同样放行，两处口径一致。）
            // 事件触发除外：编辑器为了往返保真会把隐藏的「错过必补」原样存回（把时间型改成事件型时它还在），
            // 而事件的语义是「没发生就是没发生」——凭一个残留勾选把昨晚的稍后在今早（比如满电插电时）诈尸成
            // 「电量偏低」提醒，是在补一个从未发生的事件。
            // 荒谬的未来值一并丢弃：稍后最多是几分钟到几小时（跨午夜也就落到明天），
            // 落盘里出现「一年后」只可能是当时的系统时间不对（虚拟机还原 / 主板电池没电 / 手动改错）。
            // 不丢的话，时间校回来之后这个未来时刻会把这条提醒的所有分支永久挡死——界面上看不出任何异常，
            // 编辑它也没用（迁移会把 SnoozeUntil 原样带到新 id），只能手删状态文件才能恢复。
            if (snooze > now.AddDays(1)) { st.SnoozeUntil = null; }
            else if (snooze.Date < now.Date) { st.SnoozeUntil = null; if (r.CatchUpIfMissed && !ReminderEvent.IsEvent(r.Trigger)) return new("fire", null); }
            else if (now >= snooze) { st.SnoozeUntil = null; return new("fire", null); }
            else return new("none", null);
        }

        // 重复到点优先。在途重复是"已在有效周期日触发过"的延续，像 snooze 一样跨周期日也把窗口跑完——
        // 否则 23:50→次日00:30 这类跨午夜重复会在午夜被下面的周期过滤清掉（对限定星期的提醒尤甚）。
        // 受 repeatUntil 截止 + MaxRepeats 约束，有界，不会漂到别的周期。故放在周期过滤之前。
        if (st.NextRepeatAt is DateTime nr)
        {
            // 先看这条链的截止有没有过。不查的话，23:50 起的链排到 00:05、机器合盖睡过去，
            // 次日 08:00 唤醒时它照样引爆，而 UpdateAfterFire 又会按当时的 now 把截止重新解析成
            // 「明天 00:30」，于是接着每 15 分钟催一次直到 MaxRepeats——催了一个上午。
            if (st.NextRepeatUntil is DateTime until && now > until) { EndRepeatChain(st); }
            else if (now >= nr) { st.NextRepeatAt = null; return new("fire", null); }
            else return new("none", null);
        }

        // 循环运行到点：确认不终止循环（与催促的根本区别）。与催促同侧、放在周期过滤与 LastFiredDate 之前——
        // 放后面的话当天第二轮会被「今天已弹过」挡掉，循环永远只跑一次。
        // 跨天陈旧轮次丢弃（与 snooze 陈旧口径一致；漏掉的轮询没有补发价值，「错过必补」不使其复活——
        // 它作用于 base 时刻的当天首发，下面照常判定）；当天已过期的到点即发（休眠唤醒后补上本轮，随后照常续排）。
        if (st.NextIntervalAt is DateTime ni)
        {
            if (ni.Date < now.Date) st.NextIntervalAt = null;
            else if (now >= ni) { st.NextIntervalAt = null; return new("fire", null); }
            else return new("none", null);
        }

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
                st.PendingFireAt = null;
                // 记「这次是为哪一天准备的」，而不是「引爆发生在哪一天」。23:59 的每日提醒在 23:59:xx 武装、
                // 下一跳落到 00:00:xx 才引爆；记成引爆当天的话，次日 23:59 会被下面的「今天已弹过」挡掉，
                // 每日提醒就退化成隔天一次，而且开了「错过必补」也救不回来（那条检查在补发之前）。
                st.LastFiredDate = st.PendingForDate != "" ? st.PendingForDate : today;
                st.PendingForDate = "";
                if (r.Trigger == "startup") st.StartupHandled = true;
                return new("fire", null);
            }
            return new("none", null);
        }

        // 事件触发（空闲/解锁/锁屏/唤醒/插拔电源/低电量）到这里为止：首发不由计时器判定，而是在事件发生的
        // 当下由 App 经 ReminderEvent.ShouldFire 直接触发。再往下会被当成 time 型——r.Time 默认 "09:00"，
        // 于是一条「解锁时」提醒每天早上九点还会自己弹一次。
        //
        // 位置是刻意选在 PendingFireAt 之后、而不是方法开头：上面四块（稍后 / 催促 / 循环 / 已武装的延迟）
        // 对事件型同样成立，它们是「已经响过 / 已经排上」之后的续接。挡在开头的话，事件型任务上点一次
        // 「稍后 10 分钟」就等于把它扔了；挡在 pending 之前的话，「解锁后延迟 5 分钟」这类武装好的触发
        // 永远不会到点——App.FireEventNow 对带延迟的事件正是设 PendingFireAt、交给计时器来引爆的。
        if (ReminderEvent.IsEvent(r.Trigger)) return new("none", null);

        // 周期过滤。走到这里 pending/repeat/snooze 都已在上面处理并返回，无需再清。
        // 只对「按时间」触发生效：事件在上一行已经返回，而「登录时」若也照周期过滤，就会被编辑器
        // 隐藏起来的旧 recurType 静默钉死（详见 ReminderEvent.UsesRecurrence）——而编辑器注释、
        // 列表的「每次登录」、文档三处都写着它不看周期。判据别在这里手写，走那个共享谓词。
        if (ReminderEvent.UsesRecurrence(r.Trigger) && !IsRecurrenceDueToday(r, now)) return new("none", null);

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
            if (!st.StartupHandled && now >= startTime && IsStartupHourOk(r, startTime)) return Arm(st, startTime);
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
        if (r.CatchUpIfMissed && existedAtStartup) return Arm(st, baseTime);
        // 否则只在 [base, base+grace] 窗口内弹，过了就算错过。
        int grace = r.GraceMinutes < 0 ? 0 : r.GraceMinutes;
        if (nowMin <= baseTime.AddMinutes(grace)) return Arm(st, baseTime);
        return new("none", null);
    }

    // 武装：记下这次触发的基准日，供引爆时写 LastFiredDate（引爆可能已跨过午夜，见 pending 分支）。
    // 三条 arm 出口都必须经这里，漏一条那条路径就又会把日期记错。
    private static ReminderDecision Arm(ReminderState st, DateTime baseTime)
    {
        st.PendingForDate = DateKey(baseTime);
        return new("arm", baseTime);
    }

    // 催促链结束：把链上三个字段一起清干净，别漏掉截止（留着会误判下一条链）。
    private static void EndRepeatChain(ReminderState st)
    {
        st.NextRepeatAt = null;
        st.NextRepeatUntil = null;
        st.RepeatCount = 0;
    }

    // 弹窗后推进周期重复状态。确认(yes/no/ok)=催促停；未确认('')按 repeatMinutes 排下次，受 repeatUntil 截止与 MaxRepeats 约束。
    // 循环运行(intervalMinutes)与催促是两条链：催促链结束的每个出口（确认/未配催促/达上限/过截止）都排下一轮循环——
    // 确认不终止循环正是它与催促的区别（静默组固定返回 "ok"，静默任务的周期轮询靠这条路径成立）。
    // 链在途（NextRepeatAt 刚排上）不排循环，两条链同时挂会互相插队刷屏。「稍后」由 Snooze 单独处理，不经此。
    public static ReminderState UpdateAfterFire(Reminder r, DateTime now, string result, ReminderState st)
    {
        if (result is "yes" or "no" or "ok") { EndRepeatChain(st); return ScheduleInterval(r, now, st); }

        int rep = r.RepeatMinutes;
        if (rep <= 0) { EndRepeatChain(st); return ScheduleInterval(r, now, st); }

        int count = st.RepeatCount + 1;
        if (count >= MaxRepeats) { EndRepeatChain(st); return ScheduleInterval(r, now, st); }

        var next = now.AddMinutes(rep);
        // 截止只在开链时解析一次、之后钉住（见 ReminderState.NextRepeatUntil）：每次续排都按当时的 now
        // 重新解析的话，机器睡过整个窗口再唤醒时，「是否跨午夜」会把截止一路往后推、链永远结束不了。
        var until = st.NextRepeatUntil ??= ResolveRepeatUntil(r, now);
        if (until != null && next > until) { EndRepeatChain(st); return ScheduleInterval(r, now, st); }
        st.RepeatCount = count;
        st.NextRepeatAt = next;
        return st;
    }

    // 催促链的绝对截止时刻；没配 repeatUntil（或手改 json 写成非法格式）返回 null=不设截止，只受 MaxRepeats 约束。
    private static DateTime? ResolveRepeatUntil(Reminder r, DateTime now)
    {
        // 先规整：手改 json 的 "9:30" 会过不了严格 HH:mm 校验、整个截止判定被静默跳过；
        // "9:00" 的序数比较会把 "10:30"<"9:00" 误判成跨午夜、催促窗被错误顺延一天。
        var untilStr = DurationText.FormatTimeHHmm(r.RepeatUntil ?? "");
        if (!Regex.IsMatch(untilStr, HhmmPattern)) return null;
        var until = DateTime.ParseExact($"{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {untilStr}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        // 仅当 repeatUntil 时刻早于提醒自身触发时刻（窗口真跨午夜，如 23:50→00:30）才把截止顺延到次日。
        // 若 repeatUntil 只是「今天已过」（如触发被延时推过当天截止），仍按原样停——不误判为次日、避免刷屏。
        if (until < now && string.CompareOrdinal(untilStr, DurationText.FormatTimeHHmm(r.Time)) < 0) until = until.AddDays(1);
        return until;
    }

    // 催促链结束时排下一轮循环。IntervalUntil 走与 RepeatUntil 同一套规整+校验；空/非法 = 当天 23:59。
    // 循环有意不跨午夜——「直到」的语义就是当天窗口，超截止即本日结束、次日由当天首发重新开链。
    private static ReminderState ScheduleInterval(Reminder r, DateTime now, ReminderState st)
    {
        st.NextIntervalAt = null;
        if (r.IntervalMinutes < 1) return st;
        var next = now.AddMinutes(r.IntervalMinutes);
        var untilStr = DurationText.FormatTimeHHmm(r.IntervalUntil ?? "");
        var until = Regex.IsMatch(untilStr, HhmmPattern)
            ? DateTime.ParseExact($"{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {untilStr}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : now.Date.AddHours(23).AddMinutes(59);
        if (next > until) return st;
        st.NextIntervalAt = next;
        return st;
    }

    // 钉一次性 snoozeUntil（独立于周期），并清掉进行中的周期重复。N<1 视作默认 10 分钟。
    // 手点与自动两条路共用，计数口径各自在下面把关——两处都手抄这三行的话，改一处漏一处。
    private static void SetSnooze(ReminderState st, DateTime now, int minutes)
    {
        if (minutes < 1) minutes = 10;
        st.NextRepeatAt = null;
        st.SnoozeUntil = now.AddMinutes(minutes);
    }

    // 用户手点「稍后」N 分钟。RepeatCount 在两种提醒身上含义不同，清不清也就相反：
    //   配了催促(repeatMinutes>0) → 它是催促链的已催次数：保留，手点稍后不该让这条链重新拿满 MaxRepeats 次额度。
    //   没配催促              → 它是「连续无人应答」计数(见 AutoSnooze)：清零，人在场了，之后再离开重新起算一小时。
    // 这条「人碰过就重新起算」的规则必须和 UpdateAfterFire 里明确应答走的 EndRepeatChain 待在同一层——
    // 分散到调用方的话，下一个 Snooze 调用方就会忘掉它，而 App 那层又没有测试能照到。
    public static ReminderState Snooze(Reminder r, ReminderState st, DateTime now, int minutes)
    {
        SetSnooze(st, now, minutes);
        if (r.RepeatMinutes <= 0) st.RepeatCount = 0;
        return st;
    }

    // 无人应答（弹窗超时）的自动稍后。与手点分开的理由见 Snooze：人的明确决定不设上限，没人理的必须有边界。
    // 返回 false=连续无人应答已达 MaxAutoSnoozes：计数清零、不再排稍后，呈现交回上层（App 降级为常驻卡片）。
    // 计数复用 RepeatCount：走到这里的提醒必然 repeatMinutes<=0（配了催促的超时走 UpdateAfterFire），
    // 该字段在这类提醒身上原本闲置。会话态不落盘：跨重启的链重新拿满一轮额度——宁可多弹一小时，别丢投递。
    public static bool AutoSnooze(ReminderState st, DateTime now, int minutes)
    {
        if (++st.RepeatCount >= MaxAutoSnoozes) { st.RepeatCount = 0; st.SnoozeUntil = null; return false; }
        SetSnooze(st, now, minutes);
        return true;
    }

    // 手动「今天不再提醒」：记下日期，并把当天所有在途链清干净。
    // 清链不是顺手打扫，是必需的：留着一条昨天的 SnoozeUntil，开了「错过必补」的提醒明天会把它当作
    // 一次没送达的投递补弹出来——用户明明说的是"今天别响"，结果第二天早上诈尸。
    // 会话态字段（Pending/Repeat）一并清：跳过之后当天不该还留着一个已武装的引爆时刻。
    // StartupHandled 置位是给「登录时」用的：它不看 SkippedDate 之外的日期，不置位则本次运行内反复判定。
    public static ReminderState SkipToday(ReminderState st, DateTime now)
    {
        st.SkippedDate = DateKey(now);
        st.SnoozeUntil = null;
        st.NextIntervalAt = null;
        st.PendingFireAt = null;
        st.PendingForDate = "";
        st.StartupHandled = true;
        EndRepeatChain(st);
        return st;
    }

    // 「仅一次」触发完成后是否应自动取消勾选：已实际弹过（LastFiredDate 非空）且催促/稍后链都已结束。
    // 立刻停用是错的——Decide 开头就 if(!Enabled) return none，会把已武装的催促链和用户刚点的「稍后」一起掐死。
    // 引擎不改 Reminder（保持纯函数边界）：判定在此，停用动作（Enabled=false + 存盘 + 刷新列表）归 App。
    // 事件与「登录时」都不参与：编辑器为往返保真会把隐藏的周期原样存回，它们身上残留的 "once" 只是
    // 历史配置，若按它把「解锁时」/「登录时」提醒响一次就自动取消勾选，等于替用户悄悄关掉一条还想要的提醒。
    public static bool ShouldDisableAfterOnce(Reminder r, ReminderState st)
        => r.RecurType == "once" && ReminderEvent.UsesRecurrence(r.Trigger) && r.Enabled && !string.IsNullOrEmpty(st.LastFiredDate)
           && st.NextRepeatAt == null && st.SnoozeUntil == null && st.NextIntervalAt == null;
}

// 触发决策结果：action ∈ none|arm|fire；base 在 arm 时为触发基准时刻，供上层据以算 pendingFireAt（含随机延迟）。
public sealed record ReminderDecision(string Action, DateTime? Base);
