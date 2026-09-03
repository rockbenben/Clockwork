using Clockwork.Core;
using Xunit;

// 一格的图标取哪个。三档优先级：用户指定的图片 > 目标程序自己的图标 > 步骤类型的线描字形。
// 「彩色 = 打开一个东西，线描 = 做一件事」这条视觉规则就落在这里，写错了面板会整片失去区分度。
public class PanelIconTests
{
    // 「运行程序」自动借目标自己的图标——面板好看起来全靠这一条。
    [Fact]
    public void A_launch_step_borrows_its_targets_icon()
    {
        var spec = PanelIcon.Resolve(null, "app", @"C:\Windows\System32\notepad.exe");
        Assert.Equal(PanelIconKind.Image, spec.Kind);
        Assert.EndsWith("notepad.exe", spec.Value, System.StringComparison.OrdinalIgnoreCase);
    }

    // 没有文件可取的类型不去乱猜，直接用类型字形。
    [Theory]
    [InlineData("keys")]
    [InlineData("volume")]
    [InlineData("window")]
    [InlineData("system")]
    [InlineData("mouse")]
    public void Actions_without_a_file_fall_back_to_their_kind_glyph(string kind)
    {
        var spec = PanelIcon.Resolve(null, kind);
        Assert.Equal(PanelIconKind.Glyph, spec.Kind);
        Assert.Equal(PanelGlyph.ForKind(kind), spec.Value);
    }

    // 网址 / 协议目标没有本地文件，不能拿去当路径找图标（更不该为它碰网络）。
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("ms-settings:bluetooth")]
    [InlineData("mailto:a@b.c")]
    public void A_url_target_stays_on_the_line_glyph(string target)
        => Assert.Equal(PanelIconKind.Glyph, PanelIcon.Resolve(null, "app", target).Kind);

    // 盘符里的冒号不能被当成协议——这条错了的话，所有绝对路径都取不到图标。
    [Fact]
    public void A_drive_letter_is_not_mistaken_for_a_protocol()
        => Assert.Equal(PanelIconKind.Image, PanelIcon.Resolve(null, "app", @"C:\Windows\explorer.exe").Kind);

    // 四位十六进制 = 直接指定一个 MDL2 码位，给「我就想换个字形」的人留的窄门。
    [Theory]
    [InlineData("E7C4")]
    [InlineData("e7c4")]
    [InlineData("0xE7C4")]
    public void A_hex_code_selects_a_glyph_directly(string icon)
    {
        var spec = PanelIcon.Resolve(icon, "app", @"C:\Windows\notepad.exe");
        Assert.Equal(PanelIconKind.Glyph, spec.Kind);
        Assert.Equal(char.ConvertFromUtf32(0xE7C4), spec.Value);
    }

    // 用户指定的图片压过自动取图标——「绝对优先」就是这个意思。
    [Fact]
    public void A_custom_image_wins_over_the_targets_own_icon()
    {
        var spec = PanelIcon.Resolve(@"D:\art\focus.png", "app", @"C:\Windows\notepad.exe");
        Assert.Equal(PanelIconKind.Image, spec.Kind);
        Assert.EndsWith("focus.png", spec.Value);
    }

    // 指向另一个 exe 借它的图标，是很自然的用法（给「打开网址」的步骤借浏览器的图标）。
    [Fact]
    public void Pointing_at_another_exe_borrows_its_icon()
        => Assert.Equal(PanelIconKind.Image, PanelIcon.Resolve(@"C:\Windows\explorer.exe", "keys").Kind);

    // 引号与环境变量与「目标」输入框同一口径——两处对路径的解释不该有两套规矩。
    [Fact]
    public void Quotes_and_environment_variables_are_handled_like_a_target_path()
    {
        var spec = PanelIcon.Resolve("\"%WINDIR%\\explorer.exe\"", "keys");
        Assert.Equal(PanelIconKind.Image, spec.Kind);
        Assert.DoesNotContain("%", spec.Value);
        Assert.DoesNotContain("\"", spec.Value);
    }

    // 整组一格（stepKind=null）用「一叠动作」字形，但同样能被自定义图标压过。
    [Fact]
    public void A_group_tile_uses_the_group_glyph_unless_overridden()
    {
        Assert.Equal(PanelGlyph.Group, PanelIcon.Resolve(null, null).Value);
        Assert.Equal(PanelIconKind.Image, PanelIcon.Resolve(@"D:\art\g.png", null).Kind);
    }

    // 回退字形恒非空：图片路径可能指向一个已被删掉的文件，那时得有东西顶上，不能让格子空着。
    [Fact]
    public void An_image_spec_always_carries_a_usable_fallback_glyph()
    {
        Assert.All(StepDisplay.StepKinds, k =>
        {
            var spec = PanelIcon.Resolve(@"D:\gone\missing.png", k);
            Assert.False(string.IsNullOrWhiteSpace(spec.Fallback), $"{k} 的回退字形是空的");
        });
        Assert.False(string.IsNullOrWhiteSpace(PanelIcon.Resolve(@"D:\gone\x.png", null).Fallback));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("A.PNG", true)]
    [InlineData("x.ico", true)]
    [InlineData("x.jpeg", true)]
    [InlineData("x.exe", false)]
    [InlineData("x.lnk", false)]
    [InlineData("x", false)]
    public void Image_files_are_told_apart_from_icon_sources(string path, bool isImage)
        => Assert.Equal(isImage, PanelIcon.IsImageFile(path));
}
