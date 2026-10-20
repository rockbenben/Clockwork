using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Clockwork.Core;
using Clockwork.Views;
using Xunit;
// UseWindowsForms 让 System.Drawing 也进了隐式全局 using，Point 两边同名，显式钉到 WPF。
using Point = System.Windows.Point;

// 手势笔迹的样式目录：这份表是「用户能挑什么」的**唯一出处**。
//
// 两条规矩钉在这里：
//   ① **第一项必须是 classic，而且它就是升级前那一条线。** 存量配置里没有样式这一项，
//      落到默认值上时看到的必须与升级前一模一样——「新增可选项」的第一条规矩。
//   ② **认不出来的样式回落到 classic**，而不是留在配置里等着画不出来。
//
// 渲染器那几条测的是「画出来的东西」：层数、笔触、亮头的长度、换样式时这一笔不丢。
// 它测不到「好不好看」——那件事只能靠截图矩阵与眼睛。
public class GestureTrailStyleTests
{
    // WPF 的可视元素（Canvas、Polyline）只能在 **STA 线程**上建：
    // InputManager 那个单例就是在建 FrameworkElement 的那一刻按当前线程的套间起的，
    // 而测试宿主是 MTA —— 于是同一段代码在程序里好好的，在测试里抛
    // 「调用线程必须为 STA」。所以这一段自己起一根 STA 线程跑。
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
        => new GestureTrailRenderer(style, GestureTrailWidths.Resolve("normal"),
                                    new SolidColorBrush(Colors.SteelBlue),
                                    new SolidColorBrush(Colors.White), fade);

    // 目录本身：五种、互不重名、第一项是经典。
    [Fact]
    public void The_table_lists_every_style_once()
    {
        var ids = GestureTrailStyles.All.Select(s => s.Id).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(GestureTrailStyles.DefaultId, ids[0]);
    }

    // **经典那一条就是原来那一条**：深色描边 + 强调色线芯，一层不多一层不少。
    [Fact]
    public void The_default_style_is_the_line_users_already_had()
    {
        var classic = GestureTrailStyles.Resolve(GestureTrailStyles.DefaultId);
        Assert.Equal(2, classic.Layers.Count);
        Assert.Equal(GestureTrailRole.Halo, classic.Layers[0].Role);
        Assert.Equal(GestureTrailRole.Core, classic.Layers[1].Role);
        Assert.False(classic.Rainbow);
    }

    // 描边那一层是「任何底色上都读得出来」的保险。
    // 霓虹那一种是**故意**没有它的（它自己就是那个保险的反面：亮的那一层），
    // 所以只对别的样式提这条要求——而那一种必须由用户自己去挑，不许是默认的。
    [Fact]
    public void Every_style_but_the_neon_one_keeps_the_readable_halo()
    {
        foreach (var style in GestureTrailStyles.All.Where(s => s.Id != "neon"))
            Assert.Contains(style.Layers, l => l.Role == GestureTrailRole.Halo);
        var neon = GestureTrailStyles.Resolve("neon");
        Assert.DoesNotContain(neon.Layers, l => l.Role == GestureTrailRole.Halo);
        Assert.Contains(neon.Layers, l => l.Role == GestureTrailRole.Glow);
    }

    // 认不出来的样式回落到经典，而不是「画不出来」。
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

    // 配置里写了个不存在的样式 → 读进来时回落成经典。
    // **不许留在配置里**：留着的话，界面上的下拉选不到它，而它又确实是「被选中的那一个」。
    [Fact]
    public void An_unknown_style_in_the_config_is_normalized_away()
    {
        var cfg = new RootConfig { Settings = new AppSettings { GestureTrailStyle = "sparkle" } };
        ConfigStore.Normalize(cfg);
        Assert.Equal(GestureTrailStyles.DefaultId, cfg.Settings.GestureTrailStyle);
    }

    // 全新配置里的默认值 = 升级前那一条线，且尾迹不淡出。
    [Fact]
    public void A_fresh_config_looks_exactly_like_the_old_one()
    {
        var s = new AppSettings();
        Assert.Equal(GestureTrailStyles.DefaultId, s.GestureTrailStyle);
        Assert.False(s.GestureTrailFade);
        Assert.Equal("normal", s.GestureTrailWidth);
    }

    // 粗细四档的四个数，与搬进 Core 之前那四个常量逐一相同。
    // 这一条是「搬代码不搬行为」的判据：数字变了，肉眼要画很多遍才看得出来。
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

    // 认不出来的粗细按「标准」走，而不是按最细或最粗。
    [Fact]
    public void An_unknown_width_falls_back_to_normal()
    {
        Assert.Equal(GestureTrailWidths.Resolve("normal"), GestureTrailWidths.Resolve("nope"));
        Assert.Equal(GestureTrailWidths.Resolve("normal"), GestureTrailWidths.Resolve(null));
    }

    // 每一种样式画出的层数 = 它自己声明的层数（外加一根亮头）。
    [Theory]
    [InlineData("classic")]
    [InlineData("neon")]
    [InlineData("rainbow")]
    [InlineData("dashed")]
    [InlineData("dots")]
    public void Every_style_draws_one_line_per_layer(string id)
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve(id);
            var r = Make(style);
            Assert.Equal(style.Layers.Count + 1, r.Surface.Children.Count);   // +1 = 亮头
        });
    }

    // 点喂进去，每一层都得到。
    [Fact]
    public void The_points_reach_every_layer()
    {
        OnSta(() =>
        {
            var style = GestureTrailStyles.Resolve("neon");
            var r = Make(style);
            r.Add(new Point(3, 4));
            r.Add(new Point(5, 6));
            Assert.Equal(2, r.PointCount);
            foreach (var child in r.Surface.Children.OfType<Polyline>())
                Assert.Equal(2, child.Points.Count);
        });
    }

    // 亮头只留最近的一小段：留多了就不是「笔尖」，而是半条线都亮着。
    [Fact]
    public void The_bright_head_keeps_only_the_last_few_points()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("classic"), fade: true);
            for (int i = 0; i < 60; i++) r.Add(new Point(i, i));
            var head = (Polyline)r.Surface.Children[^1];
            Assert.InRange(head.Points.Count, 2, 16);
        });
    }

    // 不淡出的时候亮头是收起来的——它不占地方，也不该在屏上留一道亮痕。
    [Fact]
    public void The_bright_head_is_hidden_when_the_tail_does_not_fade()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("classic"), fade: false);
            var head = (Polyline)r.Surface.Children[^1];
            Assert.Equal(Visibility.Collapsed, head.Visibility);
        });
    }

    // 淡出只压暗**线芯与辉光**：描边那一层是「在任何底色上都看得见」的最后一道保险，
    // 把它也压暗，浅色底上的尾巴就等于没了——而那条尾巴正是要认形状的地方。
    [Fact]
    public void The_fade_dims_the_core_but_not_the_readable_halo()
    {
        OnSta(() =>
        {
            var r = Make(GestureTrailStyles.Resolve("classic"), fade: true);
            var halo = (Polyline)r.Surface.Children[0];
            var core = (Polyline)r.Surface.Children[1];
            Assert.Equal(1.0, halo.Opacity, 3);
            Assert.True(core.Opacity < 0.6, $"线芯该被压暗，实测 {core.Opacity}");
        });
    }

    // 换样式时**正在画的那一笔不丢**：用户是在画到一半换的样式。
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

    // 命中时线芯换成「命中色」——那是「认出来了」那个信号，颜色变了才算数。
    [Fact]
    public void The_core_changes_its_colour_when_the_stroke_hits()
    {
        OnSta(() =>
        {
            var accent = new SolidColorBrush(Colors.SteelBlue);
            var armed = new SolidColorBrush(Colors.White);
            var r = new GestureTrailRenderer(GestureTrailStyles.Resolve("classic"),
                                            GestureTrailWidths.Resolve("normal"), accent, armed);
            var core = (Polyline)r.Surface.Children[1];
            Assert.Same(accent, core.Stroke);
            r.SetArmed(true);
            Assert.Same(armed, core.Stroke);
        });
    }
}
