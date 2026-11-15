"""失火者骨骼、蒙皮与动画。由 firelost_build.py 在烘焙贴图之后调用。

约定（与建模一致）：Blender Z 向上，角色面朝 -Y，角色左侧为 +X（骨骼后缀 .L）。
动画里的旋转都写成"静止姿势下骨架空间的相对旋转"，再换算成骨骼本地四元数，
这样脚本里可以直接按世界方向思考（前 = -Y，上 = +Z）。工具函数与寻火者 seeker_rig.py 同一套写法。

动作（原地，移动由 NavMeshAgent 负责）：
    Idle / Walk / Run 循环；Attack_R / Attack_L 爪击，第 ATTACK_IMPACT_FRAME 帧命中；Hit 受击；Death 倒地后停在最后一帧。
"""

import math

import bpy
from mathutils import Quaternion, Vector

FPS = 30
DOWN = Vector((0, 0, -1))
ATTACK_IMPACT_FRAME = 11   # 0.367s：与 EnemyConfig.attackWindup(0.35s) 对齐，由 Unity 驱动脚本按这个时间提前起手

# 与 firelost_build.py 的姿态常量一致
SHOULDER = Vector((0.250, -0.120, 1.39))
ELBOW = Vector((0.360, -0.130, 1.02))
WRIST = Vector((0.345, -0.240, 0.64))
HIP = Vector((0.110, 0.030, 0.88))
KNEE = Vector((0.150, -0.085, 0.50))
ANKLE = Vector((0.165, 0.020, 0.17))
HEAD_CENTER = Vector((0, -0.355, 1.53))


def side_vec(v, s):
    return Vector((v.x * s, v.y, v.z))


# ---------- 骨骼 ----------

def bone_specs(lantern_hook, lantern_tip):
    specs = [
        ("root", (0, 0, 0), (0, -0.2, 0), None),
        ("hips", (0, 0.03, 0.88), (0, 0.03, 1.0), "root"),
        ("spine", (0, 0.03, 1.0), (0, 0.005, 1.19), "hips"),
        ("chest", (0, 0.005, 1.19), (0, -0.13, 1.42), "spine"),
        ("neck", (0, -0.15, 1.43), (0, -0.28, 1.47), "chest"),
        ("head", (0, -0.28, 1.47), (0, -0.33, 1.70), "neck"),
        ("lantern", tuple(lantern_hook), tuple(lantern_tip), "hips"),
    ]
    for s, side in ((1, "L"), (-1, "R")):
        sh, el, wr = side_vec(SHOULDER, s), side_vec(ELBOW, s), side_vec(WRIST, s)
        d = (wr - el).normalized()
        palm = wr + d * 0.085
        ankle = Vector((ANKLE.x * s, 0.01, 0.10))
        specs += [
            (f"shoulder.{side}", (0.05 * s, -0.11, 1.40), tuple(sh), "chest"),
            (f"upper_arm.{side}", tuple(sh), tuple(el), f"shoulder.{side}"),
            (f"forearm.{side}", tuple(el), tuple(wr), f"upper_arm.{side}"),
            (f"hand.{side}", tuple(wr), tuple(palm), f"forearm.{side}"),
            (f"fingers.{side}", tuple(palm), tuple(palm + d * 0.13), f"hand.{side}"),
            (f"thigh.{side}", tuple(side_vec(HIP, s)), tuple(side_vec(KNEE, s)), "hips"),
            (f"shin.{side}", tuple(side_vec(KNEE, s)), tuple(ankle), f"thigh.{side}"),
            (f"foot.{side}", tuple(ankle), (ANKLE.x * s, -0.14, 0.03), f"shin.{side}"),
            (f"toe.{side}", (ANKLE.x * s, -0.14, 0.03), (ANKLE.x * s, -0.22, 0.03), f"foot.{side}"),
        ]
    return specs


def build_armature(parent, lantern_hook, lantern_tip):
    data = bpy.data.armatures.new("Firelost_Rig")
    rig = bpy.data.objects.new("Firelost_Rig", data)
    bpy.context.scene.collection.objects.link(rig)
    rig.parent = parent
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, par in bone_specs(lantern_hook, lantern_tip):
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = Vector(head), Vector(tail)
        eb.roll = 0.0
        if par:
            eb.parent = data.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


# ---------- 蒙皮 ----------
# 身体、服装都是很多独立网格块拼成的（躯干、手臂、角质片、手指……）。
# 先按连通块决定"允许跟随哪些骨骼"，再在这些骨骼间按距离加权；小块（角质片、爪、扣子）整块刚性跟随。

def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def _islands(mesh):
    parent = list(range(len(mesh.vertices)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in mesh.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    groups = {}
    for i in range(len(mesh.vertices)):
        groups.setdefault(find(i), []).append(i)
    return list(groups.values())


def _weights(p, segs, bones, power, top):
    scored = sorted(((1.0 / (_seg_dist(p, *segs[b]) + 0.01) ** power, b) for b in bones), reverse=True)[:top]
    total = sum(w for w, _ in scored)
    return {b: w / total for w, b in scored}


def skin(obj, rig, classify, power=6.0, top=2, rigid_below=24):
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
    groups = {}
    world = obj.matrix_world
    mesh = obj.data
    for island in _islands(mesh):
        pts = [world @ mesh.vertices[i].co for i in island]
        centre = sum(pts, Vector()) / len(pts)
        bones = classify(centre, pts)
        if isinstance(bones, dict):          # 直接指定权重
            fixed = bones
        elif len(island) < rigid_below:      # 小块：按中心整体刚性跟随
            fixed = _weights(centre, segs, bones, power, top)
        else:
            fixed = None
        for i, p in zip(island, pts):
            w = fixed or _weights(p, segs, bones, power, top)
            for name, value in w.items():
                if value < 0.002:
                    continue
                if name not in groups:
                    groups[name] = obj.vertex_groups.new(name=name)
                groups[name].add([i], value, "REPLACE")
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mw = obj.matrix_world.copy()
    obj.parent = rig
    obj.matrix_world = mw


def _side(x):
    return "L" if x > 0 else "R"


def classify_body(c, pts):
    if (c - HEAD_CENTER).length < 0.2:
        return {"head": 1.0}
    if c.z < 0.2:                                   # 左脚露出的脚趾、脚背
        s = _side(c.x)
        return [f"foot.{s}", f"toe.{s}"]
    if abs(c.x) < 0.14:                             # 躯干、斜方肌角质
        return ["hips", "spine", "chest", "neck", "shoulder.L", "shoulder.R"]
    s = _side(c.x)
    if c.z < 0.60:                                  # 手指、爪
        return [f"hand.{s}", f"fingers.{s}"]
    return ["chest", f"shoulder.{s}", f"upper_arm.{s}", f"forearm.{s}", f"hand.{s}"]


def classify_clothes(c, pts):
    if c.z > 0.97 and max(p.z for p in pts) > 1.1:   # 背心：只跟躯干和肩
        return ["hips", "spine", "chest", "shoulder.L", "shoulder.R"]
    if min(p.z for p in pts) > 0.95 and max(p.z for p in pts) < 1.1:   # 腰带、扣子、挂灯皮扣
        return {"hips": 1.0}
    if c.z < 0.2:                                    # 靴子
        s = _side(c.x)
        return [f"shin.{s}", f"foot.{s}", f"toe.{s}"]
    if abs(c.x) < 0.06:                              # 胯部
        return ["hips", "thigh.L", "thigh.R"]
    s = _side(c.x)
    return ["hips", f"thigh.{s}", f"shin.{s}", f"foot.{s}"]


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
    """绕 Z：正值俯视逆时针，让角色右肩（-X）转向前方。"""
    return rot((0, 0, 1), deg)


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


class Rig:
    def __init__(self, rig):
        self.rig = rig
        self.bones = rig.data.bones
        self.rest = {b.name: b.matrix_local.to_quaternion() for b in self.bones}
        self.dir = {b.name: (b.tail_local - b.head_local).normalized() for b in self.bones}
        self.head = {b.name: b.head_local.copy() for b in self.bones}
        self.len = {b.name: b.length for b in self.bones}

    def two_bone(self, pose, upper, lower, target, pole, parent_q=Quaternion()):
        """解析两节 IK。目标相对静止姿势的肩，随父骨骼（parent_q）一起转。"""
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
        pose[lower] = self.dir[lower].rotation_difference((acc.inverted() @ (wrist - elbow)).normalized())

    def apply(self, pose, frame, locs=None):
        for name in self.rest:
            q = pose.get(name, Quaternion())
            pb = self.rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            b = self.rest[name]
            pb.rotation_quaternion = b.inverted() @ q @ b
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        locs = locs or {}
        for name in ("hips",):
            pb = self.rig.pose.bones[name]
            pb.location = self.rest[name].inverted() @ Vector(locs.get(name, (0, 0, 0)))
            pb.keyframe_insert("location", frame=frame)


def new_action(rig, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    return act


# ---------- 动作 ----------

def dangle(pose, s, swing=0.0, flex=0.0):
    """下垂的手臂：swing 正值向后摆，flex 正值屈肘（前臂向前抬）。"""
    pose[f"upper_arm.{s}"] = rx(swing)
    pose[f"forearm.{s}"] = rx(-flex)
    pose[f"fingers.{s}"] = rx(-flex * 0.3)


def make_idle(r):
    new_action(r.rig, "Idle")
    for f in range(0, 61, 4):
        t = f / 60.0
        b = math.sin(t * math.tau)
        pose = {}
        pose["spine"] = rx(1.5 * b)
        pose["chest"] = rx(2.5 * b) @ rz(2 * math.sin(t * math.tau + 0.8))
        pose["neck"] = rx(-2 * b)
        pose["head"] = rz(7 * math.sin(t * math.tau * 2 + 0.5) * smooth(0.3, 0.5, t) * (1 - smooth(0.7, 0.9, t))) @ rx(3 * b)
        for s, ph in (("L", 0.0), ("R", 1.7)):
            dangle(pose, s, swing=3 * math.sin(t * math.tau + ph), flex=6 + 3 * math.sin(t * math.tau + ph + 0.6))
            pose[f"fingers.{s}"] = rx(-8 - 6 * math.sin(t * math.tau * 2 + ph))
            pose[f"thigh.{s}"] = rx(-4)
            pose[f"shin.{s}"] = rx(8)
            pose[f"foot.{s}"] = rx(-4)
        pose["lantern"] = rx(3 * math.sin(t * math.tau + 1.2))
        r.apply(pose, f, {"hips": (0.01 * math.sin(t * math.tau), 0, -0.02 + 0.006 * b)})


def make_locomotion(r, name, frames, stride, lean, bob, arm_swing, reach, limp):
    """步行/奔跑循环。左脚（鞋破）步幅略小、着地时身体下沉更多，读作一瘸一拐。"""
    new_action(r.rig, name)
    for f in range(0, frames + 1, 2):
        ph = f / frames
        pose = {}
        for s, off, amp in (("L", 0.0, 1.0 - limp), ("R", 0.5, 1.0)):
            p = (ph + off) * math.tau
            swing = math.sin(p)
            bend = 10 + (stride * 1.6) * max(0.0, math.cos(p)) ** 1.5
            pose[f"thigh.{s}"] = rx(-(stride * amp * swing) - 6)
            pose[f"shin.{s}"] = rx(bend + 8)
            pose[f"foot.{s}"] = rx(-(bend * 0.45) + 8 * swing)
            # 手臂与对侧腿同步，前后甩，带一点向前伸抓的姿态
            dangle(pose, s, swing=arm_swing * swing - reach, flex=10 + 12 * max(0.0, -swing) + reach * 0.4)
        step = math.cos(ph * 2 * math.tau)
        left_stance = max(0.0, math.cos(ph * math.tau))      # 左脚支撑时更沉
        pose["hips"] = rx(lean * 0.3) @ rz(7 * math.sin(ph * math.tau)) @ ry(4 * math.sin(ph * math.tau))
        pose["spine"] = rx(lean * 0.4) @ rz(-5 * math.sin(ph * math.tau))
        pose["chest"] = rx(lean * 0.3 + 3 * step) @ rz(-4 * math.sin(ph * math.tau))
        pose["neck"] = rx(-lean * 0.5)
        pose["head"] = rx(-lean * 0.3 - 4 * step) @ rz(3 * math.sin(ph * math.tau + 0.4))
        pose["lantern"] = rx(-8 * math.sin(ph * 2 * math.tau + 0.9)) @ ry(6 * math.sin(ph * math.tau))
        drop = -bob * 0.5 + bob * 0.5 * step - limp * 0.06 * left_stance
        r.apply(pose, f, {"hips": (0.025 * math.sin(ph * math.tau), 0, drop)})


def chain_q(pose, *names):
    q = Quaternion()
    for n in names:
        q = q @ pose.get(n, Quaternion())
    return q


def attack_pose(r, k, s):
    """右爪（s=-1）横扫的关键姿势；k 为阶段 0 起手 → 1 蓄力 → 2 命中 → 3 收势。"""
    sgn = s  # 右侧 -1
    pose = {}
    # (胸扭转, 胸前倾, 胯扭转, 胯下沉, 胯前移, 头抬起, 手目标, 指张开)
    table = {
        0: (0, 0, 0, -0.02, 0.0, 0, None, -8),
        1: (24, -10, 10, -0.05, 0.03, 14, Vector((0.36 * sgn, 0.10, 1.66)), 30),
        2: (-28, 22, -12, -0.10, -0.10, -6, Vector((-0.06 * sgn, -0.62, 0.98)), -10),
        3: (-18, 18, -8, -0.09, -0.07, -2, Vector((-0.14 * sgn, -0.36, 0.62)), -30),
    }
    twist, bow, hip_twist, drop, push, look, target, spread = table[k]
    # 右爪蓄力时右肩后撤：rz 取负；命中时右肩前送
    pose["hips"] = rz(hip_twist * sgn) @ rx(bow * 0.25)
    pose["spine"] = rx(bow * 0.35) @ rz(twist * sgn * 0.4)
    pose["chest"] = rx(bow * 0.4) @ rz(twist * sgn * 0.6)
    pose["neck"] = rx(-look * 0.5)
    pose["head"] = rx(-look)
    hand, other = ("R", "L") if sgn < 0 else ("L", "R")
    if target is None:
        dangle(pose, hand, flex=6)
    else:
        parent = chain_q(pose, "hips", "spine", "chest")
        r.two_bone(pose, f"upper_arm.{hand}", f"forearm.{hand}", target, (1.2 * sgn, 0.6, 1.0), parent)
        pose[f"hand.{hand}"] = rx(-15 if k == 1 else 25)
    pose[f"fingers.{hand}"] = rx(-spread) if spread < 0 else ry(spread * -sgn * 0.4) @ rx(spread * 0.5)
    dangle(pose, other, swing={0: 0, 1: -12, 2: 20, 3: 12}[k], flex={0: 6, 1: 25, 2: 12, 3: 10}[k])
    # 弓步：出手侧对侧的腿在前
    lunge = {0: 0, 1: 0.2, 2: 1.0, 3: 0.8}[k]
    pose[f"thigh.{other}"] = rx(-28 * lunge - 4)
    pose[f"shin.{other}"] = rx(30 * lunge + 8)
    pose[f"foot.{other}"] = rx(-4)
    pose[f"thigh.{hand}"] = rx(14 * lunge - 4)
    pose[f"shin.{hand}"] = rx(18 * lunge + 8)
    pose[f"foot.{hand}"] = rx(-10 * lunge - 4)
    pose["lantern"] = rx({0: 0, 1: -10, 2: 18, 3: 8}[k])
    return pose, {"hips": (0, push, drop)}


def make_attack(r, name, s):
    new_action(r.rig, name)
    keys = [(0, 0), (8, 1), (ATTACK_IMPACT_FRAME, 2), (16, 3), (26, 0)]
    for f, k in keys:
        pose, locs = attack_pose(r, k, s)
        r.apply(pose, f, locs)


def make_hit(r):
    new_action(r.rig, "Hit")
    for f, k in ((0, 0.0), (3, 1.0), (6, 0.7), (14, 0.0)):
        pose = {}
        pose["spine"] = rx(-8 * k)
        pose["chest"] = rx(-14 * k) @ rz(6 * k)
        pose["neck"] = rx(-10 * k)
        pose["head"] = rx(-18 * k) @ rz(-10 * k)
        for s in "LR":
            dangle(pose, s, swing=-18 * k, flex=6 + 30 * k)
            pose[f"thigh.{s}"] = rx(-4 - 6 * k)
            pose[f"shin.{s}"] = rx(8 + 12 * k)
            pose[f"foot.{s}"] = rx(-4)
        pose["lantern"] = rx(14 * k)
        r.apply(pose, f, {"hips": (0, 0.06 * k, -0.02 - 0.04 * k)})


def make_death(r):
    """后仰 → 跪倒 → 向前扑倒、脸朝下趴在地上。停在最后一帧。"""
    new_action(r.rig, "Death")
    keys = [
        # 帧, 胯前倾, 胯位移, 脊柱, 胸, 头, 大腿, 小腿, 脚, 上臂摆, 屈肘
        (0, 0, (0, 0, -0.02), 0, 0, 0, -4, 8, -4, 0, 6),
        (6, -6, (0, 0.05, -0.05), -10, -16, -22, -8, 16, -4, -20, 30),
        (16, 8, (0, -0.10, -0.36), 6, 10, 10, -40, 105, -30, 10, 15),
        (24, 40, (0, -0.25, -0.52), 10, 12, 5, -30, 90, -20, -30, 10),
        (32, 82, (0, -0.10, -0.62), 4, 2, -20, -12, 25, 10, -60, 25),
        (40, 86, (0, -0.06, -0.64), 0, -4, -30, -8, 18, 15, -55, 30),
    ]
    for f, hip, loc, spine, chest, head, thigh, shin, foot, arm, flex in keys:
        pose = {"hips": rx(hip), "spine": rx(spine), "chest": rx(chest) @ rz(4), "head": rx(head) @ rz(-12 if f >= 32 else 0)}
        for s, splay in (("L", 1), ("R", -1)):
            pose[f"thigh.{s}"] = rx(thigh) @ ry(4 * splay if f >= 24 else 0)
            pose[f"shin.{s}"] = rx(shin)
            pose[f"foot.{s}"] = rx(foot)
            pose[f"upper_arm.{s}"] = rx(arm) @ ry((8 if f >= 24 else 0) * splay)
            pose[f"forearm.{s}"] = rx(-flex)
        pose["lantern"] = rx(-hip * 0.9)   # 灯始终大致朝下垂
        r.apply(pose, f, {"hips": loc})


def make_actions(r):
    make_idle(r)
    make_locomotion(r, "Walk", frames=32, stride=24, lean=4, bob=0.025, arm_swing=9, reach=0, limp=0.25)
    make_locomotion(r, "Run", frames=20, stride=38, lean=16, bob=0.05, arm_swing=22, reach=8, limp=0.15)
    make_attack(r, "Attack_R", -1)
    make_attack(r, "Attack_L", 1)
    make_hit(r)
    make_death(r)


def setup(root, body, clothes, lantern, lantern_hook, lantern_tip):
    bpy.context.scene.render.fps = FPS
    rig = build_armature(root, lantern_hook, lantern_tip)
    skin(body, rig, classify_body)
    skin(clothes, rig, classify_clothes)
    skin(lantern, rig, lambda c, pts: {"lantern": 1.0})
    r = Rig(rig)
    make_actions(r)
    rig.animation_data.action = bpy.data.actions["Idle"]
    return rig
