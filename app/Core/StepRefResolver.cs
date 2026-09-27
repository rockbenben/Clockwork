using System.Collections.Generic;
using System.Linq;
using Clockwork.I18n;

namespace Clockwork.Core;

// 一次点击对着「引用清单项」格子的三种结局：目标可用 / 目标不存在 / 目标已禁用。
// Skip 复用 GroupSkip 这个载体（键 + 参数 + 良性标记，见其定义处的说明）——
// 分类口径与动作组引用一字不差，再造一个同形的 record 只会让调用方多记一套字段名。
public sealed record StepRefTarget(LaunchStep? Step, GroupSkip? Skip);

// 「引用启动清单项」（kind="step"）的解析与分类。仿 ActionGroupResolver：
// 「找不到 / 被禁用」的措辞与良性判定集中在这里定一次，调用方只管转发，
// 不各自决定「这算不算故障」。键走 resx（气泡按界面语言、日志固定英文）。
public static class StepRefResolver
{
    public static LaunchStep? Resolve(IEnumerable<LaunchStep>? items, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || items == null) return null;
        foreach (var s in items) if (s != null && s.Id == id) return s;
        return null;
    }

    // 与 ResolveForRun（组版）同一张三分表：不存在=坏配置 Warn；禁用=正常状态 Info。
    // 「找不到」不是理论情况：清单项能被删除，而面板格子的联动清理只在主界面删除路径上做——
    // 手改 json、或导入一份带着已死引用的配置，都走得进这里。
    public static StepRefTarget ResolveForRun(IEnumerable<LaunchStep>? items, string id)
    {
        var target = Resolve(items, id);
        if (target == null) return new StepRefTarget(null, new GroupSkip("Skip_RefNotFound", null, false));
        if (!target.Enabled)
            return new StepRefTarget(null, new GroupSkip("Skip_RefDisabled", StepDisplay.StepTitle(target), true));
        return new StepRefTarget(target, null);
    }

    /// <summary>面板全部页面上引用这条清单项的格子数（删除确认的「谁在引用它」）。</summary>
    public static int CountPanelRefs(IReadOnlyList<PanelPage>? pages, string itemId)
    {
        if (pages == null || string.IsNullOrWhiteSpace(itemId)) return 0;
        int n = 0;
        foreach (var p in pages)
            foreach (var s in p?.Steps ?? new List<LaunchStep>())
                if (s != null && s.Kind == StepDisplay.RefKind && s.RefId == itemId) n++;
        return n;
    }

    /// <summary>把所有引用该条目的格子从页上摘掉，返回摘掉的个数（清单条目删除时的联动清理，
    /// 与组删除同一条契约：确认框承诺了「一并清除」，这里就必须真清干净）。</summary>
    public static int RemovePanelRefs(IReadOnlyList<PanelPage>? pages, string itemId)
    {
        if (pages == null || string.IsNullOrWhiteSpace(itemId)) return 0;
        int removed = 0;
        foreach (var p in pages)
        {
            if (p?.Steps == null) continue;
            var gone = p.Steps.Where(s => !(s != null && s.Kind == StepDisplay.RefKind && s.RefId == itemId)).ToList();
            // 整表替换而非就地 RemoveAll：与组删除同一理由——后台可能正拿着旧列表在枚举。
            removed += p.Steps.Count - gone.Count;
            p.Steps = gone;
        }
        return removed;
    }
}
