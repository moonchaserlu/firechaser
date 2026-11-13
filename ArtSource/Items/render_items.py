"""从 Items.blend 的最终模型渲染验收图。

用法（Blender 5.x 后台）：
    blender --background Items.blend --python render_items.py -- <输出目录> [itemId ...]

输出：
    Ortho_<itemId>.png    正交三视图（TOP 左上、FRONT 左下、RIGHT 右下），同一模型、同一比例
    Beauty_<itemId>.png   斜俯视对照图（武器为侧视），#16120F 底
    Overview.png          全套在 #16120F 底上的 76 / 28 / 22 像素总览
只读 Items.blend，不保存它。
"""

import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

DARK = (0x16 / 255, 0x12 / 255, 0x0F / 255)
WEAPONS = {"short_knife", "old_pistol"}

# 视图旋转：把要看的那一面转到 +Z，由正上方的正交相机拍。
ROT_TOP = Matrix.Identity(3)
ROT_FRONT = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))  # -Y 面朝上，Z 朝屏幕上方
ROT_RIGHT = Matrix(((0, 1, 0), (0, 0, 1), (1, 0, 0)))   # +X 面朝上，Z 朝屏幕上方，+Y 朝右
ROT_TOP_WEAPON = Matrix(((0, 1, 0), (-1, 0, 0), (0, 0, 1)))  # 武器俯视：+Y 朝右，与侧视图的长度方向对齐


def item_roots():
    roots = {}
    for obj in bpy.data.objects:
        if obj.parent is None and obj.name.startswith("Item_") and obj.type == "MESH":
            roots[obj.name[5:]] = obj
    return roots


def tree(obj):
    return [obj] + [c for child in obj.children for c in tree(child)]


def setup_scene(name):
    scene = bpy.data.scenes.new(name)
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    world = bpy.data.worlds.new(name)
    world.use_nodes = True
    scene.world = world
    return scene


def copy_tree(obj, collection, parent=None):
    clone = obj.copy()
    collection.objects.link(clone)
    if parent is not None:
        clone.parent = parent
        clone.matrix_parent_inverse = obj.matrix_parent_inverse.copy()
    for child in obj.children:
        copy_tree(child, collection, clone)
    return clone


def place(root, collection, rotation, offset):
    pivot = bpy.data.objects.new("Pivot", None)
    collection.objects.link(pivot)
    clone = copy_tree(root, collection)
    clone.parent = pivot
    clone.location = (0, 0, 0)
    pivot.matrix_world = Matrix.Translation(offset) @ rotation.to_4x4()
    return pivot, clone


def world_bounds(objects):
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objects if o.type == "MESH" for c in o.bound_box]
    lo = Vector(tuple(min(p[i] for p in pts) for i in range(3)))
    hi = Vector(tuple(max(p[i] for p in pts) for i in range(3)))
    return lo, hi


def add_sun(scene, name, energy, rotation, color=(1, 1, 1)):
    light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
    light.data.energy = energy
    light.data.color = color
    light.rotation_euler = rotation
    scene.collection.objects.link(light)


def add_text(scene, text, location, size):
    curve = bpy.data.curves.new(text, "FONT")
    curve.body = text
    curve.size = size
    curve.align_x = "LEFT"
    obj = bpy.data.objects.new(text, curve)
    obj.location = location
    mat = bpy.data.materials.new("Label")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.05, 0.05, 0.05, 1)
    bsdf.inputs["Emission Color"].default_value = (0.05, 0.05, 0.05, 1)
    curve.materials.append(mat)
    scene.collection.objects.link(obj)


def render_ortho(item_id, root, out_dir):
    scene = setup_scene(f"Ortho_{item_id}")
    bpy.context.window.scene = scene
    background = scene.world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.62, 0.62, 0.6, 1)
    background.inputs[1].default_value = 1.0
    scene.render.resolution_x, scene.render.resolution_y = 1536, 1024
    scene.render.film_transparent = False
    add_sun(scene, "Key", 2.2, (math.radians(25), math.radians(-20), 0))

    weapon = item_id in WEAPONS
    slots = {"TOP": ROT_TOP_WEAPON if weapon else ROT_TOP, "FRONT": ROT_RIGHT if weapon else ROT_FRONT, "RIGHT": ROT_FRONT if weapon else ROT_RIGHT}
    placed = {}
    for label, rotation in slots.items():
        pivot, clone = place(root, scene.collection, rotation, Vector((0, 0, 0)))
        placed[label] = (pivot, clone)
    size = {label: world_bounds(tree(clone)) for label, (pivot, clone) in placed.items()}
    extent = max(max((hi - lo)[0], (hi - lo)[1]) for lo, hi in size.values())
    gap = extent * 0.35

    def move(label, dx, dy):
        pivot = placed[label][0]
        pivot.location += Vector((dx, dy, 0))

    # FRONT 放在原点；TOP 在其正上方并对齐 X；RIGHT 在其右侧并对齐底边。
    f_lo, f_hi = size["FRONT"]
    t_lo, t_hi = size["TOP"]
    move("TOP", (f_lo.x + f_hi.x) / 2 - (t_lo.x + t_hi.x) / 2, f_hi.y + gap - t_lo.y)
    r_lo, r_hi = size["RIGHT"]
    move("RIGHT", f_hi.x + gap - r_lo.x, f_lo.y - r_lo.y)

    everything = [o for (pivot, clone) in placed.values() for o in tree(clone)]
    lo, hi = world_bounds(everything)
    label_size = extent * 0.12
    for label, (pivot, clone) in placed.items():
        b_lo, b_hi = world_bounds(tree(clone))
        add_text(scene, label + ("（侧面）" if weapon and label == "FRONT" else ""), (b_lo.x, b_hi.y + label_size * 0.6, hi.z + 0.01), label_size)
    lo, hi = Vector((lo.x, lo.y, lo.z)), Vector((hi.x, hi.y + label_size * 2, hi.z))

    camera = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    width, height = hi.x - lo.x, hi.y - lo.y
    camera.data.ortho_scale = max(width, height * 1.5) * 1.18
    camera.location = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, hi.z + 2)
    camera.data.clip_end = 10
    scene.camera = camera
    scene.render.filepath = os.path.join(out_dir, f"Ortho_{item_id}.png")
    bpy.ops.render.render(write_still=True)


def render_beauty(item_id, root, out_dir, transparent_dir):
    scene = setup_scene(f"Beauty_{item_id}")
    bpy.context.window.scene = scene
    background = scene.world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.35, 0.31, 0.27, 1)
    background.inputs[1].default_value = 0.6
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.film_transparent = True
    pivot, clone = place(root, scene.collection, Matrix.Identity(3), Vector((0, 0, 0)))
    lo, hi = world_bounds(tree(clone))
    center = (lo + hi) / 2
    radius = (hi - lo).length / 2
    if item_id in WEAPONS:
        direction = Vector((1.0, 0.0, 0.25)).normalized()
    else:
        direction = Vector((-0.55, -1.0, 0.85)).normalized()
    camera = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(camera)
    camera.data.lens = 85
    distance = radius / math.tan(camera.data.angle / 2) * 1.12
    camera.location = center + direction * distance
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    camera.data.clip_end = distance * 4
    scene.camera = camera
    # 暖色主光从画面左上方来，冷一点的补光从右下。
    key_dir = (camera.matrix_world.to_3x3() @ Vector((-0.7, 0.8, 0.6))).normalized()
    add_sun(scene, "Key", 3.4, (-key_dir).to_track_quat("-Z", "Y").to_euler(), (1.0, 0.82, 0.62))
    fill_dir = (camera.matrix_world.to_3x3() @ Vector((0.8, -0.3, 0.5))).normalized()
    add_sun(scene, "Fill", 0.9, (-fill_dir).to_track_quat("-Z", "Y").to_euler(), (0.75, 0.82, 1.0))
    transparent = os.path.join(transparent_dir, f"Beauty_{item_id}.png")
    scene.render.filepath = transparent
    bpy.ops.render.render(write_still=True)
    composite(transparent, os.path.join(out_dir, f"Beauty_{item_id}.png"))


def load_rgba(path, size=None):
    image = bpy.data.images.load(path, check_existing=False)
    if size is not None:
        image.scale(size, size)
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(image)
    return pixels


def save_rgba(pixels, path):
    h, w = pixels.shape[:2]
    image = bpy.data.images.new("out", w, h, alpha=True)
    image.pixels[:] = pixels.ravel()
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def over_dark(pixels):
    out = np.empty_like(pixels)
    alpha = pixels[..., 3:4]
    out[..., :3] = pixels[..., :3] * alpha + np.array(DARK) * (1 - alpha)
    out[..., 3] = 1
    return out


def composite(src, dst):
    save_rgba(over_dark(load_rgba(src)), dst)


def overview(item_ids, transparent_dir, out_path):
    sizes = (76, 28, 22)
    cell = 96
    rows = len(sizes)
    sheet = np.zeros((rows * cell + 8, len(item_ids) * cell + 8, 4), dtype=np.float32)
    sheet[..., :3] = DARK
    sheet[..., 3] = 1
    for col, item_id in enumerate(item_ids):
        for row, size in enumerate(sizes):
            pixels = over_dark(load_rgba(os.path.join(transparent_dir, f"Beauty_{item_id}.png"), size))
            y0 = sheet.shape[0] - 4 - (row + 1) * cell + (cell - size) // 2
            x0 = 4 + col * cell + (cell - size) // 2
            sheet[y0:y0 + size, x0:x0 + size] = pixels
    save_rgba(sheet, out_path)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    out_dir = os.path.abspath(args[0])
    os.makedirs(out_dir, exist_ok=True)
    transparent_dir = os.path.join(out_dir, "_transparent")
    os.makedirs(transparent_dir, exist_ok=True)
    roots = item_roots()
    order = args[1:] or [i for i in ORDER if i in roots]
    for item_id in order:
        render_ortho(item_id, roots[item_id], out_dir)
        render_beauty(item_id, roots[item_id], out_dir, transparent_dir)
        print("[render]", item_id)
    if not args[1:]:
        overview(order, transparent_dir, os.path.join(out_dir, "Overview.png"))


ORDER = ["fuel_pack", "old_battery", "empty_battery", "charged_battery", "scrap_metal", "dirty_bandage", "first_aid_spray",
         "ammo_9mm", "ammo_12g", "ammo_556", "canned_food", "short_knife", "encrypted_drive", "intact_watch", "chemicals",
         "cloth", "mechanical_parts", "identity_tag", "old_pistol"]
main()
