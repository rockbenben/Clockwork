"""按尺寸分别生成 logo.ico 的每一张图（icon hinting），并从同一函数写出 logo.svg。

配色：黄铜，不是蓝。
  换掉的原因是**底色**。托盘、任务栏、Alt-Tab、通知卡片、设置页——这个标记出现的地方
  几乎全是 #101010~#191919 的暗底，而 #5B8FD9 和暗底同属冷色、明度也接近（实测对比度
  7:1，和黄铜那一版几乎一样），所以它不是「看不清」，是**贴在底上不出来**：冷色在暗底上后退。
  黄铜前进。而且擒纵轮本来就是黄铜件——这一版只是把材质画对了。

  同时把单色平涂改成两层：轮体亮黄铜 + 轴毂深黄铜。平涂在 16px 下只是一坨色块，
  加一层深色轴毂才有「中间有根轴」的读法，这在最小尺寸上比齿形更早被认出来。
  暗色描边保留：浅色任务栏和资源管理器白底上，黄铜只有 2:1，全靠这道边立住。

齿形沿用非对称棘齿（一侧陡、一侧斜），那是擒纵轮区别于普通齿轮的地方。

用法：SHARP_DIR=<装了 sharp 的目录> python tools/make_icon.py
"""
import io, math, os, subprocess, sys, tempfile

OUT_ICO = "assets/logo.ico"
OUT_PNG = "assets/logo-256.png"
MASTER = "assets/logo.svg"

# 黄铜三层。BRIGHT 只在 48+ 出现（小尺寸放不下第三层）。
# 黄铜。轮体给一道由亮到暗的斜向渐变——平涂在暗底上读作一块芥末色饼，
# 有明暗才读作一个车出来的黄铜件。16px 上渐变看不见，但也不碍事。
LIT, MARK, HUB, BRIGHT, EDGE = "#EBBE66", "#C08C2E", "#7A5115", "#F2DCA6", "#140E05"


LEAN = 0.86          # 见 ratchet 的说明
DEEP_16, DEEP_256 = 0.155, 0.105   # 齿深（占 span）在两端的取值


def depth_for(size):
    """齿深随尺寸**微微**变浅（16px 0.155 → 256px 0.105），按 log2 线性插值。

    这是唯一一处比例不固定的地方，因为两端的需求相反、无法用一个值同时满足：
      16px  齿深 0.105 只有 1.5 个像素，抗锯齿一抹就没了，剩一个圆饼；
      256px 齿深 0.155 的齿细长外张，读作太阳花而不是棘轮（实测，见 sweep）。
    按 log2 插值而不是分档，是为了不出现某一档突然变形——上一版 48px 那一跳就是分档来的。
    """
    return DEEP_16 - (DEEP_16 - DEEP_256) * (math.log2(size) - 4) / 4


def ratchet(cx, cy, r_tip, r_root, teeth, lean=LEAN, phase=-math.pi / 2):
    """擒纵齿的闭合折线。每颗齿：从齿根出发，沿 lean 比例的长斜背爬到齿尖，
    再几乎垂直地落回下一个齿根（锁面）。

    lean 是这个标记能不能被认出来的**唯一**关键参数，不是风格旋钮：
      0.5   两侧等长 → 对称三角 → 读作太阳/锯片
      0.86  长背 + 陡锁面 → 齿尖明显朝一个方向勾 → 读作棘轮/擒纵轮
    实测 0.62（上一版）在 256px 上仍然读作太阳花：两条边长度差得不够，眼睛看不出方向。
    """
    pts = []
    for i in range(teeth):
        a = phase + i * 2 * math.pi / teeth
        pts.append((cx + r_root * math.cos(a), cy + r_root * math.sin(a)))
        b = a + 2 * math.pi / teeth * lean
        pts.append((cx + r_tip * math.cos(b), cy + r_tip * math.sin(b)))
    return pts


def mark_svg(size, teeth):
    """所有尺寸出自这一个函数。轮径、轴毂占比、倾角在每个尺寸上完全相同，
    随尺寸变的只有细节量和齿深，所以放大不会突然变成另一个东西
    （改之前 48px 那一档会从「无瓦片 8 粗齿」跳成「带瓦片 24 细齿」，两个标识）：
      齿数    8 → 16（小尺寸装不下细齿，物理限制，不是风格选择）
      齿深    0.155 → 0.105，见 depth_for
      高光轴心 仅 48+
    轴毂占比 0.125 而不是 0.145，高光点 0.038 而不是 0.052：大深盘配一圈放射齿读作
    「向日葵花心」，收小之后才读作一根轴。同理没有用「深色圆环」画凸台——那读作靶心。
    （试过在 128+ 加一道车削槽，实测把 256px 那张读成了「花心」——同心圆比齿更抢眼。
      轮辐也试过：0.075×span 宽的辐条在 256px 上读作四块扇形饼，更差。都去掉了。）

    轮体是实心的：中心不镂空。镂空版（中心占直径 52%）在 16px 下只剩一圈糊掉的细环。
    轴毂用深黄铜**填**而不是挖洞——挖洞在暗底上直接消失（洞里就是底色），填色在任何底上都读得出。
    """
    s = float(size)
    c = s / 2
    span = s * 0.96
    # 描边随尺寸变细，且**上限压住**。0.15·s^0.68 到 256px 是 6.5 像素，
    # 白底上读成「黑齿轮镶了个黄芯」，主次颠倒。0.24·√s 给出 16→1.0 / 48→1.7 / 256→3.8。
    stroke_w = max(0.9, 0.24 * s ** 0.5)
    r_tip = span * 0.50
    r_root = r_tip - span * depth_for(size)
    hub = span * 0.125
    d = "M" + " L".join(f"{x:.2f},{y:.2f}" for x, y in ratchet(c, c, r_tip, r_root, teeth)) + " Z"

    parts = [f'<defs><linearGradient id="b" x1="0.15" y1="0" x2="0.85" y2="1">'
             f'<stop offset="0" stop-color="{LIT}"/><stop offset="1" stop-color="{MARK}"/>'
             f'</linearGradient></defs>',
             f'<path d="{d}" fill="url(#b)" stroke="{EDGE}" stroke-width="{stroke_w:.2f}" '
             f'stroke-linejoin="round"/>']
    parts.append(f'<circle cx="{c:.2f}" cy="{c:.2f}" r="{hub:.2f}" fill="{HUB}"/>')
    if size >= 48:
        parts.append(f'<circle cx="{c:.2f}" cy="{c:.2f}" r="{span*0.038:.2f}" fill="{BRIGHT}"/>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" '
            f'viewBox="0 0 {size} {size}">{"".join(parts)}</svg>')


# 尺寸 → 齿数。20 / 40 是 Win11 在 125% / 250% 缩放下真正取的两档，别删。
# 一律不带深色瓦片：瓦片原本只为给标记一个已知的暗底，而暗色描边把同一件事在**任何**
# 底色上都干成了；且 Win11 任务栏在 150% 下取 48px 这档，带瓦片时那里会出现一块黑方。
PLAN = [(16, 8), (20, 8), (24, 10), (32, 11), (40, 12),
        (48, 13), (64, 14), (128, 16), (256, 16)]

RASTER = os.environ.get("SHARP_DIR", "")   # 装了 sharp 的目录（node）


def rasterize(svg_bytes, size, out_png):
    """用 node + sharp 栅格化。density 拉高再缩，避免小尺寸下描边被抹掉。"""
    with tempfile.NamedTemporaryFile("wb", suffix=".svg", delete=False) as f:
        f.write(svg_bytes); tmp = f.name
    js = (f"import sharp from 'sharp';import fs from 'fs';"
          f"await sharp(fs.readFileSync({tmp!r}),{{density:600}})"
          f".resize({size},{size}).png().toFile({out_png!r});")
    # 脚本必须落在装了 sharp 的目录里：node 的 ESM 解析按脚本自身位置找 node_modules，
    # 放系统临时目录再用 cwd 指过去不管用（实测 ERR_MODULE_NOT_FOUND）。
    mjs = os.path.join(RASTER, "_raster.mjs")
    io.open(mjs, "w", encoding="utf-8").write(js)
    subprocess.run(["node", mjs], cwd=RASTER, check=True)
    os.unlink(tmp); os.unlink(mjs)


def main():
    from PIL import Image
    if not RASTER:
        sys.exit("需要 SHARP_DIR 指向装了 sharp 的目录")
    tmpdir = tempfile.mkdtemp()
    frames = []
    for size, teeth in PLAN:
        png = os.path.join(tmpdir, f"{size}.png")
        rasterize(mark_svg(size, teeth).encode("utf-8"), size, png)
        frames.append(Image.open(png).convert("RGBA"))
    # append_images 是关键：只传一张的话 Pillow 会**拿它缩放**出所有尺寸，
    # 分尺寸画的那几张就白画了（第一次就是这么错的，图看着没变）。
    frames[-1].save(OUT_ICO, format="ICO", sizes=[(s, s) for s, _ in PLAN],
                    append_images=frames[:-1])
    frames[-1].save(OUT_PNG)
    # 主图从同一个函数、**并且从 PLAN 的最后一档**出：社交卡的品牌锁定和 README 用的是它。
    # 齿数写字面量曾经就漂过一次——PLAN 的 256 档调成 16 齿之后这里还留着 24，
    # 于是 logo.svg 是 24 齿、logo.ico 的 256 帧是 16 齿，同一个标识两个形状。
    io.open(MASTER, "w", encoding="utf-8", newline="").write(mark_svg(*PLAN[-1]))
    print("logo.ico ->", [s for s, _ in PLAN], " logo-256.png / logo.svg 已更新")


if __name__ == "__main__":
    main()
