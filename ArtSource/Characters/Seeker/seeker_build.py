"""寻火者（Seeker）低多边形模型生成脚本。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python seeker_build.py -- <输出目录> <FBX 路径>

产物：
    <输出目录>/Seeker.blend         可继续手工编辑的源文件
    <输出目录>/renders/*.png        正/侧/背/四分之三视图
    <FBX 路径>                      Unity 用模型（Seeker_Body / Seeker_Cloak / Seeker_Backpack 三个独立网格）

坐标约定：Blender Z 向上，角色面朝 -Y，脚底在原点，身高约 1.82 m（兜帽顶）。
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seeker_rig  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(ARGS[0] if len(ARGS) > 0 else os.path.dirname(__file__))
FBX_PATH = os.path.abspath(ARGS[1] if len(ARGS) > 1 else os.path.join(OUT_DIR, "Seeker.fbx"))
TEX_DIR = os.path.join(os.path.dirname(FBX_PATH), "Textures")  # 贴图直接输出到 Unity 资源目录

random.seed(7)
TAU = math.pi * 2

# ---------- 材质 ----------

PALETTE = {
    "M_Seeker_Cloak": ((0.48, 0.41, 0.35), 0.95, 0.0),
    "M_Seeker_CloakInner": ((0.27, 0.23, 0.20), 0.95, 0.0),
    "M_Seeker_CloakPatch": ((0.40, 0.34, 0.29), 0.95, 0.0),
    "M_Seeker_CloakDirt": ((0.35, 0.27, 0.21), 0.95, 0.0),
    "M_Seeker_Cloth": ((0.23, 0.21, 0.20), 0.9, 0.0),
    "M_Seeker_Mask": ((0.13, 0.12, 0.12), 0.9, 0.0),
    "M_Seeker_Leather": ((0.33, 0.23, 0.16), 0.75, 0.0),
    "M_Seeker_Scarf": ((0.70, 0.30, 0.16), 0.9, 0.0),
    "M_Seeker_Metal": ((0.55, 0.53, 0.50), 0.45, 0.8),
    "M_Seeker_Canvas": ((0.42, 0.34, 0.27), 0.9, 0.0),
    "M_Seeker_Skin": ((0.62, 0.48, 0.40), 0.8, 0.0),
}


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def make_materials():
    mats = {}
    for name, (rgb, rough, metal) in PALETTE.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        lin = tuple(srgb_to_linear(c) for c in rgb)
        bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        mat.diffuse_color = (*lin, 1.0)
        mats[name] = mat
    return mats


# ---------- 网格构建 ----------

def ellipse_ring(cx, cy, z, rx, ry_front, ry_back, n, start=0.0, end=TAU, closed=True, jitter=0.0):
    """XY 截面环。角度从 +X 逆时针；-Y 为正前方，前后半径可不同。"""
    pts = []
    count = n if closed else n + 1
    for i in range(count):
        t = start + (end - start) * i / n
        s, c = math.sin(t), math.cos(t)
        ry = ry_back if s > 0 else ry_front
        p = Vector((cx + rx * c, cy + ry * s, z))
        if jitter:
            p += Vector((random.uniform(-jitter, jitter), random.uniform(-jitter, jitter), random.uniform(-jitter, jitter) * 0.5))
        pts.append(p)
    return pts


def axis_ring(center, axis, ru, rv, n, forward=Vector((0, -1, 0)), squash_front=1.0):
    """沿任意轴的截面环（手臂、背带等）。u 指向 forward 在截面上的投影。"""
    axis = axis.normalized()
    u = (forward - axis * forward.dot(axis)).normalized()
    v = axis.cross(u).normalized()
    pts = []
    for i in range(n):
        t = TAU * i / n
        s, c = math.sin(t), math.cos(t)
        r_u = ru * (squash_front if c > 0 else 1.0)
        pts.append(center + u * (r_u * c) + v * (rv * s))
    return pts


class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mat_names = []
        self.fixed_faces = set()  # 已手动定向的开放面片，不参与自动重算法线

    def mat(self, name):
        if name not in self.mat_names:
            self.mat_names.append(name)
        return self.mat_names.index(name)

    def sheet(self, rings, mat, back_offset):
        """双面开放面片：正面一层 + 沿 back_offset 平移的背面一层，法线分别朝外、朝内。"""
        before = set(self.bm.faces)
        self.loft(rings, mat, closed=False)
        front = [f for f in self.bm.faces if f not in before]
        back_rings = [[p + back_offset for p in r] for r in rings]
        before = set(self.bm.faces)
        self.loft(back_rings, mat, closed=False)
        back = [f for f in self.bm.faces if f not in before]
        out = -back_offset
        for faces, sign in ((front, 1), (back, -1)):
            for f in faces:
                f.normal_update()
                if f.normal.dot(out) * sign < 0:
                    f.normal_flip()
        self.fixed_faces.update(front + back)

    def loft(self, rings, mat, closed=True, cap_start=False, cap_end=False, mats_per_band=None):
        vr = [[self.bm.verts.new(p) for p in r] for r in rings]
        n = len(rings[0])
        for b, (lo, hi) in enumerate(zip(vr, vr[1:])):
            m = self.mat(mats_per_band[b] if mats_per_band else mat)
            for i in range(n if closed else n - 1):
                j = (i + 1) % n
                f = self.bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
                f.material_index = m
        if cap_start:
            self._cap(vr[0], mat, flip=True)
        if cap_end:
            self._cap(vr[-1], mat, flip=False)
        return vr

    def _cap(self, ring, mat, flip, peak=None):
        m = self.mat(mat)
        centre = sum((v.co for v in ring), Vector()) / len(ring)
        if peak is not None:
            centre = peak
        c = self.bm.verts.new(centre)
        n = len(ring)
        for i in range(n):
            a, b = ring[i], ring[(i + 1) % n]
            f = self.bm.faces.new((b, a, c) if flip else (a, b, c))
            f.material_index = m

    def cap_peak(self, ring, mat, peak):
        self._cap(ring, mat, flip=False, peak=peak)

    def box(self, center, size, mat, bevel=0.0):
        """倒角盒（八边截面拉伸），用于扣具、口袋等小件。"""
        cx, cy, cz = center
        sx, sy, sz = (s * 0.5 for s in size)
        b = bevel or min(sx, sy) * 0.3
        ring = lambda z: [Vector(p) for p in (
            (cx + sx, cy - sy + b, z), (cx + sx, cy + sy - b, z),
            (cx + sx - b, cy + sy, z), (cx - sx + b, cy + sy, z),
            (cx - sx, cy + sy - b, z), (cx - sx, cy - sy + b, z),
            (cx - sx + b, cy - sy, z), (cx + sx - b, cy - sy, z))]
        return self.loft([ring(cz - sz), ring(cz + sz)], mat, cap_start=True, cap_end=True)

    def build(self, mats, smooth=False, recalc=True):
        if recalc:
            bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces if f not in self.fixed_faces])
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for n in self.mat_names:
            mesh.materials.append(mats[n])
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        for p in mesh.polygons:
            p.use_smooth = smooth
        return obj


def mirror_x(points):
    return [Vector((-p.x, p.y, p.z)) for p in reversed(points)]


# ---------- 身体（内搭、头、围巾、手套、靴子、腰带、枪套） ----------

def build_body(mats):
    mb = MeshBuilder("Seeker_Body")
    N = 10

    # 躯干：中性比例，胸腰差小
    torso = [
        ellipse_ring(0, 0.0, 0.80, 0.165, 0.105, 0.11, N),
        ellipse_ring(0, 0.0, 0.92, 0.160, 0.100, 0.105, N),
        ellipse_ring(0, 0.0, 1.06, 0.150, 0.098, 0.100, N),
        ellipse_ring(0, -0.005, 1.22, 0.170, 0.110, 0.105, N),
        ellipse_ring(0, 0.0, 1.36, 0.190, 0.105, 0.100, N),
        ellipse_ring(0, 0.01, 1.43, 0.140, 0.080, 0.085, N),
        ellipse_ring(0, 0.01, 1.50, 0.060, 0.055, 0.060, N),
    ]
    mb.loft(torso, "M_Seeker_Cloth", cap_start=True)

    # 头部：被面罩完全包住
    head = []
    for z, r in ((1.50, 0.055), (1.55, 0.085), (1.62, 0.098), (1.69, 0.095), (1.75, 0.075)):
        head.append(ellipse_ring(0, 0.0, z, r, r * 1.08, r * 1.05, 8))
    hv = mb.loft(head, "M_Seeker_Mask")
    mb.cap_peak(hv[-1], "M_Seeker_Mask", Vector((0, 0.005, 1.785)))

    # 围巾：领口一圈 + 胸前垂下的三角布
    collar = [ellipse_ring(0, 0.0, z, r, r * 1.05, r, 12) for z, r in ((1.46, 0.105), (1.52, 0.088), (1.57, 0.080))]
    mb.loft(collar, "M_Seeker_Scarf")
    drape_top = [Vector((x, -0.095 - 0.02 * (1 - abs(x) / 0.1), 1.49)) for x in (-0.10, -0.05, 0.0, 0.05, 0.10)]
    drape_mid = [Vector((x * 0.75, -0.125, 1.39)) for x in (-0.10, -0.05, 0.0, 0.05, 0.10)]
    drape_tip = [Vector((-0.03, -0.13, 1.31)), Vector((-0.02, -0.135, 1.30)), Vector((0.0, -0.14, 1.27)),
                 Vector((0.025, -0.135, 1.31)), Vector((0.035, -0.13, 1.33))]
    mb.sheet([drape_top, drape_mid, drape_tip], "M_Seeker_Scarf", Vector((0, 0.006, 0)))

    # 腰带 + 扣
    belt = [ellipse_ring(0, 0.0, z, 0.168, 0.109, 0.114, N) for z in (0.90, 0.96)]
    mb.loft(belt, "M_Seeker_Leather")
    mb.box((0.0, -0.112, 0.93), (0.06, 0.012, 0.05), "M_Seeker_Metal", bevel=0.006)

    # 腿：宽松工装裤，膝部稍收
    for side in (-1, 1):
        x = 0.095 * side
        leg = [
            ellipse_ring(x, 0.0, 0.86, 0.100, 0.10, 0.11, N),
            ellipse_ring(x * 1.05, -0.005, 0.70, 0.088, 0.09, 0.095, N),
            ellipse_ring(x * 1.08, -0.005, 0.50, 0.072, 0.075, 0.075, N),
            ellipse_ring(x * 1.10, 0.0, 0.34, 0.076, 0.078, 0.076, N),
            ellipse_ring(x * 1.10, 0.0, 0.22, 0.072, 0.074, 0.072, N),
        ]
        mb.loft(leg, "M_Seeker_Cloth")
        # 大腿侧口袋（左腿）
        if side > 0:
            mb.box((x + 0.085, -0.01, 0.60), (0.03, 0.10, 0.12), "M_Seeker_Cloth", bevel=0.01)
            mb.box((x + 0.087, -0.01, 0.655), (0.034, 0.104, 0.03), "M_Seeker_Leather", bevel=0.008)
        build_boot(mb, x * 1.10)

    # 右腿枪套（角色右侧为 -X）
    hx = -0.17
    mb.box((hx, -0.015, 0.78), (0.05, 0.075, 0.16), "M_Seeker_Leather", bevel=0.012)
    mb.box((hx - 0.005, -0.015, 0.875), (0.04, 0.05, 0.05), "M_Seeker_Mask", bevel=0.01)  # 枪柄
    strap = [ellipse_ring(-0.1045, 0.0, z, 0.098, 0.102, 0.108, N) for z in (0.70, 0.725)]
    mb.loft(strap, "M_Seeker_Leather")

    # 手臂与手套（A 字姿势，约 14° 外展）
    for side in (-1, 1):
        build_arm(mb, side)

    return mb.build(mats)


def build_boot(mb, x):
    N = 10
    # 鞋底向前延伸（-Y），鞋头略方
    rings = [
        ellipse_ring(x, -0.04, 0.000, 0.062, 0.150, 0.085, N),
        ellipse_ring(x, -0.04, 0.035, 0.064, 0.155, 0.088, N),
        ellipse_ring(x, -0.03, 0.040, 0.060, 0.145, 0.082, N),
        ellipse_ring(x, -0.02, 0.095, 0.058, 0.120, 0.075, N),
        ellipse_ring(x, 0.00, 0.150, 0.068, 0.075, 0.072, N),
        ellipse_ring(x, 0.00, 0.255, 0.080, 0.082, 0.080, N),
    ]
    mats = ["M_Seeker_Mask", "M_Seeker_Mask", "M_Seeker_Leather", "M_Seeker_Leather", "M_Seeker_Leather"]
    mb.loft(rings, "M_Seeker_Leather", cap_start=True, mats_per_band=mats)
    # 靴扣带
    for z in (0.17, 0.22):
        band = [ellipse_ring(x, 0.0, zz, 0.072 + (zz - 0.15) * 0.1, 0.080, 0.078, N) for zz in (z, z + 0.018)]
        mb.loft(band, "M_Seeker_Leather")
        mb.box((x + (0.07 if x > 0 else -0.07), -0.02, z + 0.009), (0.012, 0.025, 0.022), "M_Seeker_Metal", bevel=0.004)


def build_arm(mb, side):
    shoulder = Vector((0.205 * side, 0.005, 1.38))
    elbow = Vector((0.285 * side, 0.0, 1.13))
    wrist = Vector((0.345 * side, -0.03, 0.89))
    upper = elbow - shoulder
    lower = wrist - elbow
    n = 8
    rings = [
        axis_ring(shoulder + upper * 0.0, upper, 0.068, 0.072, n),
        axis_ring(shoulder + upper * 0.4, upper, 0.060, 0.063, n),
        axis_ring(elbow, (upper + lower), 0.052, 0.055, n),
        axis_ring(elbow + lower * 0.45, lower, 0.050, 0.052, n),
        axis_ring(elbow + lower * 0.85, lower, 0.044, 0.046, n),
    ]
    mb.loft(rings, "M_Seeker_Cloth", cap_start=True)
    # 卷起的袖口
    cuff = [axis_ring(elbow + lower * t, lower, 0.055, 0.057, n) for t in (0.62, 0.72)]
    mb.loft(cuff, "M_Seeker_Cloth")

    # 手套：护腕 + 手掌 + 并拢四指 + 拇指
    d = lower.normalized()
    glove = [
        axis_ring(wrist - d * 0.06, d, 0.050, 0.052, n),
        axis_ring(wrist, d, 0.048, 0.050, n),
        axis_ring(wrist + d * 0.02, d, 0.040, 0.034, n),
        axis_ring(wrist + d * 0.075, d, 0.045, 0.024, n, forward=Vector((0, -1, 0))),
        axis_ring(wrist + d * 0.115, d, 0.043, 0.022, n),
    ]
    mb.loft(glove, "M_Seeker_Leather", mats_per_band=["M_Seeker_Leather", "M_Seeker_Mask", "M_Seeker_Mask", "M_Seeker_Mask"])
    fingers = [
        axis_ring(wrist + d * 0.115, d, 0.042, 0.021, n),
        axis_ring(wrist + d * 0.15, d, 0.038, 0.019, n),
        axis_ring(wrist + d * 0.17, d, 0.026, 0.014, n),
    ]
    fv = mb.loft(fingers, "M_Seeker_Skin", mats_per_band=["M_Seeker_Skin", "M_Seeker_Skin"])
    mb._cap(fv[-1], "M_Seeker_Skin", flip=False)
    tb = wrist + d * 0.05 + Vector((0, -0.03, 0))
    thumb_dir = (d + Vector((0, -0.8, 0))).normalized()
    th = [axis_ring(tb + thumb_dir * t, thumb_dir, r, r, 6) for t, r in ((0.0, 0.016), (0.035, 0.014), (0.06, 0.010))]
    tv = mb.loft(th, "M_Seeker_Mask")
    mb._cap(tv[-1], "M_Seeker_Skin", flip=False)


# ---------- 斗篷（含兜帽） ----------

def build_cloak(mats):
    mb = MeshBuilder("Seeker_Cloak")
    SEG = 28  # 前开口斗篷的环向分段（偶数，左右对称）

    # (z, 中心y, 半宽, 前半径, 后半径, 前开口半角°)
    profile = [
        (1.52, 0.012, 0.115, 0.110, 0.115, 16),
        (1.47, 0.012, 0.215, 0.125, 0.140, 18),
        (1.40, 0.018, 0.300, 0.165, 0.180, 22),
        (1.28, 0.025, 0.355, 0.205, 0.215, 30),
        (1.08, 0.030, 0.395, 0.250, 0.245, 52),
        (0.86, 0.035, 0.425, 0.285, 0.270, 66),
        (0.64, 0.040, 0.450, 0.305, 0.290, 70),
    ]
    rings = []
    for z, cy, rx, rf, rb, half in profile:
        a = math.radians(half)
        start = -math.pi / 2 + a          # 前中线右侧开口边
        end = start + TAU - 2 * a         # 绕过背部回到左侧开口边
        rings.append(ellipse_ring(0, cy, z, rx, rf, rb, SEG, start, end, closed=False, jitter=0.006))

    # 破损锯齿下摆：前侧两角更长，背中略长
    z_hem, cy, rx, rf, rb, half = 0.47, 0.045, 0.465, 0.315, 0.300, 72
    a = math.radians(half)
    start = -math.pi / 2 + a
    hem = ellipse_ring(0, cy, z_hem, rx, rf, rb, SEG, start, start + TAU - 2 * a, closed=False)
    for i, p in enumerate(hem):
        t = i / SEG
        corner = max(0.0, 1.0 - min(t, 1 - t) / 0.12)    # 开口两角
        back = math.exp(-((t - 0.5) / 0.12) ** 2)       # 背中
        tooth = -0.06 if i % 2 else 0.0
        p.z += -0.08 * corner - 0.04 * back + tooth + random.uniform(-0.02, 0.02)
    rings.append(hem)

    bands = ["M_Seeker_Cloak"] * (len(rings) - 2) + ["M_Seeker_CloakDirt"]
    vr = mb.loft(rings, "M_Seeker_Cloak", closed=False, mats_per_band=bands)

    # 补丁：按面索引挑几块换材质（右肩前、左下摆、背部）
    mb.bm.faces.ensure_lookup_table()
    patch = mb.mat("M_Seeker_CloakPatch")
    for band, i in ((2, 3), (4, 22), (4, 23), (3, 12), (4, 15), (5, 16), (5, 5)):
        mb.bm.faces[band * SEG + i].material_index = patch

    # 兜帽：领口向上，前开口，帽檐前伸遮脸
    hood_profile = [
        # (z, 中心y, 半宽, 前半径, 后半径, 前开口半角°)
        (1.50, 0.020, 0.120, 0.120, 0.125, 34),
        (1.58, 0.025, 0.135, 0.135, 0.150, 44),
        (1.67, 0.028, 0.140, 0.140, 0.160, 50),
        (1.75, 0.035, 0.120, 0.120, 0.150, 52),
        (1.81, 0.050, 0.075, 0.085, 0.110, 54),
    ]
    HSEG = 16
    hood = []
    for z, cy, rx, rf, rb, half in hood_profile:
        a = math.radians(half)
        start = -math.pi / 2 + a
        hood.append(ellipse_ring(0, cy, z, rx, rf, rb, HSEG, start, start + TAU - 2 * a, closed=False, jitter=0.004))
    # 帽檐两侧向前、向下压，形成遮挡面部的阴影口
    for r_i, ring in enumerate(hood[1:4], start=1):
        for k in (0, HSEG):
            ring[k].y -= 0.03 + 0.01 * r_i
            ring[k].z -= 0.01
    hv = mb.loft(hood, "M_Seeker_Cloak", closed=False)
    # 帽尖：最后一环收束到后上方的尖顶
    peak = mb.bm.verts.new(Vector((0, 0.095, 1.86)))
    last = hv[-1]
    m = mb.mat("M_Seeker_Cloak")
    for i in range(len(last) - 1):
        f = mb.bm.faces.new((last[i], last[i + 1], peak))
        f.material_index = m

    # 斗篷/兜帽是开放面片：逐面让法线朝外，加厚和内壁暗色才会落在正确一侧
    for f in mb.bm.faces:
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y - 0.03, 0.0))) < 0:
            f.normal_flip()

    # 肩部扣具（角色右肩，-X）
    mb.box((-0.11, -0.13, 1.40), (0.045, 0.015, 0.035), "M_Seeker_Metal", bevel=0.006)
    mb.box((-0.11, -0.124, 1.40), (0.06, 0.012, 0.05), "M_Seeker_Leather", bevel=0.006)
    # 背部补丁标签
    mb.box((0.0, 0.205, 1.36), (0.09, 0.012, 0.05), "M_Seeker_CloakPatch", bevel=0.004)

    obj = mb.build(mats, recalc=False)
    # 加厚：内侧使用暗色材质，读作斗篷内部阴影
    obj.data.materials.append(mats["M_Seeker_CloakInner"])
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = 0.014
    mod.offset = -1.0  # 向法线反方向（身体一侧）加厚，外表面保持斗篷色
    mod.use_even_offset = True
    return obj


def remap_inner_faces(obj, mats):
    """应用加厚后，把背向身体外侧之外的那一层（内壁）改成 CloakInner。"""
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    before = len(obj.data.polygons)
    bpy.ops.object.modifier_apply(modifier="Thickness")
    inner = obj.data.materials.find("M_Seeker_CloakInner")
    me = obj.data
    # Solidify 生成顺序：原面 → 偏移面 → 边缘面
    for p in me.polygons[before:]:
        p.material_index = inner
    obj.select_set(False)


# ---------- 背包 ----------

def rounded_rect(cx, cy, z, sx, sy, b, front_bulge=0.0):
    pts = [
        (cx + sx, cy - sy + b), (cx + sx, cy + sy - b), (cx + sx - b, cy + sy), (cx - sx + b, cy + sy),
        (cx - sx, cy + sy - b), (cx - sx, cy - sy + b), (cx - sx + b, cy - sy - front_bulge), (cx + sx - b, cy - sy - front_bulge),
    ]
    return [Vector((x, y, z)) for x, y in pts]


def build_backpack(mats):
    """背包朝向：贴背面为 -Y（朝角色），外侧为 +Y。原点在背包贴背面中心，便于以后挂到骨骼。"""
    mb = MeshBuilder("Seeker_Backpack")
    W, D = 0.15, 0.085  # 半宽、半厚
    # 主体：上窄下宽、外侧鼓起（外侧在 +Y，用 back 半径方向模拟）
    rings = []
    for z, sx, sy in ((-0.21, 0.150, 0.080), (-0.19, 0.160, 0.088), (0.05, 0.158, 0.092), (0.17, 0.145, 0.085), (0.20, 0.130, 0.07)):
        r = rounded_rect(0, D, z, sx, sy, 0.03)
        r = [Vector((p.x, p.y + (0.012 if p.y > D else 0.0), p.z)) for p in r]
        rings.append(r)
    mb.loft(rings, "M_Seeker_Canvas", cap_start=True, cap_end=True,
            mats_per_band=["M_Seeker_Leather", "M_Seeker_Canvas", "M_Seeker_Canvas", "M_Seeker_Canvas"])

    # 翻盖：从顶部盖到外侧上半
    flap = []
    for z, y, sx in ((0.215, D - 0.04, 0.140), (0.215, D + 0.06, 0.150), (0.17, D + 0.105, 0.162), (0.05, D + 0.112, 0.160), (-0.01, D + 0.108, 0.150)):
        flap.append([Vector((x, y, z)) for x in (-sx, -sx * 0.5, 0.0, sx * 0.5, sx)])
    flap[-1][2].z -= 0.025  # 翻盖下缘中部尖角
    mb.sheet(flap, "M_Seeker_Canvas", Vector((0, -0.008, 0)))

    # 外侧口袋 + 扣带
    mb.box((0.0, D + 0.125, -0.09), (0.21, 0.05, 0.15), "M_Seeker_Canvas", bevel=0.015)
    mb.box((0.0, D + 0.142, -0.04), (0.20, 0.025, 0.05), "M_Seeker_Canvas", bevel=0.01)  # 口袋盖
    for zc, zl in ((0.06, 0.16), (-0.08, 0.10)):
        mb.box((0.0, D + (0.118 if zc > 0 else 0.152), zc), (0.03, 0.01, zl), "M_Seeker_Leather", bevel=0.004)
        mb.box((0.0, D + (0.126 if zc > 0 else 0.160), zc - zl * 0.3), (0.04, 0.008, 0.03), "M_Seeker_Metal", bevel=0.004)

    # 两侧口袋
    for s in (-1, 1):
        mb.box((s * 0.175, D + 0.01, -0.10), (0.05, 0.11, 0.14), "M_Seeker_Canvas", bevel=0.012)
        mb.box((s * 0.178, D + 0.01, -0.035), (0.054, 0.114, 0.025), "M_Seeker_Canvas", bevel=0.008)

    # 提手
    handle = []
    hp = [Vector((-0.05 + 0.10 * i / 6, D - 0.02, 0.205 + 0.045 * math.sin(math.pi * i / 6))) for i in range(7)]
    for i, p in enumerate(hp):
        tangent = hp[min(i + 1, 6)] - hp[max(i - 1, 0)]
        handle.append(axis_ring(p, tangent, 0.008, 0.012, 6, forward=Vector((0, 0, 1))))
    mb.loft(handle, "M_Seeker_Leather")

    # 肩带：背包顶部越过斗篷肩部，沿胸前垂到胸口；下段从背包底部绕到腋下，没入斗篷侧面
    origin = Vector((0.0, 0.25, 1.12))  # 背包在角色上的挂点（与 main() 一致）
    for s in (-1, 1):
        upper = [Vector((s * x, y, z)) - origin for x, y, z in (
            (0.090, 0.250, 1.29), (0.120, 0.200, 1.43), (0.150, 0.090, 1.505), (0.160, -0.040, 1.51),
            (0.170, -0.135, 1.43), (0.185, -0.165, 1.32), (0.195, -0.170, 1.24))]
        lower = [Vector((s * x, y, z)) - origin for x, y, z in (
            (0.110, 0.250, 0.92), (0.260, 0.220, 0.94), (0.360, 0.120, 0.99))]
        for pts, width in ((upper, 0.028), (lower, 0.022)):
            rings = []
            for i, p in enumerate(pts):
                a = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
                # 带子宽面朝外（远离身体中轴）
                out = Vector((p.x, p.y + origin.y, 0.0))
                if out.length < 1e-4:
                    out = Vector((0, 1, 0))
                rings.append(axis_ring(p, a, 0.006, width, 6, forward=out.normalized()))
            mb.loft(rings, "M_Seeker_Leather", cap_start=True, cap_end=True)
        mb.box(Vector((s * 0.19, -0.175, 1.30)) - origin,
               (0.045, 0.01, 0.03), "M_Seeker_Metal", bevel=0.004)

    return mb.build(mats)


# ---------- 渲染 ----------

def setup_render(scene):
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.55, 0.57, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.display.shading.shadow_intensity = 0.35
    scene.display.shading.show_object_outline = True
    scene.render.film_transparent = False
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"


def render_views(scene, folder):
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("RenderCam")
    cam = bpy.data.objects.new("RenderCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    target = Vector((0, 0.05, 0.93))
    views = {
        "front": (Vector((0, -6, 0.93)), True),
        "side": (Vector((-6, 0.05, 0.93)), True),   # 从角色右侧看
        "back": (Vector((0, 6, 0.93)), True),
        "three_quarter": (Vector((-3.2, -4.2, 2.4)), False),
    }
    for name, (pos, ortho) in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        cam_data.type = "ORTHO" if ortho else "PERSP"
        cam_data.ortho_scale = 2.15
        cam_data.lens = 85
        scene.render.filepath = os.path.join(folder, f"Seeker_{name}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ---------- 贴图烘焙 ----------
# 程序化着色（布面斑驳 + 下摆向上渐变的尘土）烘焙成每个部件一张颜色贴图，
# 取代原先一块块纯色材质的硬拼接。

DIRT = (0.26, 0.19, 0.14)
# 材质名 → (尘土渐变起点 z, 完全变脏的 z, 尘土强度, 斑驳幅度)
WEAR = {
    "M_Seeker_Cloak": (0.95, 0.42, 0.95, 0.26),
    "M_Seeker_CloakInner": (0.90, 0.40, 0.60, 0.18),
    "M_Seeker_CloakPatch": (0.95, 0.42, 0.95, 0.18),
    "M_Seeker_CloakDirt": (0.95, 0.42, 0.95, 0.26),
    "M_Seeker_Cloth": (0.55, 0.12, 0.60, 0.18),
    "M_Seeker_Leather": (0.28, 0.00, 0.55, 0.24),
    "M_Seeker_Canvas": (0.05, -0.25, 0.55, 0.24),
    "M_Seeker_Scarf": (9.0, 8.0, 0.0, 0.14),
    "M_Seeker_Mask": (9.0, 8.0, 0.0, 0.10),
    "M_Seeker_Metal": (9.0, 8.0, 0.0, 0.10),
    "M_Seeker_Skin": (9.0, 8.0, 0.0, 0.05),
}
TEX_SIZE = {"Seeker_Body": 1024, "Seeker_Cloak": 1024, "Seeker_Backpack": 512}


def lin(rgb):
    return tuple(srgb_to_linear(c) for c in rgb)


def make_wear_shader(mat):
    rgb = PALETTE[mat.name][0]
    if mat.name == "M_Seeker_CloakDirt":
        rgb = PALETTE["M_Seeker_Cloak"][0]  # 下摆不再单独上色，交给渐变
    top, bottom, strength, mottle = WEAR[mat.name]
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    coord = nt.nodes.new("ShaderNodeTexCoord")

    def noise(scale, detail):
        n = nt.nodes.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        nt.links.new(coord.outputs["Object"], n.inputs["Vector"])
        return n

    def mix(a, b, fac):
        m = nt.nodes.new("ShaderNodeMix")
        m.data_type = "RGBA"
        for value, socket in ((a, m.inputs[6]), (b, m.inputs[7]), (fac, m.inputs[0])):
            if isinstance(value, tuple):
                socket.default_value = (*value, 1.0) if len(value) == 3 else value
            elif isinstance(value, float):
                socket.default_value = value
            else:
                nt.links.new(value, socket)
        return m.outputs[2]

    base = lin(rgb)
    dark = tuple(c * (1 - mottle) for c in base)
    light = tuple(min(1.0, c * (1 + mottle)) for c in base)
    coarse = noise(3.5, 4.0)
    fine = noise(45.0, 2.0)
    col = mix(dark, light, coarse.outputs["Fac"])
    col = mix(col, tuple(c * 0.9 for c in base), fine.outputs["Fac"])

    # 尘土：按高度渐变，用噪声扰动边界，避免一条直线
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(coord.outputs["Object"], sep.inputs["Vector"])
    wobble = nt.nodes.new("ShaderNodeMath")
    wobble.operation = "MULTIPLY_ADD"
    nt.links.new(coarse.outputs["Fac"], wobble.inputs[0])
    wobble.inputs[1].default_value = 0.25
    nt.links.new(sep.outputs["Z"], wobble.inputs[2])
    ramp = nt.nodes.new("ShaderNodeMapRange")
    ramp.interpolation_type = "SMOOTHSTEP"
    nt.links.new(wobble.outputs[0], ramp.inputs["Value"])
    ramp.inputs["From Min"].default_value = top + 0.12
    ramp.inputs["From Max"].default_value = bottom + 0.12
    ramp.inputs["To Max"].default_value = strength
    col = mix(col, lin(DIRT), ramp.outputs["Result"])
    nt.links.new(col, bsdf.inputs["Base Color"])
    return nt


def bake_object(obj, folder):
    scene = bpy.context.scene
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode="OBJECT")

    size = TEX_SIZE[obj.name]
    name = "T_" + obj.name.replace("Seeker_", "Seeker_")
    img = bpy.data.images.new(name, size, size)
    for mat in obj.data.materials:
        nt = mat.node_tree
        for n in [n for n in nt.nodes if n.type == "TEX_IMAGE"]:
            nt.nodes.remove(n)
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        nt.nodes.active = node

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, margin=6, use_clear=True)

    os.makedirs(folder, exist_ok=True)
    img.filepath_raw = os.path.join(folder, name + ".png")
    img.file_format = "PNG"
    img.save()

    # 换成单一贴图材质
    baked_name = "M_" + obj.name
    if baked_name in bpy.data.materials:  # 斗篷的源材质同名，先让位，保证导出名与 Unity 重映射一致
        bpy.data.materials[baked_name].name = baked_name + "_Src"
    baked = bpy.data.materials.new(baked_name)
    baked.use_nodes = True
    bsdf = baked.node_tree.nodes.get("Principled BSDF")
    tex = baked.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    baked.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    obj.data.materials.clear()
    obj.data.materials.append(baked)
    for p in obj.data.polygons:
        p.material_index = 0

    # 柔和明暗：35° 以内的转折平滑，大转折和斗篷厚度边保持硬边
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    obj.select_set(False)


def bake_all(objs, folder):
    for mat in bpy.data.materials:
        if mat.name in WEAR:
            make_wear_shader(mat)
    for obj in objs:
        bake_object(obj, folder)


def render_poses(scene, rig, folder):
    """关键姿势检查图：跑步、射击、换弹、翻滚各取几帧，正面 + 侧面。"""
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("PoseCam")
    cam = bpy.data.objects.new("PoseCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.3
    target = Vector((0, 0.0, 0.85))
    shots = [("Idle", 0), ("Run_F", 5), ("Run_F", 15), ("Run_L", 5), ("Fire", 1), ("Reload", 12),
             ("Roll", 4), ("Roll", 8), ("Roll", 12)]
    for action, frame in shots:
        rig.animation_data.action = bpy.data.actions[action]
        scene.frame_set(frame)
        for view, pos in (("front", Vector((0, -6, 0.85))), ("side", Vector((-6, 0, 0.85)))):
            cam.location = pos
            cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(folder, f"{action}_{frame:02d}_{view}.png")
            bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ---------- 主流程 ----------

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    mats = make_materials()

    root = bpy.data.objects.new("Seeker", None)
    scene.collection.objects.link(root)

    body = build_body(mats)
    cloak = build_cloak(mats)
    remap_inner_faces(cloak, mats)
    pack = build_backpack(mats)
    pack.location = (0.0, 0.25, 1.12)   # 背在斗篷外侧（主创待确认的佩戴方案）

    for obj in (body, cloak, pack):
        obj.parent = root

    bake_all((body, cloak, pack), TEX_DIR)
    bpy.context.view_layer.update()
    rig, pistol = seeker_rig.setup(root, body, cloak, pack)

    setup_render(scene)
    render_views(scene, os.path.join(OUT_DIR, "renders"))
    render_poses(scene, rig, os.path.join(OUT_DIR, "renders", "poses"))
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(0)

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "Seeker.blend"))

    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=False,
        object_types={"EMPTY", "MESH", "ARMATURE"},
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",  # 导出烘焙后的法线（按角度平滑）
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
    )

    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (body, cloak, pack, pistol))
    print(f"[Seeker] triangles={tris} fbx={FBX_PATH}")


main()
