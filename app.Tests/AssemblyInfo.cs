// StopSignal 是进程级单例（急停信号），多个测试类都会 Request/Clear 它。xUnit 默认并行跑测试类会导致
// 一个类的 Request 污染另一个类正在测的 InterruptibleSleep。故关闭并行、串行执行。
// 注：这里原本写着「整套仅 ~100ms，代价可忽略」——那是套件还很小时候的数字，早已不成立：
// 893 条测试实测稳定在 ~14s。理由（StopSignal 是进程级单例）没变，但「代价可忽略」这半句已经不真。
// 真嫌慢的话，正路是把 StopSignal 的进程级单例改成可注入的实例、让相关测试各自持有一份，
// 而不是直接把并行打开——那只会换来偶发的假红。
// xunit.v3 4.0 把「关掉并行」从 CollectionBehavior 挪到了新的 Parallelization 特性上：
// CollectionBehaviorAttribute.DisableTestParallelization 已废弃（4.0 里是编译期错误），
// 现在写 [assembly: Parallelization(Mode = ParallelMode.None)]。语义一样：整套串行。
// （特性在 Xunit.v3 命名空间，枚举在 Xunit.Sdk —— 两个都不是 Xunit 根命名空间。）
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

// 显示类 helper（StepDisplay/ReminderDisplay/StartupLabels）文案已 i18n，测试断言的是中文源 →
// 默认 UI 文化设为 zh-CN。StringsTests 会自行按需切文化并还原。
internal static class TestCultureInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Init()
    {
        var zh = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        System.Globalization.CultureInfo.CurrentUICulture = zh;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = zh;
    }
}
