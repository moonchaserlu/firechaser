"""失火者（Firelost）基础近战怪物低多边形模型生成脚本：模型、烘焙材质，并调用 firelost_rig.py 生成骨骼、蒙皮和动作。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python firelost_build.py -- <输出目录> <FBX 路径>

产物：
    <输出目录>/Firelost.blend       可继续手工编辑的源文件
    <输出目录>/renders/*.png        正/侧/背/四分之三视图
    <输出目录>/renders/poses/*.png  关键姿势检查图
    <FBX 路径>                      Unity 用模型（Firelost_Body / Firelost_Clothes / Firelost_Lantern 三个蒙皮网格 + Firelost_Rig 与动作）
    <FBX 目录>/Textures/*.png       每个部件一张烘焙颜色贴图

坐标约定：Blender Z 向上，角色面朝 -Y，脚底在原点，角色右侧为 -X。
弯腰驼背姿态下头顶约 1.72 m（直立身高约 1.9 m）。
统一设定（参考图左右不一致处）：旧灯挂在角色自身右侧腰间（-X），左脚（+X）鞋头破损露趾；
肩部角质是身体的一部分（Body 网格），不是护甲。没有角、尾巴，眼睛不发光。
"""

import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import firelost_rig  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(ARGS[0] if len(ARGS) > 0 else os.path.dirname(__file__))
FBX_PATH = os.path.abspath(ARGS[1] if len(ARGS) > 1 else os.path.join(OUT_DIR, "Firelost.fbx"))
TEX_DIR = os.path.join(os.path.dirname(FBX_PATH), "Textures")  # 贴图直接输出到 Unity 资源目录

random.seed(11)
TAU = math.pi * 2
X = Vector((1, 0, 0))
FWD = Vector((0, -1, 0))

# ---------- 材质 ----------
# 名称 → (sRGB, 粗糙度, 金属度)。烘焙后每个部件合成一张贴图，这里的分色只是烘焙源。

PALETTE = {
    "M_Firelost_Skin": ((0.56, 0.47, 0.43), 0.85, 0.0),
    "M_Firelost_SkinDark": ((0.30, 0.23, 0.21), 0.85, 0.0),
    "M_Firelost_Eye": ((0.10, 0.08, 0.08), 0.6, 0.0),
    "M_Firelost_Teeth": ((0.58, 0.52, 0.36), 0.6, 0.0),
    "M_Firelost_Hair": ((0.17, 0.14, 0.12), 0.9, 0.0),
    "M_Firelost_Callus": ((0.26, 0.25, 0.25), 0.95, 0.0),
    "M_Firelost_Claw": ((0.13, 0.11, 0.10), 0.6, 0.0),
    "M_Firelost_Vest": ((0.53, 0.44, 0.30), 0.95, 0.0),
    "M_Firelost_VestInner": ((0.30, 0.25, 0.18), 0.95, 0.0),
    "M_Firelost_Pants": ((0.28, 0.26, 0.23), 0.95, 0.0),
    "M_Firelost_Leather": ((0.34, 0.25, 0.18), 0.8, 0.0),
    "M_Firelost_Sole": ((0.16, 0.14, 0.12), 0.9, 0.0),
    "M_Firelost_Belt": ((0.21, 0.17, 0.14), 0.8, 0.0),
    "M_Firelost_Buckle": ((0.46, 0.44, 0.41), 0.5, 0.0),
    "M_Firelost_LanternMetal": ((0.25, 0.23, 0.22), 0.7, 0.0),
    "M_Firelost_LanternGlass": ((0.44, 0.44, 0.42), 0.4, 0.0),
}


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def lin(rgb):
    return tuple(srgb_to_linear(c) for c in rgb)


def make_materials():
    mats = {}
    for name, (rgb, rough, metal) in PALETTE.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*lin(rgb), 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        mat.diffuse_color = (*lin(rgb), 1.0)
        mats[name] = mat
    return mats


# ---------- 网格工具 ----------

def jit(amount):
    return Vector((random.uniform(-amount, amount), random.uniform(-amount, amount), random.uniform(-amount, amount)))


def ellipse_ring(cx, cy, z, rx, ry_front, ry_back, n, jitter=0.0):
    """水平截面环。角度从 +X 起；-Y 为正前方，前后半径可不同。"""
    pts = []
    for i in range(n):
        t = TAU * i / n
        s, c = math.sin(t), math.cos(t)
        ry = ry_back if s > 0 else ry_front
        p = Vector((cx + rx * c, cy + ry * s, z))
        if jitter:
            p += jit(jitter)
        pts.append(p)
    return pts


def spine_frame(t):
    """沿脊柱切线的截面坐标系：s 指向角色左侧（+X），f 指向截面上的前方。"""
    t = t.normalized()
    s = (X - t * X.dot(t)).normalized()
    f = s.cross(t).normalized()
    return s, f


def spine_ring(c, t, rx, rf, rb, n, a0=0.0, a1=TAU, closed=True, jitter=0.0):
    """垂直于脊柱切线的椭圆环；角度 π/2 为正前方，前后半径可不同。"""
    s, f = spine_frame(t)
    pts = []
    for i in range(n if closed else n + 1):
        a = a0 + (a1 - a0) * i / n
        ca, sa = math.cos(a), math.sin(a)
        p = c + s * (rx * ca) + f * ((rf if sa > 0 else rb) * sa)
        if jitter:
            p += jit(jitter)
        pts.append(p)
    return pts


def axis_ring(center, axis, ru, rv, n, forward=FWD):
    """沿任意轴的截面环（四肢、手指）。u 指向 forward 在截面上的投影。"""
    axis = axis.normalized()
    u = forward - axis * forward.dot(axis)
    if u.length < 1e-4:
        u = axis.orthogonal()
    u.normalize()
    v = axis.cross(u).normalized()
    return [center + u * (ru * math.cos(TAU * i / n)) + v * (rv * math.sin(TAU * i / n)) for i in range(n)]


def tangents(pts):
    return [pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)] for i in range(len(pts))]


class MeshBuilder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mat_names = []

    def mat(self, name):
        if name not in self.mat_names:
            self.mat_names.append(name)
        return self.mat_names.index(name)

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
            self.cap(vr[0], mat)
        if cap_end:
            self.cap(vr[-1], mat)
        return vr

    def cap(self, ring, mat, peak=None):
        m = self.mat(mat)
        centre = peak if peak is not None else sum((v.co for v in ring), Vector()) / len(ring)
        c = self.bm.verts.new(centre)
        for i in range(len(ring)):
            f = self.bm.faces.new((ring[i], ring[(i + 1) % len(ring)], c))
            f.material_index = m

    def tube(self, pts, radii, mat, n=8, forward=FWD, cap_start=False, cap_end=False, peak=None, jitter=0.0, mats_per_band=None):
        """沿折线的管子；radii 每项为半径或 (前后半径, 左右半径)。peak 给定时末端收成尖。"""
        rings = []
        for p, t, r in zip(pts, tangents(pts), radii):
            ru, rv = (r, r) if isinstance(r, (int, float)) else r
            ring = axis_ring(p, t, ru, rv, n, forward)
            if jitter:
                ring = [q + jit(jitter) for q in ring]
            rings.append(ring)
        vr = self.loft(rings, mat, cap_start=cap_start, cap_end=cap_end and peak is None, mats_per_band=mats_per_band)
        if peak is not None:
            self.cap(vr[-1], mat, peak=peak)
        return vr

    def cone(self, base, direction, radius, length, mat, n=5, forward=FWD):
        ring = axis_ring(base, direction, radius, radius, n, forward)
        vr = self.loft([ring], mat)
        self.cap(vr[0], mat)
        self.cap(vr[0], mat, peak=base + direction.normalized() * length)

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

    def plate(self, c, normal, size, mat, down=Vector((0, 0, -1)), height=0.38):
        """角质片：尖端朝下的菱形低棱锥，层层相叠读作粗糙角质。"""
        n = normal.normalized()
        d = down - n * down.dot(n)
        if d.length < 1e-3:
            d = n.orthogonal()
        d.normalize()
        s = n.cross(d).normalized()
        size *= random.uniform(0.8, 1.2)
        base = [c - d * size * 0.55, c + s * size * 0.6, c + d * size * 1.25, c - s * size * 0.6]
        base = [p + jit(size * 0.08) for p in base]
        apex = c + n * size * height + d * size * 0.3
        verts = [self.bm.verts.new(p) for p in base]
        a = self.bm.verts.new(apex)
        m = self.mat(mat)
        for i in range(4):
            self.bm.faces.new((verts[i], verts[(i + 1) % 4], a)).material_index = m
        self.bm.faces.new(list(reversed(verts))).material_index = m

    def build(self, mats, recalc=True):
        if recalc:
            bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for n in self.mat_names:
            mesh.materials.append(mats[n])
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj


# ---------- 姿态（弯腰驼背、头前探、双臂垂到膝侧） ----------

# 躯干截面：(中心, 半宽, 前半径, 后半径)。中心沿脊柱前倾，上背圆鼓成驼背。
TORSO = [
    (Vector((0, 0.030, 0.92)), 0.165, 0.110, 0.120),
    (Vector((0, 0.030, 1.05)), 0.160, 0.110, 0.115),
    (Vector((0, 0.005, 1.19)), 0.190, 0.130, 0.165),
    (Vector((0, -0.050, 1.33)), 0.235, 0.125, 0.205),
    (Vector((0, -0.130, 1.42)), 0.225, 0.085, 0.125),
    (Vector((0, -0.205, 1.470)), 0.100, 0.052, 0.060),
    (Vector((0, -0.265, 1.480)), 0.056, 0.048, 0.052),
]
SHOULDER = Vector((0.250, -0.120, 1.39))
ELBOW = Vector((0.360, -0.130, 1.02))
WRIST = Vector((0.345, -0.240, 0.64))
HIP = Vector((0.110, 0.030, 0.88))
KNEE = Vector((0.150, -0.085, 0.50))
ANKLE = Vector((0.165, 0.020, 0.17))
HEAD_OFFSET = Vector((0, 0.045, -0.03))  # 头整体相对建模位置的平移：贴近驼背，脖子更短
LANTERN_HOOK = Vector((-0.200, -0.050, 0.975))  # 角色右侧腰带上的挂点


def side_vec(v, side):
    return Vector((v.x * side, v.y, v.z))


# ---------- 身体（皮肤、头、手、角质、左脚露出的脚趾） ----------

def build_body(mats):
    mb = MeshBuilder("Firelost_Body")
    # 躯干（大部分被背心遮住，敞开的领口露出胸口）
    rings = [spine_ring(c, t, rx, rf, rb, 10, jitter=0.004)
             for (c, rx, rf, rb), t in zip(TORSO, tangents([c for c, *_ in TORSO]))]
    mb.loft(rings, "M_Firelost_Skin", cap_start=True)

    before = set(mb.bm.verts)
    build_head(mb)
    for v in mb.bm.verts:
        if v not in before:
            v.co += HEAD_OFFSET
    for side in (-1, 1):
        build_arm(mb, side)
        build_shoulder_callus(mb, side)
    build_toes(mb, side=1)
    return mb.build(mats)


def build_head(mb):
    # 由下颌到头顶的水平截面：(z, 中心y, 半宽, 前半径, 后半径)
    profile = [
        (1.385, -0.455, 0.048, 0.030, 0.030),
        (1.415, -0.435, 0.074, 0.056, 0.055),
        (1.465, -0.415, 0.080, 0.070, 0.075),
        (1.520, -0.405, 0.083, 0.078, 0.088),
        (1.575, -0.400, 0.088, 0.083, 0.095),
        (1.625, -0.393, 0.087, 0.078, 0.096),
        (1.670, -0.385, 0.073, 0.060, 0.090),
        (1.705, -0.378, 0.046, 0.038, 0.062),
    ]
    rings = [ellipse_ring(0, cy, z, rx, rf, rb, 10, jitter=0.004) for z, cy, rx, rf, rb in profile]
    bands = ["M_Firelost_Skin"] * 5 + ["M_Firelost_Hair"] * 2
    hv = mb.loft(rings, "M_Firelost_Skin", mats_per_band=bands)
    mb.cap(hv[0], "M_Firelost_Skin", peak=Vector((0, -0.46, 1.372)))
    mb.cap(hv[-1], "M_Firelost_Hair", peak=Vector((0, -0.375, 1.722)))

    # 五官：眉骨、凹陷的暗色眼窝（不发光）、鼻、咧开的嘴和黄牙、尖耳
    for s in (-1, 1):  # 眉骨：两段向外下斜，压出阴沉的眼神
        mb.tube([Vector((0.008 * s, -0.488, 1.612)), Vector((0.045 * s, -0.486, 1.606)), Vector((0.078 * s, -0.465, 1.592))],
                [(0.014, 0.012), (0.014, 0.013), (0.009, 0.009)], "M_Firelost_Skin", n=5, cap_start=True, cap_end=True)
        mb.box((0.037 * s, -0.479, 1.583), (0.036, 0.012, 0.022), "M_Firelost_Eye", bevel=0.004)
        mb.box((0.060 * s, -0.465, 1.530), (0.030, 0.030, 0.045), "M_Firelost_Skin", bevel=0.008)  # 颧骨
        mb.box((0.062 * s, -0.445, 1.440), (0.024, 0.040, 0.050), "M_Firelost_Skin", bevel=0.008)  # 下颌角
        ear = [Vector((0.084 * s, -0.392, 1.51)), Vector((0.096 * s, -0.382, 1.545)), Vector((0.104 * s, -0.368, 1.585))]
        mb.tube(ear, [(0.022, 0.009), (0.020, 0.008), (0.009, 0.005)], "M_Firelost_Skin", n=5, cap_start=True, cap_end=True)
    mb.tube([Vector((0, -0.486, 1.595)), Vector((0, -0.510, 1.540)), Vector((0, -0.503, 1.518))],
            [0.012, 0.019, 0.013], "M_Firelost_Skin", n=5, cap_start=True, cap_end=True)
    mb.box((0, -0.483, 1.462), (0.075, 0.014, 0.024), "M_Firelost_SkinDark", bevel=0.005)
    for x in (-0.026, -0.009, 0.008, 0.026):
        mb.box((x, -0.490, 1.466 + random.uniform(-0.004, 0.004)), (0.012, 0.006, 0.014), "M_Firelost_Teeth", bevel=0.002)

    # 稀疏蓬乱的头发：头顶往后翘的尖簇
    # 头顶到后脑：贴头皮的乱发尖簇，额前稀疏
    centre = Vector((0, -0.385, 1.60))
    for i in range(22):
        a = random.uniform(-1.25, 1.25)                 # 左右
        b = random.uniform(-0.15, 1.15)                 # 0 头顶 → 1 后脑
        out = Vector((math.sin(a) * 0.8, 0.35 + b * 0.9, 1.0 - b * 0.55)).normalized()
        base = centre + Vector((out.x * 0.085, out.y * 0.095, out.z * 0.105))
        d = (out + Vector((0, 0.7, 0.25)) + jit(0.25)).normalized()
        mb.cone(base, d, random.uniform(0.020, 0.032), random.uniform(0.05, 0.10), "M_Firelost_Hair", n=4)


def build_arm(mb, side):
    s, e, w = side_vec(SHOULDER, side), side_vec(ELBOW, side), side_vec(WRIST, side)
    upper, lower = e - s, w - e
    pts = [s, s + upper * 0.35, s + upper * 0.75, e, e + lower * 0.3, e + lower * 0.65, w]
    radii = [0.094, 0.078, 0.065, 0.063, (0.084, 0.074), (0.064, 0.056), (0.042, 0.046)]
    mb.tube(pts, radii, "M_Firelost_Skin", n=8, cap_start=True, jitter=0.004)

    # 前臂外侧角质
    o = Vector((side, 0, 0))
    a = lower.normalized()
    for t in (-0.04, 0.05, 0.14, 0.23, 0.32, 0.41, 0.50):
        r = 0.08 - 0.03 * max(t, 0.0)
        for phi in range(-60, 150, 30):
            if (t > 0.35 and (phi < -20 or phi > 100)) or random.random() < 0.12:
                continue
            ph = math.radians(phi + random.uniform(-12, 12))
            back = Vector((0, 1, 0))
            dir_ = (o - a * o.dot(a)).normalized() * math.cos(ph) + (back - a * back.dot(a)).normalized() * math.sin(ph)
            mb.plate(e + a * (t * lower.length) + dir_ * r * 0.88, dir_, 0.044, "M_Firelost_Callus", down=-a, height=0.24)

    build_hand(mb, side, e, w)


def build_hand(mb, side, e, w):
    d = (w - e).normalized()
    inward = Vector((-side, 0, 0))
    # 手掌朝向大腿：宽度沿前后方向，厚度沿左右方向
    palm = [w - d * 0.01, w + d * 0.045, w + d * 0.09]
    mb.tube(palm, [(0.048, 0.026), (0.055, 0.026), (0.052, 0.021)], "M_Firelost_Skin", n=6, cap_start=True)
    u = (FWD - d * FWD.dot(d)).normalized()
    root = w + d * 0.085
    for k, (off, length) in enumerate(((0.040, 0.130), (0.014, 0.160), (-0.013, 0.155), (-0.038, 0.120))):
        q0 = root + u * off
        q1 = q0 + d * length * 0.42
        q2 = q1 + (d * 0.75 + inward * 0.45).normalized() * length * 0.34
        q3 = q2 + (d * 0.35 + inward * 0.85).normalized() * length * 0.24
        mb.tube([q0, q1, q2, q3], [0.0145, 0.0125, 0.0105, 0.0085], "M_Firelost_Skin", n=5, cap_start=True)
        mb.cone(q3, q3 - q2, 0.0092, 0.038, "M_Firelost_Claw", n=4)  # 短爪
    # 拇指：从掌根前侧伸向前下
    t0 = w + d * 0.03 + u * 0.035
    td = (d * 0.6 + FWD * 0.6 + inward * 0.25).normalized()
    t1 = t0 + td * 0.045
    t2 = t1 + (td + inward * 0.6).normalized() * 0.035
    mb.tube([t0, t1, t2], [0.017, 0.014, 0.010], "M_Firelost_Skin", n=5, cap_start=True, forward=inward)
    mb.cone(t2, t2 - t1, 0.008, 0.025, "M_Firelost_Claw", n=4, forward=inward)


def build_shoulder_callus(mb, side):
    """肩、上臂外侧和斜方肌上的层叠角质。"""
    s = side_vec(SHOULDER, side)
    out = Vector((side, 0, 0))
    rows = [(72, range(-40, 140, 30), 0.088), (48, range(-55, 150, 26), 0.096),
            (22, range(-55, 140, 26), 0.098), (-5, range(-45, 110, 28), 0.092), (-32, range(-30, 90, 30), 0.084),
            (-55, range(-10, 70, 32), 0.078)]
    for elev, azs, r in rows:
        for az in azs:
            e_, a_ = math.radians(elev + random.uniform(-6, 6)), math.radians(az + random.uniform(-8, 8))
            dir_ = (out * math.cos(e_) * math.cos(a_) + Vector((0, 1, 0)) * math.cos(e_) * math.sin(a_)
                    + Vector((0, 0, 1)) * math.sin(e_)).normalized()
            mb.plate(s + dir_ * r, dir_, 0.064, "M_Firelost_Callus", down=Vector((0, 0.25, -1)))
    # 斜方肌 / 肩胛上沿，向脖子方向收
    for k in range(6):
        t = k / 5
        p = s.lerp(Vector((0.07 * side, -0.17, 1.52)), t) + Vector((0, 0.06 + 0.03 * math.sin(t * math.pi), 0.07))
        n = Vector((side * 0.35, 0.55, 0.75)).normalized()
        mb.plate(p + jit(0.01), n, 0.058 - 0.012 * t, "M_Firelost_Callus", down=Vector((side, 0.3, -1)))


def build_toes(mb, side):
    """左脚靴头破损，五个脚趾露在鞋底上。"""
    x = ANKLE.x * side
    for k, (dx, length, r) in enumerate(((-0.040, 0.050, 0.018), (-0.016, 0.044, 0.014), (0.006, 0.040, 0.013),
                                         (0.026, 0.036, 0.012), (0.044, 0.030, 0.011))):
        base = Vector((x + dx * side, -0.140, 0.068))
        tip = base + Vector((dx * 0.15 * side, -length, -0.012))
        mb.tube([base, base.lerp(tip, 0.6), tip], [r, r * 0.95, r * 0.75], "M_Firelost_Skin", n=5, cap_start=True, cap_end=True)
        mb.box(tuple(tip + Vector((0, 0.002, 0.009))), (r * 1.1, r * 0.8, 0.004), "M_Firelost_Claw", bevel=0.002)
    # 脚背从破口里露出来
    mb.tube([Vector((x, -0.05, 0.11)), Vector((x, -0.10, 0.09)), Vector((x, -0.140, 0.072))],
            [(0.04, 0.05), (0.035, 0.05), (0.025, 0.052)], "M_Firelost_Skin", n=7, cap_end=True)


# ---------- 服装（残破工装背心、腰带、旧裤子、靴子） ----------

def build_clothes(mats):
    mb = MeshBuilder("Firelost_Clothes")
    build_pants(mb)
    build_belt(mb)
    for side in (-1, 1):
        build_boot(mb, side, broken=side > 0)
    pants = mb.build(mats)

    vest = build_vest(mats)
    join([pants, vest], "Firelost_Clothes")
    return pants


def build_pants(mb):
    # 胯部
    hips = [ellipse_ring(0, 0.03, z, rx, rf, rb, 12, jitter=0.003)
            for z, rx, rf, rb in ((1.035, 0.180, 0.122, 0.132), (0.94, 0.205, 0.132, 0.142), (0.84, 0.205, 0.120, 0.130))]
    mb.loft(hips, "M_Firelost_Pants", cap_start=True)

    for side in (-1, 1):
        h, k, a = side_vec(HIP, side), side_vec(KNEE, side), side_vec(ANKLE, side)
        pts = [h + Vector((0, 0, 0.04)), h, h.lerp(k, 0.5), k, k.lerp(a, 0.45), a + Vector((0, 0, 0.10)), a + Vector((0, 0, 0.035))]
        radii = [0.120, 0.126, 0.124, 0.108, 0.118, 0.123, 0.130]
        rings = []
        for i, (p, t, r) in enumerate(zip(pts, tangents(pts), radii)):
            ring = axis_ring(p, t, r, r, 12)
            ring = [q + jit(0.006) for q in ring]
            if i == len(pts) - 1:  # 撕裂的裤脚：锯齿下垂
                ring = [q + Vector((0, 0, (-0.045 if j % 2 else 0.0) + random.uniform(-0.02, 0.015))) for j, q in enumerate(ring)]
            rings.append(ring)
        mb.loft(rings, "M_Firelost_Pants")


    # 右膝（-X）磨破的洞：露出皮肤
    k = side_vec(KNEE, -1)
    center = k + Vector((0, -0.096, 0.06))
    hole = [center + Vector((0.035 * math.cos(t) + random.uniform(-0.008, 0.008), -0.004 * math.sin(t) ** 2,
                             0.05 * math.sin(t) + random.uniform(-0.008, 0.008))) for t in (TAU * i / 7 for i in range(7))]
    hv = mb.loft([hole], "M_Firelost_Skin")
    mb.cap(hv[0], "M_Firelost_Skin", peak=center + Vector((0, -0.006, 0)))
    edge = [center + (p - center) * 1.35 + Vector((0, 0.004, 0)) for p in hole]
    mb.loft([edge, hole], "M_Firelost_VestInner")


def build_belt(mb):
    belt = [ellipse_ring(0, 0.03, z, 0.188, 0.128, 0.138, 12) for z in (0.975, 1.03)]
    mb.loft(belt, "M_Firelost_Belt")
    mb.box((0.0, -0.104, 1.002), (0.070, 0.012, 0.056), "M_Firelost_Buckle", bevel=0.006)
    mb.box((0.0, -0.110, 1.002), (0.044, 0.008, 0.032), "M_Firelost_Belt", bevel=0.004)
    # 右侧挂灯的皮扣
    mb.box(tuple(LANTERN_HOOK + Vector((0.005, -0.005, 0.02))), (0.022, 0.03, 0.06), "M_Firelost_Belt", bevel=0.005)


def build_boot(mb, side, broken):
    x = ANKLE.x * side
    # 鞋底（完整长度）
    sole = [ellipse_ring(x, -0.045, z, rx, rf, rb, 10) for z, rx, rf, rb in
            ((0.0, 0.078, 0.180, 0.100), (0.036, 0.081, 0.186, 0.103), (0.050, 0.077, 0.176, 0.098))]
    mb.loft(sole, "M_Firelost_Sole", cap_start=True, cap_end=True, mats_per_band=["M_Firelost_Sole", "M_Firelost_Leather"])

    if broken:
        # 鞋头撕开：鞋面只包住后跟和脚背后半，前缘锯齿
        upper = [(0.050, -0.02, 0.076, 0.092, 0.096), (0.11, -0.01, 0.074, 0.082, 0.088),
                 (0.17, 0.0, 0.080, 0.085, 0.088), (0.28, 0.0, 0.094, 0.098, 0.094)]
    else:
        upper = [(0.050, -0.040, 0.076, 0.168, 0.096), (0.11, -0.03, 0.074, 0.138, 0.088),
                 (0.17, 0.0, 0.080, 0.088, 0.088), (0.28, 0.0, 0.094, 0.098, 0.094)]
    rings = [ellipse_ring(x, cy, z, rx, rf, rb, 10, jitter=0.003) for z, cy, rx, rf, rb in upper]
    if broken:
        for ring in rings[:2]:
            for j, p in enumerate(ring):
                if p.y < -0.06:
                    p.z += 0.02 if j % 2 else -0.01
    mb.loft(rings, "M_Firelost_Leather", cap_start=not broken)

    # 鞋带：鞋面前侧几道横带
    if not broken:
        for z in (0.12, 0.165, 0.21, 0.245):
            mb.box((x, -0.091 - 0.13 * max(0.0, 0.17 - z), z), (0.078, 0.012, 0.010), "M_Firelost_Sole", bevel=0.003)
    else:
        # 断开的鞋面翻边
        mb.box((x + 0.03, -0.085, 0.07), (0.035, 0.03, 0.012), "M_Firelost_Leather", bevel=0.006)


def build_vest(mats):
    """残破工装背心：前胸敞开、下摆锯齿、背后撕裂。开放面片，加厚后内侧为暗色。"""
    mb = MeshBuilder("Firelost_Vest")
    centers = [c for c, *_ in TORSO]
    t_all = tangents(centers)
    # (躯干截面索引或插值, 半宽, 前半径, 后半径, 前开口半角°)
    spec = [
        (4.6, 0.170, 0.090, 0.130, 60),   # 翻领顶
        (4.35, 0.205, 0.095, 0.160, 40),
        (4.0, 0.272, 0.110, 0.196, 28),
        (3.0, 0.268, 0.142, 0.214, 20),
        (2.0, 0.212, 0.150, 0.188, 12),
        (1.0, 0.185, 0.135, 0.138, 5),
        (0.45, 0.200, 0.140, 0.148, 7),   # 下摆，落在腰带上
    ]
    SEG = 24
    rings = []
    for idx, rx, rf, rb, half in spec:
        i0 = min(int(idx), len(centers) - 2)
        f = idx - i0
        c = centers[i0].lerp(centers[i0 + 1], f)
        t = t_all[i0].lerp(t_all[i0 + 1], f)
        a = math.radians(half)
        ring = spine_ring(c, t, rx, rf, rb, SEG, math.pi / 2 + a, math.pi / 2 + TAU - a, closed=False, jitter=0.005)
        rings.append(ring)
    # 翻领外翻
    for p in rings[0]:
        p += (p - centers[5]).normalized() * 0.02
    # 下摆锯齿
    for j, p in enumerate(rings[-1]):
        p.z += (-0.06 if j % 2 else 0.0) + random.uniform(-0.025, 0.02)
    vr = mb.loft(rings, "M_Firelost_Vest", closed=False)

    # 翻领：沿前开口两侧向外翻出的三角布
    for edge in (0, SEG):
        strip = [vr[r][edge].co.copy() for r in range(4)]
        out = []
        for r, p in enumerate(strip):
            lateral = Vector((math.copysign(1.0, p.x), 0, 0))
            width = (0.055, 0.06, 0.045, 0.0)[r]
            out.append(p + lateral * width + FWD * (0.012 if r < 3 else 0.004) + Vector((0, 0, -0.01 * r)))
        mb.loft([strip, out], "M_Firelost_Vest", closed=False)

    # 背心是开放面片：让法线朝离开脊柱的方向
    for f in mb.bm.faces:
        f.normal_update()
        c = f.calc_center_median()
        k = min(range(len(centers)), key=lambda i: (centers[i] - c).length)
        if f.normal.dot(c - centers[k]) < 0:
            f.normal_flip()

    # 背后撕口：几块面换成内衬暗色，读作破洞
    mb.bm.faces.ensure_lookup_table()
    tear = mb.mat("M_Firelost_VestInner")
    for band, i in ((3, 13), (3, 14), (4, 14), (2, 12), (4, 9), (5, 9), (5, 17)):
        mb.bm.faces[band * SEG + i].material_index = tear
    for z in (1.17, 1.09):
        y = centers[1].y - 0.142 if z < 1.12 else centers[2].y - 0.155
        mb.box((-0.035, y, z), (0.014, 0.008, 0.014), "M_Firelost_Belt", bevel=0.003)

    obj = mb.build(mats, recalc=False)
    obj.data.materials.append(mats["M_Firelost_VestInner"])
    before = len(obj.data.polygons)
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = 0.012
    mod.offset = -1.0
    mod.use_even_offset = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier="Thickness")
    inner = obj.data.materials.find("M_Firelost_VestInner")
    for p in obj.data.polygons[before:]:
        p.material_index = inner
    return obj


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    objs[0].name = name
    objs[0].data.name = name
    objs[0].select_set(False)


# ---------- 旧灯（熄灭，不发光） ----------

def build_lantern(mats):
    """原点在顶部挂环，便于第二阶段挂到骨盆/腰带骨骼上。灯身向下垂。"""
    mb = MeshBuilder("Firelost_Lantern")
    M, G = "M_Firelost_LanternMetal", "M_Firelost_LanternGlass"
    # 挂环
    loop = [Vector((0.022 * math.sin(TAU * i / 8), 0.0, -0.022 - 0.022 * math.cos(TAU * i / 8))) for i in range(9)]
    mb.tube(loop, [0.0045] * 9, M, n=4, forward=Vector((0, 1, 0)))
    # 顶盖：环座 → 斗笠形顶
    top = [ellipse_ring(0, 0, z, r, r, r, 10) for z, r in ((-0.044, 0.012), (-0.050, 0.020), (-0.070, 0.030), (-0.090, 0.058), (-0.098, 0.060))]
    mb.loft(top, M, cap_start=True, cap_end=True)
    for i in range(6):  # 顶盖通风孔
        a = TAU * i / 6
        mb.box((0.042 * math.cos(a), 0.042 * math.sin(a), -0.08), (0.010, 0.010, 0.008), "M_Firelost_Sole", bevel=0.002)
    # 玻璃罩（暗淡、积灰、无光）
    glass = [ellipse_ring(0, 0, z, r, r, r, 10) for z, r in ((-0.098, 0.040), (-0.13, 0.048), (-0.18, 0.048), (-0.205, 0.042))]
    mb.loft(glass, G)
    # 护笼：四根竖条 + 两道箍
    for i in range(4):
        a = TAU * (i + 0.5) / 4
        p = lambda z, r: Vector((r * math.cos(a), r * math.sin(a), z))
        mb.tube([p(-0.098, 0.056), p(-0.12, 0.058), p(-0.155, 0.060), p(-0.19, 0.058), p(-0.208, 0.054)], [0.0045] * 5, M, n=4, forward=Vector((0, 0, 1)))
    for z, r in ((-0.125, 0.057), (-0.175, 0.059)):
        band = [ellipse_ring(0, 0, zz, r, r, r, 10) for zz in (z, z + 0.008)]
        mb.loft(band, M)
    # 底座
    base = [ellipse_ring(0, 0, z, r, r, r, 10) for z, r in ((-0.205, 0.058), (-0.215, 0.064), (-0.245, 0.064), (-0.252, 0.056))]
    mb.loft(base, M, cap_end=True)
    # 侧面提手
    bail =[Vector((0.060 + 0.022 * math.sin(math.pi * i / 6), 0.0, -0.105 - 0.075 * i / 6)) for i in range(7)]
    mb.tube(bail, [0.0035] * 7, M, n=4, forward=Vector((0, 1, 0)))
    obj = mb.build(mats)
    return obj


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
    target = Vector((0, -0.08, 0.88))
    views = {
        "front": (Vector((0, -6, 0.88)), True),
        "side": (Vector((-6, -0.08, 0.88)), True),   # 从角色右侧看，能看到旧灯
        "back": (Vector((0, 6, 0.88)), True),
        "three_quarter": (Vector((-3.2, -4.2, 2.4)), False),
    }
    for name, (pos, ortho) in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        cam_data.type = "ORTHO" if ortho else "PERSP"
        cam_data.ortho_scale = 2.05
        cam_data.lens = 85
        scene.render.filepath = os.path.join(folder, f"Firelost_{name}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def render_poses(scene, rig, folder):
    """关键姿势检查图：正面 + 侧面。"""
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("PoseCam")
    cam = bpy.data.objects.new("PoseCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.3
    target = Vector((0, -0.1, 0.85))
    shots = [("Idle", 0), ("Walk", 8), ("Walk", 24), ("Run", 5), ("Run", 15), ("Attack_R", 8), ("Attack_R", firelost_rig.ATTACK_IMPACT_FRAME),
             ("Attack_R", 16), ("Attack_L", firelost_rig.ATTACK_IMPACT_FRAME), ("Hit", 3), ("Death", 16), ("Death", 24), ("Death", 40)]
    for action, frame in shots:
        rig.animation_data.action = bpy.data.actions[action]
        scene.frame_set(frame)
        for view, pos in (("front", Vector((0, -6, 0.85))), ("side", Vector((-6, -0.1, 0.85)))):
            cam.location = pos
            cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(folder, f"{action}_{frame:02d}_{view}.png")
            bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ---------- 贴图烘焙 ----------
# 程序化着色（斑驳 + 由下往上的尘土渐变）烘焙成每个部件一张颜色贴图。

DIRT = (0.27, 0.20, 0.15)
RUST = (0.40, 0.23, 0.13)
# 材质名 → (尘土渐变起点 z, 完全变脏的 z, 尘土强度, 斑驳幅度[, 脏污颜色])
WEAR = {
    "M_Firelost_Skin": (1.1, 0.5, 0.25, 0.12),
    "M_Firelost_SkinDark": (9.0, 8.0, 0.0, 0.08),
    "M_Firelost_Eye": (9.0, 8.0, 0.0, 0.05),
    "M_Firelost_Teeth": (9.0, 8.0, 0.0, 0.15),
    "M_Firelost_Hair": (9.0, 8.0, 0.0, 0.15),
    "M_Firelost_Callus": (9.0, 8.0, 0.0, 0.30),
    "M_Firelost_Claw": (9.0, 8.0, 0.0, 0.10),
    "M_Firelost_Vest": (1.25, 0.90, 0.55, 0.28),
    "M_Firelost_VestInner": (9.0, 8.0, 0.0, 0.15),
    "M_Firelost_Pants": (0.55, 0.10, 0.85, 0.22),
    "M_Firelost_Leather": (0.30, 0.00, 0.55, 0.25),
    "M_Firelost_Sole": (9.0, 8.0, 0.0, 0.10),
    "M_Firelost_Belt": (9.0, 8.0, 0.0, 0.15),
    "M_Firelost_Buckle": (9.0, 8.0, 0.0, 0.15, RUST),
    "M_Firelost_LanternMetal": (0.0, -0.25, 0.75, 0.25, RUST),
    "M_Firelost_LanternGlass": (-0.1, -0.22, 0.6, 0.12, (0.20, 0.18, 0.16)),
}
TEX_SIZE = {"Firelost_Body": 1024, "Firelost_Clothes": 1024, "Firelost_Lantern": 512}


def make_wear_shader(mat):
    rgb = PALETTE[mat.name][0]
    top, bottom, strength, mottle, *rest = WEAR[mat.name]
    dirt = rest[0] if rest else DIRT
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
    col = mix(col, lin(dirt), ramp.outputs["Result"])

    # 块面色差：每个三角面明暗 ±7%，还原参考图的低多边形切面感
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "facet"
    shade = nt.nodes.new("ShaderNodeMath")
    shade.operation = "MULTIPLY_ADD"
    nt.links.new(attr.outputs["Fac"], shade.inputs[0])
    shade.inputs[1].default_value = 0.14
    shade.inputs[2].default_value = 0.93
    tint = nt.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs[0].default_value = 1.0
    nt.links.new(col, tint.inputs[6])
    nt.links.new(shade.outputs[0], tint.inputs[7])
    col = tint.outputs[2]
    nt.links.new(col, bsdf.inputs["Base Color"])


def bake_object(obj, folder):
    scene = bpy.context.scene
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.quads_convert_to_tris(quad_method="BEAUTY", ngon_method="BEAUTY")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.006)
    bpy.ops.object.mode_set(mode="OBJECT")

    facet = obj.data.attributes.new("facet", "FLOAT", "FACE")
    facet.data.foreach_set("value", [random.random() for _ in obj.data.polygons])

    size = TEX_SIZE[obj.name]
    name = "T_" + obj.name
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

    baked = bpy.data.materials.new("M_" + obj.name)
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

    # 低多边形块面感：只把很小的转折抹平，保留参考图那种硬朗的切面
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(12))
    obj.select_set(False)


# ---------- 主流程 ----------

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    mats = make_materials()

    root = bpy.data.objects.new("Firelost", None)
    scene.collection.objects.link(root)

    body = build_body(mats)
    clothes = build_clothes(mats)
    lantern = build_lantern(mats)
    lantern.location = LANTERN_HOOK
    lantern.scale = (1.25, 1.25, 1.25)
    lantern.rotation_euler = (math.radians(-8), math.radians(22), math.radians(25))  # 向外、略向前斜挂，贴着大腿外侧

    for obj in (body, clothes, lantern):
        obj.parent = root

    for mat in bpy.data.materials:
        if mat.name in WEAR:
            make_wear_shader(mat)
    for obj in (body, clothes, lantern):
        bake_object(obj, TEX_DIR)
    bpy.context.view_layer.update()

    # 旧灯：变换烘进网格后整块蒙皮到 lantern 骨（挂点 → 灯底），走路、攻击时能摆动
    tip = lantern.matrix_world @ Vector((0, 0, -0.25))
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = lantern
    lantern.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lantern.select_set(False)
    rig = firelost_rig.setup(root, body, clothes, lantern, LANTERN_HOOK, tip)

    setup_render(scene)
    render_views(scene, os.path.join(OUT_DIR, "renders"))
    render_poses(scene, rig, os.path.join(OUT_DIR, "renders", "poses"))
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(0)

    # 烘焙源材质留在 .blend 里，方便以后改色重烘
    for mat in bpy.data.materials:
        if mat.name in PALETTE:
            mat.use_fake_user = True
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "Firelost.blend"))

    os.makedirs(os.path.dirname(FBX_PATH), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=False,
        object_types={"EMPTY", "MESH", "ARMATURE"},
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
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

    for o in (body, clothes, lantern):
        print(f"[Firelost] {o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")
    print(f"[Firelost] fbx={FBX_PATH}")


if __name__ == "__main__":  # firelost_cache_build.py 会 import 本模块复用建模工具
    main()
