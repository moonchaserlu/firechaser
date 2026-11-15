"""失火者遗物箱：失火者死亡后原地留下的可搜索箱子。破木箱 + 搭在上面的残破背心布 + 熄灭的旧灯。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python firelost_cache_build.py -- <输出目录> <FBX 路径>

复用 firelost_build.py 的建模、材质与烘焙工具，贴图同样输出到 <FBX 目录>/Textures（T_Firelost_Cache.png）。
原点在箱底中心，长边沿 X，约 0.64 × 0.48 × 0.46 m。
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import firelost_build as fb  # noqa: E402

fb.PALETTE["M_Firelost_Wood"] = ((0.45, 0.34, 0.24), 0.9, 0.0)
fb.PALETTE["M_Firelost_WoodDark"] = ((0.22, 0.16, 0.12), 0.9, 0.0)
fb.WEAR["M_Firelost_Wood"] = (0.30, -0.05, 0.55, 0.28)
fb.WEAR["M_Firelost_WoodDark"] = (9.0, 8.0, 0.0, 0.15)
fb.TEX_SIZE["Firelost_Cache"] = 512

W, D, H = 0.62, 0.46, 0.42  # 箱体长、宽、高


def cuboid(mb, center, size, mat, tilt=None):
    """轴对齐长方体，可选绕中心旋转（tilt 为 Matrix）。"""
    c = Vector(center)
    hx, hy, hz = (s * 0.5 for s in size)
    corners = [Vector((x, y, z)) for z in (-hz, hz) for y in (-hy, hy) for x in (-hx, hx)]
    if tilt is not None:
        corners = [tilt @ p for p in corners]
    v = [mb.bm.verts.new(c + p + fb.jit(0.003)) for p in corners]
    m = mb.mat(mat)
    for face in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        mb.bm.faces.new([v[i] for i in face]).material_index = m


def build_cache(mats):
    mb = fb.MeshBuilder("Firelost_Cache")
    # 箱体：三层横板，板缝用略凹的暗色带表现
    for k in range(3):
        z0 = k * H / 3
        cuboid(mb, (0, 0, z0 + H / 6), (W, D, H / 3 - 0.012), "M_Firelost_Wood")
        cuboid(mb, (0, 0, z0 + H / 3 - 0.003), (W - 0.01, D - 0.01, 0.014), "M_Firelost_WoodDark")
    # 四角立柱、底部垫木
    for sx in (-1, 1):
        for sy in (-1, 1):
            cuboid(mb, (sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), H / 2), (0.06, 0.06, H + 0.01), "M_Firelost_WoodDark")
            cuboid(mb, (sx * (W / 2 - 0.02), sy * (D / 2 - 0.02), H + 0.008), (0.065, 0.065, 0.02), "M_Firelost_Buckle")
        cuboid(mb, (sx * (W / 2 - 0.08), 0, 0.015), (0.07, D + 0.02, 0.03), "M_Firelost_WoodDark")
    # 盖子：两块板，一块翘起错位
    cuboid(mb, (-0.15, 0, H + 0.02), (0.30, D + 0.02, 0.03), "M_Firelost_Wood")
    cuboid(mb, (0.16, 0.01, H + 0.035), (0.30, D, 0.03), "M_Firelost_Wood", tilt=Matrix.Rotation(math.radians(-7), 3, "Y"))
    # 一根撬开的横钉条
    cuboid(mb, (0, -D / 2 - 0.012, H * 0.62), (W * 0.9, 0.02, 0.05), "M_Firelost_WoodDark", tilt=Matrix.Rotation(math.radians(9), 3, "Y"))

    # 搭在箱角上的残破背心布：从盖面垂到侧面，下缘锯齿
    rows = []
    for i, t in enumerate((0.0, 0.35, 0.7, 1.0)):
        y = -0.10 + 0.20 * t
        ring = []
        for j in range(6):
            s = j / 5
            if s < 0.5:   # 盖面上
                p = Vector((0.02 + 0.30 * s * 2 * 0.8, y, H + 0.06))
            else:         # 沿 +X 侧面垂下
                p = Vector((W / 2 + 0.012, y, H + 0.05 - (s - 0.5) * 2 * 0.24))
            if j == 5:
                p.z -= 0.04 if i % 2 else 0.0
            ring.append(p + fb.jit(0.006))
        rows.append(ring)
    mb.loft(rows, "M_Firelost_Vest", closed=False)
    back = [[p + Vector((0, 0, -0.006)) if p.z > H else p + Vector((-0.006, 0, 0)) for p in r] for r in rows]
    mb.loft(back, "M_Firelost_VestInner", closed=False)
    return mb.build(mats)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    mats = fb.make_materials()
    cache = build_cache(mats)

    # 熄灭的旧灯侧倒在盖子上
    lantern = fb.build_lantern(mats)
    lantern.rotation_euler = (math.radians(90), 0, math.radians(30))
    lantern.location = (-0.10, -0.12, H + 0.035 + 0.064)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for o in (lantern, cache):
        o.select_set(True)
    bpy.context.view_layer.objects.active = cache
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    cache.select_set(False)

    for mat in bpy.data.materials:
        if mat.name in fb.WEAR:
            fb.make_wear_shader(mat)
    fb.bake_object(cache, fb.TEX_DIR)

    fb.setup_render(scene)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 60
    target = Vector((0, 0, 0.25))
    cam.location = Vector((-1.4, -1.9, 1.5))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x = scene.render.resolution_y = 800
    scene.render.filepath = os.path.join(fb.OUT_DIR, "renders", "Firelost_Cache.png")
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)

    for mat in bpy.data.materials:
        if mat.name in fb.PALETTE:
            mat.use_fake_user = True
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(fb.OUT_DIR, "Firelost_Cache.blend"))
    bpy.ops.export_scene.fbx(
        filepath=fb.FBX_PATH,
        use_selection=False,
        object_types={"MESH"},
        mesh_smooth_type="OFF",
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_anim=False,
        bake_space_transform=True,  # 不带骨骼的静态模型：轴向转换烘进网格，否则 Unity 里会躺倒
        path_mode="STRIP",
    )
    print(f"[FirelostCache] tris={sum(len(p.vertices) - 2 for p in cache.data.polygons)} fbx={fb.FBX_PATH}")


main()
