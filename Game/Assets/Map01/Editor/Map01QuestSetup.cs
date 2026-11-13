using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WildernessExtraction.EditorTools;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// 任务链（Q01–Q10）接入 Map01 场景：任务配置资产（只在缺失时生成默认值，之后由策划改文案和位置）、
    /// 庇护所炉火与室内电灯、剧情记录和旧地图交互点，以及任务导演组件。场景重建时按资产里的位置重新摆放。
    /// </summary>
    public static class Map01QuestSetup
    {
        public const string QuestChainPath = "Assets/Map01/Data/Map01_QuestChain.asset";
        private const string GameItemsFolder = "Assets/Game/Data/Items";
        private const string MaterialsFolder = "Assets/Map01/Materials";

        public static void Create(Transform mapRoot, Terrain terrain, Map01Authority authority, Map01RunFlow flow, ItemDefinition emptyBattery)
        {
            var chain = QuestChain(emptyBattery);
            var group = new GameObject("Quest").transform;
            group.SetParent(mapRoot, false);

            // 炉火：开场唯一的庇护所安全光，小半径；地面环与判定半径一致。
            var config = Map01Lighting.DarknessConfig();
            var hearth = Map01Lighting.Lamp(FirePropBuilder.LoadFirePit(), config, group, terrain, "Hearth_Safehouse", Flat(chain.hearthPosition), SafeLightKind.SafeHouse,
                chain.hearthRadius, true, 2.5f);
            hearth.gameObject.AddComponent<Map01Interactable>().Configure("map01_safehouse_hearth", Map01Station.Hearth);

            var map = Prop(group, "map01_safehouse_old_map", chain.oldMapPosition, terrain);
            Visual(PrimitiveType.Cube, "Board", map, new Vector3(0f, 1.4f, 0f), new Vector3(0.1f, 1f, 1.4f), Material("Quest_Paper", new Color(0.82f, 0.76f, 0.6f)));
            map.gameObject.AddComponent<Map01Interactable>().Configure("map01_safehouse_old_map", Map01Station.OldMap);

            foreach (var record in chain.records)
            {
                var prop = Prop(group, record.recordId, record.position, terrain);
                Visual(PrimitiveType.Cube, "Paper", prop, new Vector3(0f, 0.85f, 0f), new Vector3(0.45f, 0.04f, 0.35f), Material("Quest_Paper", new Color(0.82f, 0.76f, 0.6f)));
                Visual(PrimitiveType.Cube, "Stand", prop, new Vector3(0f, 0.4f, 0f), new Vector3(0.6f, 0.8f, 0.5f), Material("Quest_Wood", new Color(0.35f, 0.25f, 0.17f)));
                prop.gameObject.AddComponent<Map01Interactable>().Configure(record.recordId, Map01Station.Record);
                if (record.recordId == "map01_forest_note") ColdFirePit(prop);
            }

            // 庇护所室内电灯：只是视觉，照明恢复（Q02）前关闭；有效安全范围由 IndoorLight 的室内判定体决定，与灯光覆盖一致。
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var fixtures = new List<Light>();
            foreach (var offset in new[] { new Vector2(-3f, 0f), new Vector2(3f, 0f) })
            {
                var fixture = new GameObject("Safehouse_CeilingLight");
                fixture.transform.SetParent(group, false);
                fixture.transform.position = new Vector3(safehouse.Center.x + offset.x, 3.1f, safehouse.Center.y + offset.y);
                var light = fixture.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.9f, 0.72f);
                light.range = 9f;
                light.intensity = 3f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                fixtures.Add(light);
            }

            var director = authority.gameObject.AddComponent<Map01QuestDirector>();
            var serialized = new SerializedObject(director);
            serialized.FindProperty("authority").objectReferenceValue = authority;
            serialized.FindProperty("chain").objectReferenceValue = chain;
            serialized.FindProperty("flow").objectReferenceValue = flow;
            serialized.FindProperty("safehouseIndoor").objectReferenceValue = mapRoot.Find("Buildings_Static/map01_safehouse").GetComponent<IndoorLight>();
            var fixtureProperty = serialized.FindProperty("safehouseFixtures");
            fixtureProperty.arraySize = fixtures.Count;
            for (var i = 0; i < fixtures.Count; i++) fixtureProperty.GetArrayElementAtIndex(i).objectReferenceValue = fixtures[i];
            serialized.FindProperty("questProps").objectReferenceValue = group.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var authoritySerialized = new SerializedObject(authority);
            authoritySerialized.FindProperty("questMode").boolValue = true;
            authoritySerialized.FindProperty("questDirector").objectReferenceValue = director;
            authoritySerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>任务配置资产。只在缺失时生成，之后策划的修改不会被重建覆盖。</summary>
        public static Map01QuestChain QuestChain(ItemDefinition emptyBattery)
        {
            var chain = AssetDatabase.LoadAssetAtPath<Map01QuestChain>(QuestChainPath);
            if (chain != null)
            {
                return chain;
            }

            chain = ScriptableObject.CreateInstance<Map01QuestChain>();
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            Vector3 Safehouse(float x, float z) => new Vector3(safehouse.Center.x + x, 0f, safehouse.Center.y + z);
            var fuel = Load("Item_FuelPack");
            var part = Load("Item_MechanicalParts");
            var medical = Load("Item_FirstAidSpray");

            chain.hearthPosition = Safehouse(-1f, 1f);
            chain.hearthRadius = 5f;
            chain.oldMapPosition = Safehouse(5.8f, -1.5f);
            chain.bridgeArrival = new QuestArea { label = "桥北岸", center = new Vector3(Map01Layout.BridgeX, 0f, 118f), radius = 9f };
            chain.campfireSite = new QuestArea { label = "桥北头空地", center = new Vector3(27f, 0f, 118f), radius = 6f };

            chain.quests = new List<QuestDefinition>
            {
                Quest("Q01", "火不能灭", "在庇护所附近的物资点找到燃料，带回庇护所。", Map01QuestDirector.FactFuelDelivered,
                    new[] { "去庇护所东侧外墙边的物资堆找一份燃料", "回到庇护所，在炉火旁按 E 添燃料" },
                    "炉火续上了。这间废弃庇护所暂时可以落脚。"),
                Quest("Q02", "让这里亮起来", "去厂房取得机械零件、修复发电机，给空电池充电并带回安装。", Map01QuestDirector.FactSafehouseLit,
                    new[]
                    {
                        "去厂房（西北方向）找到机械零件",
                        "到厂房发电机房，按 E 修理发电机",
                        "修理中，留在发电机旁",
                        "拿上空电池（庇护所北墙边的箱子里有一块）",
                        "在发电机旁按 E 给空电池充电（发电机修好后每 30 秒存 1 格电）",
                        "把充好的电池带回庇护所，装进供电槽"
                    },
                    "庇护所恢复照明。"),
                Quest("Q03", "备一点药", "搜索厂房剩余物资，带回一份医疗用品。", Map01QuestDirector.FactMedicalConfirmed,
                    new[] { "在厂房找一份医疗用品", "回到庇护所，在炉火旁按 E 清点药品" },
                    "药品备好了，留着下次远行用。"),
                Quest("Q04", "前人留下的东西", "查看庇护所里的旧记录。", "record:map01_safehouse_log",
                    new[] { "查看庇护所西墙边的旧记录" },
                    "另一名寻火者曾在这里停留，后来去了树林。"),
                Quest("Q05", "林中的余烬", "前往树林，在熄灭的旧火堆旁寻找遗留笔记。", "record:map01_forest_note",
                    new[] { "去西边的树林，在熄灭的旧火堆旁找笔记" },
                    "那名寻火者准备过桥，去房区寻找知情人。"),
                Quest("Q06", "到桥那边去", "到达桥头，避开或击败附近怪物，穿过桥梁。", Map01QuestDirector.FactCrossedBridge,
                    new[] { "过桥抵达河对岸（桥头可能有怪物，可以绕开）" },
                    "到了河对岸，接下来去房区。"),
                Quest("Q07", "空屋里的消息", "搜索房区中可进入的住宅，找到住客记录。", "record:map01_house_note",
                    new[] { "去东边房区唯一能进的住宅，找到住客记录" },
                    "有人运送过疑似圣火的火种，曾在仓库停留。"),
                Quest("Q08", "为返程留一盏火", "在桥附近的可放置位置建起一堆火。", Map01QuestDirector.FactCampfireBuilt,
                    new[] { "找一份燃料（桥北头附近有一份）", "在桥北头西侧空地按 Q 建一堆火" },
                    "火堆点着了。它会燃尽，别指望它守一整夜。"),
                Quest("Q09", "寻找运送记录", "进入仓库，找到火种的货单或随行记录。", "record:map01_warehouse_manifest",
                    new[] { "去西边的仓库，在东侧货箱旁找运送记录" },
                    "找到了运送者和他们的下一站。"),
                Quest("Q10", "下一段路", "把运送记录带回庇护所，与旧地图对照。", Map01QuestDirector.FactMapCompared,
                    new[] { "回到庇护所，在东墙的旧地图前按 E 对照运送记录" },
                    "你在旧地图上标出了运送队的去向。")
            };

            chain.records = new List<QuestRecordEntry>
            {
                Record("map01_safehouse_log", "守夜记录（撕下的几页）", Safehouse(-6f, -0.5f),
                    "……燃料只够再烧几夜，我把最后一点倒进了炉子。\n\n东边厂房的发电机还能修，零件得去里面翻。修好了这里就能亮灯。\n\n我不等了。树林那边有人生过火，烟往西飘。我去看看是谁。\n\n——一个路过的寻火者"),
                Record("map01_forest_note", "压在石头下的笔记", new Vector3(-84f, 0f, 11f),
                    "火是我生的，也是我熄的。燃料要省着用。\n\n听人说，桥那边的房区还有人住过，见过一只透着光的铁箱被人推着走。有人管那叫圣火。\n\n我要过桥去找知情的人。看到这张纸的人，别在林子里过夜。"),
                Record("map01_house_note", "住客的日记", new Vector3(179.4f, 0f, 112.2f),
                    "第三十一天。\n\n一队人推着板车从桥那边过来，车上一只铁箱，缝里透着光，不冷不热，像炉膛。\n\n领头的说那是圣火的火种，要往北送。他们在西边仓库歇了一晚。\n\n我没敢多问。是真是假，谁知道呢。"),
                Record("map01_warehouse_manifest", "运送记录", new Vector3(-76f, 0f, 125f),
                    "货物：密封铁箱一只（“火种”，勿开）\n押运：〔运送者姓名待定〕\n经停：西岸仓库，一夜\n下一站：〔北方目的地待定〕\n\n背面潦草地写着：如果火种是真的，所有人都会来抢。别写在明面上。")
            };

            chain.supplies = new List<QuestSupply>
            {
                new QuestSupply { questId = "Q01", item = fuel, label = "物资堆", position = Safehouse(9.5f, 3.2f) },
                new QuestSupply { questId = "Q02", item = emptyBattery, label = "电池箱", position = Safehouse(1.5f, 4f) },
                new QuestSupply { questId = "Q02", item = part, label = "零件箱", position = new Vector3(91f, 0f, 12f) },
                new QuestSupply { questId = "Q03", item = medical, label = "急救箱", position = new Vector3(91f, 0f, 17f) },
                new QuestSupply { questId = "Q08", item = fuel, label = "物资", position = new Vector3(30f, 0f, 111f) }
            };

            chain.chainCompleteText = "本地图第一段故事完成：沿着运送队的方向，继续追寻圣火。";
            AssetDatabase.CreateAsset(chain, QuestChainPath);
            return chain;
        }

        private static QuestDefinition Quest(string id, string title, string goal, string fact, string[] steps, string completion)
        {
            return new QuestDefinition { questId = id, title = title, goal = goal, completionFact = fact, steps = steps, completionText = completion };
        }

        private static QuestRecordEntry Record(string id, string title, Vector3 position, string body)
        {
            return new QuestRecordEntry { recordId = id, title = title, position = position, body = body, fact = $"record:{id}" };
        }

        private static ItemDefinition Load(string file) => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{GameItemsFolder}/{file}.asset");

        private static Vector2 Flat(Vector3 position) => new Vector2(position.x, position.z);

        private static Transform Prop(Transform parent, string name, Vector3 position, Terrain terrain)
        {
            var go = new GameObject(name).transform;
            go.SetParent(parent, false);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            go.position = position;
            return go;
        }

        private static void ColdFirePit(Transform parent)
        {
            var pit = new GameObject("ColdFirePit").transform;
            pit.SetParent(parent, false);
            pit.localPosition = new Vector3(1.3f, 0f, 0.4f);
            FirePropBuilder.BuildColdPit(pit);
        }

        /// <summary>纯视觉陈设：去掉碰撞，不挡路也不影响已烘焙的导航。</summary>
        private static void Visual(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material Material(string name, Color color, bool emissive = false)
        {
            var path = $"{MaterialsFolder}/M_Map01_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2f);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
