using System.Linq;
using Clockwork.Core;
using Xunit;

// 三条「界面上什么都看不出来，东西却已经没了 / 崩了」的路径。
// 它们的共同点是：出事的那一刻没有任何提示，用户要等到很久以后才发现，
// 而那时已经无从判断是哪一步弄的。
public class SilentDataLossTests
{
    // ── 手写的手势轨迹不能被「洗」没 ──
    // Normalize 遇到任何一个不认识的字符就整串拒收并返回空。那是「不合法」的判定，
    // 不是「它应该变成空」的修正——写回盘等于替用户删掉他手写的绑定，没有提示也找不回来。
    [Theory]
    [InlineData("R-D")]        // 连字符：手写时最自然的分隔法
    [InlineData("R\tD")]
    [InlineData("R\nD")]
    [InlineData("RDRDRDRDRDRDRDRDRDRD")]   // 超过 MaxPath
    public void An_unparsable_gesture_survives_a_config_load(string raw)
    {
        var cfg = new RootConfig();
        cfg.Gestures.Add(new LaunchStep { Kind = "system", Command = "lockScreen", Gesture = raw });
        ConfigStore.Normalize(cfg);
        Assert.Equal(raw, cfg.Gestures[0].Gesture);
    }

    // 能洗的照样洗：箭头和小键盘数字都折成规范形，纯空白洗成空
    //（留着空白会让钩子为一条根本不存在的手势装上）。
    [Theory]
    [InlineData("→↓", "RD")]
    [InlineData("64", "RL")]   // 小键盘：6=右 4=左
    [InlineData("   ", "")]
    public void A_parsable_gesture_is_still_normalized(string raw, string want)
    {
        var cfg = new RootConfig();
        cfg.Gestures.Add(new LaunchStep { Kind = "system", Command = "lockScreen", Gesture = raw });
        ConfigStore.Normalize(cfg);
        Assert.Equal(want, cfg.Gestures[0].Gesture);
    }

    // ── 代理区码位不能让面板崩 ──
    // 图标框里填 "DEAD" 这种看着很合理的十六进制，落在 D800–DFFF 里就不对应任何字符，
    // char.ConvertFromUtf32 会抛，而这一路上下都没有 catch——面板从此开一次崩一次。
    [Theory]
    [InlineData("DEAD")]
    [InlineData("D800")]
    [InlineData("DFFF")]
    [InlineData("0xDBAD")]
    public void A_surrogate_glyph_code_does_not_throw(string code)
    {
        var spec = PanelIcon.Resolve(code, "app");
        Assert.NotNull(spec.Fallback);   // 当路径处理，取不到就回退字形——总之不崩
    }

    // 正常码位照旧当字形使
    [Theory]
    [InlineData("E7C4")]
    [InlineData("0xE710")]
    public void A_normal_glyph_code_still_works(string code)
        => Assert.Equal(PanelIconKind.Glyph, PanelIcon.Resolve(code, "app").Kind);

    // 同一条契约在**迁移路径**上也得成立。手势曾经绑在动作组上（ActionGroup.Gesture），
    // 那段迁移遇到解析不出来的笔迹时会把字段清空，而 App.LoadConfig 会把清空写回盘——
    // 用户手写的绑定就此消失，没有提示、没有 .bad 备份、也找不回来。
    // 上面那条用的是同一个 "R-D"，只是从没覆盖到这条路。
    [Fact]
    public void An_unparsable_gesture_on_a_group_is_not_wiped_by_the_migration()
    {
        var g = new ActionGroup { Name = "专注", Gesture = "R-D", ShowInPanel = false };
        g.Steps.Add(new LaunchStep { Kind = "system", Command = "lockScreen" });
        var cfg = new RootConfig { ActionGroups = new List<ActionGroup> { g } };
        ConfigStore.Normalize(cfg);
        Assert.Equal("R-D", cfg.ActionGroups[0].Gesture);
    }
}
