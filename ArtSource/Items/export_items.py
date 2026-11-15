"""把 Items.blend 里的物品逐件导出为 Unity 用的 FBX，并复制色板贴图。

用法（Blender 5.x 后台）：
    blender --background Items.blend --python export_items.py -- [itemId ...]

输出到 Game/Assets/Game/Art/Items/Models/Item_<itemId>.fbx 与 Textures/T_Item_Palette.png。
每件导出前把根物体临时移回原点（源文件里为了查看排成一行），导出后恢复；不保存 Items.blend。
轴向：Blender -Y（物品正面）→ Unity +Z，Z 向上 → Unity Y 向上。
导出器的轴换算会让正面落到 Unity -Z，所以导出时临时把网格绕 Z 转 180°（导完还原），
不在 Unity 预制体上补旋转；Unity 侧 bakeAxisConversion，根节点旋转为零。
"""

import os
import shutil
import sys

import math

import bpy
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UNITY_ITEMS = os.path.join(ROOT, "Game", "Assets", "Game", "Art", "Items")


def tree(obj):
    return [obj] + [c for child in obj.children for c in tree(child)]


HALF_TURN = Matrix.Rotation(math.pi, 4, "Z")


def turn(root):
    """网格与子物体一起绕 Z 转 180°；再调一次即还原。"""
    root.data.transform(HALF_TURN)
    for child in root.children:
        child.matrix_basis = HALF_TURN @ child.matrix_basis


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    models = os.path.join(UNITY_ITEMS, "Models")
    textures = os.path.join(UNITY_ITEMS, "Textures")
    os.makedirs(models, exist_ok=True)
    os.makedirs(textures, exist_ok=True)
    shutil.copyfile(os.path.join(HERE, "Textures", "T_Item_Palette.png"), os.path.join(textures, "T_Item_Palette.png"))

    roots = [o for o in bpy.data.objects if o.parent is None and o.name.startswith("Item_")]
    for root in roots:
        item_id = root.name[5:]
        if args and item_id not in args:
            continue
        saved = root.location.copy()
        root.location = (0, 0, 0)
        turn(root)
        anchors = [o for o in tree(root) if o.name.startswith("GripAnchor_")]
        for anchor in anchors:
            anchor.name = "GripAnchor"
        bpy.ops.object.select_all(action="DESELECT")
        for obj in tree(root):
            obj.hide_set(False)
            obj.select_set(True)
        bpy.context.view_layer.objects.active = root
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(models, f"Item_{item_id}.fbx"),
            use_selection=True,
            object_types={"MESH", "EMPTY"},
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            apply_scale_options="FBX_SCALE_UNITS",
            axis_forward="-Z",
            axis_up="Y",
            bake_space_transform=True,
            add_leaf_bones=False,
            bake_anim=False,
            path_mode="STRIP",
            embed_textures=False,
        )
        for anchor in anchors:
            anchor.name = f"GripAnchor_{item_id}"
        turn(root)
        root.location = saved
        print("[export]", item_id)


main()
