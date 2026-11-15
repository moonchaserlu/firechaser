"""守灯人骨骼、蒙皮与动画。由 lampkeeper_build.py 在烘焙贴图之后调用。

静止姿势就是坐姿（模型本身按坐姿搭建）。工具函数复用失火者 firelost_rig.py（Rig / rx / ry / rz / 距离加权）。
蒙皮：建模时每个顶点写了 skin_group 属性（按材质分组），每个连通块按组决定能跟随哪些骨骼，再按距离加权。

动作：只有一个原地循环 Idle（4 秒）——喘气含胸、偶尔低头按一下伤腿、灯在钩上轻轻晃。
NPC 面向玩家的转身由 Unity 里转整个根节点完成，不需要额外动作。
"""

import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Firelost"))
import firelost_rig as fr  # noqa: E402
from firelost_rig import rx, ry, rz, smooth  # noqa: E402

FPS = 30
IDLE_FRAMES = 120

# 与 lampkeeper_build.py 的姿态常量一致
SHOULDER = Vector((0.205, 0.03, 0.965))
ELBOW = {"L": Vector((0.30, 0.08, 0.72)), "R": Vector((-0.27, -0.02, 0.70))}
WRIST = {"L": Vector((0.355, -0.165, 0.83)), "R": Vector((-0.17, -0.24, 0.62))}
HAND_TIP = {"L": Vector((0.404, -0.182, 0.83)), "R": Vector((-0.142, -0.345, 0.600))}
HIP = {"L": Vector((0.10, 0.08, 0.50)), "R": Vector((-0.10, 0.08, 0.50))}
KNEE = {"L": Vector((0.13, -0.36, 0.33)), "R": Vector((-0.13, -0.30, 0.50))}
ANKLE = {"L": Vector((0.15, -0.68, 0.11)), "R": Vector((-0.14, -0.30, 0.10))}
TOE = {"L": Vector((0.17, -0.80, 0.19)), "R": Vector((-0.14, -0.47, 0.03))}


def bone_specs(lamp_hook, lamp_tip):
    specs = [
        ("root", (0, 0, 0), (0, -0.2, 0), None),
        ("hips", (0, 0.12, 0.50), (0, 0.11, 0.62), "root"),
        ("spine", (0, 0.11, 0.62), (0, 0.07, 0.86), "hips"),
        ("chest", (0, 0.07, 0.86), (0, -0.01, 1.00), "spine"),
        ("neck", (0, -0.01, 1.00), (0, -0.09, 1.11), "chest"),
        ("head", (0, -0.09, 1.11), (0, -0.11, 1.34), "neck"),
        ("lamp", tuple(lamp_hook), tuple(lamp_tip), "root"),
    ]
    for s, side in ((1, "L"), (-1, "R")):
        sh = Vector((SHOULDER.x * s, SHOULDER.y, SHOULDER.z))
        specs += [
            (f"shoulder.{side}", (0.05 * s, 0.03, 0.98), tuple(sh), "chest"),
            (f"upper_arm.{side}", tuple(sh), tuple(ELBOW[side]), f"shoulder.{side}"),
            (f"forearm.{side}", tuple(ELBOW[side]), tuple(WRIST[side]), f"upper_arm.{side}"),
            (f"hand.{side}", tuple(WRIST[side]), tuple(HAND_TIP[side]), f"forearm.{side}"),
            (f"thigh.{side}", tuple(HIP[side]), tuple(KNEE[side]), "hips"),
            (f"shin.{side}", tuple(KNEE[side]), tuple(ANKLE[side]), f"thigh.{side}"),
            (f"foot.{side}", tuple(ANKLE[side]), tuple(TOE[side]), f"shin.{side}"),
        ]
    return specs


def build_armature(parent, lamp_hook, lamp_tip):
    data = bpy.data.armatures.new("Lampkeeper_Rig")
    rig = bpy.data.objects.new("Lampkeeper_Rig", data)
    bpy.context.scene.collection.objects.link(rig)
    rig.parent = parent
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, par in bone_specs(lamp_hook, lamp_tip):
        eb = data.edit_bones.new(name)
        eb.head, eb.tail = Vector(head), Vector(tail)
        eb.roll = 0.0
        if par:
            eb.parent = data.edit_bones[par]
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


# ---------- 蒙皮 ----------

def bones_for(group, centre):
    s = "L" if centre.x > 0 else "R"
    return {
        "head": {"head": 1.0},
        "neck": ["chest", "neck", "head"],
        "torso": ["hips", "spine", "chest", "neck", "shoulder.L", "shoulder.R", "upper_arm.L", "upper_arm.R"],
        "skirt": ["hips", "thigh.L", "thigh.R"],
        "arm": ["chest", f"shoulder.{s}", f"upper_arm.{s}", f"forearm.{s}", f"hand.{s}"],
        "hand": {f"hand.{s}": 1.0},
        "leg": ["hips", f"thigh.{s}", f"shin.{s}", f"foot.{s}"],
        "prop": {"root": 1.0},
        "lamp": {"lamp": 1.0},
    }[group]


def skin(obj, rig, group_names, power=6.0, top=2, rigid_below=24):
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
    mesh = obj.data
    tags = [0] * len(mesh.vertices)
    mesh.attributes["skin_group"].data.foreach_get("value", tags)
    world = obj.matrix_world
    groups = {}
    for island in fr._islands(mesh):
        pts = [world @ mesh.vertices[i].co for i in island]
        centre = sum(pts, Vector()) / len(pts)
        votes = {}
        for i in island:
            votes[tags[i]] = votes.get(tags[i], 0) + 1
        group = group_names[max(votes, key=votes.get)]
        bones = bones_for(group, centre)
        if isinstance(bones, dict):
            fixed = bones
        elif len(island) < rigid_below:
            fixed = fr._weights(centre, segs, bones, power, top)
        else:
            fixed = None
        for i, p in zip(island, pts):
            w = fixed or fr._weights(p, segs, bones, power, top)
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


# ---------- 动作 ----------

def make_idle(r):
    """4 秒循环：慢而深的喘气（胸口起伏、肩随着动），头先低下看伤腿、再抬起望向前方；
    右手在膝盖上轻轻揉两下；左手攥着灯杆不动；灯在钩上轻晃。"""
    fr.new_action(r.rig, "Idle")
    for f in range(0, IDLE_FRAMES + 1, 4):
        t = f / IDLE_FRAMES
        breath = math.sin(t * math.tau * 2)          # 两次呼吸
        look = smooth(0.15, 0.35, t) * (1 - smooth(0.55, 0.8, t))   # 中段低头看腿
        pose = {}
        pose["hips"] = rx(0.6 * breath)
        pose["spine"] = rx(1.0 * breath)
        pose["chest"] = rx(-1.6 * breath + 2.5 * look) @ rz(-1.5 * math.sin(t * math.tau))
        pose["neck"] = rx(3 * look + 0.8 * breath)
        pose["head"] = rx(10 * look - 2 + 1.2 * breath) @ rz(9 * look - 4 * math.sin(t * math.tau + 0.7) * (1 - look))
        for s in "LR":
            pose[f"shoulder.{s}"] = rx(-1.2 * breath)
        # 左手握杆：抵消胸口起伏，拳头基本钉在杆上
        pose["upper_arm.L"] = rx(1.2 * breath - 2.5 * look)
        pose["forearm.L"] = rx(0.5 * breath)
        rub = math.sin(t * math.tau * 4) * look
        pose["upper_arm.R"] = rx(-3 * look + 2 * rub)
        pose["forearm.R"] = rx(-2 * look - 3 * rub)
        pose["hand.R"] = rx(4 * rub)
        pose["foot.L"] = rx(2 * math.sin(t * math.tau))
        pose["lamp"] = rx(3.5 * math.sin(t * math.tau * 2 + 0.6)) @ ry(2.5 * math.sin(t * math.tau + 1.4))
        r.apply(pose, f)


def setup(root, body, clothes, props, lamp, group_names, lamp_hook, lamp_length):
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = IDLE_FRAMES
    rig = build_armature(root, lamp_hook, lamp_hook + Vector((0, 0, -lamp_length)))
    for obj in (body, clothes, props, lamp):
        skin(obj, rig, group_names)
    r = fr.Rig(rig)
    make_idle(r)
    rig.animation_data.action = bpy.data.actions["Idle"]
    return rig
