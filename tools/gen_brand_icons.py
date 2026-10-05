#!/usr/bin/env python3
"""生成 app/Core/BrandIcons.g.cs —— 面板可选的「常用程序」品牌图标。

数据来源 Simple Icons（CC0），只取这里列出的 slug。图标是 24x24 的单条 path，
连同官方品牌色一起编进源码：不打包字体、不加资源目录、单文件 exe 照旧。

重新生成（改了 SLUGS 或想升级版本时）：
    python tools/gen_brand_icons.py            # 联网下载 simple-icons 最新版
    python tools/gen_brand_icons.py --from <目录>   # 用已经解开的 package/ 目录，不联网
"""
import io
import json
import re
import sys
import tarfile
import urllib.request
from pathlib import Path

# 顺序就是选择器里铺格子的顺序：社交/内容 → 浏览器/开发 → 数据/笔记/云 → 工具/媒体/系统。
# 每 16 个一行，与图标库同一档宽度；总数加进 IconLibrary.Glyphs 后仍得是 16 的整数倍
# （选择器是 16 列 UniformGrid，缺角看着像库坏了，IconLibraryTests 钉着这条）。
# 挑的是「桌面上一键就要打开的那些」，不是全量：Simple Icons 有 3400+ 枚，这里只留 64 枚。
SLUGS = [
    "wechat", "qq", "discord", "telegram", "whatsapp", "signal", "instagram", "x",
    "facebook", "reddit", "bilibili", "tiktok", "twitch", "youtube", "google", "duckduckgo",
    "googlechrome", "firefox", "brave", "opera", "github", "gitlab", "git", "docker",
    "kubernetes", "nginx", "nodedotjs", "npm", "python", "rust", "dotnet", "electron",
    "postgresql", "mysql", "redis", "sqlite", "mongodb", "supabase", "firebase", "cloudflare",
    "vercel", "notion", "obsidian", "ticktick", "trello", "dropbox", "googledrive", "keepassxc",
    "7zip", "sharex", "obsstudio", "vlcmediaplayer", "mpv", "spotify", "plex", "jellyfin",
    "steam", "blender", "gimp", "figma", "termius", "autohotkey", "thunderbird", "ubuntu",
]

OUT = Path(__file__).resolve().parent.parent / "app" / "Core" / "BrandIcons.g.cs"
REGISTRY = "https://registry.npmjs.org/simple-icons"


def latest_version():
    with urllib.request.urlopen(REGISTRY, timeout=60) as r:
        return json.load(r)["dist-tags"]["latest"]


def fetch_package(dest: Path) -> Path:
    ver = latest_version()
    url = f"{REGISTRY}/-/simple-icons-{ver}.tgz"
    print(f"downloading simple-icons {ver}")
    blob = urllib.request.urlopen(url, timeout=180).read()
    dest.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(blob)) as t:
        t.extractall(dest)
    return dest / "package"


def main():
    if "--from" in sys.argv:
        pkg = Path(sys.argv[sys.argv.index("--from") + 1])
    else:
        pkg = fetch_package(Path(__file__).resolve().parent / ".cache")
    icons = json.load((pkg / "data" / "simple-icons.json").open(encoding="utf-8"))
    version = json.load((pkg / "package.json").open(encoding="utf-8"))["version"]
    by_slug = {i["slug"]: i for i in icons}

    missing = [s for s in SLUGS if s not in by_slug]
    if missing:
        sys.exit("simple-icons 里没有这些 slug: " + " ".join(missing))
    dupes = [s for s in set(SLUGS) if SLUGS.count(s) > 1]
    if dupes:
        sys.exit("清单里有重复 slug: " + " ".join(dupes))
    if len(SLUGS) % 16:
        sys.exit(f"清单得是 16 的整数倍（选择器一行 16 格），现在 {len(SLUGS)} 个")

    rows, total = [], 0
    for slug in SLUGS:
        meta = by_slug[slug]
        svg = (pkg / "icons" / f"{slug}.svg").read_text(encoding="utf-8")
        m = re.search(r'<path[^>]*\sd="([^"]+)"', svg)
        if not m:
            sys.exit(f"{slug}: svg 里找不到 path d")
        d = m.group(1).strip()
        if not re.fullmatch(r"[MmLlHhVvCcSsQqTtAaZz0-9eE.,\-+ ]+", d):
            sys.exit(f"{slug}: path 里有 WPF 迷你语言不认的字符")
        title = meta["title"].replace("\\", "").replace('"', "'")
        hexv = meta["hex"]
        if not re.fullmatch(r"[0-9A-Fa-f]{6}", hexv):
            sys.exit(f"{slug}: hex 不是 6 位 {hexv}")
        # F0 = 非零环绕，与 SVG 的默认 fill-rule 同一条口径（WPF 自己默认是 even-odd，
        # 不写这一位的话，微信、QQ 这种带镂空的 logo 会把洞填成实心）。
        rows.append(f'        ("{slug}", "{title}", 0x{hexv.upper()},\n            "{d}"),')
        total += len(d)

    body = "\n".join(rows)
    text = f"""// 生成文件，别手改 —— 改清单或升级版本请跑 `python tools/gen_brand_icons.py`。
// 数据源：Simple Icons（CC0）v{version}，重新生成请跑 tools/gen_brand_icons.py。
// 每条 = (slug, 官方名, 品牌色, 24x24 的 path)。path 前缀 F0 是非零环绕，与 SVG 默认 fill-rule 一致。
// 认不出来的 slug 只会让那一格回退成线描字形，不会崩（见 BrandIcons.TryGet 与 PanelIcon）。
namespace Clockwork.Core;

public static partial class BrandIcons
{{
    /// <summary>内置的品牌图标，顺序即选择器里铺格子的顺序。</summary>
    internal static readonly (string Slug, string Title, uint Hex, string Path)[] Entries =
    {{
{body}
    }};
}}
"""
    OUT.write_text(text.replace("\n", "\r\n"), encoding="utf-8", newline="")
    print(f"wrote {OUT}  entries={len(SLUGS)}  path chars={total}  bytes={OUT.stat().st_size}")


if __name__ == "__main__":
    main()
