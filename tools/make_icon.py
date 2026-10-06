# -*- coding: utf-8 -*-
"""从整张图里裁出徽章，做成正方形多尺寸 .ico
徽章边界是靠"背景平面残差"找到的：背景拟合为 -0.022x -0.022y + 251，
徽章的右/下边缘在残差里表现为明显的暗带（投影阴影），上/左边缘是微弱阶跃。
"""
from PIL import Image, ImageDraw
import os

BASE = r"D:\输入法"
ASSETS = os.path.join(BASE, "assets")
os.makedirs(ASSETS, exist_ok=True)

src = os.path.join(ASSETS, "icon-source.jpg")
im = Image.open(src).convert("RGB")

# 测得的徽章边界（含 2px 内缩，避开外圈阴影）
BOX = (198, 411, 824, 609)   # left, top, right, bottom
badge = im.crop(BOX)
bw, bh = badge.size
print("徽章裁切: %dx%d  宽高比 %.2f" % (bw, bh, bw/float(bh)))

# 圆角蒙版（原图圆角看起来约为高度的 15%）
radius = int(bh * 0.16)
mask = Image.new("L", (bw, bh), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, bw-1, bh-1], radius=radius, fill=255)
badge.putalpha(mask)

# 放进正方形透明画布：宽度占 92%，垂直居中
def to_square(img, size, fill_ratio=0.92):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    tw = int(size * fill_ratio)
    th = max(1, int(round(tw * img.height / float(img.width))))
    r = img.resize((tw, th), Image.LANCZOS)
    canvas.paste(r, ((size - tw)//2, (size - th)//2), r)
    return canvas

icon256 = to_square(badge, 256)
icon256.save(os.path.join(ASSETS, "icon.png"))

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [to_square(badge, s) for s in sizes]
frames[-1].save(os.path.join(ASSETS, "icon.ico"), format="ICO",
                sizes=[(s, s) for s in sizes], append_images=frames[:-1])
print("已生成: assets/icon.png (256)  assets/icon.ico " + str(sizes))

# 预览图：把几个尺寸放大排一排，方便肉眼判断
sheet = Image.new("RGBA", (720, 300), (235, 235, 238, 255))
x = 20
for s in [256, 128, 64, 48, 32, 16]:
    fr = to_square(badge, s)
    box = Image.new("RGBA", (fr.width + 2, fr.height + 2), (200, 200, 205, 255))
    box.paste(fr, (1, 1), fr)
    sheet.paste(box, (x, 20 + (256 - s)//2), box)
    x += s + 20
sheet.convert("RGB").save(os.path.join(BASE, "_icon_preview.png"))
print("预览: _icon_preview.png")
