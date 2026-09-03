"""注释与文档里"指名道姓"的东西，是否还存在；文档之间是否还同步。

    python tools/audit_comment_refs.py

查五类纯机械可验的事：
  ① 注释里的 `Type.Member` —— 类型在本仓库里存在时，成员也必须存在
  ② 注释里的 resx 键 —— 必须在中性 Strings.resx 里找得到
  ③ 注释 / 文档里的文件路径 —— 必须在仓库里找得到
  ④ 18 份 README 的结构指纹（要点数、表格行、小节数、要点顺序）必须与 README.md 一致
  ⑤ 文档里加粗引用的界面文字，与该语言 resx 的真值只差一个字 —— 十有八九是打错了

**为什么是工具而不是测试。** 这个仓库有意在注释里引述已删掉的写法来解释它为何错
（"上一版写的是 `Assert.Equal(!Themes.SystemPrefersLight(), ...)`"、"Sum_Unset 原名
Sum_Group_None"、"那个键在 18 份 resx 里一个都没有"）。那是好惯例，而任何"名字必须存在"
的检查都会和它永久打架——做成测试就得为每一处历史引述写一条豁免，名单只会越攒越长，
最后变成没人敢删的橡皮图章。所以这里只做成手动跑的工具：输出要人看一眼，
分清"引述历史"和"真的漂了"。带「上一版 / 原名 / 曾经 / 一个都没有」这类措辞的命中会标出来，
好让人先跳过它们。

首次跑出来 350 处引用里 2 处真漂移（EventTrigger.All → ReminderEvent.All、
App.ShouldSkipStartupList → App.SkipStartupReason）；⑤ 是事后补的——同一轮里我手打
Unicode 转义拼出的三个界面标签各错了一个码点（「鼠」「빠」「처」），靠事后比对才抓到。
⑤ 的第一版把「句首小写 vs Title Case 标签」和带「」引号的引用都当成了打错，一百多条噪声；
现在大小写不敏感、剥引号，短串只容一个字的差。
"""
import io, os, re, subprocess, sys, glob

# Windows 控制台默认 gbk，打印 CJK / 한글 / 泰文会直接崩——本轮崩了四次。
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

files = subprocess.run(["git", "ls-files"], capture_output=True, text=True).stdout.split()
cs = [f for f in files if f.endswith(".cs") and "/obj/" not in f and not f.startswith("obj/")]
xaml = [f for f in files if f.endswith(".xaml")]
docs = [f for f in files if f.endswith(".md")]


def read(f):
    try:
        return io.open(f, encoding="utf-8-sig").read()
    except Exception:
        return ""


src = {f: read(f) for f in cs + xaml}
doctext = {f: read(f) for f in docs}

LINE_COMMENT = re.compile(r"//.*?$", re.M)
BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
XML_COMMENT = re.compile(r"<!--.*?-->", re.S)

live = set()
declared_types = set()
TYPEDECL = re.compile(r"\b(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)")
for f, t in src.items():
    body = LINE_COMMENT.sub("", BLOCK_COMMENT.sub("", t)) if f.endswith(".cs") else XML_COMMENT.sub("", t)
    live.update(re.findall(r"[A-Za-z_][A-Za-z0-9_]*", body))
    declared_types.update(TYPEDECL.findall(body))
    declared_types.add(os.path.basename(f).split(".")[0])   # partial class App 按文件名引用是惯例

resx_keys = set(re.findall(r'<data name="([^"]+)"', read("app/Resources/Strings.resx")))
all_paths = set(files)
basenames = {os.path.basename(f) for f in files}

# 「这是在引述历史」的措辞。命中行 ±2 行内出现，就标出来让人先跳过。
HISTORY = re.compile(r"上一版|原名|曾经|一个都没有|已删|改名|以前|原来|曾把|那时")


def comments(f, t):
    pat = r"//(.*)$" if f.endswith(".cs") else r"<!--(.*)$"
    for i, line in enumerate(t.split("\n"), 1):
        m = re.search(pat, line)
        if m:
            yield i, m.group(1)


def looks_historical(f, ln):
    lines = (src.get(f) or doctext.get(f) or "").split("\n")
    lo, hi = max(0, ln - 3), min(len(lines), ln + 2)
    return any(HISTORY.search(l) for l in lines[lo:hi])


rows = []

# ── ① Type.Member ──
REF = re.compile(r"\b([A-Z][A-Za-z0-9]{2,})\.([A-Za-z_][A-Za-z0-9_]{1,})\b")
n1 = 0
for f, t in src.items():
    for ln, c in comments(f, t):
        for typ, mem in REF.findall(c):
            if typ not in declared_types:
                continue
            if mem in ("cs", "xaml", "resx", "md", "py", "exe", "dll", "json", "log", "txt"):
                continue
            n1 += 1
            if mem not in live:
                rows.append(("①", f, ln, "%s.%s" % (typ, mem), "成员不存在"))

# ── ② resx 键 ──
KEY = re.compile(r"\b((?:Warn|Err|Toast|Tray|Sum|Ed|Settings|Log|Mig|Confirm|Panel|Gesture|Win|Sys|Icon|Theme|"
                 r"Picker|Hotkey|Ask|Col|Btn|Skip|Update|Ports|Mouse|Rem|Cond|Day|Month|Dlg|Cmd|Unit|Tab)_[A-Za-z0-9_]+)")
n2 = 0
for f, t in src.items():
    for ln, c in comments(f, t):
        for k in KEY.findall(c):
            if k in live:
                continue
            n2 += 1
            if k not in resx_keys:
                rows.append(("②", f, ln, k, "resx 里没有这个键"))

# ── ③ 文件路径 ──
PATH = re.compile(r"\b((?:[A-Za-z0-9_.\-]+/)*[A-Za-z0-9_.\-]+\.(?:cs|xaml|resx|md|py|yml|pubxml|csproj))\b")
n3 = 0
seen = set()
for f, t in list(src.items()) + list(doctext.items()):
    it = comments(f, t) if f.endswith((".cs", ".xaml")) else enumerate(t.split("\n"), 1)
    for ln, c in it:
        for p in PATH.findall(c):
            n3 += 1
            if p in all_paths or os.path.basename(p) in basenames or (f, p) in seen:
                continue
            seen.add((f, p))
            rows.append(("③", f, ln, p, "仓库里找不到这个文件"))

# ── ④ 18 份 README 结构同步 ──
# 要点顺序用行首 emoji 当语言无关的锚。只取第一个码点：🖱️ 是 🖱+FE0F 两个，
# 取多了各语言就对不上（第一版用 \S{1,4}，17 份全报"顺序不同"，而它们明明是同步的）。
READMES = ["README.md", "README.zh.md"] + sorted(g.replace("\\", "/") for g in glob.glob("docs/i18n/README.*.md"))


def fingerprint(f):
    lines = doctext[f].split("\n")
    bullets = [l for l in lines if l.startswith("- ")]
    order = tuple(l[2] for l in bullets if len(l) > 2 and (ord(l[2]) >= 0x1F300 or 0x2600 <= ord(l[2]) <= 0x27BF))
    return (len(bullets), sum(1 for l in lines if l.startswith("| ")),
            sum(1 for l in lines if l.startswith("## ")), order)


base = fingerprint("README.md")
for f in READMES[1:]:
    fp = fingerprint(f)
    if fp[:3] != base[:3]:
        rows.append(("④", f, 0, "要点 %d / 表行 %d / 小节 %d" % fp[:3],
                     "与 README.md 不同（%d / %d / %d）" % base[:3]))
    elif fp[3] != base[3]:
        rows.append(("④", f, 0, "要点顺序", "与 README.md 的 emoji 顺序不同"))

# ── ⑤ 文档里加粗的界面文字 ↔ 该语言 resx 真值（差一个字 = 大概率打错） ──
LANG_OF = {"README.md": "en", "docs/USAGE.md": "en", "README.zh.md": "", "docs/USAGE.zh.md": "",
           "docs/i18n/README.zh-Hant.md": "zh-TW"}
for f in glob.glob("docs/i18n/README.*.md"):
    LANG_OF.setdefault(f.replace("\\", "/"), os.path.basename(f)[7:-3])


def resx_values(lang):
    p = "app/Resources/Strings.resx" if lang == "" else "app/Resources/Strings.%s.resx" % lang
    return set(re.findall(r"<value>([^<]{4,60})</value>", read(p)))


def lev(a, b, cap):
    if abs(len(a) - len(b)) > cap:
        return cap + 1
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        if min(cur) > cap:
            return cap + 1
        prev = cur
    return prev[-1]


BOLD = re.compile(r"\*\*([^*`\n]{4,40})\*\*")
QUOTES = "「」『』“”\"'"
n5 = 0
for f, lang in LANG_OF.items():
    f = f.replace("\\", "/")
    if f not in doctext:
        continue
    vals = resx_values(lang)
    lower = {v.casefold() for v in vals}
    by_len = {}
    for v in vals:
        by_len.setdefault(len(v), []).append(v)
    for ln, line in enumerate(doctext[f].split("\n"), 1):
        for span in BOLD.findall(line):
            # 正文里的引用常带引号、句末标点，且按句首小写写——这些都不是"打错"
            s = span.strip().strip(QUOTES).rstrip("：:。.,▸ ")
            if len(s) < (5 if s.isascii() else 4):   # 拉丁短词（step/Steps）差一个字母太常见
                continue
            key = s.casefold()
            if key in lower:
                continue
            n5 += 1
            # 短串只容一个字的差（四字词差两个字已是另一个词），长串容两个
            cap = 1 if len(s) < 8 else 2
            near = [v for L in range(len(s) - cap, len(s) + cap + 1) for v in by_len.get(L, ())
                    if lev(key, v.casefold(), cap) <= cap]
            if near:
                rows.append(("⑤", f, ln, s, "像是打错的界面文字，resx 里是「%s」" % near[0]))

# ── 输出 ──
print("检查：Type.Member %d · resx 键 %d · 文件路径 %d · README 同步 %d 份 · 文档加粗 %d 处"
      % (n1, n2, n3, len(READMES) - 1, n5))
print("可疑 %d 处。标 [历史?] 的多半是刻意引述已删代码，先跳过；其余逐条看。" % len(rows))
for tag, f, ln, what, why in rows:
    hist = " [历史?]" if ln and looks_historical(f, ln) else ""
    where = "%s:%d" % (f, ln) if ln else f
    print("  %s %s  %s  — %s%s" % (tag, where, what, why, hist))
sys.exit(0)
