using System.Globalization;
using Clockwork.Core;
using Clockwork.I18n;
using Xunit;

// 系统命令有两种用法，文案也该有两种长度：
// 下拉里要能分辨（锁屏 / 息屏 / 睡眠 / 休眠 光看名字分不出），格子上要能认（88 宽装不下一句解释）。
public class SystemCommandShortLabelTests
{
    [Theory]
    [InlineData("锁屏（回来需输密码）", "锁屏")]
    [InlineData("Lock Screen (password on return)", "Lock Screen")]
    [InlineData("Bildschirm sperren (Passwort bei Rückkehr)", "Bildschirm sperren")]
    [InlineData("قفل الشاشة (كلمة مرور عند العودة)", "قفل الشاشة")]
    public void Drops_the_trailing_aside(string full, string expected)
        => Assert.Equal(expected, StepDisplay.StripTrailingAside(full));

    [Theory]
    [InlineData("显示桌面")]
    [InlineData("Show Desktop")]
    [InlineData("")]
    public void Leaves_a_label_without_an_aside_alone(string label)
        => Assert.Equal(label, StepDisplay.StripTrailingAside(label));

    // 括号在中间不是补充说明，是名字的一部分——只砍结尾那一段。
    [Fact]
    public void Only_the_trailing_one_goes()
        => Assert.Equal("显示器：仅第二屏幕", StepDisplay.StripTrailingAside("显示器：仅第二屏幕"));

    // 整条都在括号里时宁可啰嗦，也不能返回空——空标签的格子看着像坏了。
    [Fact]
    public void Never_strips_everything_away()
        => Assert.Equal("（全部）", StepDisplay.StripTrailingAside("（全部）"));

    // 真正要防的回归：任何一种语言下，短名都不该以「括号开了没下文」收尾。
    [Fact]
    public void No_language_ends_mid_parenthesis()
    {
        var old = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (_, lang) in Languages.All)
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(lang);
                foreach (var kv in StepDisplay.SystemCommandMap())
                {
                    var s = StepDisplay.SystemCommandShortLabel(kv.Key);
                    Assert.False(s.Contains('（') || s.Contains('('), $"{lang}/{kv.Key}: {s}");
                    Assert.False(string.IsNullOrWhiteSpace(s), $"{lang}/{kv.Key} 短名为空");
                }
            }
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }
}
