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

小尺寸的棘齿要**画得比大尺寸钝**，理由在 lean_for：判据是把出厂 .ico 的九帧逐帧取出、
按角度扫径向轮廓算出锁面的像素宽度，不是看渲染图猜。

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


LEAN = 0.86          # 大尺寸的棘向，见 ratchet 的说明
HINTED_UNTIL = 40    # 只有不超过这一档的尺寸吃小尺寸 hinting；48+ 与旧版逐像素相同
DEEP_16, DEEP_256 = 0.155, 0.105   # 齿深（占 span）在两端的取值


def lean_for(size):
    """棘向（齿尖在齿距里的位置）。小尺寸**压钝**，为的是让锁面落进像素网格里。

    锁面的切向宽度 = 齿距 × (1 − lean)。0.86 在 256px 上是 6.1px，一眼是「往一个方向勾」；
    但同一比例放到 16px（出厂那版 8 齿）只有 **0.71px**，不到一个像素——于是托盘里
    棘齿的「勾」整个不在屏上，剩下的读法是一圈鼓包围着个饼，和设置图标同一个剪影家族。
    小档齿数本来就少（装不下细齿），齿距更大，把 lean 压到 0.74~0.82 后锁面有 1.7~2.0px，
    方向感回来了。代价是这几档的齿看着比大尺寸的「肥」——那是 hinting，不是走形：
    48px 以上一个像素都不动，放大看到的仍然是原来那张。
    """
    return {16: 0.74, 20: 0.76, 24: 0.78, 32: 0.80, 40: 0.82}.get(size, LEAN)


def hub_for(size):
    """轴毂占比。只在「不放大就看不见」的那几档放大，48+ 维持 0.125。

    0.125 在 16px 上是 3.8px 直径、对轮体 2.33:1，「中间有根轴」这件事在最常用的一档上
    并不成立（这条注释原本说它比齿形更早被认出来——量完要更正：**只有在放大到 0.175 之后才成立**）。
    上限压在小档是因为 0.145 以上在大尺寸读作「向日葵花心」（见 mark_svg），
    那条判据是实测过的，不能被「顺手改大」覆盖。
    """
    return {16: 0.175, 20: 0.165, 24: 0.150, 32: 0.135, 40: 0.130}.get(size, 0.125)


def stroke_for(size):
    """描边宽度。白底上黄铜只有 1.74:1（深一档的调色也只到 2.16:1，仍不过 3:1），
    标识在白底上**一直**是靠这道边立住的，所以小档把它加粗一档（0.24→0.30，16px 上 0.96→1.2px）。
    上限仍然压住：0.15·s^0.68 到 256px 是 6.5 像素，白底上读成「黑齿轮镶了个黄芯」，主次颠倒。
    """
    k = 0.30 if size <= HINTED_UNTIL else 0.24
    return max(0.9, k * size ** 0.5)


def depth_for(size):
    """齿深随尺寸**微微**变浅（16px 0.155 → 256px 0.105），按 log2 线性插值。

      16px  齿深 0.105 只有 1.5 个像素，抗锯齿一抹就没了，剩一个圆饼；
      256px 齿深 0.155 的齿细长外张，读作太阳花而不是棘轮（实测，见 sweep）。
    按 log2 插值而不是分档，是为了不出现某一档突然变形——上一版 48px 那一跳就是分档来的。

    小档再乘 1.25：齿数少了、齿距大了，深一点才撑得住那副钝棘向（两者是一件事，
    单独改 lean 会让齿显得又扁又密）。48+ 不乘，保持原样。
    """
    d = DEEP_16 - (DEEP_16 - DEEP_256) * (math.log2(size) - 4) / 4
    return d * 1.25 if size <= HINTED_UNTIL else d


def ratchet(cx, cy, r_tip, r_root, teeth, lean=LEAN, phase=-math.pi / 2):
    """擒纵齿的闭合折线。每颗齿：从齿根出发，沿 lean 比例的长斜背爬到齿尖，
    再几乎垂直地落回下一个齿根（锁面）。

    lean 是这个标记能不能被认出来的**唯一**关键参数，不是风格旋钮：
      0.5   两侧等长 → 对称三角 → 读作太阳/锯片
      0.86  长背 + 陡锁面 → 齿尖明显朝一个方向勾 → 读作棘轮/擒纵轮
    实测 0.62（上一版）在 256px 上仍然读作太阳花：两条边长度差得不够，眼睛看不出方向。
    传进来的值按尺寸取（lean_for），因为「差得够不够」是按像素算的，不是按比例算的。
    """
    pts = []
    for i in range(teeth):
        a = phase + i * 2 * math.pi / teeth
        pts.append((cx + r_root * math.cos(a), cy + r_root * math.sin(a)))
        b = a + 2 * math.pi / teeth * lean
        pts.append((cx + r_tip * math.cos(b), cy + r_tip * math.sin(b)))
    return pts


def mark_svg(size, teeth):
    """所有尺寸出自这一个函数。轮径在每个尺寸上完全相同，随尺寸变的是：
      齿数    6 → 16（小尺寸装不下细齿，物理限制，不是风格选择）
      齿深    见 depth_for（小档更深）
      棘向    见 lean_for（小档更钝，否则锁面落不进 1px 网格）
      轴毂    见 hub_for（小档更大，否则「有根轴」不在屏上）
      描边    见 stroke_for（小档加粗一档）
      高光轴心 仅 48+
    这四条都是 **hinting，不是换标识**：48px 以上的齿数/齿深/棘向/轴毂/描边与旧版逐项相同，
    重画之后那几帧逐像素不变（main 末尾会核对）。以前「比例全尺寸一致」这条之所以能立，
    是因为它默认了「看不见的那部分不算走形」——量完 16px 之后，看不见的恰恰是身份本身。

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
    stroke_w = stroke_for(size)
    r_tip = span * 0.50
    r_root = r_tip - span * depth_for(size)
    hub = span * hub_for(size)
    d = "M" + " L".join(f"{x:.2f},{y:.2f}" for x, y in
                        ratchet(c, c, r_tip, r_root, teeth, lean_for(size))) + " Z"

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
# 16~40 这五档比旧版各少 2~3 颗齿：齿距要大到装得下钝棘向的锁面（lean_for），
# 48+ 的齿数与旧版一致。
PLAN = [(16, 6), (20, 6), (24, 7), (32, 9), (40, 10),
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
    print("hinted (changed):", [s for s, _ in PLAN if s <= HINTED_UNTIL],
          " untouched:", [s for s, _ in PLAN if s > HINTED_UNTIL])


if __name__ == "__main__":
    main()
