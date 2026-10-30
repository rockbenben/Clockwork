using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Clockwork;

// 托盘菜单的**截图开关**（--trayshot <png>）。
//
// 为什么要有它：托盘菜单是本程序最常被看见的一屏，却从来没有被拍过——
// 「字形是不是撞车」「区段标题和项文字对不对齐」「哪几项该有自己的段」全靠读代码猜。
// 而这个菜单是 WinForms 画的，--shots 那套 WPF 离屏渲染罩不住它。
//
// 走的是屏幕上那一条真路径（弹出菜单 → 抓屏 → 立刻关掉），所以画出来的就是用户看到的那张。
// **只抓菜单自己那一块**：抓整屏会把用户别的窗口也拍进文件——那是别人的隐私，不该落盘。
//
// 用法：Clockwork.exe --trayshot <输出 png 路径> [--repair]
// 副作用：屏幕左上角会**闪一下菜单**（约 0.2 秒）。与 --smoke / --selftest 打开真窗同一性质，
// 在别人正用着这台机器时跑，会短暂抢一下焦点。
public partial class App
{
    private static string TrayShotPath(string[] args)
    {
        int i = Array.IndexOf(args, "--trayshot");
        return i >= 0 && i + 1 < args.Length
            ? args[i + 1]
            : Path.Combine(Path.GetTempPath(), "clockwork-traymenu.png");
    }

    public void ProbeTrayMenuShot(string path, string[] args)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayMenuRenderer(),
            BackColor = TrayPalette.Ink,
            Font = new Font("Segoe UI", 9.75f),
            ShowImageMargin = false,
            ShowCheckMargin = false,
        };
        // forShots：把「修复」段那三项摆出来——它们平时只在出问题时才在菜单里，
        // 而平时看不到的那一段，正是最该被看一遍的那一段。
        // 默认**不**摆（拍的就是用户平时看到的那张）；加 --repair 才摆出来。
        TrayIcon.Rebuild(menu, this, null, forShots: args.Contains("--repair"));

        // 弹到屏幕左上角再抓：坐标原点上逻辑像素与物理像素重合，不必换算偏移。
        menu.Show(new Point(0, 0));
        Application.DoEvents();
        System.Threading.Thread.Sleep(150);
        Application.DoEvents();

        // 菜单的尺寸**已经是物理像素**（本进程是 PerMonitorV2，WinForms 在这里报的就是设备像素），
        // 所以不再乘缩放系数——早先乘了一次，抓出来的是菜单外加右边一块别人的窗口。
        int w = Math.Min(SystemInformation.VirtualScreen.Width, menu.Width);
        int h = Math.Min(SystemInformation.VirtualScreen.Height, menu.Height);
        using var bmp = new Bitmap(Math.Max(w, 1), Math.Max(h, 1));
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
        bmp.Save(path, ImageFormat.Png);
        menu.Close();
        menu.Dispose();
        Shutdown(0);
    }
}
