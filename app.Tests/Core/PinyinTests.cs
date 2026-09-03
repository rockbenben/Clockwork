using Clockwork.Core;
using Xunit;

// 拼音首字母是借 .NET 的 zh-CN 排序规则算出来的，而「zh-CN 按拼音排」不是文档承诺的行为。
// 所以这里逐字钉住：哪天排序规则变了（换 ICU 版本、换运行时），测试先叫，而不是用户先发现搜不到东西。
public class PinyinTests
{
    [Theory]
    [InlineData('截', 'j')]
    [InlineData('图', 't')]
    [InlineData('息', 'x')]
    [InlineData('屏', 'p')]
    [InlineData('锁', 's')]
    [InlineData('静', 'j')]
    [InlineData('音', 'y')]
    [InlineData('闭', 'b')]
    [InlineData('麦', 'm')]
    [InlineData('免', 'm')]
    [InlineData('打', 'd')]
    [InlineData('扰', 'r')]
    [InlineData('恢', 'h')]
    [InlineData('复', 'f')]
    [InlineData('通', 't')]
    [InlineData('知', 'z')]
    [InlineData('双', 's')]
    [InlineData('扩', 'k')]
    [InlineData('展', 'z')]
    [InlineData('仅', 'j')]
    [InlineData('主', 'z')]
    [InlineData('清', 'q')]
    [InlineData('剪', 'j')]
    [InlineData('贴', 't')]
    [InlineData('板', 'b')]
    [InlineData('任', 'r')]
    [InlineData('务', 'w')]
    [InlineData('管', 'g')]
    [InlineData('理', 'l')]
    [InlineData('器', 'q')]
    [InlineData('显', 'x')]
    [InlineData('示', 's')]
    [InlineData('桌', 'z')]
    [InlineData('面', 'm')]
    public void Maps_a_hanzi_to_its_initial(char hanzi, char expected)
        => Assert.Equal(expected, Pinyin.Initial(hanzi));

    // 用户配置里真实存在的那些标签，整串验一遍——逐字对是一回事，连起来对才是搜索真正用到的。
    [Theory]
    [InlineData("截图", "jt")]
    [InlineData("息屏", "xp")]
    [InlineData("锁屏", "sp")]
    [InlineData("静音", "jy")]
    [InlineData("闭麦", "bm")]
    [InlineData("免打扰", "mdr")]
    [InlineData("恢复通知", "hftz")]
    [InlineData("双屏扩展", "spkz")]
    [InlineData("仅主屏", "jzp")]
    [InlineData("清剪贴板", "qjtb")]
    [InlineData("任务管理器", "rwglq")]
    [InlineData("显示桌面", "xszm")]
    [InlineData("专注·开始工作", "zz·ksgz")]
    [InlineData("收工·下班", "sg·xb")]
    public void Maps_a_whole_label(string label, string expected)
        => Assert.Equal(expected, Pinyin.Initials(label));

    // 非汉字原样留下（压成小写）：标签里混着英文和数字是常态（「PicGo」「输入2」）。
    [Theory]
    [InlineData("PicGo", "picgo")]
    [InlineData("输入2", "sr2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Leaves_non_hanzi_alone(string? text, string expected)
        => Assert.Equal(expected, Pinyin.Initials(text));
}
