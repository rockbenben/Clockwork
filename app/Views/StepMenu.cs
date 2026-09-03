using System.Linq;
using System.Windows.Controls;
using Clockwork.Core;
using Clockwork.I18n;

namespace Clockwork.Views;

// 「新增 ▾」步骤菜单：按用户意图分节（打开 / 控制程序 / 系统与声音 / 流程），
// 启动清单与动作组编辑器共用——分节结构在 StepDisplay.StepKindSections，只此一份。
//
// **每一节是一个子菜单**，顶层只有意图词。这不是排版偏好，是量出来的（见 DevChecks.CheckStepMenuFits）：
// 平铺时 15 个类型 + 5 个小节头 + 5 条分隔线是一堵 26 行、721px 高的墙，
// 而最紧的那档工作区只有 566px（1366×768 @125%，小本的出厂设置）——菜单不像窗口，
// 长过屏幕不会报错，只会悄悄长出上下滚动箭头，把末尾几项藏起来。
//
// 顺带它把分节这件事做实了：分节本来就是为了让人先想「我要对什么做事」再想机制名
//（不然新用户不知道「关掉微信」该点「窗口动作」还是「发送按键」）。机制名藏到悬停之后，
// 那个顺序就是强制的，而不是靠小节头提示。也随类型增长：这一轮加了两种就撞线，再加还是这几行。
public static class StepMenu
{
    // pick：选中一项后的回调，收 (kind, seed)。seed 为 null 表示「从这个类型新建」——
    // 交给步骤编辑器按类型填默认值；非 null 是「常用」里预填好的那一份，编辑器直接拿它当初值。
    // firstOpenItem：插在「打开」节最前面的额外项——启动清单把「从开始菜单选择…」放这儿，
    // 它语义上就是「打开」的一种，且是零配置那条路，理应排最前。
    // presets：是否显示「常用」那一节。开机清单关掉它（见 StepDisplay.CommonPresets 的说明）。
    public static ContextMenu Build(Action<string, LaunchStep?> pick, MenuItem? firstOpenItem = null, bool presets = true)
    {
        var menu = new ContextMenu();
        // 「常用」排最前：它是预填好的成品，比「先挑一个类型再自己配」短得多。
        if (presets)
        {
            var common = new MenuItem { Header = Strings.Get("Menu_SecCommon") };
            foreach (var item in PresetItems(pick)) common.Items.Add(item);
            menu.Items.Add(common);
        }
        foreach (var (sectionKey, _) in StepDisplay.StepKindSections)
        {
            var items = SectionItems(sectionKey, pick, firstOpenItem).ToList();
            // 只有一项的节直接摆在顶层，不套一层子菜单——为一项开一层，那个 ▸ 是在承诺
            // 里面还有别的东西，而展开只有它自己。（「动作」节就是这一种。）
            if (items.Count == 1) { menu.Items.Add(items[0]); continue; }
            var sec = new MenuItem { Header = Strings.Get(sectionKey) };
            foreach (var it in items) sec.Items.Add(it);
            menu.Items.Add(sec);
        }
        return menu;
    }

    /// <summary>某一节里那一串菜单项，每次调用都是新的一份。</summary>
    //
    // 与 PresetItems 同一个理由单拎出来：**子菜单也会长过屏幕**，而它长过去的表现同样是
    // 无声的滚动箭头。冒烟自查拿这批项另建一个菜单去量（DevChecks.CheckStepMenuFits），
    // 量的必须是真的这些项——照着行数估算迟早和实际对不上。
    public static IEnumerable<MenuItem> SectionItems(string sectionKey, Action<string, LaunchStep?> pick,
                                                     MenuItem? firstOpenItem = null)
    {
        // 「从开始菜单选择…」语义上就是「打开」的一种，跟着进那一节，排最前（零配置那条路）。
        if (sectionKey == "Menu_SecOpen" && firstOpenItem != null) yield return firstOpenItem;
        var section = StepDisplay.StepKindSections.FirstOrDefault(x => x.SectionKey == sectionKey);
        foreach (var kind in section.Kinds ?? Array.Empty<string>())
        {
            var k = kind;
            var mi = new MenuItem { Header = StepDisplay.StepKindLabel(k) };
            mi.Click += (_, _) => pick(k, null);
            yield return mi;
        }
    }

    /// <summary>「常用」里那一串菜单项，每次调用都是新的一份（一个 MenuItem 只能挂一个父级）。
    /// 单拎出来的理由同 <see cref="SectionItems"/>：要能被冒烟自查量。</summary>
    public static IEnumerable<MenuItem> PresetItems(Action<string, LaunchStep?> pick)
    {
        foreach (var (labelKey, kind, arg) in StepDisplay.CommonPresets)
        {
            var (k, a) = (kind, arg);
            var item = new MenuItem { Header = Strings.Get(labelKey) };
            item.Click += (_, _) => pick(k, StepDisplay.MakePreset(k, a));
            yield return item;
        }
    }
}
