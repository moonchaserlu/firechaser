"""把一个目录里的图标拼成一张预览图（暗底，模拟面板背景）。

用法：blender --background --factory-startup --python icon_sheet.py -- <图标目录> <输出 PNG> [每行个数]
"""

import os
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
folder, output = args[0], args[1]
per_row = int(args[2]) if len(args) > 2 else 6
files = sorted(f for f in os.listdir(folder) if f.lower().endswith(".png"))
cell, gap = 256, 8
rows = (len(files) + per_row - 1) // per_row
height = rows * (cell + gap) + gap
width = per_row * (cell + gap) + gap
sheet = np.zeros((height, width, 4), dtype=np.float32)
sheet[..., :3] = (0.09, 0.075, 0.06)
sheet[..., 3] = 1.0
for index, name in enumerate(files):
    image = bpy.data.images.load(os.path.join(folder, name))
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    step = max(1, max(w, h) // cell)
    pixels = pixels[::step, ::step][:cell, :cell]
    row, col = index // per_row, index % per_row
    # Blender 图像原点在左下；第一行放在最上面。
    y0 = height - (row + 1) * (cell + gap)
    x0 = gap + col * (cell + gap)
    region = sheet[y0:y0 + pixels.shape[0], x0:x0 + pixels.shape[1]]
    alpha = pixels[..., 3:4]
    region[..., :3] = pixels[..., :3] * alpha + region[..., :3] * (1 - alpha)
    print(index, name)
result = bpy.data.images.new("sheet", width, height, alpha=True)
result.pixels[:] = sheet.ravel()
result.filepath_raw = output
result.file_format = "PNG"
result.save()
