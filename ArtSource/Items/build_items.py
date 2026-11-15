"""《向火而生》19 件物品的低多边形模型（程序化生成）。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python build_items.py -- [itemId ...]

不写 itemId 时生成全部 19 件。每次都从空场景重建，只写本目录下的
Items.blend、Textures/T_Item_Palette.png 和 build_stats.json，不碰其他文件。
手工修改 Items.blend 后不要再跑本脚本（会被重建覆盖），直接用 export_items.py 导出。

约定：米制；Blender Z 向上，物品正面朝 -Y（导出到 Unity 后为 +Z）；
根原点在底部中心。武器枪口/刀尖朝 -Y，侧面朝 ±X。
颜色全部来自一张 512×512 的 8×8 色板图集：每个面的 UV 落在对应色块中心，
因此一件物品通常只用 1–2 个材质槽（色板 / 色板金属），发光部件另加一个。
"""

import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TEX_DIR = os.path.join(HERE, "Textures")
PALETTE_PATH = os.path.join(TEX_DIR, "T_Item_Palette.png")

# ---------- 色板（sRGB） ----------

PALETTE = [
    ("cloth", "6E6252"), ("cloth_d", "564B3F"), ("cloth_l", "857862"), ("patch", "7A5A44"),
    ("oily", "4E3E30"), ("oily_d", "382C22"), ("oily_edge", "8A4A22"), ("rope", "8C7650"),
    ("rope_d", "6E5B3D"), ("white", "C9BCA0"), ("white_d", "A8977A"), ("stain", "7A6248"),
    ("charcoal", "3B3D40"), ("charcoal_l", "5A5E63"), ("charcoal_d", "2A2B2D"), ("steel", "7D8288"),
    ("steel_l", "A3A8AD"), ("steel_d", "5E6369"), ("rust", "8E4A26"), ("rust_d", "6A3820"),
    ("rust_l", "B0642E"), ("brass", "B08A3E"), ("brass_d", "8A6A2C"), ("brass_l", "D2AE5A"),
    ("copper", "A0603A"), ("copper_l", "C27A4C"), ("red_shell", "A3302A"), ("red_shell_d", "7E2420"),
    ("red_dark", "7A2622"), ("red_dark_l", "96332C"), ("green_tip", "4E7A2E"), ("corrosion", "B8C9A8"),
    ("window_off", "2B3230"), ("glow_green", "5CFF6A"), ("glow_cyan", "4FE3E0"), ("liquid", "2F8C8A"),
    ("liquid_d", "1F6664"), ("glass", "9DB7B0"), ("cork", "9A7448"), ("gold", "C8B070"),
    ("gold_d", "A08A50"), ("dial", "E2D6B8"), ("hand", "2A2420"), ("can", "8F8A80"),
    ("can_d", "6E6A62"), ("can_l", "A9A498"), ("grip", "2E2B28"), ("wrap", "7E6A50"),
    ("wrap_d", "5F503C"), ("blade", "B5B8BA"), ("blade_d", "8A8E90"), ("panel", "4A4F55"),
]
PI = {name: i + 1 for i, (name, _) in enumerate(PALETTE)}  # 0 保留为“未着色”
GRID = 8
SEGMENT_SCALE = 1.6  # 圆柱分段加密倍数（精细化第二版）

MATERIALS = ["M_Item_Palette", "M_Item_Metal", "M_Item_GlowGreen", "M_Item_GlowCyan"]
METAL_COLORS = {"charcoal", "charcoal_l", "charcoal_d", "steel", "steel_l", "steel_d", "brass", "brass_d",
                "brass_l", "copper", "copper_l", "gold", "gold_d", "blade", "blade_d", "can", "can_d", "can_l", "panel"}


def hex_rgb(code):
    return tuple(int(code[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def mat_for(color, mat):
    if mat is not None:
        return MATERIALS.index(mat)
    if color == "glow_green":
        return 2
    if color == "glow_cyan":
        return 3
    return 1 if color in METAL_COLORS else 0


def M(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.Translation(Vector(loc)) @ Euler(rot, "XYZ").to_matrix().to_4x4() @ Matrix.Diagonal((*scale, 1.0))


# ---------- 网格构建器 ----------

class Builder:
    """在一个 bmesh 里按“部件”累积几何；每个部件记录色块、材质和部件号。"""

    def __init__(self):
        self.bm = bmesh.new()
        self.pal = self.bm.faces.layers.int.new("pal")
        self.part = self.bm.faces.layers.int.new("part")
        self.mat = self.bm.faces.layers.int.new("mat")
        self.next_part = 1
        self.colors = {}

    def _claim(self, color, mat=None, pid=None):
        if pid is None:
            pid = self.next_part
            self.next_part += 1
            self.colors[pid] = (color, mat)
        for f in self.bm.faces:
            if f[self.part] == 0:
                f[self.part] = pid
                f[self.pal] = PI[color]
                f[self.mat] = mat_for(color, mat)
        return pid

    def faces(self, pid):
        return [f for f in self.bm.faces if f[self.part] == pid]

    def verts(self, pid):
        return list({v for f in self.faces(pid) for v in f.verts})

    # 基本体 -------------------------------------------------------

    def _bevel(self, edges, offset):
        if offset > 0 and edges:
            bmesh.ops.bevel(self.bm, geom=list(edges), offset=offset, offset_type="OFFSET", segments=1,
                            profile=0.5, affect="EDGES", clamp_overlap=True)

    def box(self, size, loc=(0, 0, 0), rot=(0, 0, 0), color="cloth", mat=None, bevel=None):
        """方块；默认按最短边自动倒一圈小角（薄片不倒），让折面边缘接到光。"""
        geom = bmesh.ops.create_cube(self.bm, size=1.0)
        bmesh.ops.transform(self.bm, matrix=M(loc, rot, size), verts=geom["verts"])
        if bevel is None:
            bevel = 0.0 if min(size) < 0.006 else min(0.003, min(size) * 0.14)
        self._bevel({e for v in geom["verts"] for e in v.link_edges}, bevel)
        return self._claim(color, mat)

    def cyl(self, r, depth, loc=(0, 0, 0), rot=(0, 0, 0), color="steel", segs=10, r2=None, mat=None, bevel=None):
        """圆柱/圆台，局部 Z 轴，loc 为底面中心。六棱（螺母）以外的分段数统一加密；端面边缘自动倒角。"""
        if segs > 6:
            segs = int(round(segs * SEGMENT_SCALE))
        geom = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segs,
                                     radius1=r, radius2=r if r2 is None else r2, depth=depth)
        bmesh.ops.transform(self.bm, matrix=M(loc, rot) @ Matrix.Translation((0, 0, depth / 2)), verts=geom["verts"])
        if bevel is None:
            bevel = 0.0 if min(r, depth) < 0.005 else min(0.002, min(r, depth) * 0.12)
        caps = [f for v in geom["verts"] for f in v.link_faces if len(f.verts) > 4]
        self._bevel({e for f in caps for e in f.edges}, bevel)
        return self._claim(color, mat)

    def sphere(self, r, loc=(0, 0, 0), color="steel", u=6, v=4, scale=(1, 1, 1), mat=None):
        geom = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=r)
        bmesh.ops.transform(self.bm, matrix=M(loc, (0, 0, 0), scale), verts=geom["verts"])
        return self._claim(color, mat)

    def prism(self, points, depth, loc=(0, 0, 0), rot=(0, 0, 0), color="steel", mat=None):
        """二维多边形（局部 XY）沿局部 Z 挤出 depth，底面在 z=0。"""
        matrix = M(loc, rot)
        bottom = [self.bm.verts.new(matrix @ Vector((x, y, 0))) for x, y in points]
        top = [self.bm.verts.new(matrix @ Vector((x, y, depth))) for x, y in points]
        n = len(points)
        self.bm.faces.new(list(reversed(bottom)))
        self.bm.faces.new(top)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
        pid = self._claim(color, mat)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.faces(pid))
        return pid

    def ring(self, outer, inner, depth, loc=(0, 0, 0), rot=(0, 0, 0), color="steel", mat=None):
        """带孔的板：outer、inner 是一一对应的二维点列，沿局部 Z 挤出。"""
        matrix = M(loc, rot)
        ob = [self.bm.verts.new(matrix @ Vector((x, y, 0))) for x, y in outer]
        ot = [self.bm.verts.new(matrix @ Vector((x, y, depth))) for x, y in outer]
        ib = [self.bm.verts.new(matrix @ Vector((x, y, 0))) for x, y in inner]
        it = [self.bm.verts.new(matrix @ Vector((x, y, depth))) for x, y in inner]
        n = len(outer)
        for i in range(n):
            j = (i + 1) % n
            self.bm.faces.new((ot[i], ot[j], it[j], it[i]))
            self.bm.faces.new((ob[j], ob[i], ib[i], ib[j]))
            self.bm.faces.new((ob[i], ob[j], ot[j], ot[i]))
            self.bm.faces.new((ib[j], ib[i], it[i], it[j]))
        pid = self._claim(color, mat)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.faces(pid))
        return pid

    def tube(self, r_out, r_in, depth, loc=(0, 0, 0), rot=(0, 0, 0), color="steel", segs=10, mat=None):
        if segs > 6:
            segs = int(round(segs * SEGMENT_SCALE))
        return self.ring(circle(r_out, segs), circle(r_in, segs), depth, loc, rot, color, mat)

    # 修饰 ---------------------------------------------------------

    def subdivide(self, pid, cuts=1):
        edges = list({e for f in self.faces(pid) for e in f.edges})
        bmesh.ops.subdivide_edges(self.bm, edges=edges, cuts=cuts, use_grid_fill=True)
        color, mat = self.colors[pid]
        self._claim(color, mat, pid)

    def jitter(self, pid, amount, seed, axes=(1, 1, 1), where=None):
        rng = random.Random(seed)
        for v in sorted(self.verts(pid), key=lambda v: (round(v.co.x, 5), round(v.co.y, 5), round(v.co.z, 5))):
            if where is None or where(v.co):
                v.co += Vector(tuple(rng.uniform(-amount, amount) * a for a in axes))

    def wear(self, pid, color, fraction, seed, where=None):
        rng = random.Random(seed)
        faces = sorted(self.faces(pid), key=lambda f: tuple(round(c, 5) for c in f.calc_center_median()))
        for f in faces:
            if (where is None or where(f)) and rng.random() < fraction:
                f[self.pal] = PI[color]
                f[self.mat] = mat_for(color, None)

    def paint(self, pid, color, where):
        for f in self.faces(pid):
            if where(f):
                f[self.pal] = PI[color]
                f[self.mat] = mat_for(color, None)

    def transform(self, pid, matrix):
        bmesh.ops.transform(self.bm, matrix=matrix, verts=self.verts(pid))

    def bend(self, pid, pivot, axis, angle, side):
        """把 side(co) 为真的顶点绕过 pivot 的轴旋转 angle。"""
        rot = Matrix.Translation(pivot) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-Vector(pivot))
        for v in self.verts(pid):
            if side(v.co):
                v.co = rot @ v.co


def circle(r, segs, start=0.0, sx=1.0, sy=1.0):
    return [(math.cos(start + 2 * math.pi * i / segs) * r * sx, math.sin(start + 2 * math.pi * i / segs) * r * sy) for i in range(segs)]


def rounded_rect(w, h, r, per_corner=3):
    pts = []
    corners = [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]
    for cx, cy, a0 in corners:
        for k in range(per_corner + 1):
            a = math.radians(a0 + 90 * k / per_corner)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


# ---------- 材质与色板 ----------

def make_palette():
    os.makedirs(TEX_DIR, exist_ok=True)
    size, cell = 512, 512 // GRID
    image = bpy.data.images.new("T_Item_Palette", size, size, alpha=False)
    pixels = [0.0] * (size * size * 4)
    swatches = [("unset", "FF00FF")] + PALETTE
    for index, (_, code) in enumerate(swatches):
        rgb = hex_rgb(code)
        col, row = index % GRID, index // GRID
        for y in range(size - (row + 1) * cell, size - row * cell):
            for x in range(col * cell, (col + 1) * cell):
                o = (y * size + x) * 4
                pixels[o:o + 4] = (*rgb, 1.0)
    image.pixels[:] = pixels
    image.filepath_raw = PALETTE_PATH
    image.file_format = "PNG"
    image.save()
    return image


def make_materials(image):
    mats = []
    for name in MATERIALS:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        bsdf = nodes["Principled BSDF"]
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Closest"
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.85
        if name == "M_Item_Metal":
            bsdf.inputs["Metallic"].default_value = 0.55
            bsdf.inputs["Roughness"].default_value = 0.5
        if name.startswith("M_Item_Glow"):
            color = hex_rgb("5CFF6A" if "Green" in name else "4FE3E0")
            bsdf.inputs["Emission Color"].default_value = (*[c ** 2.2 for c in color], 1)
            bsdf.inputs["Emission Strength"].default_value = 3.0 if "Green" in name else 1.6
        mats.append(mat)
    return mats


# ---------- 生成对象 ----------

def finish(builder, name, collection, mats, origin="bottom", parent=None):
    bm = builder.bm
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="EAR_CLIP")
    if origin == "bottom":
        xs = [v.co.x for v in bm.verts]
        ys = [v.co.y for v in bm.verts]
        zs = [v.co.z for v in bm.verts]
        shift = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
        bmesh.ops.translate(bm, vec=-shift, verts=bm.verts)
    else:
        shift = Vector((0, 0, 0))

    uv = bm.loops.layers.uv.new("UVMap")
    used = sorted({f[builder.mat] for f in bm.faces})
    for f in bm.faces:
        index = f[builder.pal]
        assert index > 0, f"{name}: 有未着色的面"
        u = (index % GRID + 0.5) / GRID
        v = 1 - (index // GRID + 0.5) / GRID
        for loop in f.loops:
            loop[uv].uv = (u, v)
        f.material_index = used.index(f[builder.mat])
        f.smooth = False

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for index in used:
        mesh.materials.append(mats[index])
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj, shift


def item_collection(item_id):
    collection = bpy.data.collections.new(f"Item_{item_id}")
    bpy.context.scene.collection.children.link(collection)
    return collection


def add_empty(name, location, parent, collection):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "ARROWS"
    empty.empty_display_size = 0.03
    collection.objects.link(empty)
    empty.parent = parent
    empty.location = location
    return empty


# ---------- 物品 ----------

def build_fuel_pack(b):
    # 浸油布一层层卷起来再压扁：两端能看到卷层，表面有褶皱，十字捆绳由一节节绳股组成。
    body = b.box((0.17, 0.11, 0.072), (0, 0, 0.036), color="oily", bevel=0.008)
    b.subdivide(body, 2)
    b.jitter(body, 0.0035, 11, where=lambda co: abs(co.x) < 0.08)
    b.wear(body, "oily_d", 0.3, 12)
    b.wear(body, "oily_edge", 0.08, 13, where=lambda f: abs(f.normal.z) < 0.5)
    for side in (-1, 1):  # 端面卷层：三圈逐渐缩小的布片
        for k, (w, h) in enumerate(((0.104, 0.066), (0.08, 0.05), (0.054, 0.032))):
            layer = b.box((0.006, w, h), (side * (0.086 + k * 0.003), 0.0, 0.036 + k * 0.002), color=("oily_d", "oily", "oily_edge")[k], bevel=0.0015)
            b.jitter(layer, 0.0015, 20 + k + side * 7)
    for i, x in enumerate((-0.05, 0.0, 0.045)):  # 布面褶皱
        crease = b.box((0.006, 0.112, 0.004), (x + 0.004 * i, 0.0, 0.0725), rot=(0, 0.15 * (i - 1), 0.06 * i), color="oily_d", bevel=0.0)
        b.jitter(crease, 0.0012, 30 + i)
    b.box((0.13, 0.004, 0.012), (0.0, -0.0555, 0.058), rot=(0.05, 0, 0.02), color="oily_d", bevel=0.0)  # 卷口
    for x in (-0.042, 0.038):
        rope_loop(b, (x, 0.0), (0.012, 0.118, 0.08), axis="Y", seed=int(x * 1000))
    rope_loop(b, (0.0, 0.006), (0.182, 0.012, 0.08), axis="X", seed=7)
    for k, (dx, dy) in enumerate(((0.0, 0.0), (0.008, 0.006), (-0.006, 0.007))):  # 绳结
        b.sphere(0.0065, (0.038 + dx, 0.006 + dy, 0.078), color="rope_d" if k else "rope", u=8, v=6, scale=(1.2, 0.9, 0.7))
    tail = b.box((0.004, 0.03, 0.004), (0.05, -0.004, 0.079), rot=(0.2, 0, -0.7), color="rope", bevel=0.0)
    b.jitter(tail, 0.001, 41)


def rope_loop(b, center, size, axis, seed):
    """绕着方块一圈的绳子：沿矩形路径（底、侧、顶、侧）排一节节略扭的绳股，深浅交替。
    axis="Y" 时绳圈在 YZ 平面（横跨宽度），axis="X" 时在 XZ 平面（横跨长度）。"""
    sx, sy, sz = size
    w = sy if axis == "Y" else sx
    path = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, sz), (-w / 2, sz)]
    for side in range(4):
        a, c = Vector(path[side]), Vector(path[(side + 1) % 4])
        steps = max(2, int((c - a).length / 0.014))
        for i in range(steps):
            p = a + (c - a) * ((i + 0.5) / steps)
            length = (c - a).length / steps + 0.002
            twist = 0.14 if (i + seed) % 2 == 0 else -0.14
            vertical = side % 2 == 1  # 第 2、4 段是竖直的侧边
            if axis == "Y":
                loc = (center[0], center[1] + p.x, p.y)
                size_box = (0.007, 0.0045, length) if vertical else (0.007, length, 0.0045)
                rot = (0, twist, 0) if vertical else (twist * 0.3, twist, 0)
            else:
                loc = (center[0] + p.x, center[1], p.y)
                size_box = (0.0045, 0.007, length) if vertical else (length, 0.007, 0.0045)
                rot = (twist, 0, 0) if vertical else (twist, twist * 0.3, 0)
            b.box(size_box, loc, rot, color="rope" if (i + side) % 2 == 0 else "rope_d", bevel=0.0)


def battery_base(b, worn):
    body = b.box((0.2, 0.13, 0.125), (0, 0, 0.0625), color="charcoal" if not worn else "charcoal_d", mat="M_Item_Palette", bevel=0.004)
    b.subdivide(body, 1)
    for x in (-0.1, 0.1):  # 四角竖筋让轮廓更硬朗
        for y in (-0.065, 0.065):
            b.box((0.014, 0.014, 0.12), (x, y, 0.062), color="charcoal_l", mat="M_Item_Palette")
    for z in (0.03, 0.09):  # 两侧横向加强筋
        for x in (-0.1005, 0.1005):
            b.box((0.004, 0.1, 0.006), (x, 0, z), color="charcoal_l", mat="M_Item_Palette")
    b.box((0.21, 0.14, 0.02), (0, 0, 0.135), color="charcoal_l", mat="M_Item_Palette", bevel=0.004)
    b.box((0.17, 0.004, 0.012), (0, -0.0665, 0.112), color="charcoal_d", mat="M_Item_Palette")
    # 顶盖上的提手凹槽与一排注液盖
    b.box((0.09, 0.016, 0.004), (0, -0.045, 0.1455), color="charcoal_d", mat="M_Item_Palette")
    for i in range(4):
        x = -0.03 + i * 0.02
        b.cyl(0.0055, 0.003, (x, 0.045, 0.145), color="charcoal_d", segs=10, mat="M_Item_Palette")
        b.box((0.007, 0.0015, 0.0012), (x, 0.045, 0.1482), color="charcoal_l", mat="M_Item_Palette")
    # 正面指示窗框
    b.box((0.08, 0.006, 0.034), (0, -0.066, 0.075), color="charcoal_d", mat="M_Item_Palette")
    terminal_color = "corrosion" if worn else "steel_l"
    for x in (-0.06, 0.06):
        b.cyl(0.016, 0.006, (x, 0.02, 0.145), color="charcoal_d", segs=6, mat="M_Item_Palette")
        b.cyl(0.011, 0.018, (x, 0.02, 0.145), color=terminal_color, segs=8, mat=None if not worn else "M_Item_Palette")
        b.cyl(0.0125, 0.003, (x, 0.02, 0.153), color="steel_d" if not worn else "rust_d", segs=6)  # 端子螺母
    # 正负极标记用凸起的十字/横杠（不写字）
    b.box((0.012, 0.003, 0.0012), (-0.06, -0.006, 0.1452), color="charcoal_d", mat="M_Item_Palette")
    b.box((0.012, 0.003, 0.0012), (0.06, -0.006, 0.1452), color="charcoal_d", mat="M_Item_Palette")
    b.box((0.003, 0.012, 0.0012), (0.06, -0.006, 0.1452), color="charcoal_d", mat="M_Item_Palette")
    return body


def battery_window(b, lit):
    if lit:
        b.box((0.064, 0.006, 0.02), (0, -0.068, 0.075), color="glow_green")
        b.box((0.15, 0.004, 0.006), (0, -0.0685, 0.03), color="glow_green")
    else:
        b.box((0.064, 0.006, 0.02), (0, -0.068, 0.075), color="window_off")


def battery_repairs(b):
    # 两处金属补片和铆钉：空电池与充好的电池共用。
    b.box((0.05, 0.004, 0.04), (-0.065, -0.0665, 0.035), rot=(0, 0.08, 0), color="steel", bevel=0.0)
    b.box((0.004, 0.045, 0.05), (0.1035, 0.015, 0.06), color="steel_d", bevel=0.0)
    for x, z in ((-0.085, 0.05), (-0.045, 0.05), (-0.085, 0.02), (-0.045, 0.02)):
        b.cyl(0.003, 0.003, (x, -0.0685, z), rot=(math.pi / 2, 0, 0), color="steel_l", segs=6)
    for y, z in ((-0.004, 0.08), (0.034, 0.08), (-0.004, 0.04), (0.034, 0.04)):
        b.cyl(0.003, 0.003, (0.1055, y, z), rot=(0, math.pi / 2, 0), color="steel_l", segs=6)
    # 新换的接线：一截短线从正极绕到侧面
    b.cyl(0.003, 0.04, (0.06, 0.02, 0.158), rot=(0, math.pi / 2, 0), color="red_dark", segs=8)


def build_old_battery(b):
    body = battery_base(b, worn=True)
    b.wear(body, "rust_d", 0.25, 31)
    b.wear(body, "rust", 0.12, 33)
    # 大裂痕：斜着的深色缝，穿过正面和侧面。
    b.box((0.006, 0.006, 0.09), (0.045, -0.0655, 0.06), rot=(0, 0.45, 0), color="hand")
    b.box((0.005, 0.006, 0.05), (0.07, -0.0655, 0.03), rot=(0, -0.35, 0), color="hand")
    b.box((0.006, 0.07, 0.006), (0.1005, -0.02, 0.09), rot=(0.5, 0, 0), color="hand")
    for x in (-0.06, 0.06):  # 柱头腐蚀
        blob = b.sphere(0.013, (x, 0.02, 0.152), color="corrosion", u=6, v=4, scale=(1, 1, 0.55))
        b.jitter(blob, 0.003, 32 + int(x * 100))
    battery_window(b, lit=False)


def build_empty_battery(b):
    battery_base(b, worn=False)
    battery_repairs(b)
    battery_window(b, lit=False)


def build_charged_battery(b):
    battery_base(b, worn=False)
    battery_repairs(b)
    battery_window(b, lit=True)


def build_scrap_metal(b):
    rng = random.Random(41)
    plates = [((0.0, 0.0, 0.006), 0.2, (0.1, 0.07)), ((0.03, 0.03, 0.016), -0.6, (0.085, 0.06)),
              ((-0.04, 0.02, 0.022), 1.1, (0.08, 0.05)), ((0.01, -0.025, 0.03), 2.2, (0.07, 0.045))]
    for i, (loc, yaw, (w, d)) in enumerate(plates):
        plate = b.box((w, d, 0.005), (0, 0, 0), color="rust" if i % 2 == 0 else "rust_d", mat="M_Item_Palette")
        b.subdivide(plate, 2)
        b.jitter(plate, 0.008, 42 + i, axes=(1, 1, 0), where=lambda co, w=w, d=d: abs(co.x) > w * 0.4 or abs(co.y) > d * 0.4)
        b.bend(plate, (w * 0.1, 0, 0), "Y", -0.5 - 0.2 * i, lambda co, w=w: co.x > w * 0.1)
        b.bend(plate, (0, -d * 0.2, 0), "X", 0.25, lambda co, d=d: co.y < -d * 0.2)
        b.wear(plate, "rust_l", 0.25, 50 + i)
        b.transform(plate, M(loc, (rng.uniform(-0.15, 0.15), rng.uniform(-0.1, 0.1), yaw)))
    strip = b.box((0.07, 0.012, 0.003), (0, 0, 0), color="rust_l", mat="M_Item_Palette", bevel=0.0)
    b.subdivide(strip, 3)
    b.bend(strip, (0.0, 0, 0), "Y", -0.6, lambda co: co.x > 0.0)
    b.bend(strip, (-0.02, 0, 0), "Y", 0.5, lambda co: co.x < -0.02)
    b.transform(strip, M((-0.01, -0.05, 0.012), (0.2, 0, 0.5)))
    b.tube(0.007, 0.0035, 0.004, loc=(0.03, -0.055, 0.0), color="steel_d", segs=6)  # 一颗六角螺母
    for i, (loc, rot) in enumerate((((0.065, -0.03, 0.006), (0, math.pi / 2, 0.4)), ((-0.06, -0.035, 0.006), (0, math.pi / 2, 2.4)))):
        bolt_head = b.cyl(0.009, 0.007, (0, 0, 0), color="steel_d", segs=6)
        shaft = b.cyl(0.0045, 0.022 - i * 0.006, (0, 0, 0.007), color="rust_d", segs=6)
        for pid in (bolt_head, shaft):
            b.transform(pid, M(loc, rot))


def build_dirty_bandage(b):
    radius, width = 0.042, 0.07
    radii = [radius, 0.035, 0.028, 0.021, 0.0125]
    for i in range(len(radii) - 1):  # 同心层：端面能看出一圈圈布
        b.tube(radii[i], radii[i + 1], width - i * 0.002, loc=(-(width - i * 0.002) / 2, 0, radius), rot=(0, math.pi / 2, 0),
               color="white" if i % 2 == 0 else "white_d", segs=12)
    roll = b.tube(radius + 0.002, radius, 0.06, loc=(-0.03, 0, radius), rot=(0, math.pi / 2, 0), color="white", segs=12)
    b.wear(roll, "stain", 0.12, 61)
    # 尾巴：从卷顶前方垂下来，最后搭在地上。
    t = 0.003
    tail = [(0.0, radius), (-0.75, radius), (-1.35, radius - 0.002)]  # 沿卷外圈的角度（绕 X），从正上方往前绕
    pts = [(0.0, -math.sin(a) * r, radius + math.cos(a) * r) for a, r in tail]
    pts += [(0.0, -0.05, 0.03), (0.0, -0.058, 0.004), (0.0, -0.09, 0.0015)]
    for i in range(len(pts) - 1):
        p0, p1 = Vector(pts[i]), Vector(pts[i + 1])
        mid, d = (p0 + p1) / 2, p1 - p0
        angle = math.atan2(d.z, -d.y)
        seg = b.box((0.056 - i * 0.002, d.length + 0.002, t), tuple(mid), rot=(angle, 0, 0.02 * i), color="white" if i % 2 == 0 else "white_d")
        if i == len(pts) - 2:
            b.subdivide(seg, 1)
            b.jitter(seg, 0.004, 62, axes=(1, 1, 0), where=lambda co: co.y < -0.08)
        b.wear(seg, "stain", 0.15, 63 + i)
    for k, (y, a) in enumerate(((-0.092, 0.4), (-0.088, -0.3))):  # 尾端的线头
        b.box((0.0012, 0.012, 0.0008), (0.02 - k * 0.03, y - 0.004, 0.0012), rot=(0, 0, a), color="white_d", bevel=0.0)


def build_first_aid_spray(b):
    body = b.cyl(0.033, 0.106, (0, 0, 0.006), color="red_dark", segs=12, bevel=0.0)
    b.wear(body, "red_dark_l", 0.2, 71)
    b.cyl(0.031, 0.006, (0, 0, 0), color="steel_d", segs=12)  # 底部内凹圈
    b.tube(0.0335, 0.03, 0.004, loc=(0, 0, 0.004), color="steel_d", segs=12)
    b.cyl(0.033, 0.012, (0, 0, 0.112), r2=0.026, color="red_dark", segs=12, bevel=0.0)  # 圆肩分两段
    b.cyl(0.026, 0.008, (0, 0, 0.124), r2=0.016, color="red_dark", segs=12, bevel=0.0)
    b.tube(0.0165, 0.012, 0.004, loc=(0, 0, 0.13), color="steel_l", segs=12)  # 阀门卷边
    b.cyl(0.012, 0.01, (0, 0, 0.132), color="steel", segs=10)
    head = b.cyl(0.015, 0.024, (0, 0, 0.142), color="red_dark_l", segs=10)
    b.box((0.02, 0.012, 0.003), (0, 0.002, 0.1665), color="red_dark", bevel=0.0)  # 按压面
    b.box((0.011, 0.028, 0.011), (0, -0.017, 0.157), color="red_dark_l")
    b.cyl(0.0035, 0.008, (0, -0.031, 0.157), rot=(math.pi / 2, 0, 0), color="charcoal_d", segs=8)  # 喷嘴
    # 布条：两道斜缠，加一个打结的垂头，不挡喷头。
    for z, tilt, seed in ((0.026, 0.12, 72), (0.066, -0.1, 73)):
        band = b.cyl(0.0356, 0.016, (0, 0, z), rot=(tilt, 0.05, 0), color="white", segs=12, bevel=0.0)
        b.wear(band, "white_d", 0.4, seed)
    b.sphere(0.006, (0.03, -0.02, 0.05), color="white_d", u=8, v=6, scale=(1, 0.8, 0.8))
    for k, a in enumerate((-0.6, -0.2)):
        tail = b.box((0.011, 0.003, 0.03), (0.032 + k * 0.004, -0.024, 0.033), rot=(0.1, 0, a), color="white_d" if k else "white", bevel=0.0)
        b.jitter(tail, 0.0012, 74 + k)


def ammo_9mm(b, loc=(0, 0, 0), rot=(0, 0, 0)):
    parts = [b.cyl(0.0052, 0.0015, (0, 0, 0), color="brass_d", segs=10),
             b.cyl(0.0047, 0.0012, (0, 0, 0.0015), color="brass_d", segs=10),
             b.cyl(0.0049, 0.0163, (0, 0, 0.0027), color="brass", segs=10),
             b.cyl(0.0046, 0.004, (0, 0, 0.019), color="copper", segs=10),
             b.cyl(0.0046, 0.004, (0, 0, 0.023), r2=0.0035, color="copper", segs=10),
             b.cyl(0.0035, 0.0025, (0, 0, 0.027), r2=0.0012, color="copper_l", segs=10)]
    for pid in parts:
        b.transform(pid, M(loc, rot))


def ammo_12g(b, loc=(0, 0, 0), rot=(0, 0, 0)):
    parts = [b.cyl(0.0112, 0.0018, (0, 0, 0), color="brass_d", segs=12),
             b.cyl(0.0105, 0.0122, (0, 0, 0.0018), color="brass", segs=12),
             b.cyl(0.0102, 0.052, (0, 0, 0.014), color="red_shell", segs=12),
             b.cyl(0.0102, 0.004, (0, 0, 0.066), r2=0.0085, color="red_shell_d", segs=12)]
    b.wear(parts[2], "red_shell_d", 0.2, 81)
    for pid in parts:
        b.transform(pid, M(loc, rot))


def ammo_556(b, loc=(0, 0, 0), rot=(0, 0, 0)):
    parts = [b.cyl(0.0048, 0.0015, (0, 0, 0), color="brass_d", segs=10),
             b.cyl(0.0042, 0.0012, (0, 0, 0.0015), color="brass_d", segs=10),
             b.cyl(0.0048, 0.034, (0, 0, 0.0027), r2=0.0045, color="brass", segs=10),
             b.cyl(0.0045, 0.0045, (0, 0, 0.0367), r2=0.0032, color="brass", segs=10),
             b.cyl(0.0032, 0.0055, (0, 0, 0.0412), color="brass_l", segs=10),
             b.cyl(0.0029, 0.007, (0, 0, 0.0467), r2=0.0019, color="copper", segs=10),
             b.cyl(0.0019, 0.0045, (0, 0, 0.0537), r2=0.0003, color="green_tip", segs=10)]
    for pid in parts:
        b.transform(pid, M(loc, rot))


AMMO = {"ammo_9mm": ammo_9mm, "ammo_12g": ammo_12g, "ammo_556": ammo_556}
# 组合摆放：每组三发同规格，只靠位置和轻微倾斜错落，全部立在地面上。
AMMO_GROUPS = {
    "ammo_9mm": [((-0.006, 0.004, 0), 0), ((0.006, 0.004, 0), 0.4), ((0.0, -0.0065, 0), 1.1)],
    "ammo_12g": [((-0.0112, 0.006, 0), 0), ((0.0112, 0.006, 0), 0.5), ((0.0, -0.0128, 0), 1.0)],
    "ammo_556": [((-0.011, 0, 0), 0), ((0.0, 0.0, 0), 0.6), ((0.011, 0, 0), 1.2)],
}


def build_canned_food(b):
    body = b.cyl(0.044, 0.05, (0, 0, 0.003), color="can", segs=14, bevel=0.0)
    b.jitter(body, 0.0022, 91, where=lambda co: 0.01 < co.z < 0.05 and math.hypot(co.x, co.y) > 0.04)
    b.wear(body, "rust_d", 0.12, 92)
    b.wear(body, "can_d", 0.15, 93)
    for z in (0.016, 0.028, 0.04):  # 罐身加强筋
        b.tube(0.0447, 0.043, 0.0022, loc=(0, 0, z), color="can_l", segs=14)
    b.tube(0.0458, 0.041, 0.004, loc=(0, 0, 0), color="can_d", segs=14)  # 底部卷边
    b.tube(0.0462, 0.0405, 0.0045, loc=(0, 0, 0.052), color="can_l", segs=14)  # 顶部卷边
    lid = b.cyl(0.041, 0.0018, (0, 0, 0.0525), color="can_l", segs=14, bevel=0.0)
    b.wear(lid, "rust_l", 0.12, 94)
    b.tube(0.032, 0.0295, 0.0008, loc=(0, 0, 0.0543), color="can_d", segs=14)  # 盖子压痕
    # 拉环：铆钉、舌板和扁环。
    b.cyl(0.0028, 0.0018, (0, 0.004, 0.0543), color="steel", segs=8)
    b.box((0.011, 0.016, 0.0012), (0, 0.003, 0.0548), color="steel_l", bevel=0.0)
    b.ring(circle(0.011, 12, sx=1.0, sy=1.3), circle(0.0068, 12, sx=1.0, sy=1.35), 0.0016, loc=(0, -0.013, 0.0546), color="steel_l")


def build_short_knife(b):
    # 刀身侧面在 YZ 平面，沿 X 挤出厚度；刀尖朝 -Y。
    blade = [(0.0, 0.012), (-0.095, 0.012), (-0.112, 0.008), (-0.13, 0.0), (-0.118, -0.008), (-0.1, -0.012), (0.0, -0.012)]
    pid = b.prism([(y, z) for y, z in blade], 0.0045, loc=(-0.00225, 0, 0.07), rot=(math.pi / 2, 0, math.pi / 2), color="blade")
    for v in b.verts(pid):  # 刃口变薄：下缘和尖部顶点向中线收
        if v.co.z < 0.07 - 0.003 or v.co.y < -0.11:
            v.co.x *= 0.15
    b.paint(pid, "blade_d", lambda f: f.calc_center_median().z < 0.065)
    for x in (-0.0024, 0.0024):  # 血槽
        b.box((0.0006, 0.07, 0.003), (x, -0.045, 0.074), color="blade_d", bevel=0.0)
    b.box((0.0048, 0.1, 0.0015), (0, -0.05, 0.0815), color="steel_l", bevel=0.0)  # 刀背亮边
    guard = b.cyl(0.006, 0.006, (0, 0.0005, 0.07), rot=(-math.pi / 2, 0, 0), color="steel_d", segs=10)
    b.transform(guard, Matrix.Translation((0, 0.0005, 0.07)) @ Matrix.Diagonal((1.0, 1.0, 3.2, 1)) @ Matrix.Translation((0, -0.0005, -0.07)))
    grip = b.cyl(0.0105, 0.084, (0, 0.007, 0.07), rot=(-math.pi / 2, 0, 0), color="wrap", segs=8)
    b.wear(grip, "wrap_d", 0.3, 101)
    for i in range(7):  # 斜缠的布条
        b.cyl(0.0114, 0.0065, (0, 0.012 + i * 0.0115, 0.07), rot=(-math.pi / 2 + 0.22, 0, 0), color="wrap_d" if i % 2 else "cloth_l", segs=8)
    b.cyl(0.0122, 0.012, (0, 0.091, 0.07), rot=(-math.pi / 2, 0, 0), color="steel_d", segs=8)  # 柄头
    b.cyl(0.004, 0.004, (0, 0.103, 0.07), rot=(-math.pi / 2, 0, 0), color="steel", segs=8)
    b.ring(circle(0.006, 8), circle(0.0035, 8), 0.0025, loc=(-0.00125, 0.112, 0.07), rot=(0, math.pi / 2, 0), color="steel_d")  # 挂绳环
    return (0, 0.05, 0.07)


def build_encrypted_drive(b):
    w, d, h = 0.13, 0.085, 0.022
    body = b.prism(rounded_rect(w, d, 0.008, 3), h, color="charcoal")
    b.prism(rounded_rect(w * 0.72, d * 0.62, 0.004, 2), 0.002, loc=(0, 0.004, h), color="panel")  # 面板
    b.prism(rounded_rect(w * 0.5, d * 0.32, 0.003, 2), 0.0012, loc=(-0.008, 0.008, h + 0.002), color="charcoal_l")  # 标签凹位
    for i in range(6):  # 散热槽
        b.box((0.0025, 0.022, 0.0012), (0.032 + i * 0.0045, 0.006, h + 0.0026), color="charcoal_d", bevel=0.0)
    for x in (-1, 1):  # 金属护角加螺丝
        for y in (-1, 1):
            b.box((0.02, 0.02, h + 0.003), (x * (w / 2 - 0.0085), y * (d / 2 - 0.0085), h / 2), color="steel", bevel=0.002)
            b.cyl(0.0025, 0.001, (x * (w / 2 - 0.0085), y * (d / 2 - 0.0085), h + 0.0015), color="steel_d", segs=6)
    for k in range(5):  # 两侧防滑棱
        for x in (-1, 1):
            b.box((0.0015, 0.003, 0.012), (x * (w / 2 + 0.0004), -0.012 + k * 0.006, 0.011), color="charcoal_l", bevel=0.0)
    b.box((0.026, 0.0035, 0.008), (0.028, -d / 2 + 0.0008, h * 0.5), color="charcoal_d", bevel=0.0)  # 指示灯框
    b.box((0.022, 0.004, 0.005), (0.028, -d / 2 - 0.0005, h * 0.5), color="glow_cyan", bevel=0.0)
    b.box((0.03, 0.004, 0.008), (-0.03, -d / 2 - 0.0005, h * 0.5), color="charcoal_d", bevel=0.0)  # 接口
    b.box((0.022, 0.0042, 0.003), (-0.03, -d / 2 - 0.0007, h * 0.5), color="steel_d", bevel=0.0)
    b.wear(body, "charcoal_l", 0.12, 111)


def build_watch_body(b):
    case = b.cyl(0.03, 0.011, (0, 0, 0), color="brass", segs=16)
    b.wear(case, "brass_d", 0.2, 121)
    b.tube(0.0305, 0.026, 0.003, loc=(0, 0, 0.011), color="brass_l", segs=16)
    b.cyl(0.026, 0.001, (0, 0, 0.0105), color="dial", segs=16)
    for a in range(4):  # 只有刻度，不写数字
        angle = a * math.pi / 2
        b.box((0.002, 0.005, 0.0006), (math.sin(angle) * 0.021, math.cos(angle) * 0.021, 0.0117), rot=(0, 0, -angle), color="hand")
    b.box((0.0018, 0.016, 0.0008), (0.0, -0.007, 0.0122), rot=(0, 0, 0.6), color="hand")
    b.box((0.0018, 0.011, 0.0008), (0.0, -0.005, 0.0128), rot=(0, 0, -1.3), color="hand")
    b.cyl(0.0018, 0.0012, (0, 0, 0.0115), color="hand", segs=6)
    # 表冠在正面（-Y），链子从表冠拖出去；铰链在背面（+Y）。
    b.cyl(0.0035, 0.006, (0, -0.0295, 0.0055), rot=(math.pi / 2, 0, 0), color="brass_d", segs=8)
    b.cyl(0.0055, 0.004, (0, -0.0355, 0.0055), rot=(math.pi / 2, 0, 0), color="brass_l", segs=8)
    b.ring(circle(0.0075, 10, sy=0.8), circle(0.0045, 10, sy=0.7), 0.0018, loc=(0, -0.045, 0.0046), color="brass")
    # 链节一平一立地扣在一起，平躺的贴地，立着的底边落地。
    yaw = -math.pi / 2 - 0.3
    direction = Vector((math.cos(yaw), math.sin(yaw), 0))
    start = Vector((0, -0.049, 0))
    for i in range(5):
        center = start + direction * (0.0045 + i * 0.0072)
        if i % 2 == 0:
            b.ring(circle(0.005, 8, sy=0.6), circle(0.0029, 8, sy=0.4), 0.0016, loc=(center.x, center.y, 0), rot=(0, 0, yaw), color="brass_d")
        else:
            b.ring(circle(0.005, 8, sy=0.6), circle(0.0029, 8, sy=0.4), 0.0016, loc=(center.x, center.y, 0.003), rot=(math.pi / 2, 0, yaw), color="brass")
    b.box((0.012, 0.004, 0.004), (0, 0.031, 0.009), color="brass_d")


def build_watch_lid(b):
    # 原点在铰链上（表盖局部）；闭合时表盖平放在表壳上方。
    b.cyl(0.03, 0.0035, (0, -0.031, 0), color="brass", segs=16)
    inner = b.cyl(0.026, 0.0006, (0, -0.031, -0.0006), color="brass_d", segs=16)
    b.box((0.012, 0.004, 0.004), (0, 0.0, 0.0018), color="brass_l")
    return inner


def build_chemicals(b):
    body = b.cyl(0.036, 0.055, (0, 0, 0.003), color="liquid", segs=8, bevel=0.0)
    b.wear(body, "liquid_d", 0.3, 131)
    b.cyl(0.0345, 0.004, (0, 0, 0), color="glass", segs=8)  # 厚玻璃底
    b.cyl(0.036, 0.015, (0, 0, 0.058), color="glass", segs=8, bevel=0.0)
    b.cyl(0.036, 0.014, (0, 0, 0.073), r2=0.017, color="glass", segs=8, bevel=0.0)
    b.cyl(0.0145, 0.012, (0, 0, 0.087), color="glass", segs=8, bevel=0.0)
    b.tube(0.0172, 0.0135, 0.004, loc=(0, 0, 0.096), color="glass", segs=12)  # 瓶口唇
    b.cyl(0.0155, 0.014, (0, 0, 0.097), r2=0.0145, color="cork", segs=10)
    b.sphere(0.0148, (0, 0, 0.111), color="cork", u=10, v=6, scale=(1, 1, 0.35))
    for z in (0.088, 0.092):  # 麻绳两圈
        b.tube(0.0168, 0.0142, 0.0022, loc=(0, 0, z), rot=(0.08, 0, 0), color="rope", segs=12)
    b.cyl(0.0176, 0.008, (0, 0, 0.085), color="white_d", segs=8, bevel=0.0)  # 布条
    flap = b.box((0.016, 0.003, 0.034), (0.017, -0.012, 0.07), rot=(0.25, 0.1, -0.7), color="white_d", bevel=0.0)
    b.subdivide(flap, 1)
    b.jitter(flap, 0.0015, 133)
    b.wear(flap, "stain", 0.4, 132)
    b.box((0.03, 0.0015, 0.003), (0, -0.0355, 0.058), color="liquid_d", bevel=0.0)  # 液面线


def build_cloth(b):
    w, d, t = 0.24, 0.165, 0.016
    for i in range(3):
        z = i * (t + 0.001)
        layer = b.box((w - i * 0.01, d - i * 0.006, t), (i * 0.004, -i * 0.003, z + t / 2), rot=(0, 0, (i - 1) * 0.04),
                      color="cloth" if i % 2 == 0 else "cloth_d")
        b.subdivide(layer, 2)
        side = 1 if i % 2 == 0 else -1
        # 折边一侧做圆，另一侧破边。
        b.jitter(layer, 0.006, 140 + i, axes=(1, 1, 0.2), where=lambda co, s=side: co.x * -s > 0.08)
        fold = b.cyl(t / 2 + 0.001, d - i * 0.006 - 0.004, (side * (w - i * 0.01) / 2 + i * 0.004, -(d - i * 0.006 - 0.004) / 2 - i * 0.003, z + t / 2),
                     rot=(-math.pi / 2, 0, 0), color="cloth_l" if i % 2 == 0 else "cloth", segs=6)
        b.wear(layer, "cloth_l", 0.12, 150 + i)
    top = 2 * (t + 0.001) + t
    patch = b.box((0.06, 0.05, 0.002), (0.03, 0.02, top + 0.001), rot=(0, 0, 0.15), color="patch")
    b.jitter(patch, 0.002, 160, axes=(1, 1, 0))
    stitch_m = M((0.03, 0.02, top + 0.0022), (0, 0, 0.15))
    for k in range(14):  # 补丁四周的针脚
        side, f = divmod(k, 4) if k < 12 else (k - 12, 0.5)
        t_ = (k % 4 + 0.5) / 4
        pts = [(-0.027 + 0.054 * t_, 0.022), (0.027, 0.022 - 0.044 * t_), (0.027 - 0.054 * t_, -0.022), (-0.027, -0.022 + 0.044 * t_)]
        x, y = pts[min(side, 3)]
        b.box((0.004, 0.0012, 0.0008), tuple(stitch_m @ Vector((x, y, 0))), rot=(0, 0, 0.15 + (0.6 if side % 2 else -0.6)), color="rope_d", bevel=0.0)
    rng = random.Random(161)
    for k in range(7):  # 破边垂下的线头
        y = rng.uniform(-0.07, 0.07)
        b.box((0.014, 0.0012, 0.0012), (-0.123 - rng.uniform(0, 0.004), y, rng.uniform(0.004, 0.04)), rot=(0, rng.uniform(-0.6, 0.6), rng.uniform(-0.5, 0.5)), color="cloth_l", bevel=0.0)


def gear_outline(teeth, r_root, r_tip):
    pts = []
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        step = 2 * math.pi / teeth
        for frac, r in ((0.0, r_root), (0.18, r_tip), (0.5, r_tip), (0.68, r_root)):
            ang = a + step * frac
            pts.append((math.cos(ang) * r, math.sin(ang) * r))
    return pts


def build_mechanical_parts(b):
    outer = gear_outline(10, 0.04, 0.05)
    inner = [(x * 0.38, y * 0.38) for x, y in outer]
    gear = b.ring(outer, [(math.cos(math.atan2(y, x)) * 0.014, math.sin(math.atan2(y, x)) * 0.014) for x, y in inner], 0.012,
                  loc=(-0.02, 0.01, 0), color="steel")
    b.wear(gear, "steel_d", 0.25, 171)
    b.wear(gear, "rust", 0.06, 172)
    b.tube(0.026, 0.014, 0.016, loc=(-0.02, 0.01, 0), color="steel_d", segs=12)
    for k in range(4):  # 减重孔（深色凹位）
        a = k * math.pi / 2 + math.pi / 4
        b.cyl(0.0045, 0.0008, (-0.02 + math.cos(a) * 0.031, 0.01 + math.sin(a) * 0.031, 0.0118), color="charcoal_d", segs=8)
    b.tube(0.016, 0.0145, 0.0012, loc=(-0.02, 0.01, 0.016), color="steel_l", segs=12)
    # 连杆：两端圆环加中间连杆。
    rod = M((0.04, -0.04, 0.0), (0, 0, 0.5))
    for pid in (b.tube(0.012, 0.0055, 0.008, loc=(0, 0, 0), color="charcoal_l", segs=10),
                b.tube(0.0085, 0.004, 0.008, loc=(0.06, 0, 0), color="charcoal_l", segs=10),
                b.box((0.05, 0.009, 0.007), (0.03, 0, 0.004), color="charcoal_l")):
        b.transform(pid, rod)
    for i, (loc, yaw) in enumerate((((0.035, 0.03, 0.009), 0.3), ((0.055, -0.005, 0.009), -0.9))):
        bolt = M(loc, (0, math.pi / 2, yaw))
        for pid in (b.cyl(0.009, 0.007, (0, 0, -0.018), color="steel_d", segs=6),
                    b.cyl(0.0045, 0.03, (0, 0, -0.011), color="steel", segs=6),
                    b.cyl(0.0052, 0.003, (0, 0, 0.004), color="steel_l", segs=6),
                    b.cyl(0.0052, 0.003, (0, 0, 0.011), color="steel_l", segs=6),
                    b.tube(0.009, 0.0048, 0.0015, loc=(0, 0, -0.011), color="steel_l", segs=10)):
            b.transform(pid, bolt)


def build_identity_tag(b):
    w, d, t = 0.06, 0.034, 0.0025
    hole = (-w / 2 + 0.008, 0)
    outline = rounded_rect(w, d, 0.006, 3)
    ordered = sorted(outline, key=lambda p: math.atan2(p[1] - hole[1], p[0] - hole[0]))
    inner = [(hole[0] + math.cos(math.atan2(p[1] - hole[1], p[0] - hole[0])) * 0.0028,
              hole[1] + math.sin(math.atan2(p[1] - hole[1], p[0] - hole[0])) * 0.0028) for p in ordered]
    tag = b.ring(ordered, inner, t, loc=(0.008, 0.0, 0), color="gold")
    rim_inner = [(p[0] * 0.86 + 0.0012, p[1] * 0.78) for p in outline]
    b.ring(outline, rim_inner, 0.0006, loc=(0.008, 0.0, t), color="gold_d")  # 压出来的边框
    b.wear(tag, "gold_d", 0.3, 181)
    b.paint(tag, "gold_d", lambda f: abs(f.normal.z) < 0.5)
    for i, (x, y, a) in enumerate(((0.01, 0.006, 0.4), (0.02, -0.005, -0.3), (0.0, -0.008, 0.9))):
        b.box((0.016, 0.0012, 0.0004), (0.008 + x, y, t + 0.0001), rot=(0, 0, a), color="gold_d")
    # 断掉的珠链：从孔里穿出，绕一小段后断开。
    chain = [(hole[0] + 0.008 - 0.0036 * i, 0.001 + math.sin(i * 0.5) * 0.006 - 0.0009 * i, 0.0018) for i in range(1, 13)]
    for i, (x, y, z) in enumerate(chain):
        b.sphere(0.0017, (x, y, z), color="steel" if i % 3 else "steel_d", u=6, v=4)


def build_old_pistol(b):
    # 枪口朝 -Y；侧面朝 ±X。
    slide = b.box((0.024, 0.165, 0.028), (0, -0.04, 0.114), color="charcoal_l", bevel=0.0035)
    b.wear(slide, "rust_d", 0.06, 191)
    b.box((0.016, 0.15, 0.004), (0, -0.04, 0.1295), color="steel", bevel=0.0)  # 顶边提亮
    for side in (-1, 1):
        for k in range(6):  # 后部防滑纹
            b.box((0.0012, 0.0022, 0.02), (side * 0.0122, 0.012 + k * 0.0045, 0.114), color="charcoal_d", bevel=0.0)
        b.box((0.0012, 0.15, 0.002), (side * 0.0122, -0.04, 0.1015), color="steel_d", bevel=0.0)  # 套筒下沿亮线
    b.box((0.0016, 0.03, 0.011), (0.0122, -0.02, 0.119), color="charcoal_d", bevel=0.0)  # 抛壳窗
    b.box((0.002, 0.022, 0.007), (0.0128, -0.02, 0.119), color="brass", bevel=0.0)  # 窗里露出的枪管
    frame = b.box((0.021, 0.115, 0.02), (0, -0.032, 0.091), color="charcoal", bevel=0.002)
    b.box((0.016, 0.05, 0.006), (0, -0.09, 0.083), color="charcoal_d", bevel=0.0)  # 防尘盖
    b.cyl(0.0075, 0.01, (0, -0.1225, 0.114), rot=(math.pi / 2, 0, 0), color="steel_d", segs=10)  # 枪管衬套
    b.tube(0.0048, 0.003, 0.003, loc=(0, -0.1325, 0.114), rot=(math.pi / 2, 0, 0), color="charcoal_d", segs=12)  # 枪口
    b.box((0.004, 0.007, 0.006), (0, -0.112, 0.1325), color="steel_l")  # 准星
    for side in (-1, 1):  # 照门两片
        b.box((0.004, 0.006, 0.007), (side * 0.0045, 0.035, 0.132), color="steel_l")
    hammer = b.box((0.007, 0.012, 0.016), (0, 0.047, 0.128), rot=(-0.5, 0, 0), color="charcoal_l")
    b.box((0.002, 0.016, 0.004), (-0.012, -0.005, 0.1), color="steel", bevel=0.0)  # 空仓挂机柄
    b.cyl(0.0025, 0.026, (-0.013, -0.038, 0.091), rot=(0, math.pi / 2, 0), color="steel_l", segs=8)  # 销钉
    # 握把：后倾，两侧握把片，缠旧布。
    grip_rot = (-0.22, 0, 0)
    grip_center = (0, 0.022, 0.045)
    grip_m = M(grip_center, grip_rot)
    grip = b.box((0.02, 0.034, 0.085), grip_center, rot=grip_rot, color="grip", bevel=0.003)
    for side in (-1, 1):
        b.box((0.003, 0.028, 0.07), tuple(grip_m @ Vector((side * 0.0105, 0, 0.004))), rot=grip_rot, color="charcoal_l", bevel=0.001)
    for i in range(5):  # 布条沿握把轴线缠
        loc = grip_m @ Vector((0, 0, 0.032 - i * 0.016))
        band = b.box((0.0262, 0.0368, 0.0095), tuple(loc), rot=(-0.22 + (0.07 if i % 2 else -0.05), 0, 0),
                     color="wrap" if i % 2 == 0 else "cloth_l", bevel=0.0015)
        b.wear(band, "wrap_d", 0.25, 192 + i)
    b.box((0.022, 0.036, 0.006), tuple(grip_m @ Vector((0, 0, -0.044))), rot=grip_rot, color="steel_d")  # 弹匣底板
    b.box((0.016, 0.012, 0.016), tuple(grip_m @ Vector((0, 0.016, 0.036))), rot=grip_rot, color="charcoal_l")  # 握把保险
    # 圆角扳机护圈（侧面看是一个回环）与扳机。
    guard_outer = rounded_rect(0.036, 0.024, 0.008, 3)
    guard_inner = [(x * 0.72, y * 0.55 + 0.002) for x, y in guard_outer]
    b.ring([(y, z) for y, z in guard_outer], [(y, z) for y, z in guard_inner], 0.007,
           loc=(-0.0035, -0.016, 0.071), rot=(math.pi / 2, 0, math.pi / 2), color="charcoal_l")
    trigger = b.box((0.005, 0.004, 0.013), (0, -0.01, 0.073), rot=(0.3, 0, 0), color="steel")
    return (0, 0.022, 0.05)


BUILDERS = {
    "fuel_pack": build_fuel_pack, "old_battery": build_old_battery, "empty_battery": build_empty_battery,
    "charged_battery": build_charged_battery, "scrap_metal": build_scrap_metal, "dirty_bandage": build_dirty_bandage,
    "first_aid_spray": build_first_aid_spray, "canned_food": build_canned_food, "short_knife": build_short_knife,
    "encrypted_drive": build_encrypted_drive, "chemicals": build_chemicals, "cloth": build_cloth,
    "mechanical_parts": build_mechanical_parts, "identity_tag": build_identity_tag, "old_pistol": build_old_pistol,
}
ORDER = ["fuel_pack", "old_battery", "empty_battery", "charged_battery", "scrap_metal", "dirty_bandage", "first_aid_spray",
         "ammo_9mm", "ammo_12g", "ammo_556", "canned_food", "short_knife", "encrypted_drive", "intact_watch", "chemicals",
         "cloth", "mechanical_parts", "identity_tag", "old_pistol"]
WEAPONS = {"short_knife", "old_pistol"}


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def dims(objects):
    corners = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    return [round(max(c[i] for c in corners) - min(c[i] for c in corners), 4) for i in range(3)]


def build(item_id, mats, ammo_collection):
    collection = item_collection(item_id)
    name = f"Item_{item_id}"
    stats = {"itemId": item_id, "object": name, "collection": collection.name}
    if item_id in AMMO:
        single = Builder()
        AMMO[item_id](single)
        single_obj, _ = finish(single, f"AmmoSingle_{item_id}", ammo_collection, mats)
        group = Builder()
        for loc, yaw in AMMO_GROUPS[item_id]:
            AMMO[item_id](group, loc, (0, 0, yaw))
        obj, _ = finish(group, name, collection, mats)
        stats["single"] = {"object": single_obj.name, "triangles": triangles(single_obj), "size": dims([single_obj])}
        objects = [obj]
    elif item_id == "intact_watch":
        body = Builder()
        build_watch_body(body)
        obj, shift = finish(body, name, collection, mats)
        lid_builder = Builder()
        build_watch_lid(lid_builder)
        lid, _ = finish(lid_builder, f"{name}_Lid", collection, mats, origin="none", parent=obj)
        lid.name = "Lid"
        # 铰链在表壳背面上沿；打开约 105°。
        lid.location = Vector((0, 0.031, 0.0125)) - shift
        lid.rotation_euler = (math.radians(-105), 0, 0)
        objects = [obj, lid]
        stats["children"] = ["Lid（铰链原点，局部 X 轴旋转开合）"]
    else:
        b = Builder()
        grip = BUILDERS[item_id](b)
        obj, shift = finish(b, name, collection, mats)
        objects = [obj]
        if item_id in WEAPONS:
            # 源文件里名字要唯一，导出时 export_items.py 临时改回 GripAnchor。
            add_empty(f"GripAnchor_{item_id}", Vector(grip) - shift, obj, collection)
            stats["children"] = ["GripAnchor"]
    bpy.context.view_layer.update()
    stats["triangles"] = sum(triangles(o) for o in objects)
    stats["materials"] = sorted({m.name for o in objects for m in o.data.materials})
    stats["size_xyz_m"] = dims(objects)
    return stats


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = args or ORDER
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    mats = make_materials(make_palette())
    ammo_collection = bpy.data.collections.new("AmmoSingle")
    scene.collection.children.link(ammo_collection)
    all_stats = []
    for index, item_id in enumerate(wanted):
        stats = build(item_id, mats, ammo_collection)
        # 在源文件里排成一行方便查看；导出时会把根物体移回原点。
        root = bpy.data.objects[f"Item_{item_id}"]
        root.location.x = index * 0.3
        all_stats.append(stats)
        print("[item]", item_id, stats["triangles"], stats["size_xyz_m"])
    for index, obj in enumerate(ammo_collection.objects):
        obj.location = (index * 0.05, -0.4, 0)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Items.blend"), relative_remap=True)
    with open(os.path.join(HERE, "build_stats.json"), "w", encoding="utf-8") as f:
        json.dump(all_stats, f, ensure_ascii=False, indent=2)


main()
