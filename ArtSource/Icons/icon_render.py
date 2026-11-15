"""把工程里已有的 3D 模型渲染成物品图标（透明底 PNG）。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python icon_render.py -- <输出目录> [名称 ...]

不写名称时渲染 ICONS 里的全部图标。输出文件名是 Icon_<物品 itemId>.png，放进
Assets/Game/Art/Icons 后由 Unity 的 ItemIconSetup 按 itemId 自动绑到物品上。
新生成的图标只要按同样的文件名放进去，就能直接替换这里的代用图。
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
KAY = os.path.join(ROOT, "Game", "Assets", "ThirdPartyAssets", "itch.io", "KayLousberg")
DUNGEON = ("DungeonPack", "1.1_SOURCE", "dungeon_texture.png")
FURNITURE = ("FurnitureBits", "1.0_SOURCE", "furniturebits_texture.png")

def dungeon(name):
    return (DUNGEON, name)


def furniture(name):
    return (FURNITURE, name)


# 图标名 -> (素材包, 模型名)。素材包为 None 时画角色手里那把手枪。
# 除了手枪都是外形相近的代用品，有正式图标后直接覆盖同名 PNG。
ICONS = {
    "Icon_fuel_pack": dungeon("keg"),
    "Icon_chemicals": furniture("cup"),
    "Icon_cloth": furniture("rug_rectangle_stripes_A"),
    "Icon_mechanical_parts": dungeon("box_small"),
    "Icon_identity_tag": furniture("pictureframe_small_A"),
    "Icon_old_pistol": (None, "pistol"),
}

# 挑选代用品时试渲染过的模型，用法：在命令行写这里的名称。
CANDIDATES = {
    "keg": dungeon("keg"), "bucket": dungeon("bucket"), "barrel_small": dungeon("barrel_small"),
    "box_small": dungeon("box_small"), "crate_small": dungeon("crate_small"), "trunk_small_A": dungeon("trunk_small_A"),
    "rubble_half": dungeon("rubble_half"), "rocks_small": dungeon("rocks_small"),
    "cup": furniture("cup"), "mug_A": furniture("mug_A"), "cup_pencils": furniture("cup_pencils"),
    "book_single": furniture("book_single"), "pictureframe_small_A": furniture("pictureframe_small_A"),
    "pictureframe_standing_A": furniture("pictureframe_standing_A"), "lamp_table": furniture("lamp_table"),
    "pillow_A": furniture("pillow_A"), "rug_rectangle_A": furniture("rug_rectangle_A"),
    "rug_rectangle_stripes_A": furniture("rug_rectangle_stripes_A"), "pistol": (None, "pistol"),
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_kay(pack, model):
    path = os.path.join(KAY, pack[0], pack[1], "Models", model + ".fbx")
    bpy.ops.import_scene.fbx(filepath=path)
    image = bpy.data.images.load(os.path.join(KAY, pack[0], pack[1], "Textures", pack[2]))
    mat = bpy.data.materials.new("Atlas")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    mat.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in meshes:
        obj.data.materials.clear()
        obj.data.materials.append(mat)
    return meshes


def build_pistol():
    """和 seeker_rig.build_pistol 同样的三个盒子（套筒、握把、扳机护圈），摆正后才好拍侧面；颜色提亮，否则在暗底上看不见。"""
    bm = bmesh.new()
    for center, size in (((0, -0.08, 0), (0.026, 0.19, 0.034)), ((0, 0.01, -0.05), (0.024, 0.04, 0.08)), ((0, -0.04, -0.03), (0.01, 0.035, 0.02))):
        geom = bmesh.ops.create_cube(bm, size=1.0)
        for v in geom["verts"]:
            v.co = Vector((center[0] + v.co.x * size[0], center[1] + v.co.y * size[1], center[2] + v.co.z * size[2]))
    mesh = bpy.data.meshes.new("Pistol")
    bm.to_mesh(mesh)
    bm.free()
    mat = bpy.data.materials.new("Pistol")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.3, 0.32, 0.35, 1)
    bsdf.inputs["Roughness"].default_value = 0.6
    mesh.materials.append(mat)
    obj = bpy.data.objects.new("Pistol", mesh)
    bpy.context.scene.collection.objects.link(obj)
    return [obj]


def bounds(objects):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for obj in objects for c in obj.bound_box]
    low = Vector(min(c[i] for c in corners) for i in range(3))
    high = Vector(max(c[i] for c in corners) for i in range(3))
    return (low + high) * 0.5, (high - low).length


def frame_and_light(center, diagonal, size, direction):
    scene = bpy.context.scene
    camera = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = diagonal * 1.02
    direction = Vector(direction).normalized()
    camera.location = center + direction * diagonal * 3
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    camera.data.clip_end = diagonal * 10
    scene.camera = camera

    key = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "SUN"))
    key.data.energy = 3.2
    key.rotation_euler = (math.radians(50), 0, math.radians(35))
    scene.collection.objects.link(key)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 1.1
    fill.rotation_euler = (math.radians(70), 0, math.radians(-120))
    scene.collection.objects.link(fill)
    world = bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.55

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"


def render(name, source, out_dir, size=256):
    reset()
    pack, model = source
    objects = import_kay(pack, model) if pack else build_pistol()
    center, diagonal = bounds(objects)
    # 手枪从侧面拍，其他物品用四分之三俯视。
    frame_and_light(center, diagonal, size, (0.75, -1.0, 0.8) if pack else (1.0, -0.12, 0.1))
    bpy.context.scene.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    out_dir = os.path.abspath(args[0])
    os.makedirs(out_dir, exist_ok=True)
    table = {**CANDIDATES, **ICONS}
    for name in (args[1:] or list(ICONS)):
        render(name, table[name], out_dir)
        print("[icon]", name)


main()
