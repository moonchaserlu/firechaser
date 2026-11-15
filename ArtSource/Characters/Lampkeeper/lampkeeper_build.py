"""守灯人（Lampkeeper）：教学关事件选择 NPC。坐在翻倒木箱上的受伤老人，左手拄挂灯长杆，左小腿缠着渗血绷带。

用法（Blender 5.x 后台）：
    blender --background --factory-startup --python lampkeeper_build.py -- <输出目录> <FBX 路径>

产物：
    <输出目录>/Lampkeeper.blend        可继续手工编辑的源文件
    <输出目录>/renders/*.png           正/侧/背/四分之三视图
    <输出目录>/renders/poses/*.png     待机关键帧检查图
    <FBX 路径>                         Unity 用模型（Body / Clothes / Props / Lamp 四个蒙皮网格 + Lampkeeper_Rig 与 Idle 动作）
    <FBX 目录>/Textures/*.png          每个部件一张烘焙颜色贴图

与寻火者、失火者同一套流程：复用 firelost_build.py 的建模、烘焙工具和 firelost_rig.py 的骨骼工具。
坐标约定：Blender Z 向上，角色面朝 -Y，脚底（地面）在 z=0，原点在木箱底面中心附近，角色右侧为 -X。
造型要点（和玩家区分）：不戴兜帽而是宽檐旧毡帽、白胡子、灰蓝长外套披旧毛毯、无背包；
左手（+X）拄一根顶端弯钩挂着小灯的长灯杆，斜俯视镜头下读作"圆帽檐 + 竖灯杆"。
"""

import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "..", "Firelost"))
import firelost_build as fb  # noqa: E402
import lampkeeper_rig  # noqa: E402

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = os.path.abspath(ARGS[0] if len(ARGS) > 0 else HERE)
FBX_PATH = os.path.abspath(ARGS[1] if len(ARGS) > 1 else os.path.join(OUT_DIR, "Lampkeeper.fbx"))
TEX_DIR = os.path.join(os.path.dirname(FBX_PATH), "Textures")

TAU = math.pi * 2
FWD = Vector((0, -1, 0))
UP = Vector((0, 0, 1))
MeshBuilder, tube_ring, ellipse_ring, spine_ring, tangents = fb.MeshBuilder, fb.axis_ring, fb.ellipse_ring, fb.spine_ring, fb.tangents

# ---------- 材质 ----------
# 名称 → (sRGB, 粗糙度, 金属度)。同色不同名的材质用来区分蒙皮分组（烘焙后合成一张贴图，不影响外观）。
SKIN = (0.64, 0.50, 0.42)
COAT = (0.31, 0.35, 0.39)
fb.PALETTE.clear()
fb.PALETTE.update({
    "M_Keeper_Face": (SKIN, 0.85, 0.0),
    "M_Keeper_Neck": (SKIN, 0.85, 0.0),
    "M_Keeper_Hand": (SKIN, 0.85, 0.0),
    "M_Keeper_SkinDark": ((0.36, 0.25, 0.21), 0.85, 0.0),
    "M_Keeper_Eye": ((0.08, 0.07, 0.07), 0.6, 0.0),
    "M_Keeper_Beard": ((0.80, 0.78, 0.73), 0.9, 0.0),
    "M_Keeper_Hair": ((0.66, 0.64, 0.60), 0.9, 0.0),
    "M_Keeper_Hat": ((0.25, 0.22, 0.19), 0.9, 0.0),
    "M_Keeper_HatBand": ((0.46, 0.30, 0.18), 0.85, 0.0),
    "M_Keeper_Shirt": ((0.47, 0.42, 0.32), 0.95, 0.0),
    "M_Keeper_Coat": (COAT, 0.95, 0.0),
    "M_Keeper_CoatInner": ((0.15, 0.16, 0.18), 0.95, 0.0),
    "M_Keeper_CoatPatch": ((0.42, 0.34, 0.25), 0.95, 0.0),
    "M_Keeper_Skirt": (COAT, 0.95, 0.0),
    "M_Keeper_SkirtInner": ((0.15, 0.16, 0.18), 0.95, 0.0),
    "M_Keeper_Sleeve": (COAT, 0.95, 0.0),
    "M_Keeper_Cuff": ((0.20, 0.22, 0.25), 0.95, 0.0),
    "M_Keeper_Blanket": ((0.44, 0.43, 0.33), 0.95, 0.0),
    "M_Keeper_BlanketStripe": ((0.52, 0.27, 0.19), 0.95, 0.0),
    "M_Keeper_BlanketInner": ((0.26, 0.25, 0.19), 0.95, 0.0),
    "M_Keeper_Pants": ((0.27, 0.24, 0.21), 0.95, 0.0),
    "M_Keeper_Leather": ((0.33, 0.24, 0.17), 0.8, 0.0),
    "M_Keeper_Sole": ((0.15, 0.13, 0.11), 0.9, 0.0),
    "M_Keeper_Bandage": ((0.74, 0.70, 0.60), 0.95, 0.0),
    "M_Keeper_BandageDark": ((0.60, 0.56, 0.47), 0.95, 0.0),
    "M_Keeper_Blood": ((0.42, 0.11, 0.08), 0.8, 0.0),
    "M_Keeper_Wood": ((0.45, 0.34, 0.24), 0.9, 0.0),
    "M_Keeper_WoodDark": ((0.22, 0.16, 0.12), 0.9, 0.0),
    "M_Keeper_Staff": ((0.38, 0.28, 0.19), 0.85, 0.0),
    "M_Keeper_Iron": ((0.27, 0.25, 0.24), 0.7, 0.0),
    "M_Keeper_LampMetal": ((0.30, 0.27, 0.24), 0.7, 0.0),
    "M_Keeper_LampGlass": ((0.98, 0.74, 0.40), 0.4, 0.0),
    "M_Keeper_LampFlame": ((1.00, 0.88, 0.60), 0.4, 0.0),
})

DIRT = (0.27, 0.20, 0.15)
RUST = (0.40, 0.23, 0.13)
CLEAN = (9.0, 8.0, 0.0)
fb.WEAR.clear()
fb.WEAR.update({
    "M_Keeper_Face": (*CLEAN, 0.12), "M_Keeper_Neck": (*CLEAN, 0.12), "M_Keeper_Hand": (0.70, 0.50, 0.30, 0.12),
    "M_Keeper_SkinDark": (*CLEAN, 0.08), "M_Keeper_Eye": (*CLEAN, 0.05),
    "M_Keeper_Beard": (*CLEAN, 0.10), "M_Keeper_Hair": (*CLEAN, 0.12),
    "M_Keeper_Hat": (*CLEAN, 0.22), "M_Keeper_HatBand": (*CLEAN, 0.15),
    "M_Keeper_Shirt": (*CLEAN, 0.20),
    "M_Keeper_Coat": (1.00, 0.55, 0.45, 0.24), "M_Keeper_CoatInner": (*CLEAN, 0.12), "M_Keeper_CoatPatch": (*CLEAN, 0.25),
    "M_Keeper_Skirt": (0.45, 0.10, 0.65, 0.24), "M_Keeper_SkirtInner": (*CLEAN, 0.12),
    "M_Keeper_Sleeve": (0.85, 0.55, 0.35, 0.24), "M_Keeper_Cuff": (*CLEAN, 0.15),
    "M_Keeper_Blanket": (*CLEAN, 0.30), "M_Keeper_BlanketStripe": (*CLEAN, 0.25), "M_Keeper_BlanketInner": (*CLEAN, 0.15),
    "M_Keeper_Pants": (0.45, 0.05, 0.75, 0.22), "M_Keeper_Leather": (0.25, 0.00, 0.55, 0.25), "M_Keeper_Sole": (*CLEAN, 0.10),
    "M_Keeper_Bandage": (0.30, 0.05, 0.45, 0.18, DIRT), "M_Keeper_BandageDark": (0.30, 0.05, 0.45, 0.18, DIRT),
    "M_Keeper_Blood": (*CLEAN, 0.25),
    "M_Keeper_Wood": (0.30, -0.05, 0.55, 0.28), "M_Keeper_WoodDark": (*CLEAN, 0.15),
    "M_Keeper_Staff": (0.40, 0.00, 0.60, 0.25), "M_Keeper_Iron": (*CLEAN, 0.25, RUST),
    "M_Keeper_LampMetal": (2.00, 1.60, 0.55, 0.25, RUST), "M_Keeper_LampGlass": (*CLEAN, 0.06), "M_Keeper_LampFlame": (*CLEAN, 0.02),
})
fb.TEX_SIZE.clear()
fb.TEX_SIZE.update({"Keeper_Body": 1024, "Keeper_Clothes": 1024, "Keeper_Props": 512, "Keeper_Lamp": 256})

# 蒙皮分组：材质 → 组。lampkeeper_rig 按组决定每个连通块可以跟随哪些骨骼。
GROUPS = {
    "head": ["Face", "SkinDark", "Eye", "Beard", "Hair", "Hat", "HatBand"],
    "neck": ["Neck"],
    "torso": ["Shirt", "Coat", "CoatInner", "CoatPatch", "Blanket", "BlanketStripe", "BlanketInner"],
    "skirt": ["Skirt", "SkirtInner"],
    "arm": ["Sleeve", "Cuff"],
    "hand": ["Hand"],
    "leg": ["Pants", "Leather", "Sole", "Bandage", "BandageDark", "Blood"],
    "prop": ["Wood", "WoodDark", "Staff", "Iron"],
    "lamp": ["LampMetal", "LampGlass", "LampFlame"],
}
GROUP_INDEX = {f"M_Keeper_{m}": i for i, (g, ms) in enumerate(GROUPS.items()) for m in ms}
GROUP_NAMES = list(GROUPS)

# ---------- 坐姿 ----------
SEAT_Z = 0.44                  # 木箱顶面
CRATE_C = Vector((0, 0.10, 0))
CRATE_W, CRATE_D, CRATE_H = 0.56, 0.44, 0.42

# 躯干截面：(中心, 半宽, 前半径, 后半径)。上身微微前倾、含胸。
TORSO = [
    (Vector((0, 0.13, 0.50)), 0.175, 0.130, 0.120),
    (Vector((0, 0.12, 0.62)), 0.165, 0.120, 0.115),
    (Vector((0, 0.10, 0.76)), 0.175, 0.125, 0.130),
    (Vector((0, 0.06, 0.88)), 0.195, 0.125, 0.150),
    (Vector((0, 0.02, 0.965)), 0.190, 0.100, 0.130),
    (Vector((0, -0.02, 1.015)), 0.100, 0.060, 0.070),
    (Vector((0, -0.05, 1.045)), 0.055, 0.050, 0.050),
]
SHOULDER = Vector((0.205, 0.03, 0.965))
# 左臂（+X）握灯杆；右臂（-X）搭在右膝上
ELBOW_L, WRIST_L = Vector((0.30, 0.08, 0.72)), Vector((0.355, -0.165, 0.83))
ELBOW_R, WRIST_R = Vector((-0.27, -0.02, 0.70)), Vector((-0.17, -0.24, 0.62))
# 右腿正常屈膝着地；左腿伤腿往前伸直，脚跟着地
HIP_R, KNEE_R, ANKLE_R = Vector((-0.10, 0.08, 0.50)), Vector((-0.13, -0.30, 0.50)), Vector((-0.14, -0.30, 0.10))
HIP_L, KNEE_L, ANKLE_L = Vector((0.10, 0.08, 0.50)), Vector((0.13, -0.36, 0.33)), Vector((0.15, -0.68, 0.11))
TOE_R, TOE_L = Vector((-0.14, -0.47, 0.03)), Vector((0.17, -0.80, 0.19))
HEAD_PIVOT = Vector((0, -0.10, 1.20))
# 灯杆：底端插地，顶端弯钩挂灯
STAFF_BOTTOM, STAFF_TOP = Vector((0.40, -0.20, 0.0)), Vector((0.41, -0.16, 1.90))
LAMP_HOOK = Vector((0.41, -0.33, 1.885))
LAMP_LENGTH = 0.26


def staff_at(z):
    return STAFF_BOTTOM.lerp(STAFF_TOP, z / STAFF_TOP.z)


# ---------- 身体（脸、脖子、手） ----------

def build_body(mats):
    mb = MeshBuilder("Keeper_Body")
    build_head(mb)
    mb.tube([Vector((0, -0.01, 0.99)), Vector((0, -0.06, 1.07)), Vector((0, -0.10, 1.13))], [0.052, 0.050, 0.048], "M_Keeper_Neck", n=8)
    build_hand_on_knee(mb)
    build_fist(mb)
    return mb.build(mats)


def build_head(mb):
    # 由下颌到头顶的水平截面：(z, 中心y, 半宽, 前半径, 后半径)。瘦削、颧骨略凹。
    profile = [
        (1.090, -0.150, 0.045, 0.030, 0.030),
        (1.115, -0.130, 0.068, 0.056, 0.055),
        (1.155, -0.115, 0.074, 0.070, 0.075),
        (1.200, -0.105, 0.080, 0.078, 0.086),
        (1.245, -0.100, 0.084, 0.080, 0.092),
        (1.280, -0.095, 0.080, 0.072, 0.088),
        (1.308, -0.090, 0.064, 0.055, 0.072),
        (1.325, -0.088, 0.040, 0.032, 0.045),
    ]
    rings = [ellipse_ring(0, cy, z, rx, rf, rb, 10, jitter=0.003) for z, cy, rx, rf, rb in profile]
    hv = mb.loft(rings, "M_Keeper_Face")
    mb.cap(hv[0], "M_Keeper_Face", peak=Vector((0, -0.155, 1.08)))
    mb.cap(hv[-1], "M_Keeper_Hair", peak=Vector((0, -0.088, 1.335)))

    for s in (-1, 1):
        # 下垂的白眉、深陷的眼窝、耳朵
        mb.tube([Vector((0.010 * s, -0.185, 1.236)), Vector((0.040 * s, -0.182, 1.232)), Vector((0.068 * s, -0.166, 1.218))],
                [(0.010, 0.012), (0.010, 0.013), (0.007, 0.008)], "M_Keeper_Beard", n=5, cap_start=True, cap_end=True)
        mb.box((0.034 * s, -0.178, 1.214), (0.022, 0.008, 0.009), "M_Keeper_Eye", bevel=0.003)   # 眯着的眼睛
        ear = [Vector((0.078 * s, -0.098, 1.17)), Vector((0.090 * s, -0.092, 1.20)), Vector((0.088 * s, -0.088, 1.235))]
        mb.tube(ear, [(0.024, 0.010), (0.024, 0.010), (0.012, 0.007)], "M_Keeper_Face", n=5, cap_start=True, cap_end=True)
        # 帽檐下露出的灰白头发：两鬓和后脑
        for k in range(5):
            a = math.radians(40 + 30 * k) * s
            base = HEAD_PIVOT + Vector((math.sin(a) * 0.082, math.cos(a) * 0.085 + 0.005, 0.045 + random.uniform(-0.01, 0.01)))
            d = (Vector((math.sin(a) * 0.6, math.cos(a) * 0.6, -1.0)) + fb.jit(0.2)).normalized()
            mb.cone(base, d, random.uniform(0.020, 0.028), random.uniform(0.05, 0.08), "M_Keeper_Hair", n=4)
    # 鹰钩鼻
    mb.tube([Vector((0, -0.182, 1.225)), Vector((0, -0.208, 1.180)), Vector((0, -0.200, 1.160))],
            [0.011, 0.018, 0.012], "M_Keeper_Face", n=5, cap_start=True, cap_end=True)
    # 胡子：上唇八字须 + 垂到胸口的尖胡子
    mb.tube([Vector((-0.055, -0.168, 1.130)), Vector((-0.025, -0.192, 1.150)), Vector((0, -0.196, 1.152)),
             Vector((0.025, -0.192, 1.150)), Vector((0.055, -0.168, 1.130))],
            [0.010, 0.016, 0.017, 0.016, 0.010], "M_Keeper_Beard", n=5, cap_start=True, cap_end=True, forward=UP)
    beard = [ellipse_ring(0, cy, z, rx, rf, rb, 10, jitter=0.004) for z, cy, rx, rf, rb in (
        (1.165, -0.115, 0.082, 0.072, 0.030),
        (1.115, -0.140, 0.078, 0.052, 0.040),
        (1.055, -0.145, 0.062, 0.044, 0.034),
        (0.995, -0.130, 0.036, 0.030, 0.024),
    )]
    bv = mb.loft(beard, "M_Keeper_Beard", cap_start=True)
    mb.cap(bv[-1], "M_Keeper_Beard", peak=Vector((0, -0.118, 0.945)))


def build_hand_on_knee(mb):
    """右手：手掌搭在右膝上，四指并拢顺着膝盖往前下垂。"""
    w = WRIST_R
    p1, p2 = Vector((-0.155, -0.300, 0.612)), Vector((-0.142, -0.345, 0.600))
    mb.tube([w, p1, p2], [(0.022, 0.040), (0.024, 0.046), (0.022, 0.045)], "M_Keeper_Hand", n=6, forward=UP, cap_start=True)
    f = [p2, Vector((-0.136, -0.385, 0.585)), Vector((-0.132, -0.405, 0.552)), Vector((-0.130, -0.410, 0.528))]
    mb.tube(f, [(0.018, 0.042), (0.017, 0.040), (0.015, 0.036), (0.012, 0.030)], "M_Keeper_Hand", n=6, forward=UP, cap_end=True)
    t0 = Vector((-0.120, -0.270, 0.600))
    mb.tube([t0, Vector((-0.100, -0.305, 0.596)), Vector((-0.098, -0.335, 0.590))], [0.015, 0.013, 0.010], "M_Keeper_Hand", n=5,
            cap_start=True, cap_end=True)


def build_fist(mb):
    """左手：攥着灯杆的拳头，包住杆身一圈，指节朝前外。"""
    c = staff_at(0.83)
    axis = (STAFF_TOP - STAFF_BOTTOM).normalized()
    pts = [c - axis * 0.050, c - axis * 0.015, c + axis * 0.020, c + axis * 0.050]
    mb.tube(pts, [0.036, 0.046, 0.046, 0.036], "M_Keeper_Hand", n=8, forward=Vector((-1, 0, 0)), cap_start=True, cap_end=True, jitter=0.002)
    # 手背连到手腕
    mb.tube([WRIST_L, WRIST_L.lerp(c, 0.6)], [(0.030, 0.042), (0.034, 0.046)], "M_Keeper_Hand", n=6, forward=UP, cap_start=True)
    # 拇指压在杆前
    mb.tube([c + Vector((-0.02, -0.030, 0.045)), c + Vector((0.005, -0.045, 0.050)), c + Vector((0.025, -0.040, 0.040))],
            [0.014, 0.013, 0.010], "M_Keeper_Hand", n=5, cap_start=True, cap_end=True)


# ---------- 服装 ----------

def orient_outward(mb, centre_of):
    """开放面片：让法线朝离开身体的方向。"""
    for f in mb.bm.faces:
        f.normal_update()
        c = f.calc_center_median()
        if f.normal.dot(c - centre_of(c)) < 0:
            f.normal_flip()


def solidify(obj, mats, inner, thickness):
    obj.data.materials.append(mats[inner])
    before = len(obj.data.polygons)
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = thickness
    mod.offset = -1.0
    mod.use_even_offset = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier="Thickness")
    idx = obj.data.materials.find(inner)
    for p in obj.data.polygons[before:]:
        p.material_index = idx
    return obj


def torso_axis(c):
    k = min(range(len(TORSO)), key=lambda i: abs(TORSO[i][0].z - c.z))
    return TORSO[k][0]


def build_clothes(mats):
    mb = MeshBuilder("Keeper_Clothes")
    # 衬衣躯干（被外套挡住大半，敞开的前襟里露出来）
    rings = [spine_ring(c, t, rx, rf, rb, 10, jitter=0.003) for (c, rx, rf, rb), t in zip(TORSO, tangents([c for c, *_ in TORSO]))]
    mb.loft(rings, "M_Keeper_Shirt", cap_start=True)
    build_hat(mb)
    for side in (-1, 1):
        build_sleeve(mb, side)
    build_legs(mb)
    base = mb.build(mats)

    parts = [base, build_coat(mats), build_skirt(mats), build_blanket(mats)]
    fb.join(parts, "Keeper_Clothes")
    return base


def build_hat(mb):
    """宽檐旧毡帽：帽檐前后略垂，整体随低头往前倾。"""
    before = set(mb.bm.verts)
    c = Vector((0, -0.098, 0))
    brim = []
    for z_off, r_scale in ((0.0, 1.0), (0.012, 0.98)):
        ring = []
        for i in range(16):
            a = TAU * i / 16
            droop = 0.018 * math.sin(a) ** 2 + 0.006 * math.cos(3 * a)
            ring.append(Vector((c.x + 0.215 * r_scale * math.cos(a), c.y + 0.205 * r_scale * math.sin(a), 1.272 + z_off - droop)) + fb.jit(0.002))
        brim.append(ring)
    inner = [Vector((c.x + 0.105 * math.cos(TAU * i / 16), c.y + 0.100 * math.sin(TAU * i / 16), 1.284)) for i in range(16)]
    mb.loft([inner, brim[0]], "M_Keeper_Hat")          # 帽檐底面
    mb.loft([brim[0], brim[1]], "M_Keeper_Hat")         # 檐边
    crown = [ellipse_ring(c.x, c.y, z, rx, ry, ry, 16, jitter=0.002) for z, rx, ry in (
        (1.284, 0.108, 0.103), (1.300, 0.108, 0.103), (1.335, 0.104, 0.099), (1.385, 0.096, 0.091), (1.405, 0.080, 0.076))]
    rim_top = [Vector((p.x, p.y, 1.286)) for p in crown[0]]
    mb.loft([brim[1], rim_top], "M_Keeper_Hat")        # 帽檐顶面
    cv = mb.loft(crown, "M_Keeper_Hat", mats_per_band=["M_Keeper_HatBand", "M_Keeper_HatBand", "M_Keeper_Hat", "M_Keeper_Hat"])
    mb.cap(cv[-1], "M_Keeper_Hat", peak=Vector((c.x, c.y + 0.01, 1.392)))   # 帽顶中间压出凹痕
    tilt = Matrix.Rotation(math.radians(9), 3, "X") @ Matrix.Rotation(math.radians(-4), 3, "Y")
    for v in mb.bm.verts:
        if v not in before:
            v.co = HEAD_PIVOT + tilt @ (v.co - HEAD_PIVOT)


def build_sleeve(mb, side):
    sh = Vector((SHOULDER.x * side, SHOULDER.y, SHOULDER.z))
    el, wr = (ELBOW_L, WRIST_L) if side > 0 else (ELBOW_R, WRIST_R)
    up, lo = el - sh, wr - el
    pts = [sh + Vector((-0.02 * side, 0, 0.01)), sh + up * 0.4, sh + up * 0.8, el, el + lo * 0.4, el + lo * 0.8, wr - lo.normalized() * 0.01]
    radii = [0.078, 0.072, 0.066, 0.064, 0.060, 0.056, 0.054]
    mb.tube(pts, radii, "M_Keeper_Sleeve", n=8, cap_start=True, jitter=0.004)
    cuff = [wr - lo.normalized() * 0.05, wr - lo.normalized() * 0.005]
    mb.tube(cuff, [0.062, 0.060], "M_Keeper_Cuff", n=8, cap_end=True)
    # 肘部的补丁
    n = (Vector((side, 0.6, 0))).normalized()
    p = el + n * 0.062
    mb.box(tuple(p), (0.05, 0.05, 0.06), "M_Keeper_CoatPatch", bevel=0.01)


def build_legs(mb):
    # 右腿：裤子 + 完整靴子
    pts = [HIP_R + Vector((0, 0, 0.03)), HIP_R, HIP_R.lerp(KNEE_R, 0.5), KNEE_R, KNEE_R.lerp(ANKLE_R, 0.5), ANKLE_R + Vector((0, 0, 0.08))]
    mb.tube(pts, [0.10, 0.098, 0.090, 0.078, 0.070, 0.068], "M_Keeper_Pants", n=10, cap_start=True, jitter=0.004)
    build_boot(mb, ANKLE_R, TOE_R, flat=True)

    # 左腿：裤管卷到膝下，小腿缠绷带，脚跟着地、脚尖朝上
    pts = [HIP_L + Vector((0, 0, 0.03)), HIP_L, HIP_L.lerp(KNEE_L, 0.5), KNEE_L, KNEE_L.lerp(ANKLE_L, 0.12)]
    mb.tube(pts, [0.10, 0.098, 0.090, 0.080, 0.078], "M_Keeper_Pants", n=10, cap_start=True, jitter=0.004)
    cuff_a, cuff_b = KNEE_L.lerp(ANKLE_L, 0.08), KNEE_L.lerp(ANKLE_L, 0.20)
    mb.tube([cuff_a, cuff_b], [0.088, 0.084], "M_Keeper_Pants", n=10, cap_start=True, cap_end=True, jitter=0.006)  # 卷起的裤脚
    shin = [KNEE_L.lerp(ANKLE_L, t) for t in (0.18, 0.30, 0.42, 0.54, 0.66, 0.78, 0.90)]
    bands = ["M_Keeper_Bandage", "M_Keeper_BandageDark"] * 3
    mb.tube(shin, [0.066, 0.070, 0.066, 0.068, 0.062, 0.062, 0.058], "M_Keeper_Bandage", n=8, cap_start=True, cap_end=True,
            jitter=0.005, mats_per_band=bands)
    # 渗血：小腿前侧两块
    d = (ANKLE_L - KNEE_L).normalized()
    front = (UP - d * UP.dot(d)).normalized()  # 胫骨前侧（伸直的腿上朝上）
    side = front.cross(d).normalized()
    for t, w, l in ((0.42, 0.055, 0.09), (0.60, 0.035, 0.05)):
        c = KNEE_L.lerp(ANKLE_L, t) + front * 0.064
        patch = [c + d * (l * 0.5 * math.cos(a)) + side * (w * 0.5 * math.sin(a)) + fb.jit(0.006) for a in (TAU * i / 7 for i in range(7))]
        pv = mb.loft([patch], "M_Keeper_Blood")
        mb.cap(pv[0], "M_Keeper_Blood", peak=c + front * 0.006)
    # 松开垂下的绷带尾
    tail0 = KNEE_L.lerp(ANKLE_L, 0.35) + Vector((0.06, 0, 0))
    mb.tube([tail0, tail0 + Vector((0.025, -0.01, -0.06)), tail0 + Vector((0.03, -0.03, -0.13))], [(0.006, 0.022), (0.005, 0.020), (0.004, 0.016)],
            "M_Keeper_Bandage", n=4, cap_end=True, forward=Vector((1, 0, 0)))
    build_boot(mb, ANKLE_L, TOE_L, flat=False)


def build_boot(mb, ankle, toe, flat):
    """短靴：靴筒包住脚踝，鞋身顺着脚背到脚尖，底下一层鞋底。"""
    shaft = [ankle + Vector((0, 0.01, 0.12)), ankle + Vector((0, 0.01, -0.04))]
    mb.tube(shaft, [0.074, 0.074], "M_Keeper_Leather", n=8, cap_start=True)
    heel = ankle + Vector((0, 0.035, -0.06)) if flat else ankle + Vector((0, 0.03, -0.075))
    d = (toe - heel).normalized()
    up = (UP - d * UP.dot(d)).normalized()
    pts = [heel, heel.lerp(toe, 0.35) + up * 0.02, heel.lerp(toe, 0.75) + up * 0.01, toe]
    mb.tube(pts, [(0.055, 0.060), (0.065, 0.062), (0.050, 0.058), (0.030, 0.045)], "M_Keeper_Leather", n=8, forward=up,
            cap_start=True, cap_end=True)
    sole = [p - up * 0.045 for p in pts]
    mb.tube(sole, [(0.014, 0.062), (0.014, 0.066), (0.014, 0.060), (0.012, 0.046)], "M_Keeper_Sole", n=6, forward=up,
            cap_start=True, cap_end=True)


def build_coat(mats):
    """长外套上身：前襟敞开、立领，开放面片加厚。"""
    mb = MeshBuilder("Keeper_Coat")
    centers = [c for c, *_ in TORSO]
    t_all = tangents(centers)
    spec = [  # (截面索引, 半宽, 前半径, 后半径, 前开口半角°)
        (5.3, 0.090, 0.075, 0.085, 35),   # 立领顶
        (5.0, 0.120, 0.080, 0.095, 30),
        (4.2, 0.230, 0.105, 0.150, 32),
        (3.2, 0.225, 0.140, 0.172, 26),
        (2.0, 0.200, 0.148, 0.155, 20),
        (1.0, 0.188, 0.140, 0.135, 16),
        (0.2, 0.198, 0.146, 0.140, 16),
    ]
    rings = []
    for idx, rx, rf, rb, half in spec:
        i0 = min(int(idx), len(centers) - 2)
        f = idx - i0
        c = centers[i0].lerp(centers[i0 + 1], f)
        t = t_all[i0].lerp(t_all[i0 + 1], f)
        a = math.radians(half)
        rings.append(spine_ring(c, t, rx, rf, rb, 20, math.pi / 2 + a, math.pi / 2 + TAU - a, closed=False, jitter=0.004))
    mb.loft(rings, "M_Keeper_Coat", closed=False)
    orient_outward(mb, torso_axis)
    obj = mb.build(mats, recalc=False)
    solidify(obj, mats, "M_Keeper_CoatInner", 0.012)
    # 背上的补丁
    pmb = MeshBuilder("Keeper_CoatPatch")
    pmb.box((0.07, 0.205, 0.80), (0.10, 0.02, 0.12), "M_Keeper_CoatPatch", bevel=0.01)
    patch = pmb.build(mats)
    fb.join([obj, patch], "Keeper_Coat")
    return obj


def build_skirt(mats):
    """外套下摆：两侧和背后垂过木箱边，前片搭在大腿上，下摆锯齿。"""
    mb = MeshBuilder("Keeper_Skirt")
    cy, z0 = 0.12, 0.53
    half = math.radians(14)
    seg = 22
    top, mid, bot = [], [], []
    for i in range(seg + 1):
        a = math.pi / 2 + half + (TAU - 2 * half) * i / seg
        u, v = math.cos(a), math.sin(a)                  # v > 0 为前方
        p_top = Vector((0.205 * u, cy - 0.150 * v, z0))
        hang = Vector((0.205 * u * 1.55, cy - 0.160 * v * 1.45, 0.33))
        lap = Vector((0.205 * u * 1.08, cy - 0.30 - 0.06 * v, 0.58 - (0.07 if u > 0 else 0.0)))
        w = fb_smooth(0.15, 0.75, v)
        p_bot = hang.lerp(lap, w)
        p_mid = p_top.lerp(p_bot, 0.5) + Vector((u * 0.035, -v * 0.02 * (1 - w), 0.02 * w))
        if i % 2:
            p_bot = p_bot + Vector((0, 0, 0.045 * (1 - w))) + (p_bot - p_top).normalized() * (-0.03 * w)
        p_bot += fb.jit(0.008)
        top.append(p_top + fb.jit(0.003))
        mid.append(p_mid + fb.jit(0.006))
        bot.append(p_bot)
    mb.loft([top, mid, bot], "M_Keeper_Skirt", closed=False)
    orient_outward(mb, lambda c: Vector((0, cy - 0.05, c.z + 0.1)))
    obj = mb.build(mats, recalc=False)
    return solidify(obj, mats, "M_Keeper_SkirtInner", 0.012)


def fb_smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def build_blanket(mats):
    """披在肩背上的旧毛毯：背后垂到腰，前面敞开，靠近下摆一道褪色红条。"""
    mb = MeshBuilder("Keeper_Blanket")
    seg = 18
    spec = [  # (z, 中心y, 半宽, 前半径, 后半径, 前开口半角°)
        (1.045, -0.02, 0.120, 0.085, 0.105, 70),
        (1.000, 0.010, 0.255, 0.120, 0.165, 58),
        (0.930, 0.040, 0.300, 0.150, 0.205, 58),
        (0.830, 0.070, 0.300, 0.160, 0.225, 66),
        (0.760, 0.085, 0.296, 0.160, 0.230, 72),
        (0.700, 0.090, 0.290, 0.150, 0.232, 78),
    ]
    rings = []
    for z, cy, rx, rf, rb, half in spec:
        a0 = math.pi / 2 + math.radians(half)
        ring = []
        for i in range(seg + 1):
            a = a0 + (TAU - 2 * math.radians(half)) * i / seg
            ca, sa = math.cos(a), math.sin(a)
            ring.append(Vector((rx * ca, cy - (rf if sa > 0 else rb) * sa, z)) + fb.jit(0.005))
        rings.append(ring)
    for j, p in enumerate(rings[-1]):
        p.z += (-0.05 if j % 2 else 0.0) + random.uniform(-0.02, 0.015)
    mb.loft(rings, "M_Keeper_Blanket", closed=False,
            mats_per_band=["M_Keeper_Blanket", "M_Keeper_Blanket", "M_Keeper_Blanket", "M_Keeper_BlanketStripe", "M_Keeper_Blanket"])
    orient_outward(mb, torso_axis)
    obj = mb.build(mats, recalc=False)
    return solidify(obj, mats, "M_Keeper_BlanketInner", 0.014)


# ---------- 道具：木箱、灯杆、小灯 ----------

def cuboid(mb, center, size, mat, tilt=None):
    c = Vector(center)
    hx, hy, hz = (s * 0.5 for s in size)
    corners = [Vector((x, y, z)) for z in (-hz, hz) for y in (-hy, hy) for x in (-hx, hx)]
    if tilt is not None:
        corners = [tilt @ p for p in corners]
    v = [mb.bm.verts.new(c + p + fb.jit(0.003)) for p in corners]
    m = mb.mat(mat)
    for face in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        mb.bm.faces.new([v[i] for i in face]).material_index = m


def build_props(mats):
    mb = MeshBuilder("Keeper_Props")
    W, D, H = CRATE_W, CRATE_D, CRATE_H
    cx, cy = CRATE_C.x, CRATE_C.y
    for k in range(3):
        z0 = k * H / 3
        cuboid(mb, (cx, cy, z0 + H / 6), (W, D, H / 3 - 0.012), "M_Keeper_Wood")
        cuboid(mb, (cx, cy, z0 + H / 3 - 0.003), (W - 0.01, D - 0.01, 0.014), "M_Keeper_WoodDark")
    for sx in (-1, 1):
        for sy in (-1, 1):
            cuboid(mb, (cx + sx * (W / 2 - 0.02), cy + sy * (D / 2 - 0.02), H / 2), (0.06, 0.06, H + 0.01), "M_Keeper_WoodDark")
    cuboid(mb, (cx, cy, H + 0.012), (W + 0.02, D + 0.02, 0.026), "M_Keeper_Wood")
    # 灯杆：木杆 + 铁箍 + 顶端弯钩
    axis = STAFF_TOP - STAFF_BOTTOM
    pts = [STAFF_BOTTOM + axis * t for t in (0.0, 0.3, 0.6, 0.85, 1.0)]
    mb.tube(pts, [0.016, 0.019, 0.019, 0.018, 0.017], "M_Keeper_Staff", n=6, cap_start=True, jitter=0.002)
    for t in (0.0, 0.62, 0.97):
        p = STAFF_BOTTOM + axis * t
        mb.tube([p - axis.normalized() * 0.02, p + axis.normalized() * 0.02], [0.022, 0.022], "M_Keeper_Iron", n=6, cap_start=True, cap_end=True)
    crook = [STAFF_TOP - axis.normalized() * 0.02, STAFF_TOP + Vector((0, -0.02, 0.05)), STAFF_TOP + Vector((0, -0.08, 0.075)),
             STAFF_TOP + Vector((0, -0.14, 0.06)), LAMP_HOOK + Vector((0, 0.01, 0.03)), LAMP_HOOK]
    mb.tube(crook, [0.013, 0.012, 0.011, 0.010, 0.009, 0.008], "M_Keeper_Iron", n=5, cap_end=True, forward=Vector((1, 0, 0)))
    return mb.build(mats)


def build_lamp(mats):
    """挂在灯杆钩上的小提灯，点着。原点在挂环，灯身向下垂。"""
    mb = MeshBuilder("Keeper_Lamp")
    M, G, F = "M_Keeper_LampMetal", "M_Keeper_LampGlass", "M_Keeper_LampFlame"
    loop = [Vector((0.0, 0.018 * math.sin(TAU * i / 8), -0.018 - 0.018 * math.cos(TAU * i / 8))) for i in range(9)]
    mb.tube(loop, [0.004] * 9, M, n=4, forward=Vector((1, 0, 0)))
    top = [ellipse_ring(0, 0, z, r, r, r, 8) for z, r in ((-0.036, 0.010), (-0.042, 0.018), (-0.058, 0.026), (-0.074, 0.050), (-0.080, 0.052))]
    mb.loft(top, M, cap_start=True, cap_end=True)
    glass = [ellipse_ring(0, 0, z, r, r, r, 8) for z, r in ((-0.080, 0.036), (-0.110, 0.044), (-0.160, 0.044), (-0.185, 0.038))]
    mb.loft(glass, G)
    flame = [ellipse_ring(0, 0, z, r, r, r, 6) for z, r in ((-0.168, 0.012), (-0.150, 0.016))]
    fv = mb.loft(flame, F, cap_start=True)
    mb.cap(fv[-1], F, peak=Vector((0, 0, -0.112)))
    for i in range(4):
        a = TAU * (i + 0.5) / 4
        p = lambda z, r: Vector((r * math.cos(a), r * math.sin(a), z))
        mb.tube([p(-0.080, 0.048), p(-0.11, 0.050), p(-0.15, 0.050), p(-0.188, 0.046)], [0.004] * 4, M, n=4, forward=UP)
    base = [ellipse_ring(0, 0, z, r, r, r, 8) for z, r in ((-0.185, 0.050), (-0.195, 0.054), (-0.222, 0.054), (-0.228, 0.046))]
    mb.loft(base, M, cap_end=True)
    return mb.build(mats)


# ---------- 蒙皮分组属性 ----------

def tag_groups(obj):
    """按面材质给每个顶点写入蒙皮分组（int 属性 skin_group），烘焙换材质后 lampkeeper_rig 仍能读到。"""
    mesh = obj.data
    names = [m.name for m in mesh.materials]
    values = [0] * len(mesh.vertices)
    for p in mesh.polygons:
        g = GROUP_INDEX[names[p.material_index]]
        for vi in p.vertices:
            values[vi] = g
    attr = mesh.attributes.new("skin_group", "INT", "POINT")
    attr.data.foreach_set("value", values)


# ---------- 渲染 ----------

def render_views(scene, folder):
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("RenderCam")
    cam = bpy.data.objects.new("RenderCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    target = Vector((0.05, -0.15, 1.0))
    views = {
        "front": (Vector((0.05, -6, 1.0)), True),
        "side": (Vector((-6, -0.15, 1.0)), True),
        "back": (Vector((0.05, 6, 1.0)), True),
        "three_quarter": (Vector((-3.2, -4.2, 2.6)), False),
        "game_angle": (Vector((0.05, -3.6, 6.1)), False),   # 与游戏镜头俯角（55°）接近
    }
    for name, (pos, ortho) in views.items():
        cam.location = pos
        cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
        cam_data.type = "ORTHO" if ortho else "PERSP"
        cam_data.ortho_scale = 2.25
        cam_data.lens = 85 if name != "game_angle" else 70
        scene.render.filepath = os.path.join(folder, f"Lampkeeper_{name}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


def render_poses(scene, rig, folder):
    os.makedirs(folder, exist_ok=True)
    cam_data = bpy.data.cameras.new("PoseCam")
    cam = bpy.data.objects.new("PoseCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.25
    target = Vector((0.05, -0.15, 1.0))
    for frame in (0, 30, 60, 90):
        rig.animation_data.action = bpy.data.actions["Idle"]
        scene.frame_set(frame)
        for view, pos in (("front", Vector((0.05, -6, 1.0))), ("side", Vector((-6, -0.15, 1.0)))):
            cam.location = pos
            cam.rotation_euler = (target - pos).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(folder, f"Idle_{frame:03d}_{view}.png")
            bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


# ---------- 主流程 ----------

def main():
    random.seed(23)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    mats = fb.make_materials()

    root = bpy.data.objects.new("Lampkeeper", None)
    scene.collection.objects.link(root)

    body = build_body(mats)
    clothes = build_clothes(mats)
    props = build_props(mats)
    lamp = build_lamp(mats)
    lamp.location = LAMP_HOOK
    for obj in (body, clothes, props, lamp):
        obj.parent = root
        tag_groups(obj)

    for mat in bpy.data.materials:
        if mat.name in fb.WEAR:
            fb.make_wear_shader(mat)
    for obj in (body, clothes, props, lamp):
        fb.bake_object(obj, TEX_DIR)
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = lamp
    lamp.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lamp.select_set(False)

    rig = lampkeeper_rig.setup(root, body, clothes, props, lamp, GROUP_NAMES, LAMP_HOOK, LAMP_LENGTH)

    fb.setup_render(scene)
    render_views(scene, os.path.join(OUT_DIR, "renders"))
    render_poses(scene, rig, os.path.join(OUT_DIR, "renders", "poses"))
    rig.animation_data.action = bpy.data.actions["Idle"]
    scene.frame_set(0)

    for mat in bpy.data.materials:
        if mat.name in fb.PALETTE:
            mat.use_fake_user = True
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "Lampkeeper.blend"))

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
    for o in (body, clothes, props, lamp):
        print(f"[Lampkeeper] {o.name}: {sum(len(p.vertices) - 2 for p in o.data.polygons)} tris")
    print(f"[Lampkeeper] fbx={FBX_PATH}")


if __name__ == "__main__":
    main()
