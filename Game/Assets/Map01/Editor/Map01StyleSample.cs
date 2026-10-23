using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 指南阶段 2 的“同镜头、同灯光、同地面”并排样板：Forest / Dungeon / Furniture 与自制墙体屋顶、
    /// 白盒角色放在一起，用游戏相机角度截图，检查比例、轮廓、色板和材质是否统一。
    /// </summary>
    public static class Map01StyleSample
    {
        public const string ScenePath = "Assets/Map01/Scenes/Map01_StyleTest.unity";

        [MenuItem("Wilderness/Map01/Build Style Test Scene")]
        public static void Build()
        {
            var art = Map01Art.Load();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Map01Builder.CreateLighting();

            var root = new GameObject("StyleTest").transform;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(root, false);
            ground.transform.localScale = new Vector3(4f, 1f, 3f);
            ground.GetComponent<Renderer>().sharedMaterial = GroundMaterial();

            // 第一排：Forest 正常 / 枯黄 / 枯死，灌木与岩石。
            var x = -14f;
            foreach (var (model, withered) in new[] { ("Tree_1_A", false), ("Tree_5_B", false), ("Tree_1_A", true), ("Tree_Bare_1_A", false) })
            {
                art.Place(Map01Art.ForestModel(model, withered), art.Forest, root, new Vector3(x, 0f, 6f), 0f);
                x += 5f;
            }

            art.Place(Map01Art.ForestModel("Bush_1_A", false), art.Forest, root, new Vector3(6f, 0f, 6f), 0f);
            art.Place(Map01Art.ForestModel("Rock_3_A", false), art.Forest, root, new Vector3(9f, 0f, 6f), 0f);

            // 第二排：Dungeon 箱桶、货架、路障。
            x = -14f;
            foreach (var model in new[] { "barrel_large", "crate_large", "crates_stacked", "shelf_large", "barrier", "table_long" })
            {
                art.Place(Map01Art.DungeonModel(model), art.Dungeon, root, new Vector3(x, 0f, 0f), 0f);
                x += 4.5f;
            }

            // 第三排：Furniture 家具。
            x = -14f;
            foreach (var model in new[] { "bed_single_A", "table_medium", "chair_A_wood", "cabinet_medium", "couch", "lamp_standing" })
            {
                art.Place(Map01Art.FurnitureModel(model), art.Furniture, root, new Vector3(x, 0f, -5f), 0f);
                x += 4.5f;
            }

            // 自制墙体与双坡屋顶（与 Map01 同材质），以及 2 米高的白盒角色作为比例尺。
            var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map01/Materials/M_Map01_Wall.mat");
            var roofMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Map01/Materials/M_Map01_RoofHouse.mat");
            var shell = new GameObject("SelfMadeShell").transform;
            shell.SetParent(root, false);
            shell.localPosition = new Vector3(13f, 0f, -1f);
            Cube(shell, new Vector3(0f, 1.75f, 0f), new Vector3(5f, 3.5f, 0.4f), wallMat);
            var slab = Cube(shell, new Vector3(0f, 4.3f, -1f), new Vector3(5.4f, 0.25f, 2.6f), roofMat);
            slab.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
            var player = Cube(root, new Vector3(-17f, 1f, -1f), new Vector3(0.8f, 2f, 0.8f), AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/M_Player.mat"));
            player.name = "Scale_Player_2m";

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.62f, 0.7f, 0.76f);
            // 与游戏相机同角度（55°），距离拉远到能看到整排样板。
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 26f, -19f), Quaternion.Euler(55f, 0f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Map01] 并排样板场景已生成：{ScenePath}");
        }

        private static Material GroundMaterial()
        {
            // 与 Map01 地形草地同色，保证“同地面”。
            var path = "Assets/Map01/Materials/M_Map01_StyleGround.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.42f, 0.5f, 0.32f) };
                material.SetFloat("_Smoothness", 0.1f);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static GameObject Cube(Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = scale;
            if (material != null)
            {
                cube.GetComponent<Renderer>().sharedMaterial = material;
            }

            return cube;
        }
    }
}
