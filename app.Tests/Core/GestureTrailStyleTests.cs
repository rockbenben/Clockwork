using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Clockwork.Core;
using Clockwork.Views;
using Xunit;
// UseWindowsForms 让 System.Drawing 也进了隐式全局 using，Point / Color 两边同名，显式钉到 WPF。
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

// 手势笔迹的样式目录：这份表是「用户能挑什么」的**唯一出处**。
//
// 两条规矩钉在这里：
//   ① **第一项仍是 classic id，但画法已经是流光 Lumen**：一条有宽窄的丝带，
//      而不是等粗折线。id 保留只是为了不迁移配置文件。
//   ② **认不出来的样式回落到默认项**，而不是留在配置里等着画不出来。
//
// 渲染器那几条测的是「画出来的东西」：层数、宽度轮廓、笔触、亮头的长度、换样式时这一笔不丢。
// 它测不到「好不好看」——那件事只能靠截图矩阵与眼睛。
public class GestureTrailStyleTests
{
    // WPF 的可视元素（Canvas、Polyline）只能在 **STA 线程**上建。
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static GestureTrailRenderer Make(GestureTrailStyle style, bool fade = false)
        => new(style, GestureTrailWidths.Resolve("normal"),
               new SolidColorBrush(Colors.SteelBlue),
               new SolidColorBrush(Colors.White), fade);

    private static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$");

    private static void Feed(GestureTrailRenderer r, int count = 24)
    {
        for (int i = 0; i < count; i++)
            r.Add(new Point(10 + i * 3, 80 - Math.Sin(i / 6.0) * 30));
    }

    // 目录本身：五种、互不重名、第一项仍是 classic（配置兼容）。
    [Fact]
    public void The_table_lists_every_style_once()
    {
        var ids = GestureTrailStyles.All.Select(s => s.Id).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(GestureTrailStyles.DefaultId, ids[0]);
    }

    // 新默认是流光：丝带 + 深钢带身 + 冰蓝亮芯 + 冷光 accent。
    [Fact]
    public void The_default_style_is_lumen_ribbon()
    {
        var lumen = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
        Assert.True(lumen.Ribbon);
        Assert.False(lumen.Rainbow);
        Assert.Empty(lumen.Layers);
        Assert.Equal("#7FD8FF", lumen.Accent);
        Assert.Equal("#DFF4FF", lumen.CoreIdle);
        Assert.Equal("#16202E", lumen.Body);
    }

    // 等粗线样式仍要有深色描边；丝带用单独的画法（见下面的六格测试）。
    [Fact]
    public void Every_line_style_keeps_a_dark_halo()
    {
        foreach (var style in GestureTrailStyles.All.Where(s => !s.Ribbon))
            Assert.Contains(style.Layers, l => l.Role == GestureTrailRole.Halo);
    }

    [Fact]
    public void The_neon_style_keeps_its_glow()
    {
        var neon = GestureTrailStyles.Resolve("neon");
        Assert.True(neon.Layers.Count(l => l.Role == GestureTrailRole.Glow) >= 2);
    }

    [Fact]
    public void Every_style_declares_a_valid_ui_accent()
    {
        foreach (var style in GestureTrailStyles.All)
            Assert.Matches(Hex, style.Accent);
    }

    [Fact]
    public void Every_non_rainbow_style_declares_a_core_colour()
    {
        foreach (var style in GestureTrailStyles.All.Where(s => !s.Rainbow))
            Assert.Matches(Hex, style.CoreIdle);
    }

    [Fact]
    public void The_ribbon_declares_its_body_colour()
    {
        foreach (var style in GestureTrailStyles.All.Where(s => s.Ribbon))
            Assert.Matches(Hex, style.Body);
    }

    // 认不出来的样式回落到默认，而不是「画不出来」。
    [Theory]
    [InlineData("neon")]
    [InlineData("NEON")]
    [InlineData(" neon ")]
    public void A_known_style_resolves_to_itself(string id)
    {
        Assert.True(GestureTrailStyles.IsKnown(id));
        Assert.Equal("neon", GestureTrailStyles.Resolve(id).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nope")]
    [InlineData("经典")]
    public void An_unknown_style_falls_back_to_the_default(string? id)
    {
        Assert.False(GestureTrailStyles.IsKnown(id));
        Assert.Equal(GestureTrailStyles.DefaultId, GestureTrailStyles.Resolve(id).Id);
    }

    [Fact]
    public void An_unknown_style_in_the_config_is_normalized_away()
    {
        var cfg = new RootConfig { Settings = new AppSettings { GestureTrailStyle = "sparkle" } };
        ConfigStore.Normalize(cfg);
        Assert.Equal(GestureTrailStyles.DefaultId, cfg.Settings.GestureTrailStyle);
    }

    [Fact]
    public void A_fresh_config_uses_lumen_and_no_fade()
    {
        var s = new AppSettings();
        Assert.Equal(GestureTrailStyles.DefaultId, s.GestureTrailStyle);
        Assert.False(s.GestureTrailFade);
        Assert.Equal("normal", s.GestureTrailWidth);
    }

    [Fact]
    public void The_width_modes_map_to_the_numbers_they_used_to_be()
    {
        var thin = GestureTrailWidths.Resolve("thin");
        Assert.Equal(2.5, thin.CoreIdle); Assert.Equal(3.5, thin.CoreArmed);
        Assert.Equal(5, thin.HaloIdle); Assert.Equal(7, thin.HaloArmed);

        var normal = GestureTrailWidths.Resolve("normal");
        Assert.Equal(4, normal.CoreIdle); Assert.Equal(5.5, normal.CoreArmed);
        Assert.Equal(8, normal.HaloIdle); Assert.Equal(10, normal.HaloArmed);

        var thick = GestureTrailWidths.Resolve("thick");
        Assert.Equal(6, thick.CoreIdle); Assert.Equal(8.5, thick.CoreArmed);
        Assert.Equal(12, thick.HaloIdle); Assert.Equal(15, thick.HaloArmed);
    }

    [Fact]
    public void An_unknown_width_falls_back_to_normal()
    {
        Assert.Equal(GestureTrailWidths.Resolve("normal"), GestureTrailWidths.Resolve("nope"));
        Assert.Equal(GestureTrailWidths.Resolve("normal"), GestureTrailWidths.Resolve(null));
    }

    // 丝带的宽度轮廓：起笔细、向笔头变宽、最后一小段收尖。
    [Fact]
    public void The_ribbon_width_profile_starts_thin_and_tapers_at_the_head()
    {
        double start = GestureTrailRenderer.RibbonWidthFactor(0);
        double middle = GestureTrailRenderer.RibbonWidthFactor(0.5);
        double nearHead = GestureTrailRenderer.RibbonWidthFactor(0.85);
        double tip = GestureTrailRenderer.RibbonWidthFactor(1);
        Assert.True(start < 0.3, $"起笔应细，实测 {start}");
        Assert.True(middle > start, $"中段应比起笔宽：{middle} vs {start}");
        Assert.Equal(1.0, nearHead, 3);
        Assert.True(tip < 0.2, $"笔尖应收尖，实测 {tip}");
    }

    // **投递失败不能把重建永久卡住**：标志还挂着的话，后面的每一次加点都会在这里
    // 直接返回，几何从此再也不重建——笔迹的带身冻住，而亮头还在动。
    //
    // 让它变红的改动：把 catch 里那句 `_ribbonDirty = false;` 删掉。
    [Fact]
    public void A_failed_post_does_not_freeze_the_ribbon()
    {
        OnSta(() =>
        {
            var queued = new List<Action>();
            Action<Action>? post = null;
            var renderer = new GestureTrailRenderer(
                GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId),
                GestureTrailWidths.Resolve("normal"),
                new SolidColorBrush(Colors.SteelBlue), new SolidColorBrush(Colors.White),
                fade: false, postRebuild: a => post!(a));

            post = _ => throw new InvalidOperationException("dispatcher is closing");
            renderer.Add(new Point(10, 80));        // 投递失败 → 标志必须被放开

            post = a => queued.Add(a);             // 换一个会成功的投递口
            renderer.Add(new Point(13, 78));        // 下一次还要能重新排上

            Assert.Single(queued);
        });
    }

    // **一帧里的多个采样点只重建一次**。
    //
    // 丝带每加一个点都要按**全部**点重建三条几何（辉光 / 外缘 / 带身），而采样点是按
    // 鼠标移动事件来的——高报率鼠标一秒能上千个。每个采样点都重建一遍的话，一笔按住
    // 两秒的笔画总工作量随点数**平方**地长，而那些活儿全在 UI 线程上：UI 一卡，
    // 钩子投过来的点就排队，笔迹跟着发涩。
    //
    // 所以重建被**合并到一帧一次**。这条钉的就是「合并」：24 个采样点只排**一次**重建；
    // 泵过那一帧之后，几何反映的是全部 24 个点。
    //
    // 让它变红的改动：把 RequestRibbonRebuild 改回每次 Add 都重建（去掉那个 dirty 标志），
    // 或者让 Flush 提前半拍执行（那样几何就只反映泵之前的那些点了）。
    [Fact]
    public void The_ribbon_rebuilds_once_per_frame_not_once_per_sample()
    {
        OnSta(() =>
        {
            var queued = new List<Action>();
            var style = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
            var renderer = new GestureTrailRenderer(style, GestureTrailWidths.Resolve("normal"),
                new SolidColorBrush(Colors.SteelBlue), new SolidColorBrush(Colors.White),
                fade: false, postRebuild: a => queued.Add(a));

            for (int i = 0; i < 24; i++)
                renderer.Add(new Point(10 + i * 3, 80 - Math.Sin(i / 6.0) * 30));

            Assert.Single(queued);                         // 24 个采样点 → 只排了一次重建
            var body = renderer.Surface.Children.OfType<Path>().ElementAt(2);
            Assert.Null(body.Data);                        // 还没泵 → 几何还没重建

            queued[0]();                                  // 泵这一帧

            Assert.NotNull(body.Data);                     // 泵过之后反映全部点
            Assert.True(body.Data!.Bounds.Width > 20, $"丝带几何应有宽度，实测 {body.Data.Bounds.Width}");

            // **下一帧还能再排一次**（这一格是「每帧一次」的后半句）：
            // 重建时会把「待重建」那个标志清回去；清不回去的话，
            // 后面的每一次加点都会在这里直接返回——笔迹的带身从此冻住，
            // 而亮头还在动。只验「一帧里排一次」看不出这个。
            renderer.Add(new Point(200, 200));
            Assert.Equal(2, queued.Count);
            queued[1]();
            Assert.NotNull(body.Data);
        });
    }

    // 流光画成六格：外晕 / 深色描边 / 带身 / 亮芯 / 亮头 / 笔尖点。
    [Fact]
    public void The_ribbon_draws_glow_halo_body_core_head_and_tip()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
            var r = Make(style);
            Feed(r);
            Assert.Equal(6, r.Surface.Children.Count);
            Assert.Equal(3, r.Surface.Children.OfType<Path>().Count());
            Assert.Equal(2, r.Surface.Children.OfType<Polyline>().Count());
            Assert.Single(r.Surface.Children.OfType<Ellipse>());
            var body = r.Surface.Children.OfType<Path>().ElementAt(2);
            Assert.NotNull(body.Data);
            Assert.True(body.Data.Bounds.Width > 20, $"丝带几何应有宽度，实测 {body.Data.Bounds.Width}");
        });
    }

    // 丝带自带颜色必须真的落到带身/亮芯/外晕上。
    [Fact]
    public void The_ribbon_uses_its_body_core_and_accent_colours()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
            var r = Make(style);
            Feed(r);
            var paths = r.Surface.Children.OfType<Path>().ToArray();
            var core = r.Surface.Children.OfType<Polyline>().First();
            Assert.Equal(Color.FromRgb(0x7F, 0xD8, 0xFF), ((SolidColorBrush)paths[0].Fill).Color);
            Assert.Equal(Color.FromRgb(0x16, 0x20, 0x2E), ((SolidColorBrush)paths[2].Fill).Color);
            Assert.Equal(Color.FromRgb(0xDF, 0xF4, 0xFF), ((SolidColorBrush)core.Stroke).Color);
        });
    }

    // 淡出压暗带身与亮芯；外晕和深色描边是两套底色上的可读保险，不跟着暗。
    [Fact]
    public void The_fade_dims_the_ribbon_body_and_core_but_keeps_the_outer_halo()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
            var r = Make(style, fade: true);
            Feed(r);
            var paths = r.Surface.Children.OfType<Path>().ToArray();
            var core = r.Surface.Children.OfType<Polyline>().First();
            Assert.Equal(0.18, paths[0].Opacity, 3);   // glow 不暗
            Assert.Equal(1.0, paths[1].Opacity, 3);    // dark halo 不暗
            Assert.True(paths[2].Opacity < 0.5, $"带身应被压暗，实测 {paths[2].Opacity}");
            Assert.True(core.Opacity < 0.6, $"亮芯应被压暗，实测 {core.Opacity}");
            Assert.Equal(Visibility.Visible, r.Surface.Children.OfType<Polyline>().Last().Visibility);
        });
    }

    // 等粗线样式仍按层数画（外加一根亮头）。
    [Theory]
    [InlineData("neon")]
    [InlineData("rainbow")]
    [InlineData("dashed")]
    [InlineData("dots")]
    public void Every_line_style_draws_one_line_per_layer(string id)
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve(id);
            var r = Make(style);
            Assert.Equal(style.Layers.Count + 1, r.Surface.Children.Count);
        });
    }

    [Fact]
    public void The_points_reach_every_line_layer()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("neon"));
            r.Add(new Point(3, 4));
            r.Add(new Point(5, 6));
            Assert.Equal(2, r.PointCount);
            foreach (var child in r.Surface.Children.OfType<Polyline>())
                Assert.Equal(2, child.Points.Count);
        });
    }

    [Fact]
    public void The_bright_head_keeps_only_the_last_few_points()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("neon"), fade: true);
            for (int i = 0; i < 60; i++) r.Add(new Point(i, i));
            var head = (Polyline)r.Surface.Children[^1];
            Assert.InRange(head.Points.Count, 2, 16);
        });
    }

    [Fact]
    public void The_line_head_is_hidden_when_the_tail_does_not_fade()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("neon"), fade: false);
            var head = (Polyline)r.Surface.Children[^1];
            Assert.Equal(Visibility.Collapsed, head.Visibility);
        });
    }

    [Fact]
    public void The_fade_dims_the_line_core_but_not_the_dark_halo()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve("neon");
            var r = Make(style, fade: true);
            var children = r.Surface.Children.OfType<Polyline>().ToArray();
            int halo = style.Layers.ToList().FindIndex(l => l.Role == GestureTrailRole.Halo);
            Assert.Equal(1.0, children[halo].Opacity, 3);
            Assert.True(children[^2].Opacity < 0.6, $"线芯该被压暗，实测 {children[^2].Opacity}");
        });
    }

    [Fact]
    public void The_line_style_colours_override_the_theme_fallbacks()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve("dashed");
            var r = Make(style);
            var children = r.Surface.Children.OfType<Polyline>().ToArray();
            Assert.Equal(Color.FromRgb(0x9C, 0xC8, 0xFF), ((SolidColorBrush)children[1].Stroke).Color);
            Assert.Equal(Color.FromRgb(0x18, 0x24, 0x35), ((SolidColorBrush)children[2].Stroke).Color);
        });
    }

    [Fact]
    public void The_core_changes_its_colour_when_the_stroke_hits()
    {
        OnSta(() =>
        {
            var accent = new SolidColorBrush(Colors.SteelBlue);
            var armed = new SolidColorBrush(Colors.White);
            var style = new GestureTrailStyle
            {
                Id = "test",
                Layers = new[]
                {
                    new GestureTrailLayer { Role = GestureTrailRole.Halo },
                    new GestureTrailLayer { Role = GestureTrailRole.Core },
                },
            };
            var r = new GestureTrailRenderer(style, GestureTrailWidths.Resolve("normal"), accent, armed);
            var core = (Polyline)r.Surface.Children[1];
            Assert.Same(accent, core.Stroke);
            r.SetArmed(true);
            Assert.Same(armed, core.Stroke);
        });
    }

    [Fact]
    public void Hex_brushes_parse_and_invalid_values_return_null()
    {
        var brush = GestureTrailRenderer.BrushFromHex("#7FB6FF");
        Assert.NotNull(brush);
        Assert.Equal(Color.FromRgb(0x7F, 0xB6, 0xFF), ((SolidColorBrush)brush!).Color);
        Assert.Null(GestureTrailRenderer.BrushFromHex(""));
        Assert.Null(GestureTrailRenderer.BrushFromHex("nope"));
    }

    [Fact]
    public void Switching_the_style_mid_stroke_keeps_the_stroke()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("classic"));
            r.Add(new Point(1, 1));
            r.Add(new Point(2, 2));
            r.SetStyle(GestureTrailStyles.Resolve("neon"), GestureTrailWidths.Resolve("normal"), false);
            Assert.Equal(2, r.PointCount);
            var first = (Polyline)r.Surface.Children[0];
            Assert.Equal(2, first.Points.Count);
        });
    }
}
