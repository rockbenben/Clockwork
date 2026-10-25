namespace Clockwork.Core;

// 提醒正文的显示上限：卡片和弹窗都按当前屏幕工作区算，而不是写死一个像素数。
// 写死值在 1366×768@150%（工作区约 464 DIP）和 4K 上不能同时合适：小屏会把
// 「是/否/稍后」挤出屏，大屏又只显示一条窄带。上限之外的内容由 ScrollViewer 滚动。
public static class MessageAreaSizing
{
    public const double MinHeight = 120;
    public const double MaxHeight = 320;
    public const double Fallback = 240;   // 拿不到工作区时退回 XAML 里同量级的保守值

    public static double ForWorkArea(double workAreaHeight)
    {
        if (!double.IsFinite(workAreaHeight) || workAreaHeight <= 0) return Fallback;
        return Math.Clamp(workAreaHeight * 0.35, MinHeight, MaxHeight);
    }
}
