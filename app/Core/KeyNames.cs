namespace Clockwork.Core;

/// <summary>
/// 键名 ↔ 虚拟键码。只覆盖「等待按键」这一步真正用得上的那批键。
///
/// 为什么只收这一批、不收全键位表：这张表的键名**要被人手写进输入框**，
/// 收了四万个 Unicode 键位也没有一个会有人用，而每多一个别名就多一个要维护、
/// 也多一个「为什么这个写法认、那个不认」的问题。字母 / 数字 / 功能键 / 导航 / 修饰键
/// 之外的那些（Pause、ScrollLock、OEM 键……）不收：要用就换个键。
///
/// 别名与 KeyCombo.NamedSendKeys 同一口径（ins/Insert 都认、esc/Escape 都认）——
/// 两处对同一个词的看法不一样的话，用户在「发送按键」里能写的写法到了这一步就不认了。
/// </summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> Map = Build();
    private static readonly Dictionary<int, string> Reverse = BuildReverse();

    private static Dictionary<string, int> Build()
    {
        var m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Put(string name, int vk, params string[] alias)
        {
            m[name] = vk;
            foreach (var a in alias) m[a] = vk;
        }

        for (int c = 'A'; c <= 'Z'; c++) Put(((char)c).ToString(), c);
        for (int d = '0'; d <= '9'; d++) Put(((char)d).ToString(), d);

        for (int f = 1; f <= 24; f++) Put("F" + f, 0x70 + f - 1);
        for (int n = 0; n <= 9; n++) Put("Num" + n, 0x60 + n);

        Put("Enter", 0x0D, "Return");
        Put("Escape", 0x1B, "Esc");
        Put("Tab", 0x09);
        Put("Space", 0x20);
        Put("Backspace", 0x08, "Back");
        Put("Delete", 0x2E, "Del");
        Put("Insert", 0x2D, "Ins");
        Put("Home", 0x24);
        Put("End", 0x23);
        Put("PageUp", 0x21, "PgUp");
        Put("PageDown", 0x22, "PgDn");
        Put("Up", 0x26);
        Put("Down", 0x28);
        Put("Left", 0x25);
        Put("Right", 0x27);
        Put("CapsLock", 0x14);

        // 修饰键：写出来是为了「按住 Ctrl 才算」这类用法能写。它们在别处是前缀（Ctrl+A），
        // 这里单独当一个可等待的键——用户要等的往往正是「我按的那个 Ctrl」。
        Put("Shift", 0x10);
        Put("Control", 0x11, "Ctrl");
        Put("Alt", 0x12, "Menu");
        Put("LWin", 0x5B, "WinLeft");
        Put("RWin", 0x5C, "WinRight");

        // 小键盘那一片：小键盘 Enter 与主键盘 Enter **不是**同一个虚拟键码（0x0D vs 0x0D+扩展位）。
        // 这里只收「小键盘上写着 Num 字样的那几颗」，主键盘的同名键仍走主键盘的码。
        Put("NumEnter", 0x0D);      // 见 KeyWaiter：靠扩展位区分，这里登记同一码
        Put("NumAdd", 0x6B);
        Put("NumSubtract", 0x6D);
        Put("NumMultiply", 0x6A);
        Put("NumDivide", 0x6F);
        Put("NumDecimal", 0x6E);

        return m;
    }

    private static Dictionary<int, string> BuildReverse()
    {
        var r = new Dictionary<int, string>();
        // 后登记的覆盖先登记的：0x0D 先被 Enter 登记，再被 NumEnter 登记两次。
        // 反查表要的是**人最可能写出来的那个名字**，也就是先登记的那个，所以这里反过来：
        // 只在还没见过这个码位时才写。
        foreach (var (k, v) in Map) if (!r.ContainsKey(v)) r[v] = k;
        return r;
    }

    /// <summary>键名 → 虚拟键码；认不出来返回 null。</summary>
    public static int? ToVk(string? name)
        => !string.IsNullOrWhiteSpace(name) && Map.TryGetValue(name.Trim(), out var vk) ? vk : null;

    /// <summary>虚拟键码 → 键名；认不出来返回该码的十进制形式（总有话说，不会给空串）。</summary>
    public static string ToName(int vk)
        => Reverse.TryGetValue(vk, out var n) ? n : vk.ToString();

    /// <summary>
    /// 解析一串逗号/加号分隔的键名，返回 (键名, 虚拟键码) 列表。
    /// 认不出来的**整条丢弃**并在这里如实说出来——静默忽略的话，用户写了个错的名字、
    /// 这一步却永远不返回，而界面上看不出任何异常。
    /// </summary>
    public static (List<(string Name, int Vk)> Ok, List<string> Unknown) ParseList(string? text)
    {
        var ok = new List<(string, int)>();
        var bad = new List<string>();
        foreach (var raw in (text ?? "").Split(new[] { ',', '+', '，', '、', ' ', '\t', '\n', '\r' },
                                               StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var vk = ToVk(raw);
            if (vk == null) bad.Add(raw);
            else ok.Add((raw, vk.Value));
        }
        return (ok, bad);
    }

    /// <summary>界面下拉里可选的键名（去掉别名的那批，够用且短）。</summary>
    public static readonly string[] Preset =
    {
        "Enter", "Escape", "Space", "Tab", "Backspace", "Delete", "Insert",
        "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Shift", "Control", "Alt", "LWin", "RWin", "CapsLock",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
    };
}
