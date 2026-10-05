using System.Text.RegularExpressions;

namespace Clockwork.Core;

/// <summary>这一格的图标从哪儿来。</summary>
public enum PanelIconKind
{
    /// <summary>Segoe MDL2 Assets 的一个码位字符（见 <see cref="PanelGlyph"/>）。</summary>
    Glyph,
    /// <summary>一个文件的路径：图片直接用，其余（exe / lnk / 文档）取它的系统图标。</summary>
    Image,
    /// <summary>一枚内置的品牌轮廓，<see cref="PanelIconSpec.Value"/> 是它的 slug（见 <see cref="BrandIcons"/>）。</summary>
    Brand,
}

/// <summary>解析结果。<see cref="Value"/> 对 Glyph 是那个字符，对 Image 是文件路径，对 Brand 是 slug。
/// <see cref="Fallback"/> 恒是一个字形：图片路径可能指向一个已被删掉 / 读不了的文件，
/// 那时得有东西顶上，不能让格子空着——所以「回退画什么」在这一层就定好，不留给视图临场决定。</summary>
public sealed record PanelIconSpec(PanelIconKind Kind, string Value, string Fallback);

// 一格的图标取哪个。纯逻辑，不碰文件系统、不碰 WPF——真正去把文件变成位图的是 Native.IconLoader，
// 真正把 path 变成轮廓的是 Views.IconVisual。
//
// 四档，按优先级：
//   1. 用户指定的图标        —— 一张图片、一个 exe / lnk（借它自己的图标）、一枚 `brand:slack`
//                              品牌轮廓，或四位十六进制点一个 MDL2 字形。绝对优先。
//   2. 目标程序自己的图标    —— 「运行程序」步骤自动从它要启动的那个文件上取。零配置，
//                              而这正是面板好看起来的关键：一屏彩色的真实图标，一眼就能认出哪个是哪个。
//   3. 脚本的线性图标        —— 目标是 .bat / .ps1 / .ahk 这类时不借关联图标（见 PanelGlyph.ForScript）。
//   4. 步骤类型的线性图标    —— 兜底。发送按键、音量、窗口动作这些没有文件可取，用 MDL2 字形。
//
// 于是面板上会自然分成两种质感：**彩色 = 打开一个东西，线描 = 做一件事**。
// 这不是装饰，是把「这一格会发生什么」编进了视觉——找程序时眼睛扫彩色块，找系统动作时扫线描。
// 品牌图标走彩色那一侧：它标的正是一个程序。
public static class PanelIcon
{
    // 四位十六进制 = 直接指定一个 MDL2 码位（E7C4 / 0xE7C4 都收）。
    // 放行这种写法是给「我就想换个字形」的人留的窄门，不必去准备一张图片。
    private static readonly Regex GlyphCode = new(@"^(?:0x)?([0-9a-fA-F]{4,5})$", RegexOptions.Compiled);

    // 能直接解码的图片格式。其余后缀（exe / lnk / ico / 文档）走系统图标提取。
    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico" };

    public static bool IsImageFile(string? path)
    {
        var p = (path ?? "").Trim().TrimEnd('"').TrimStart('"');
        foreach (var e in ImageExt)
            if (p.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <param name="icon">用户填的图标（空=自动）。</param>
    /// <param name="stepKind">步骤类型；组的整格传 null。</param>
    /// <param name="target">「运行程序」/「打开文件或文件夹」步骤的目标，用于自动取图标。</param>
    /// <param name="altTargets">备用路径（多机路径不同时用），与 target 同口径解析。</param>
    public static PanelIconSpec Resolve(string? icon, string? stepKind, string? target = null, string? altTargets = null)
    {
        // 这一格「本来该有」的线描字形。它同时是图片取不到时的回退，所以先算出来。
        var glyph = stepKind == null ? PanelGlyph.Group : PanelGlyph.ForKind(stepKind);
        var custom = (icon ?? "").Trim();
        if (custom.Length > 0)
        {
            var m = GlyphCode.Match(custom);
            // 码位落在代理区（D800–DFFF）时 ConvertFromUtf32 会抛，而这一路上下都没有 catch：
            // 面板每次打开都会走到这里，于是变成开一次崩一次、面板再也出不来，
            // 而用户只不过在图标框里填了 "DEAD" 这样一串看着很合理的十六进制。
            // 那 2048 个值不对应任何字符，当它不是码位、按路径处理即可（路径取不到会回退字形）。
            if (m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber,
                                          System.Globalization.CultureInfo.InvariantCulture, out int cp)
                && (cp < 0xD800 || cp > 0xDFFF))
            {
                var ch = char.ConvertFromUtf32(cp);
                return new PanelIconSpec(PanelIconKind.Glyph, ch, ch);
            }
            // `brand:slack` —— 内置的品牌轮廓。名字认不出来也在这里收口：那一格回退线描字形，
            // 而不是拿 "brand:nope" 去磁盘上找一趟文件图标（那会白等一次 IO 再悄悄回退）。
            if (BrandIcons.IsBrandSyntax(custom))
                return BrandIcons.TryGet(custom, out var brand)
                    ? new PanelIconSpec(PanelIconKind.Brand, brand.Slug, glyph)
                    : new PanelIconSpec(PanelIconKind.Glyph, glyph, glyph);
            // 不是码位就当路径。NormalizeTarget 顺手把引号去掉、把 %USERPROFILE% 这类展开，
            // 与「目标」输入框同一口径——图标路径和程序路径本来就该按同一套规矩解释。
            // 这里不判存不存在——那是磁盘的事，交给 IconLoader；它取不到就回退到字形，
            // 而不是让这一格空着。
            return new PanelIconSpec(PanelIconKind.Image, LaunchTarget.NormalizeTarget(custom), glyph);
        }

        // 自动：只有「运行程序」和「打开文件或文件夹」有文件可取。其余类型（按键 / 音量 / 窗口……）
        // 本来就没有对应的文件，硬去猜一个只会取到错的图标。
        if (stepKind is "app" or "path")
        {
            // blocking: false —— 这一句跑在 UI 线程上（面板每次呼出都会为每个「运行程序」格子走一遍），
            // 而它原本做的是同步的 File.Exists：一条断开的网络盘路径就能把整个界面卡住几十秒，
            // 顺带让低级鼠标钩子超时被卸掉。取图标这件事等不起，也不需要真答案——
            // 猜错了无非是 IconLoader 取不到、回退线描字形，而那条路本身也是不阻塞的。
            var resolved = LaunchTarget.ResolveLaunchTarget(target ?? "", altTargets ?? "", blocking: false);
            // 脚本先于「取文件图标」判定：借来的关联图标说的是这台机器装了哪个编辑器，
            // 而不是这一格要干什么（见 PanelGlyph.ForScript）。
            var script = PanelGlyph.ForScript(resolved);
            if (script != null) return new PanelIconSpec(PanelIconKind.Glyph, script, script);
            // URL / 协议（https:、ms-settings:）没有本地文件，取不到图标——回退线描，别去碰网络。
            // 但 shell:AppsFolder\<AUMID>（商店应用、wt 这类别名解析后的 UWP 目标）是例外：
            // 它也匹配协议正则，却能由 IShellItemImageFactory 直接出图，不能在这一层被挡掉。
            if (resolved.Length > 0 && (IsShellTarget(resolved) || !IsUrlLike(resolved)))
                return new PanelIconSpec(PanelIconKind.Image, resolved, glyph);
        }
        return new PanelIconSpec(PanelIconKind.Glyph, glyph, glyph);
    }

    // 「协议开头」而不是「含冒号」：C:\ 也含冒号。要求冒号前至少两个字母，把盘符排除掉。
    private static bool IsUrlLike(string s) => Regex.IsMatch(s, @"^[a-zA-Z][a-zA-Z0-9+.\-]+:");

    /// <summary>shell 虚拟目标（shell:AppsFolder\&lt;AUMID&gt; 等），由 IShellItemImageFactory 取图。</summary>
    public static bool IsShellTarget(string? s) =>
        !string.IsNullOrEmpty(s) && s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
