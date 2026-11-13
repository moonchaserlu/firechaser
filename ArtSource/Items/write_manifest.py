"""合并 Blender 的 build_stats.json 和 Unity 的 unity_validation.json，写出 items_manifest.json。

用法：python write_manifest.py（在 Blender 构建、导出、渲染和 Unity Setup 都跑完之后）
只记录磁盘上真实存在的文件；缺文件的项会写成 null 并标记未通过。
"""

import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
QA = os.path.join(ROOT, "Docs", "Art", "ItemModels_QA")
WEAPONS = {"short_knife", "old_pistol"}


def existing(path):
    return path if os.path.exists(os.path.join(ROOT, path)) else None


def main():
    with open(os.path.join(HERE, "build_stats.json"), encoding="utf-8") as f:
        blender = {s["itemId"]: s for s in json.load(f)}
    with open(os.path.join(QA, "unity_validation.json"), encoding="utf-8") as f:
        unity = {s["itemId"]: s for s in json.load(f)}
    items = []
    for item_id, stats in blender.items():
        check = unity.get(item_id, {})
        fbx = existing(f"Game/Assets/Game/Art/Items/Models/Item_{item_id}.fbx")
        prefab = existing(f"Game/Assets/Game/Art/Items/Prefabs/Item_{item_id}.prefab")
        ortho = existing(f"Docs/Art/ItemModels_QA/Ortho_{item_id}.png")
        beauty = existing(f"Docs/Art/ItemModels_QA/Beauty_{item_id}.png")
        entry = {
            "itemId": item_id,
            "source": {"file": "ArtSource/Items/Items.blend", "collection": stats["collection"], "object": stats["object"]},
            "fbx": fbx,
            "prefab": prefab,
            "triangles": stats["triangles"],
            "triangles_unity": check.get("triangles"),
            "materials": stats["materials"],
            "size_xyz_m_blender": stats["size_xyz_m"],
            "size_xyz_m_unity": check.get("size_xyz_m"),
            "axes": ("Unity：枪口/刀尖朝 +Z，侧面朝 ±X，Y 向上" if item_id in WEAPONS else "Unity：正面朝 +Z，Y 向上") + "；根原点在底部中心",
            "ortho": ortho,
            "beauty": beauty,
            "unity_validation": "通过" if check.get("ok") and fbx and prefab else ("未执行" if not check else "未通过"),
            "unity_problems": check.get("problems", []),
            "children": stats.get("children", []),
        }
        if "single" in stats:
            entry["ammo_single"] = {"object": stats["single"]["object"], "triangles": stats["single"]["triangles"],
                                    "size_xyz_m": stats["single"]["size"]}
        items.append(entry)
    with open(os.path.join(QA, "items_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
    passed = sum(1 for i in items if i["unity_validation"] == "通过")
    print(f"[manifest] {len(items)} items, {passed} passed")
    for i in items:
        print(i["itemId"], i["triangles"], i["triangles_unity"], i["materials"], i["size_xyz_m_unity"], i["unity_validation"])


main()
