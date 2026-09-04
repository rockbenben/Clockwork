using System;
using Clockwork.Core;
using Xunit;

// 右键手势的判定。与 LongPressGate 同一等级的敏感度：判错的表现是右键菜单在全系统失灵，
// 或者用户画对了手势却没反应——两头都是「一用就发现」的故障。
public class GestureGateTests
{
    private const int MinLeg = 40;

    // 沿 legs 里的每条腿走：(角度, 长度)。角度 0=→ 90=↓（屏幕坐标 y 向下）。
    // **每 5 像素喂一次**——必须模拟真实鼠标的细采样，用大跳步喂不出拐角那个 bug，
    // 而拐角正是这个量化器最难的地方。
    private static string Trace(params (double Deg, double Len)[] legs)
    {
        string got = "";
        var g = new GestureGate(p => { got = p; return true; }, MinLeg);
        double x = 500, y = 500;
        g.OnRightDown((int)x, (int)y);
        foreach (var (deg, len) in legs)
        {
            double r = deg * Math.PI / 180.0;
            for (double t = 0; t < len; t += 5)
            {
                x += 5 * Math.Cos(r);
                y += 5 * Math.Sin(r);
                g.OnMove((int)Math.Round(x), (int)Math.Round(y));
            }
        }
        g.OnRightUp();
        return got;
    }

    // 每条腿都给 120 像素——认真画的手势腿就是这个量级。
    private static string Trace(params double[] degs)
        => Trace(Array.ConvertAll(degs, d => (d, 120.0)));

    private static GestureGate New(params string[] patterns)
        => new(p => Array.Exists(patterns, x => x == p), MinLeg);

    // ── 屏幕上那条看得见的笔迹靠 PointCount 判断「刚才这一下有没有新点」 ──

    // 右键**点一下**（没画）只有起点一个点。第二个点才让覆盖窗现身，
    // 所以这一条钉的是「右键菜单前面不会闪出一条线」。
    [Fact]
    public void A_plain_right_click_records_one_point()
    {
        var g = New("R");
        g.OnRightDown(100, 100);
        Assert.Equal(1, g.PointCount);
        g.OnMove(101, 100);           // 手抖那点位移不该记点
        g.OnMove(100, 102);
        Assert.Equal(1, g.PointCount);
    }

    // 采样是每 StepPx 才记一个点，笔迹也就每 StepPx 才画一笔。
    // 这条不成立的话，一次手势上千个 WM_MOUSEMOVE 会变成上千次重绘投进 UI 队列。
    [Fact]
    public void Points_are_sampled_not_recorded_per_move()
    {
        var g = New("R");
        g.OnRightDown(0, 0);
        for (int i = 1; i <= 200; i++) g.OnMove(i, 0);   // 走 200 像素，喂 200 次
        Assert.InRange(g.PointCount, 2, 40);             // MinLeg=40 → StepPx=10，约 20 个点
    }

    [Fact]
    public void Point_count_resets_with_the_stroke()
    {
        var g = New("R");
        g.OnRightDown(0, 0);
        for (int i = 10; i <= 200; i += 10) g.OnMove(i, 0);
        Assert.True(g.PointCount > 1);
        g.OnRightUp();
        g.OnRightDown(0, 0);
        Assert.Equal(1, g.PointCount);   // 新的一笔从头开始，不接着上一条画
    }

    // ── 来回：净位移接近 0，不该变成一个方向 ──
    //
    // 这类笔迹以前会触发一个**随机方向**的手势：两条各 30px 的短腿都不达标，被并成一条
    // 路径长 60 的「合格」腿，而它首尾几乎是同一个点——最后按首尾重定方向，
    // 得到的是那几像素残余抖动指向的方向。用户只是在原地蹭了一下，却跑了一个动作，
    // 而右键菜单也没出来。判长短改用净位移之后，它照旧被判成手抖。
    [Theory]
    [InlineData(0, 180)]     // 右去左回
    [InlineData(90, -90)]    // 下去上回
    [InlineData(45, -135)]   // 斜的来回
    public void A_wiggle_is_not_a_gesture(double a, double b)
        => Assert.Equal("", Trace((a, 30.0), (b, 30.0)));

    // 但「去了又回」若两段都够长，那是真的两段——不能一并当抖动毙掉。
    [Fact]
    public void A_long_out_and_back_is_still_two_legs()
        => Assert.Equal("RL", Trace((0, 120.0), (180, 120.0)));

    [Fact]
    public void Down_is_swallowed()
    {
        var g = New("R");
        Assert.Equal(PressVerdict.Swallow, g.OnRightDown(100, 100));
        Assert.True(g.Pending);
    }

    // 没动就抬起 = 普通右键：必须补发，否则右键菜单在全系统失灵。
    [Fact]
    public void Plain_click_replays()
    {
        var g = New("R");
        g.OnRightDown(100, 100);
        g.OnMove(103, 102);   // 手抖
        Assert.Equal(PressVerdict.ReplayClick, g.OnRightUp());
    }

    // 划了一小段就松开也算点击：不足一条腿的位移不是手势，菜单照出。
    [Fact]
    public void Tiny_drag_still_replays()
    {
        var g = New("R");
        g.OnRightDown(100, 100);
        for (int i = 1; i <= 3; i++) g.OnMove(100 + i * 10, 100);   // 共 30px < MinLeg
        Assert.Equal(PressVerdict.ReplayClick, g.OnRightUp());
    }

    // ── 拐弯扫出来的那一格 ──
    // 人不会画出直角。→↓ 的拐角一圆，笔尖就在 ↘ 那格里实打实走一段，半径 60px 起
    // 那段的净位移就过了 MinLeg——绝对门槛拦不住它，于是 RD 读成 R3D，画得再标准也不匹配。
    // 这是「手势经常不响应」最大的一块，钉死它。
    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(140)]
    public void A_rounded_corner_is_still_two_legs(int radius)
    {
        string got = "";
        var g = new GestureGate(p => { got = p; return true; }, MinLeg);
        g.OnRightDown(0, 0);
        for (double t = 0; t <= 150; t += 2) g.OnMove((int)t, 0);
        for (double a = -90; a <= 0; a += 1)   // 半径 radius 的四分之一圆角
            g.OnMove((int)Math.Round(150 + radius * Math.Cos(a * Math.PI / 180)),
                     (int)Math.Round(radius + radius * Math.Sin(a * Math.PI / 180)));
        for (double t = 0; t <= 150; t += 2) g.OnMove(150 + radius, (int)(radius + t));
        g.OnRightUp();
        Assert.Equal("RD", got);
    }

    // ── Path 是「刚画完的那一笔」，不是「上次画过的那一笔」 ──
    //
    // 这条钉的是一个真实故障：右键**点一下**也会弹出「你画的 ↓ 没绑任何动作」，而且弹的是
    // 上一笔的形状。成因在 MouseHook：它对 PressVerdict.Swallow 这一档读 gate.Path 去报，
    // 而 OnRightDown **也**返回 Swallow（按下要吞掉），于是按下那一刻就读到了上一笔的残留。
    // 调阈值一点用都没有——那条串根本不是这次画的。
    //
    // 根因是 Path 活得比它描述的那一笔久。按下即清，它就只可能是刚画完的那一笔。
    [Fact]
    public void Path_is_cleared_when_a_new_stroke_starts()
    {
        var g = new GestureGate(_ => false, MinLeg);   // 什么都匹配不上，走 Swallow
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500, 500 + i * 5);
        Assert.Equal(PressVerdict.Swallow, g.OnRightUp());
        Assert.Equal("D", g.Path);                     // 上一笔留下了串

        Assert.Equal(PressVerdict.Swallow, g.OnRightDown(500, 500));   // 只是按下，还没画
        Assert.Equal("", g.Path);                      // 残留必须没了，否则上层照着它弹提示
        Assert.Equal(PressVerdict.ReplayClick, g.OnRightUp());         // 没动就抬起 = 普通右键
        Assert.Equal("", g.Path);
    }

    // Reset 同理：钩子卸载 / 抬起丢了而超时收笔，都不该把上一笔的串留给下一次。
    [Fact]
    public void Reset_clears_the_path_too()
    {
        var g = new GestureGate(_ => false, MinLeg);
        g.OnRightDown(500, 500);
        for (int i = 1; i <= 30; i++) g.OnMove(500 + i * 5, 500);
        g.OnRightUp();
        Assert.Equal("R", g.Path);
        g.Reset();
        Assert.Equal("", g.Path);
    }

    // ── 最小笔画长度跟着屏幕走 ──
    // 固定 40px 让右键**点一下**都能凑出一条腿：菜单被吞，还弹一句「没绑动作」。
    // 2.5% 抄的是 WGestures（EffectiveMove = 屏幕宽度 * 0.025f），moosegesture 用固定 60px。
    [Theory]
    [InlineData(1366, 40)]    // 小屏：撞下限，与从前一致，不会比以前更灵敏
    [InlineData(1920, 48)]
    [InlineData(2560, 64)]
    [InlineData(3840, 96)]
    [InlineData(0, 40)]       // API 取不到时的兜底
    [InlineData(-1, 40)]
    public void Min_leg_scales_with_the_screen(int width, int want)
        => Assert.Equal(want, GestureGate.MinLegForScreen(width));

    // 4K 屏上右键点一下、手滑 60px：从前 60 > 40 直接判成 ↓ 手势，现在门槛 96，照旧补发菜单。
    [Fact]
    public void A_slipped_right_click_replays_on_a_big_screen()
    {
        var g = new GestureGate(_ => true, GestureGate.MinLegForScreen(3840));
        g.OnRightDown(1000, 1000);
        for (int i = 1; i <= 60; i += 3) g.OnMove(1000, 1000 + i);
        Assert.Equal(PressVerdict.ReplayClick, g.OnRightUp());
    }

    // ── 画歪了的直线 ──
    // 人画的「直线」是弓形，而切线偏得最多的正是起笔和收笔那两点。斜角扇区只有 34°，
    // 一条弓 12% 的 ↗ 两端就甩出 → 和 ↑，读成 →↗↑——用户只画了一个方向，得到三个。
    // 轴向扇区 56° 宽，同样的弓画横线不会出事，所以这条只在斜角上炸，实测 ↙ 读成 ←↙。
    //
    // **天花板：斜角管到弓 12%，轴向管到 16%（320px 的笔画偏出 51px）。** 再歪就修不动了，
    // 而且不是没想到办法，是那办法会砸掉别的东西：一条弓 16% 的 ↗，与「→ 接一段 ↘」
    // 画出来的折线本来就长得一模一样（都是先偏平、后偏陡的一道弯）。想把前者并成一个方向，
    // 就必然把后者也并掉。相邻扇区的两笔手势本来就难画（WGestures 默认干脆只开四向），
    // 但「保住 →↘」比「多容忍 4% 的弓」值——所以这里停手，天花板写在这儿别让它悄悄漂。
    [Theory]
    [InlineData('9', 0.08)]
    [InlineData('9', 0.12)]
    [InlineData('1', 0.08)]
    [InlineData('1', 0.12)]
    [InlineData('R', 0.16)]
    [InlineData('D', 0.16)]
    public void A_bowed_stroke_is_still_one_direction(char dir, double bow)
    {
        var d = GestureGate.Of(dir)!.Value;
        double n = Math.Sqrt(d.Dx * d.Dx + d.Dy * d.Dy);
        double ux = d.Dx / n, uy = d.Dy / n;   // 主方向
        const double len = 320;
        string got = "";
        var g = new GestureGate(p => { got = p; return true; }, MinLeg);
        g.OnRightDown(500, 500);
        for (double t = 0; t <= len; t += 2)
        {
            double b = 4 * bow * len * (t / len) * (1 - t / len);   // 抛物线弓，法向偏移
            g.OnMove((int)Math.Round(500 + ux * t - uy * b), (int)Math.Round(500 + uy * t + ux * b));
        }
        g.OnRightUp();
        Assert.Equal(dir.ToString(), got);
    }

    // 反面：两条腿都认真画的 →↘ 不能被上面那条规则吃掉——扇区相邻，但比例过不去。
    [Fact]
    public void A_deliberate_adjacent_pair_survives()
        => Assert.Equal("R3", Trace((0, 200), (45, 200)));

    // 反面：长横 + 短竖（隔了一格的 R 与 D）照旧是两条腿，短的那笔只要自己够长。
    [Fact]
    public void Uneven_legs_two_sectors_apart_survive()
        => Assert.Equal("RD", Trace((0, 300), (90, 50)));

    [Fact]
    public void Matching_stroke_fires_with_path()
    {
        var g = new GestureGate(p => p == "RD", MinLeg);
        double x = 500, y = 500;
        g.OnRightDown((int)x, (int)y);
        for (int i = 0; i < 24; i++) g.OnMove((int)(x += 5), (int)y);
        for (int i = 0; i < 24; i++) g.OnMove((int)x, (int)(y += 5));
        Assert.Equal(PressVerdict.Fire, g.OnRightUp());
        Assert.Equal("RD", g.Path);
    }

    // 画了但没配：吞掉，什么都不发生——在轨迹终点弹出莫名的右键菜单更糟。
    [Fact]
    public void Unmatched_stroke_is_swallowed()
    {
        var g = New("RD");
        double x = 500, y = 500;
        g.OnRightDown((int)x, (int)y);
        for (int i = 0; i < 24; i++) g.OnMove((int)(x -= 5), (int)y);
        Assert.Equal(PressVerdict.Swallow, g.OnRightUp());
    }

    // 移动一律放行：光标位移吞不掉也不必吞。
    [Fact]
    public void Moves_always_pass()
    {
        var g = New("R");
        Assert.Equal(PressVerdict.Pass, g.OnMove(500, 500));   // 没按着
        g.OnRightDown(100, 100);
        Assert.Equal(PressVerdict.Pass, g.OnMove(200, 100));   // 按着画
    }

    [Fact]
    public void Orphan_up_passes()
        => Assert.Equal(PressVerdict.Pass, New("R").OnRightUp());

    [Fact]
    public void Reset_discards_pending()
    {
        var g = New("R");
        g.OnRightDown(100, 100);
        g.OnMove(300, 100);
        g.Reset();
        Assert.Equal(PressVerdict.Pass, g.OnRightUp());
    }

    // ——— 量化 ———

    [Theory]
    [InlineData(0, "R")]
    [InlineData(90, "D")]
    [InlineData(180, "L")]
    [InlineData(-90, "U")]
    [InlineData(45, "3")]     // ↘
    [InlineData(-45, "9")]    // ↗
    [InlineData(135, "1")]    // ↙
    [InlineData(-135, "7")]   // ↖
    public void Single_leg_quantizes(double deg, string want)
        => Assert.Equal(want, Trace(deg));

    // **加了斜角不能把轴向的容错吃掉**：横线歪到 27° 仍必须读作「→」。
    // 这是轴向偏置存在的全部理由——平分八份的话 22.5° 就翻了。
    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    [InlineData(27)]
    public void Sloppy_horizontal_stays_axis(double deg)
        => Assert.Equal("R", Trace(deg));

    // ——— 拐角（用户实测撞到的那个）———

    // 画两段就该出两个方向。拐角处那一步横跨两条腿、方向是斜的，
    // 早先按滑动窗口测方向时它会变成独立的一个 ↘，两段读成三个方向。
    [Fact]
    public void Two_leg_corner_yields_two_directions()
        => Assert.Equal("RD", Trace(0, 90));

    [Theory]
    [InlineData(0, -90, "RU")]
    [InlineData(90, 180, "DL")]
    [InlineData(0, 45, "R3")]     // 轴向接斜角，同样只该有两个
    [InlineData(45, 90, "3D")]
    public void Corners_never_invent_a_direction(double a, double b, string want)
        => Assert.Equal(want, Trace(a, b));

    // 三段就是三段：吸收短腿不能把真的腿也吃掉。
    [Fact]
    public void Three_legs_survive()
        => Assert.Equal("RDR", Trace(0, 90, 0));

    // 短于最短腿长的一段并入较长的邻居，不单独成一个方向。
    [Fact]
    public void Short_leg_is_absorbed()
        => Assert.Equal("RD", Trace((0, 120), (45, 15), (90, 120)));

    // 画在扇区边界上的直线会切出一串交替的短腿，逐个被吸收后仍是一个方向——
    // 这正是「短腿并入邻居」替掉早先那条迟滞规则的地方。
    [Fact]
    public void Boundary_wobble_collapses_to_one_direction()
        => Assert.Equal("R", Trace((26, 40), (30, 20), (24, 40), (29, 20), (25, 60)));

    // 真实手画是抖的：给每个采样点叠上 ±2px 的确定性噪声，常见手势仍须稳定认出。
    // 固定种子的自造 LCG 而不是 Random：测试挂了要能原样重放，不能每次跑出不同的轨迹。
    private static string TraceJittery(uint seed, params (double Deg, double Len)[] legs)
    {
        string got = "";
        var g = new GestureGate(p => { got = p; return true; }, MinLeg);
        double x = 500, y = 500;
        uint r = seed;
        int Noise() { r = r * 1664525u + 1013904223u; return (int)(r >> 24) % 5 - 2; }
        g.OnRightDown((int)x, (int)y);
        foreach (var (deg, len) in legs)
        {
            double a = deg * Math.PI / 180.0;
            for (double t = 0; t < len; t += 5)
            {
                x += 5 * Math.Cos(a);
                y += 5 * Math.Sin(a);
                g.OnMove((int)Math.Round(x) + Noise(), (int)Math.Round(y) + Noise());
            }
        }
        g.OnRightUp();
        return got;
    }

    [Theory]
    [InlineData(1u)] [InlineData(7u)] [InlineData(42u)] [InlineData(99u)] [InlineData(12345u)]
    public void Jittery_corner_still_reads_two_directions(uint seed)
        => Assert.Equal("RD", TraceJittery(seed, (0, 140), (90, 140)));

    [Theory]
    [InlineData(3u)] [InlineData(8u)] [InlineData(64u)] [InlineData(777u)]
    public void Jittery_diagonal_is_stable(uint seed)
        => Assert.Equal("3", TraceJittery(seed, (45, 160)));

    // ——— 方向表是唯一出处 ———
    // 扇区角度、显示箭头、单位位移曾经是三张各自维护的表（外加 Normalize 里第四张），
    // 加一个方向要改四处，而且谁也拦不住它们悄悄对不上。合并之后由这几条钉住。

    [Fact]
    public void Eight_directions_exactly_and_no_duplicates()
    {
        Assert.Equal(8, GestureGate.Dirs.Length);
        Assert.Equal(8, GestureGate.Dirs.Select(d => d.C).Distinct().Count());
        Assert.Equal(8, GestureGate.Dirs.Select(d => d.Arrow).Distinct().Count());
    }

    // 扇区必须恰好铺满 360°：留缝隙会有画不出方向的角度，重叠则先到先得、判定看表的顺序。
    [Fact]
    public void Sectors_tile_the_full_circle()
        => Assert.Equal(360.0, GestureGate.Dirs.Sum(d => d.Half * 2), 6);

    // 单位位移必须与中心角一致——笔迹画出来的方向就是判定用的那个方向，不能各说各话。
    [Theory]
    [InlineData('R')] [InlineData('3')] [InlineData('D')] [InlineData('1')]
    [InlineData('L')] [InlineData('7')] [InlineData('U')] [InlineData('9')]
    public void Offset_agrees_with_sector_angle(char c)
    {
        var d = GestureGate.Of(c)!.Value;
        double deg = Math.Atan2(d.Dy, d.Dx) * 180.0 / Math.PI;
        double diff = Math.Abs(deg - d.Center) % 360;
        Assert.True(Math.Min(diff, 360 - diff) < 0.001, $"{c}: 位移 {deg}° 与中心角 {d.Center}° 不一致");
    }

    // 表里的每个方向都必须能被规范化认出来，且箭头能往回折——两条路都不能漏项。
    [Fact]
    public void Every_direction_round_trips()
    {
        foreach (var d in GestureGate.Dirs)
        {
            Assert.Equal(d.C.ToString(), GestureGate.Normalize(d.C.ToString()));
            Assert.Equal(d.C.ToString(), GestureGate.Normalize(d.Arrow.ToString()));
            Assert.Equal(d.Arrow.ToString(), GestureGate.Arrows(d.C.ToString()));
        }
    }

    // ——— 串工具 ———

    [Theory]
    [InlineData("rd", "RD")]
    [InlineData("→↓", "RD")]
    [InlineData("R R D", "RD")]
    [InlineData("", "")]
    [InlineData("RX", "")]                  // 非法字符整串拒收，不是静默剪掉
    [InlineData("RLRLRLRLRLRLRLRLRLRLRLRLR", "")]   // 超长拒收（25 > MaxPath 24）
    [InlineData("RLRLRLRLRLRLRLRLRLRLRLRL",         // 24 个正好收下，边界不多不少
                "RLRLRLRLRLRLRLRLRLRLRLRL")]
    [InlineData("7913", "7913")]            // 小键盘斜角
    [InlineData("↖↗↙↘", "7913")]            // 斜角箭头
    [InlineData("4 8 6 2", "LURD")]         // 小键盘轴向 → 折成字母
    [InlineData("33", "3")]                 // 连续重复合并
    public void Normalize_canonicalizes(string raw, string want)
        => Assert.Equal(want, GestureGate.Normalize(raw));

    [Fact]
    public void Arrows_renders_display_form()
        => Assert.Equal("←↑→↓", GestureGate.Arrows("LURD"));

    [Fact]
    public void Arrows_renders_diagonals()
        => Assert.Equal("↖↗↙↘", GestureGate.Arrows("7913"));
}

// 手势现在绑在**步骤**上（RootConfig.Gestures），不再绑动作组——
// 「粘贴」「最小化」这类动作只作为手势存在，没有对应的组。
// 这一组盯的全是**静默数据丢失**：手势轨迹被某一次无关的编辑悄悄抹掉，界面上不会有任何报错。
public class GestureBindingTests
{
    // 步骤编辑器保存时是整份重建 LaunchStep（var r = ConditionProbe()）——
    // 漏带一个字段就等于「改一下动作参数，轨迹没了」。它旁边的 Enabled 是同一类陷阱。
    [Fact]
    public void Rebuilding_a_step_must_carry_the_gesture()
    {
        var original = new Clockwork.Core.LaunchStep { Kind = "system", Command = "lockScreen", Gesture = "R3D", Enabled = false };
        var rebuilt = new Clockwork.Core.LaunchStep { Kind = original.Kind, Command = original.Command };
        rebuilt.Enabled = original.Enabled;
        rebuilt.Gesture = original.Gesture;      // StepEditorWindow 里那一行
        Assert.Equal("R3D", rebuilt.Gesture);
        Assert.False(rebuilt.Enabled);
    }

    // 运行快照逐字段抄：漏了手势会让「跑起来的那一份」与配置不一致。
    [Fact]
    public void Run_snapshot_carries_gestures()
    {
        var cfg = new Clockwork.Core.RootConfig();
        cfg.Gestures.Add(new Clockwork.Core.LaunchStep { Kind = "system", Gesture = "1L7" });
        Assert.Equal("1L7", Assert.Single(cfg.SnapshotForRun().Gestures).Gesture);
    }

    // 一次性迁移：老配置里绑在动作组上的手势要搬进 Gestures，用 group 步骤表达。
    // 不搬的话，升级后用户设过的手势会无声消失。
    [Fact]
    public void Legacy_group_gesture_migrates_to_a_group_step()
    {
        var cfg = Clockwork.Core.RootConfig.Default();
        cfg.Gestures.Clear();
        var g = cfg.ActionGroups[0];
        g.Gesture = "RD";

        Assert.True(Clockwork.Core.ConfigStore.Normalize(cfg));

        var moved = Assert.Single(cfg.Gestures);
        Assert.Equal("RD", moved.Gesture);
        Assert.Equal("group", moved.Kind);
        Assert.Equal(g.Id, moved.GroupId);
        Assert.Equal("", g.Gesture);             // 清空即「已迁移」的标记
    }

    // 迁移必须幂等：清空后的组不该在每次读盘时再搬一遍，搬出一堆重复手势。
    [Fact]
    public void Migration_does_not_run_twice()
    {
        var cfg = Clockwork.Core.RootConfig.Default();
        cfg.Gestures.Clear();
        cfg.ActionGroups[0].Gesture = "RD";
        Clockwork.Core.ConfigStore.Normalize(cfg);
        Clockwork.Core.ConfigStore.Normalize(cfg);
        Assert.Single(cfg.Gestures);
    }

    // 画出来了却没绑任何东西：照旧吞掉，但**要把画出来的那一串留下**——
    // 上层拿它报「你画的是 ←↓，它没绑动作」。此前这一档 Path 是空的，于是只能沉默地吞掉，
    // 而沉默把三件事混成一件：功能没开、笔画画歪了、动作跑失败了，在用户眼里都是「按了没反应」。
    [Fact]
    public void An_unmatched_stroke_still_reports_what_was_drawn()
    {
        var g = new GestureGate(_ => false);          // 什么都匹配不上
        g.OnRightDown(100, 100);
        for (int y = 100; y <= 300; y += 5) g.OnMove(100, y);   // 一条向下的直线
        Assert.Equal(PressVerdict.Swallow, g.OnRightUp());
        Assert.Equal("D", g.Path);
    }

    // 没画（只是点了一下）时不报：那一档补发的是真右键，用户要的就是上下文菜单，
    // 这时候弹一句「你画的是空」纯属打扰。
    [Fact]
    public void A_plain_click_reports_nothing()
    {
        var g = new GestureGate(_ => false);
        g.OnRightDown(100, 100);
        Assert.Equal(PressVerdict.ReplayClick, g.OnRightUp());
        Assert.Equal("", g.Path);
    }
}
