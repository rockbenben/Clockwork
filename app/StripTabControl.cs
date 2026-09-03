using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
// UseWindowsForms 把 WinForms 也加进了隐式全局 using，Panel 两边同名（同别处那批别名）。
using Panel = System.Windows.Controls.Panel;

namespace Clockwork;

// 标签条右端挂常驻控件（急停按钮，见 Theme.xaml 的 TabControl 模板 + MainWindow 的 TabControl.Tag）的 TabControl。
//
// 只为无障碍而存在：TabControlAutomationPeer 给出的子节点是「各标签头 + 选中页的内容宿主」，
// 模板里加在标签条上的东西一律不在其中——实测那个按钮完全不出现在 UIA 树里，
// 于是给它设的 AutomationProperties.Name 对读屏软件等于没设（纯图形按钮又没有可读文字可退）。
// 这里把 Tag 里那个控件的 peer 补进子节点，让它和其它按钮一样能被读出来、能被 Invoke。
public sealed class StripTabControl : TabControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new StripTabControlPeer(this);

    private sealed class StripTabControlPeer(TabControl owner) : TabControlAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore()
        {
            var children = base.GetChildrenCore() ?? new List<AutomationPeer>();
            // Tag 现在挂的是一排按钮（管理面板 / 管理手势 / 急停），不是单个控件。
            // 只补容器自己的 peer 不够：容器的 peer 未必把子控件展开，读屏软件就会漏掉整排。
            // 逐个补子元素；Tag 仍是单控件时按原样补它自己。
            if (Owner is TabControl { Tag: UIElement extra })
                foreach (var el in extra is Panel panel ? panel.Children.OfType<UIElement>() : new[] { extra })
                    if (UIElementAutomationPeer.CreatePeerForElement(el) is { } peer)
                        children.Add(peer);
            return children;
        }
    }
}
