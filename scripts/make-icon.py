#!/usr/bin/env python3
"""生成 HotspotGo.exe 的图标(一次性资源生成脚本,不参与构建、CI 也不需要跑它)。

为什么把生成脚本也放进仓库:图标是二进制文件,没有来源就没人能改它。
想换配色 / 换形状,改下面的常量重跑一次即可:

    python scripts/make-icon.py

产物:src/HotspotGo/Assets/HotspotGo.ico

尺寸只留 16 / 32 / 48 / 256:16 与 32 是资源管理器与任务栏实际用到的,
48 给"中等图标"视图,256 给大图标视图。多留中间尺寸会让 exe 变大
(ICO 里 64 / 128 是未压缩位图,128 一张就 65 KB —— 而整个 exe 才几十 KB),
这个项目"体积小"本身是有意义的,别为了图标把它翻好几倍。
"""

import os

from PIL import Image, ImageChops, ImageDraw

# ---------------------------------------------------------------- 设计常量

SIZE = 256                      # 源图尺寸,ICO 里各尺寸都由它缩出来
CORNER = 56                     # 圆角半径
TOP_COLOR = (56, 189, 248)      # 渐变起点:亮蓝
BOTTOM_COLOR = (29, 78, 216)    # 渐变终点:深蓝
GLYPH = (255, 255, 255)         # 图形颜色:白
GLYPH_WIDTH = 22                # 弧的粗细
DOT_RADIUS = 18                 # 中心圆点半径
ARC_RADII = (52, 92, 132)       # 三道弧的外半径(由内到外)
ARC_SPAN = (218, 322)           # 弧的角度范围(正上方约 104° 的扇形)
ARC_CENTER_Y = 192              # 圆心压在下方,让扇形占满上半部分

ASSET_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "..", "src", "HotspotGo", "Assets")
ICON_PATH = os.path.join(ASSET_DIR, "HotspotGo.ico")
ICON_SIZES = [16, 32, 48, 256]


def make_background():
    """圆角方块 + 竖向渐变。"""
    # 先做一条 1 像素宽的竖线再拉伸,比逐行画 256 个像素省事
    column = Image.new("RGBA", (1, SIZE))
    for y in range(SIZE):
        t = y / (SIZE - 1)
        column.putpixel((0, y), tuple(
            round(TOP_COLOR[i] + (BOTTOM_COLOR[i] - TOP_COLOR[i]) * t) for i in range(3)
        ) + (255,))

    background = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), CORNER, fill=255)
    background.paste(column.resize((SIZE, SIZE)), (0, 0), mask)
    return background


def draw_glyph(image):
    """把 Wi-Fi / 热点扇形画上去:三道弧 + 中心圆点。

    弧用"环形扇区"画(填满的扇形再挖掉内圈),端面是平的 ——
    这是 Windows 自己那套信号图标的形状,也避开了描边 + 圆头端点在
    小尺寸下糊成一团的问题。

    先在 L 通道的蒙版上画好形状,最后一次性合成:这样弧之间不会互相挖空,
    也不用手写"内圈用背景色覆盖"(背景是渐变,覆盖不了)。
    """
    mask = Image.new("L", (SIZE, SIZE), 0)
    cx = SIZE // 2
    cy = ARC_CENTER_Y

    for radius in ARC_RADII:
        ring = Image.new("L", (SIZE, SIZE), 0)
        draw = ImageDraw.Draw(ring)
        draw.pieslice((cx - radius, cy - radius, cx + radius, cy + radius),
                      ARC_SPAN[0], ARC_SPAN[1], fill=255)

        inner = radius - GLYPH_WIDTH
        if inner > 0:
            draw.pieslice((cx - inner, cy - inner, cx + inner, cy + inner), 0, 360, fill=0)

        mask = ImageChops.lighter(mask, ring)

    dot = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(dot).ellipse((cx - DOT_RADIUS, cy - DOT_RADIUS,
                                 cx + DOT_RADIUS, cy + DOT_RADIUS), fill=255)
    mask = ImageChops.lighter(mask, dot)

    image.paste(GLYPH, (0, 0), mask)


def main():
    icon = make_background()
    draw_glyph(icon)

    os.makedirs(ASSET_DIR, exist_ok=True)
    icon.save(ICON_PATH, format="ICO", sizes=[(s, s) for s in ICON_SIZES])

    print("已生成 " + os.path.normpath(ICON_PATH))
    print("尺寸: " + ", ".join(str(s) for s in ICON_SIZES) +
          "  文件大小: " + str(os.path.getsize(ICON_PATH)) + " 字节")


if __name__ == "__main__":
    main()
