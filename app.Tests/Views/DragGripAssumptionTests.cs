using Xunit;
// 项目开了 UseWindowsForms，ButtonBase 在 WinForms 与 WPF 下同名（CS0104）——
// 与 DataGridReorder.cs 同一条约定：用别名钉死到 WPF 版本。
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using DataGridRowHeader = System.Windows.Controls.Primitives.DataGridRowHeader;

// DataGridReorder 的起拖闸靠「是不是 ButtonBase」放过复选框与按钮上的点击。
// 行首拖动手柄用的 DataGridRowHeader 恰好也继承自 ButtonBase，所以那道闸里专门为它开了口子。
// 这条假设一旦不成立（框架换了继承链、或有人把那个特判删掉），表现是：手柄画得出来、
// 从别处拖也照常能用，唯独对着手柄拖不动——截图看不出、现有测试也碰不到，只能在这里钉住。
public class DragGripAssumptionTests
{
    [Fact]
    public void RowHeader_is_a_ButtonBase_so_the_drag_guard_must_special_case_it()
        => Assert.True(typeof(ButtonBase).IsAssignableFrom(typeof(DataGridRowHeader)));
}
