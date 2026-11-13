"""寻火者骨骼、蒙皮、手枪与动画。由 seeker_build.py 在烘焙贴图之后调用。

约定（与建模一致）：Blender Z 向上，角色面朝 -Y，角色左侧为 +X（骨骼后缀 .L）。
动画里的旋转都写成"静止姿势下骨架空间的相对旋转"，再换算成骨骼本地四元数，
这样脚本里可以直接按世界方向思考（前 = -Y，上 = +Z）。
"""

import math

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 30
DOWN = Vector((0, 0, -1))
FORWARD = Vector((0, -1, 0))


# ---------- 骨骼 ----------

def _arm_points(s):
    shoulder = Vector((0.205 * s, 0.005, 1.38))
    elbow = Vector((0.285 * s, 0.0, 1.13))
    wrist = Vector((0.345 * s, -0.03, 0.89))
    hand_tip = wrist + (wrist - elbow).normalized() * 0.15
    return shoulder, elbow, wrist, hand_tip


def bone_specs():
    specs = [
        ("root", (0, 0, 0), (0, -0.2, 0), None),
        ("hips", (0, 0, 0.88), (0, 0, 1.0), "root"),
        ("spine", (0, 0, 1.0), (0, 0, 1.18), "hips"),
        ("chest", (0, 0, 1.18), (0, 0, 1.42), "spine"),
        ("neck", (0, 0.01, 1.44), (0, 0.01, 1.54), "chest"),
        ("head", (0, 0.01, 1.54), (0, 0.01, 1.78), "neck"),
    ]
    for s, side in ((1, "L"), (-1, "R")):
        shoulder, elbow, wrist, tip = _arm_points(s)
        specs += [
            (f"shoulder.{side}", (0.04 * s, 0.0, 1.40), tuple(shoulder), "chest"),
            (f"upper_arm.{side}", tuple(shoulder), tuple(elbow), f"shoulder.{side}"),
            (f"forearm.{side}", tuple(elbow), tuple(wrist), f"upper_arm.{side}"),
            (f"hand.{side}", tuple(wrist), tuple(tip), f"forearm.{side}"),
            (f"thigh.{side}", (0.095 * s, 0, 0.86), (0.103 * s, -0.005, 0.50), "hips"),
            (f"shin.{side}", (0.103 * s, -0.005, 0.50), (0.1045 * s, 0, 0.10), f"thigh.{side}"),
            (f"foot.{side}", (0.1045 * s, 0, 0.10), (0.1045 * s, -0.12, 0.03), f"shin.{side}"),
            (f"toe.{side}", (0.1045 * s, -0.12, 0.03), (0.1045 * s, -0.19, 0.03), f"foot.{side}"),
        ]
    # 斗篷摆动链：背、左右侧、左右前襟，每条两节
    chains = {
        "cloak_back": [(0, 0.24, 1.30), (0, 0.28, 0.92), (0, 0.31, 0.45)],
        "cloak_side.L": [(0.34, 0.03, 1.28), (0.40, 0.03, 0.92), (0.45, 0.04, 0.45)],
        "cloak_side.R": [(-0.34, 0.03, 1.28), (-0.40, 0.03, 0.92), (-0.45, 0.04, 0.45)],
        "cloak_front.L": [(0.18, -0.17, 1.30), (0.31, -0.14, 0.92), (0.44, -0.05, 0.45)],
        "cloak_front.R": [(-0.18, -0.17, 1.30), (-0.31, -0.14, 0.92), (-0.44, -0.05, 0.45)],
    }
    for name, (a, b, c) in chains.items():
        base, _, side = name.partition(".")
        suffix = f".{side}" if side else ""
        specs.append((f"{base}_1{suffix}", a, b, "chest"))
        specs.append((f"{base}_2{suffix}", b, c, f"{base}_1{suffix}"))
    return specs


def build_armature(parent):
    data = bpy.data.armatures.new("Seeker_Rig")
    rig = bpy.data.objects.new("Seeker_Rig", data)
    bpy.context.scene.collection.objects.link(rig)
    rig.parent = parent
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, par in bone_specs():
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = Vector(head), Vector(tail)
        eb.roll = 0.0
        if par:
            eb.parent = data.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


# ---------- 蒙皮 ----------

def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def _side_ok(bone, x, band):
    if bone.endswith(".L"):
        return x > -band
    if bone.endswith(".R"):
        return x < band
    return True


def skin(obj, rig, bones, band=0.03, power=4.0, bias=None, rule=None, top=4):
    """按到骨骼线段的距离反比加权；左右骨骼只影响本侧顶点，中线附近 band 内两侧都可用。"""
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones if b.name in bones}
    groups = {name: obj.vertex_groups.new(name=name) for name in segs}
    world = obj.matrix_world
    for v in obj.data.vertices:
        p = world @ v.co
        weights = rule(p) if rule else None
        if not weights:
            scored = []
            for name, (a, b) in segs.items():
                if not _side_ok(name, p.x, band):
                    continue
                d = _seg_dist(p, a, b) + 0.01
                scale = bias.get(name, 1.0) if bias else 1.0
                scored.append((scale / d ** power, name))
            scored.sort(reverse=True)
            scored = scored[:top]
            total = sum(w for w, _ in scored)
            weights = {name: w / total for w, name in scored}
        for name, w in weights.items():
            if w > 0.002:
                groups[name].add([v.index], w, "REPLACE")
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mw = obj.matrix_world.copy()
    obj.parent = rig
    obj.matrix_world = mw


BODY_BONES = ["hips", "spine", "chest", "neck", "head"] + [
    f"{b}.{s}" for s in "LR" for b in ("shoulder", "upper_arm", "forearm", "hand", "thigh", "shin", "foot", "toe")]
CLOAK_BONES = ["chest", "neck", "head", "shoulder.L", "shoulder.R", "upper_arm.L", "upper_arm.R"] + [
    f"cloak_{c}_{i}{s}" for c, s in (("back", ""), ("side", ".L"), ("side", ".R"), ("front", ".L"), ("front", ".R")) for i in (1, 2)]


def _smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def hood_rule(p):
    # 兜帽和领口：只跟脖子、头走
    if p.z > 1.50:
        t = _smooth(1.52, 1.64, p.z)
        return {"head": t, "neck": 1 - t} if t < 0.999 else {"head": 1.0}
    return None


def skin_all(rig, body, cloak, pack):
    # 身体用高次幂、只取最近两根骨骼：关节处平滑过渡，其余部位基本刚性，避免靴扣、裤脚被邻骨拉扯
    skin(body, rig, BODY_BONES, power=8.0, top=2, bias={"hips": 0.8, "spine": 0.9})
    skin(cloak, rig, CLOAK_BONES, band=0.06, power=3.0, rule=hood_rule,
         bias={"chest": 1.2, "neck": 0.6, "head": 0.4, "shoulder.L": 0.8, "shoulder.R": 0.8,
               "upper_arm.L": 0.35, "upper_arm.R": 0.35})
    skin(pack, rig, ["chest"], rule=lambda p: {"chest": 1.0})


# ---------- 姿势工具 ----------

def rot(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


def rx(deg):
    """绕 X：正值让向上的骨骼向前（-Y）倒，让向下的骨骼向后（+Y）摆。"""
    return rot((1, 0, 0), deg)


def ry(deg):
    """绕 Y：正值让向上的骨骼倒向 +X。"""
    return rot((0, 1, 0), deg)


def rz(deg):
    return rot((0, 0, 1), deg)


def toward(src, dst, deg):
    """把方向 src 朝 dst 转 deg 度。"""
    axis = Vector(src).cross(Vector(dst))
    if axis.length < 1e-6:
        return Quaternion()
    return Quaternion(axis.normalized(), math.radians(deg))


class Rig:
    def __init__(self, rig):
        self.rig = rig
        self.bones = rig.data.bones
        self.rest = {b.name: b.matrix_local.to_quaternion() for b in self.bones}
        self.dir = {b.name: (b.tail_local - b.head_local).normalized() for b in self.bones}
        self.head = {b.name: b.head_local.copy() for b in self.bones}
        self.len = {b.name: b.length for b in self.bones}

    def two_bone(self, pose, upper, lower, end, target, pole, end_dir=None, parent_q=Quaternion()):
        """解析两节 IK：目标、极向量都是静止骨架空间坐标（随父骨骼一起动）。"""
        a = self.head[upper]
        l1, l2 = self.len[upper], self.len[lower]
        d = Vector(target) - a
        dist = min(d.length, l1 + l2 - 1e-4)
        dn = d.normalized()
        cos_a = max(-1.0, min(1.0, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist)))
        n = dn.cross(Vector(pole) - a)
        m = n.cross(dn).normalized()
        ang = math.acos(cos_a)
        elbow = a + (dn * math.cos(ang) + m * math.sin(ang)) * l1
        wrist = a + dn * dist
        q1 = self.dir[upper].rotation_difference((parent_q.inverted() @ (elbow - a)).normalized())
        pose[upper] = q1
        acc = parent_q @ q1
        q2 = self.dir[lower].rotation_difference((acc.inverted() @ (wrist - elbow)).normalized())
        pose[lower] = q2
        if end_dir is not None:
            acc = acc @ q2
            pose[end] = self.dir[end].rotation_difference((acc.inverted() @ Vector(end_dir)).normalized())

    def apply(self, pose, frame, locs=None):
        for name, q in pose.items():
            pb = self.rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            b = self.rest[name]
            pb.rotation_quaternion = b.inverted() @ q @ b
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        for name, v in (locs or {}).items():
            pb = self.rig.pose.bones[name]
            pb.location = self.rest[name].inverted() @ Vector(v)
            pb.keyframe_insert("location", frame=frame)


# ---------- 动作 ----------

AIM_R = Vector((-0.035, -0.36, 1.12))
AIM_L = Vector((0.025, -0.33, 1.085))


def all_bones(r):
    return {b.name: Quaternion() for b in r.bones}


def aim_arms(r, pose, right=AIM_R, left=AIM_L, right_dir=(0, -1, 0.03), left_dir=(-0.7, -0.7, 0.0)):
    r.two_bone(pose, "upper_arm.R", "forearm.R", "hand.R", right, (-0.45, 0.15, 0.85), right_dir)
    r.two_bone(pose, "upper_arm.L", "forearm.L", "hand.L", left, (0.45, 0.15, 0.85), left_dir)


def cloak(pose, back=0.0, side=0.0, front_l=0.0, front_r=0.0, trail=None, lower_scale=0.7):
    """斗篷摆动：正值向外/向后张开；trail 为拖尾方向（移动的反方向）及角度。"""
    outward = {
        "cloak_back": Vector((0, 1, 0)),
        "cloak_side.L": Vector((1, 0, 0)), "cloak_side.R": Vector((-1, 0, 0)),
        "cloak_front.L": Vector((0.8, -0.6, 0)), "cloak_front.R": Vector((-0.8, -0.6, 0)),
    }
    amount = {"cloak_back": back, "cloak_side.L": side, "cloak_side.R": side,
              "cloak_front.L": front_l, "cloak_front.R": front_r}
    for chain, out in outward.items():
        base, _, s = chain.partition(".")
        suf = f".{s}" if s else ""
        q = toward(DOWN, out, amount[chain])
        if trail is not None:
            q = toward(DOWN, trail[0], trail[1]) @ q
        pose[f"{base}_1{suf}"] = q
        pose[f"{base}_2{suf}"] = toward(DOWN, out, amount[chain] * lower_scale) if trail is None else \
            toward(DOWN, trail[0], trail[1] * lower_scale) @ toward(DOWN, out, amount[chain] * lower_scale)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    return act


def make_idle(r):
    new_action(r.rig, "Idle")
    for f in range(0, 61, 5):
        t = f / 60.0
        pose = all_bones(r)
        breath = math.sin(t * math.tau)
        pose["chest"] = rx(1.2 * breath)
        pose["head"] = rx(4.0)
        aim_arms(r, pose)
        cloak(pose, back=2 + 1.5 * breath, side=2 + 1.5 * math.sin(t * math.tau + 1), front_l=3, front_r=3)
        # 微屈膝站姿
        for s in "LR":
            pose[f"thigh.{s}"] = rx(-6)
            pose[f"shin.{s}"] = rx(12)
            pose[f"foot.{s}"] = rx(-6)
        r.apply(pose, f, {"hips": (0, 0, -0.015 + 0.004 * breath)})


def make_run(r, name, move):
    """move：静止骨架空间里的移动方向（XY）。前进 = (0,-1)。"""
    new_action(r.rig, name)
    mv = Vector((move[0], move[1], 0)).normalized()
    fwd = -mv.y   # 前后分量（前为正）
    lat = mv.x    # 左右分量（+X 为正）
    frames = 20
    for f in range(0, frames + 1, 2):
        ph = f / frames
        pose = all_bones(r)
        for s, off in (("L", 0.0), ("R", 0.5)):
            p = (ph + off) * math.tau
            swing = math.sin(p)
            bend = 18 + 62 * max(0.0, math.cos(p)) ** 1.5
            lift = bend * 0.35 * abs(lat)
            thigh = ry(-lat * 16 * swing) @ rx(-(fwd * 38 * swing + lift))
            pose[f"thigh.{s}"] = thigh
            pose[f"shin.{s}"] = rx(bend)
            pose[f"foot.{s}"] = rx(-(bend * 0.45) + fwd * 10 * swing)
        bob = math.cos(ph * 2 * math.tau)
        lean = 9
        pose["hips"] = rx(lean * fwd * 0.4) @ ry(lean * lat * 0.4) @ rz(6 * math.sin(ph * math.tau) * fwd)
        pose["spine"] = rx(lean * fwd * 0.6) @ ry(lean * lat * 0.6) @ rz(-6 * math.sin(ph * math.tau) * fwd)
        pose["chest"] = rx(-lean * fwd * 0.3)  # 上身回正，保持瞄准
        pose["head"] = rx(4)
        aim_arms(r, pose)
        trail = (Vector((-mv.x, -mv.y, 0)), (12 if abs(fwd) > 0.5 else 8) + 3 * bob)
        legs = math.sin(ph * math.tau)
        cloak(pose, back=4, side=5 + 3 * bob, front_l=6 + 10 * max(0.0, legs * fwd), front_r=6 + 10 * max(0.0, -legs * fwd),
              trail=trail)
        r.apply(pose, f, {"hips": (0, 0, -0.045 + 0.025 * bob)})


def make_fire(r):
    new_action(r.rig, "Fire")
    keys = [(0, 0.0), (1, 1.0), (3, 0.6), (8, 0.0)]
    for f, k in keys:
        pose = all_bones(r)
        pose["chest"] = rx(-3 * k)
        pose["head"] = rx(4)
        aim_arms(r, pose, right=AIM_R + Vector((0, 0.035, 0.05)) * k, left=AIM_L + Vector((0, 0.03, 0.045)) * k,
                 right_dir=(0, -1, 0.03 + 0.45 * k))
        cloak(pose, back=2, side=2, front_l=3, front_r=3)
        r.apply(pose, f)


def make_reload(r):
    new_action(r.rig, "Reload")
    keys = [
        (0, AIM_R, (0, -1, 0.03), AIM_L, (-0.7, -0.7, 0)),
        (6, Vector((-0.07, -0.30, 1.03)), (0.35, -0.8, 0.35), Vector((0.06, -0.26, 1.0)), (-0.6, -0.6, -0.2)),
        (12, Vector((-0.07, -0.30, 1.03)), (0.35, -0.8, 0.35), Vector((0.14, -0.12, 0.93)), (0, -0.3, -1)),
        (18, Vector((-0.07, -0.30, 1.03)), (0.35, -0.8, 0.35), Vector((-0.03, -0.27, 0.97)), (-0.3, -0.6, 0.6)),
        (24, AIM_R, (0, -1, 0.03), AIM_L, (-0.7, -0.7, 0)),
        (30, AIM_R, (0, -1, 0.03), AIM_L, (-0.7, -0.7, 0)),
    ]
    for f, rt, rd, lt, ld in keys:
        pose = all_bones(r)
        pose["head"] = rx(10 if 4 < f < 24 else 4)
        aim_arms(r, pose, rt, lt, rd, ld)
        cloak(pose, back=2, side=2, front_l=3, front_r=3)
        r.apply(pose, f)


def make_roll(r):
    """前滚翻，原地动画：移动由 CharacterController 负责，动画驱动组件把模型转向翻滚方向。"""
    new_action(r.rig, "Roll")
    frames = 18
    for f in range(0, frames + 1):
        t = f / frames
        tuck = _smooth(0.0, 0.2, t) * (1 - _smooth(0.8, 1.0, t))
        spin = 360 * _smooth(0.12, 0.85, t)
        pose = all_bones(r)
        pose["hips"] = rx(spin)
        pose["spine"] = rx(45 * tuck)
        pose["chest"] = rx(40 * tuck)
        pose["neck"] = rx(35 * tuck)
        for s, sx in (("L", 1), ("R", -1)):
            pose[f"thigh.{s}"] = rx(-115 * tuck)
            pose[f"shin.{s}"] = rx(135 * tuck)
            pose[f"foot.{s}"] = rx(-30 * tuck)
        # 手臂：从瞄准收到抱膝
        aim = all_bones(r)
        aim_arms(r, aim)
        hug = all_bones(r)
        r.two_bone(hug, "upper_arm.R", "forearm.R", "hand.R", (-0.14, -0.30, 1.02), (-0.5, 0.1, 1.0), (0.3, -0.5, -0.8))
        r.two_bone(hug, "upper_arm.L", "forearm.L", "hand.L", (0.14, -0.30, 1.02), (0.5, 0.1, 1.0), (-0.3, -0.5, -0.8))
        for b in ("upper_arm", "forearm", "hand"):
            for s in "LR":
                n = f"{b}.{s}"
                pose[n] = aim[n].slerp(hug[n], tuck)
        # 斗篷向身体收拢，裹住蜷起的身体
        cloak(pose, back=-55 * tuck, side=-30 * tuck, front_l=-30 * tuck, front_r=-30 * tuck, lower_scale=0.9)
        r.apply(pose, f, {"hips": (0, 0, -0.30 * tuck)})


def make_actions(r):
    make_idle(r)
    make_run(r, "Run_F", (0, -1))
    make_run(r, "Run_B", (0, 1))
    make_run(r, "Run_L", (1, 0))   # 角色左侧 = +X
    make_run(r, "Run_R", (-1, 0))
    make_fire(r)
    make_reload(r)
    make_roll(r)


# ---------- 手枪 ----------

def build_pistol(r, mats_factory):
    """在瞄准姿势下按世界方向建手枪，再换算回静止姿势，100% 蒙皮到右手。"""
    import bmesh
    rig = r.rig
    rig.animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    pb = rig.pose.bones["hand.R"]
    to_rest = rig.data.bones["hand.R"].matrix_local @ pb.matrix.inverted()

    bm = bmesh.new()

    def box(c, size):
        geom = bmesh.ops.create_cube(bm, size=1.0)
        for v in geom["verts"]:
            v.co = Vector((c[0] + v.co.x * size[0], c[1] + v.co.y * size[1], c[2] + v.co.z * size[2]))

    grip_top = AIM_R + Vector((0.0, -0.06, 0.012))
    box(grip_top + Vector((0, -0.08, 0.0)), (0.026, 0.19, 0.034))        # 套筒（枪口约在 y=-0.55）
    box(grip_top + Vector((0, 0.01, -0.05)), (0.024, 0.04, 0.08))        # 握把
    box(grip_top + Vector((0, -0.04, -0.03)), (0.01, 0.035, 0.02))       # 扳机护圈
    for v in bm.verts:
        v.co = to_rest @ v.co
    mesh = bpy.data.meshes.new("Seeker_Pistol")
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new("Seeker_Pistol", mesh)
    bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(mats_factory())
    obj.vertex_groups.new(name="hand.R").add(list(range(len(mesh.vertices))), 1.0, "REPLACE")
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    obj.parent = rig
    return obj


def pistol_material():
    mat = bpy.data.materials.new("M_Seeker_Pistol")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (0.05, 0.05, 0.05, 1)
    bsdf.inputs["Metallic"].default_value = 0.7
    bsdf.inputs["Roughness"].default_value = 0.45
    return mat


def setup(root, body, cloak_obj, pack):
    scene = bpy.context.scene
    scene.render.fps = FPS
    rig = build_armature(root)
    skin_all(rig, body, cloak_obj, pack)
    r = Rig(rig)
    make_actions(r)
    pistol = build_pistol(r, pistol_material)
    rig.animation_data.action = bpy.data.actions["Idle"]
    return rig, pistol
