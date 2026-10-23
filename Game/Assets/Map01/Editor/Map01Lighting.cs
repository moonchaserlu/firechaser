using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WildernessExtraction.Map01.EditorTools
{
    /// <summary>
    /// Map01 的永久黑暗布光：安全屋常亮光、沿主路的固定灯（离安全屋越远越稀）、桥头与各 POI 门口的灯、
    /// 山顶祭坛灯；厂房灯随发电机点亮，撤离点灯随救援点亮。灯没有碰撞，不影响导航。
    /// </summary>
    public static class Map01Lighting
    {
        public const string DarknessConfigPath = "Assets/Map01/Data/Map01_DarknessConfig.asset";
        private const string LampPrefabPath = "Assets/Game/Prefabs/SafeLamp.prefab";

        public sealed class Result
        {
            public SafeLightSource FactoryLamp;
            public readonly List<SafeLightSource> ExtractionLamps = new List<SafeLightSource>();
            public int FixedLampCount;
        }

        /// <summary>
        /// Map01 自己的黑暗数值。地图约为垂直切片的 8 倍大，原值（预警 1.5 秒、每秒 20）下离光 30 米就会死，
        /// 这里放宽为预警 2 秒、每秒 5（满血约 22 秒、约 110 米），灯半径 7 米。只在缺失时创建，之后由策划调。
        /// </summary>
        public static DarknessConfig DarknessConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<DarknessConfig>(DarknessConfigPath);
            if (config != null)
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<DarknessConfig>();
            config.warningDuration = 2f;
            config.damagePerSecond = 5f;
            config.safeHouseRadius = 14f;
            config.fixedLampRadius = 7f;
            config.lightRangeMultiplier = 1.25f;
            AssetDatabase.CreateAsset(config, DarknessConfigPath);
            return config;
        }

        public static Result PlaceLamps(Transform mapRoot, Terrain terrain)
        {
            var config = DarknessConfig();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
            var group = new GameObject("Lighting").transform;
            group.SetParent(mapRoot, false);
            var result = new Result();
            var placed = new List<Vector2>();

            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            Lamp(prefab, config, group, terrain, "Lamp_Safehouse", safehouse.Center, SafeLightKind.SafeHouse, 0f, true, 6f);
            placed.Add(safehouse.Center);

            // 关键落脚点。
            var x = Map01Layout.BridgeX;
            var fixedPoints = new (string name, Vector2 position)[]
            {
                ("Lamp_BridgeSouth", new Vector2(x, Map01Layout.SouthBankZ(x) - 5f)),
                ("Lamp_BridgeNorth", new Vector2(x, Map01Layout.NorthBankZ(x) + 5f)),
                ("Lamp_FactoryDoor", new Vector2(99.6f, -3f)),
                ("Lamp_WarehouseDoor", new Vector2(-96f, 113f)),
                ("Lamp_HouseYard", new Vector2(180f, 99f)),
                ("Lamp_Altar", Map01Layout.AltarPosition + new Vector2(0f, -3f))
            };
            foreach (var (name, position) in fixedPoints)
            {
                Lamp(prefab, config, group, terrain, name, position, SafeLightKind.FixedLamp, 0f, true);
                placed.Add(position);
                result.FixedLampCount++;
            }

            // 沿路的灯：安全屋附近密，越深越稀，不连成直达深区的安全链。
            var roadIndex = 0;
            foreach (var road in Map01Layout.RoadsWorld())
            {
                var travelled = 0f;
                for (var i = 0; i < road.Length - 1; i++)
                {
                    var a = road[i];
                    var b = road[i + 1];
                    var segment = Vector2.Distance(a, b);
                    for (var t = 0f; t < segment; t += 2f)
                    {
                        var p = Vector2.Lerp(a, b, t / segment);
                        travelled += 2f;
                        if (travelled < Spacing(p, safehouse.Center)) continue;
                        if (Map01Layout.IsInRiver(p, 6f) || TooClose(p, placed, 30f)) continue;
                        var side = Vector2.Perpendicular((b - a).normalized) * 3.5f;
                        var lampPosition = p + side;
                        Lamp(prefab, config, group, terrain, $"Lamp_Road{roadIndex}_{result.FixedLampCount}", lampPosition, SafeLightKind.FixedLamp, 0f, true);
                        placed.Add(lampPosition);
                        result.FixedLampCount++;
                        travelled = 0f;
                    }
                }

                roadIndex++;
            }

            // 厂房发电机房：发电机修好后才亮。
            var factory = Map01Layout.FindBuilding("map01_factory");
            result.FactoryLamp = Lamp(prefab, config, group, terrain, "Lamp_FactoryGenerator", factory.Center + new Vector2(11.6f, 0f),
                SafeLightKind.FixedLamp, 0f, false);

            // 撤离点：救援激活时才亮。
            foreach (var point in Map01Layout.Points)
            {
                if (point.Type != SpawnPointType.ExtractionCandidate) continue;
                result.ExtractionLamps.Add(Lamp(prefab, config, group, terrain, $"Lamp_{point.Id}", point.Position, SafeLightKind.FixedLamp,
                    point.Radius + 2f, false));
            }

            return result;
        }

        private static float Spacing(Vector2 p, Vector2 safehouse)
        {
            var distance = Vector2.Distance(p, safehouse);
            if (distance < 150f) return 45f;
            return distance < 300f ? 70f : 100f;
        }

        private static bool TooClose(Vector2 p, List<Vector2> placed, float minimum)
        {
            foreach (var other in placed)
            {
                if ((other - p).sqrMagnitude < minimum * minimum) return true;
            }

            return false;
        }

        private static SafeLightSource Lamp(GameObject prefab, DarknessConfig config, Transform parent, Terrain terrain, string name, Vector2 world,
            SafeLightKind kind, float radius, bool startLit, float intensity = 4f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            var position = new Vector3(world.x, 0f, world.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            instance.transform.position = position;

            var source = instance.GetComponent<SafeLightSource>();
            var serialized = new SerializedObject(source);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("radiusOverride").floatValue = radius;
            serialized.FindProperty("startLit").boolValue = startLit;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var light = instance.GetComponentInChildren<Light>();
            if (light != null)
            {
                light.intensity = intensity;
            }

            source.RefreshVisuals();
            foreach (var component in instance.GetComponentsInChildren<Component>())
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            return source;
        }
    }
}
