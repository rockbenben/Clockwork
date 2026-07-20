using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace Clockwork.Core;

// 配置读写（原子写 + 缺失容错）。
// 原子写：先写同目录临时文件、再原子替换——直接写目标是非原子的，写到一半崩溃会截断配置、下次读失败落回默认、全部设置静默丢失。
// 不做 launchItems/specialSteps 旧格式迁移（按 spec：项目未发布、不考虑旧版兼容）。
public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,       // C# LaunchSteps ↔ json launchSteps
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文/+ 不转义成 \uXXXX，保持可读 JSON
    };

    // 深拷贝：序列化再反序列化。复制动作组/提醒共用一处，与读写走同一套 JsonOptions（含未来的 [JsonIgnore] 等约定），
    // 避免各 VM 各写一份、日后改克隆策略时漏改其一。
    public static T DeepClone<T>(T obj) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, JsonOptions), JsonOptions)!;

    public static void Write(RootConfig config, string path)
        => WriteTextAtomic(path, JsonSerializer.Serialize(config, JsonOptions), attempts: 5, delayMs: 100, throwOnFail: true);

    // 单次原子写（不睡眠、失败返回 false 不抛）：给不能在调用线程阻塞重试的场景（如 UI 线程上的状态存盘快路径）。
    public static bool TryWriteTextAtomic(string path, string text, int attempts = 1, int delayMs = 0)
        => WriteTextAtomic(path, text, attempts, delayMs, throwOnFail: false);

    // 原子写文本的唯一实现：Write（配置）与 ReminderStateStore（运行态）共用，写策略只此一份。
    // 整个「写临时 + 替换」都在重试循环内：临时与目标都可能被瞬时占用（OneDrive/索引/杀软持句柄，文件常在 Documents 下）；
    // 且 File.Replace 出错时已消耗 tmp，只有每轮重写临时文件，下次重试才有源可用、不退化成 FileNotFound 误报。
    private static bool WriteTextAtomic(string path, string text, int attempts, int delayMs, bool throwOnFail)
    {
        var tmp = path + ".tmp";
        var enc = new UTF8Encoding(false); // 无 BOM
        for (int i = 0; ; i++)
        {
            try
            {
                File.WriteAllText(tmp, text, enc);
                if (File.Exists(path)) File.Replace(tmp, path, null); // 第三参 null=不留备份
                else File.Move(tmp, path);
                return true;
            }
            catch
            {
                // 重试耗尽（持久占用）→ 清掉本轮临时文件（尽力）；目标文件保持原样、绝不损坏。
                if (i >= attempts - 1) { try { File.Delete(tmp); } catch { } if (throwOnFail) throw; return false; }
                if (delayMs > 0) Thread.Sleep(delayMs);
            }
        }
    }

    public static RootConfig Read(string path) => Read(path, out _);

    // normalized：本次读入是否做了「重启后有影响」的规范化（剔 null 元素 / 补生或重发 id）。
    // 为 true 时调用方应把规范化结果写回文件——尤其重发的提醒 id 若不落盘，每次启动都换新 id、
    // 运行态（今天已弹/稍后）永远接不上，被去重那条提醒会每次重启都重弹。
    public static RootConfig Read(string path, out bool normalized)
    {
        normalized = false;
        if (!File.Exists(path)) return RootConfig.Default();
        RootConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<RootConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return RootConfig.Default(); // 解析失败落回默认（不损坏、不崩溃）
        }
        if (cfg is null) return RootConfig.Default();
        normalized = Normalize(cfg);
        return cfg;
    }

    // 反序列化后的规范化管线：启动读取与配置导入共用——「什么算合法配置」只定义这一份，
    // 导入落盘的就是规范形，不把修补推迟到下次启动。返回是否做了「重启后有影响」的修补。
    public static bool Normalize(RootConfig cfg)
    {
        bool normalized = false;
        // 缺集合容错：任一为 null（json 显式 null）用默认补齐，避免下游 NRE。
        var def = RootConfig.Default();
        cfg.LaunchSteps = OrDefault(cfg.LaunchSteps, def.LaunchSteps);
        cfg.Reminders = OrDefault(cfg.Reminders, def.Reminders);
        cfg.Settings = OrDefault(cfg.Settings, def.Settings);
        cfg.ActionGroups = OrDefault(cfg.ActionGroups, def.ActionGroups);
        // 剔除数组里的 null 元素（手改出 "reminders":[null] 之类）：反序列化会留下 null 引用，
        // 下面按元素解引用即 NRE，而这些兜底在 try/catch 之外，一崩就违背「解析失败落默认、绝不崩」。
        normalized |= cfg.LaunchSteps.RemoveAll(x => x is null) > 0;
        normalized |= cfg.Reminders.RemoveAll(x => x is null) > 0;
        normalized |= cfg.ActionGroups.RemoveAll(x => x is null) > 0;
        // 补生缺失/空白 id：运行态与组引用都按 id 做键，json 里写成 "id":"" 会让多条共用一份状态、
        // 或组引用失效。默认值仅在 json 省略 id 时生效，显式空串需在此规范化。
        // 提醒 id 还要去重：运行态按 id 做键，重复 id（复制粘贴/手改出）会让两条共用一份状态、当天一条被另一条压制。
        // （reminder id 不被任何配置引用，可安全重发；组 id 被 groupId/silentGroupId/onYes 引用，只补空白不去重。）
        var seenReminderIds = new HashSet<string>();
        foreach (var r in cfg.Reminders)
            if (string.IsNullOrWhiteSpace(r.Id) || !seenReminderIds.Add(r.Id))
            {
                seenReminderIds.Add(r.Id = Guid.NewGuid().ToString());
                normalized = true;
            }
        foreach (var g in cfg.ActionGroups) if (string.IsNullOrWhiteSpace(g.Id)) { g.Id = Guid.NewGuid().ToString(); normalized = true; }
        // 嵌套引用容错：json 显式写 "onYes":null / "steps":null 会覆盖模型初始化器，下游（编辑器读 .OnYes.Type、遍历 Steps）会 NRE。
        foreach (var s in cfg.LaunchSteps) { s.OnYes ??= new(); normalized |= NormalizeOnYes(s.OnYes); }
        // json 显式 "repeatUntil":null 会覆盖模型的 "" 默认；UpdateAfterFire 直接 Regex.IsMatch(它) 会 NPE 崩，补回空串。
        foreach (var r in cfg.Reminders) { r.OnYes ??= new(); r.RepeatUntil ??= ""; normalized |= NormalizeOnYes(r.OnYes); }
        foreach (var g in cfg.ActionGroups) { g.Steps ??= new(); normalized |= g.Steps.RemoveAll(s => s is null) > 0; foreach (var s in g.Steps) { s.OnYes ??= new(); normalized |= NormalizeOnYes(s.OnYes); } }
        return normalized;
    }

    private static T OrDefault<T>(T? value, T fallback) where T : class => value ?? fallback;

    // 「点是后」空组引用规范化：旧版编辑器允许存下 type="group" 而目标为空（下拉留在「（无）」），
    // 点「是」什么都不做。读入时归一成 none 并触发写回，运行期不必再对这种残留误报「组被删」。
    private static bool NormalizeOnYes(OnYes y)
    {
        if (y.Type != "group" || !string.IsNullOrWhiteSpace(y.Target)) return false;
        y.Type = "none";
        return true;
    }
}
