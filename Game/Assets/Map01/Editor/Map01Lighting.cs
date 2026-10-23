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
            public ParticleSystem[] Fireflies = new ParticleSystem[0];
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

            // 安全屋：室内整体由 IndoorLight 常亮；常亮大灯放在北门外照亮院落。
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var yard = safehouse.Center + new Vector2(-4f, safehouse.Size.y * 0.5f + 4f);
            Lamp(prefab, config, group, terrain, "Lamp_Safehouse", yard, SafeLightKind.SafeHouse, 0f, true, 6f);
            placed.Add(yard);

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

            // 有灯的室内整间安全：安全屋常亮，厂房跟随发电机房的灯。
            AddIndoorLight(mapRoot, "map01_safehouse", true, null);
            AddIndoorLight(mapRoot, "map01_factory", false, result.FactoryLamp);

            // 撤离点：救援激活时才亮。
            foreach (var point in Map01Layout.Points)
            {
                if (point.Type != SpawnPointType.ExtractionCandidate) continue;
                result.ExtractionLamps.Add(Lamp(prefab, config, group, terrain, $"Lamp_{point.Id}", point.Position, SafeLightKind.FixedLamp,
                    point.Radius + 2f, false));
            }

            result.Fireflies = PlaceFireflies(mapRoot);
            return result;
        }

        private static void AddIndoorLight(Transform mapRoot, string buildingId, bool alwaysLit, SafeLightSource lamp)
        {
            var building = mapRoot.Find($"Buildings_Static/{buildingId}");
            building.gameObject.AddComponent<IndoorLight>().Configure(alwaysLit, lamp);
        }

        /// <summary>树林萤火虫：纯视觉粒子，夜里可见，白天由 Map01Daylight 停掉。不算光源。</summary>
        public static ParticleSystem[] PlaceFireflies(Transform mapRoot)
        {
            var group = new GameObject("Fireflies").transform;
            group.SetParent(mapRoot.Find("Props_Static"), false);
            var material = FireflyMaterial();
            var zones = new (string name, Vector2 center, Vector2 size, int rate)[]
            {
                ("Forest", new Vector2(-91f, 19f), new Vector2(140f, 60f), 14),
                ("SouthWestWoods", new Vector2(-175f, -60f), new Vector2(110f, 120f), 12),
                ("SouthEdge", new Vector2(-30f, -140f), new Vector2(220f, 30f), 8),
                ("EastWoods", new Vector2(150f, -60f), new Vector2(80f, 80f), 6),
                ("NorthBankWest", new Vector2(-150f, 145f), new Vector2(150f, 20f), 5)
            };

            var systems = new List<ParticleSystem>();
            foreach (var zone in zones)
            {
                var go = new GameObject($"Fireflies_{zone.name}");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(zone.center.x, 1.8f, zone.center.y);
                var system = go.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = system.main;
                main.loop = true;
                main.duration = 10f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 1f, 0.45f), new Color(1f, 0.9f, 0.35f));
                main.maxParticles = zone.rate * 8;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.prewarm = true;
                main.playOnAwake = true;

                var emission = system.emission;
                emission.rateOverTime = zone.rate;

                var shape = system.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(zone.size.x, 2.5f, zone.size.y);

                // 缓慢游走 + 明暗闪烁。
                var noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.25f;
                noise.scrollSpeed = 0.2f;

                var fade = system.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.3f, 0.45f),
                        new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                systems.Add(system);
            }

            return systems.ToArray();
        }

        private static Material FireflyMaterial()
        {
            const string path = "Assets/Map01/Materials/M_Map01_Firefly.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            // 柔和圆点贴图：中心亮、边缘透明。
            const string texturePath = "Assets/Map01/Materials/T_Map01_FireflyDot.png";
            var dot = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
                    var a = Mathf.Clamp01(1f - d);
                    dot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }

            dot.Apply();
            System.IO.File.WriteAllBytes(texturePath, dot.EncodeToPNG());
            Object.DestroyImmediate(dot);
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(1f, 1f, 0.6f, 1f));
            AssetDatabase.CreateAsset(material, path);
            return material;
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
