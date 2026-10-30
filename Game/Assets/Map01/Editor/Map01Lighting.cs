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
        internal const string LampPrefabPath = "Assets/Game/Prefabs/SafeLamp.prefab";

        public sealed class Result
        {
            public SafeLightSource FactoryLamp;
            public readonly List<SafeLightSource> ExtractionLamps = new List<SafeLightSource>();
            public int FixedLampCount;
            public FireflySwarm[] Fireflies = new FireflySwarm[0];
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

            // 安全屋：室内整体由 IndoorLight 常亮；常亮大灯放在北门外照亮院落，
            // 光圈要把整栋安全屋罩进去，不让圈线从屋子中间切过。
            var safehouse = Map01Layout.FindBuilding("map01_safehouse");
            var yard = safehouse.Center + new Vector2(-4f, safehouse.Size.y * 0.5f + 4f);
            Lamp(prefab, config, group, terrain, "Lamp_Safehouse", yard, SafeLightKind.SafeHouse,
                SafehouseLampRadius(config, safehouse, yard), true, 6f);
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

            result.Fireflies = PlaceFireflies(mapRoot, terrain);
            return result;
        }

        private static void AddIndoorLight(Transform mapRoot, string buildingId, bool alwaysLit, SafeLightSource lamp)
        {
            var building = mapRoot.Find($"Buildings_Static/{buildingId}");
            building.gameObject.AddComponent<IndoorLight>().Configure(alwaysLit, lamp);
        }

        /// <summary>
        /// 树林萤火虫：在各片林地里聚成若干团，每团是一小圈有效光（半径 FireflyRadius），站在旁边不受黑暗伤害。
        /// 位置用固定种子生成，避开河、道路、建筑和既有灯光；只在黑夜出现，白天由 Map01Daylight 关掉。
        /// </summary>
        public const float FireflyRadius = 4.5f;
        private const int FireflySeed = 41031;

        public static FireflySwarm[] PlaceFireflies(Transform mapRoot, Terrain terrain)
        {
            var group = new GameObject("Fireflies").transform;
            group.SetParent(mapRoot.Find("Props_Static"), false);
            var material = FireflyMaterial();
            var zones = new (string name, Vector2 center, Vector2 size, int swarms)[]
            {
                ("Forest", new Vector2(-91f, 19f), new Vector2(140f, 60f), 6),
                ("SouthWestWoods", new Vector2(-175f, -60f), new Vector2(110f, 120f), 5),
                ("SouthEdge", new Vector2(-30f, -140f), new Vector2(220f, 30f), 4),
                ("EastWoods", new Vector2(150f, -60f), new Vector2(80f, 80f), 3),
                ("NorthBankWest", new Vector2(-150f, 145f), new Vector2(150f, 20f), 3)
            };

            var random = new System.Random(FireflySeed);
            var lamps = mapRoot.GetComponentsInChildren<SafeLightSource>(true);
            var placed = new List<Vector2>();
            var swarms = new List<FireflySwarm>();
            foreach (var zone in zones)
            {
                var count = 0;
                for (var attempt = 0; attempt < 200 && count < zone.swarms; attempt++)
                {
                    var p = zone.center + new Vector2(((float)random.NextDouble() - 0.5f) * zone.size.x, ((float)random.NextDouble() - 0.5f) * zone.size.y);
                    if (!FireflySpotAllowed(p, placed, lamps)) continue;
                    placed.Add(p);
                    count++;
                    swarms.Add(Swarm(group, terrain, material, $"Fireflies_{zone.name}_{count}", p, random));
                }
            }

            return swarms.ToArray();
        }

        private static bool FireflySpotAllowed(Vector2 p, List<Vector2> placed, SafeLightSource[] lamps)
        {
            if (Mathf.Abs(p.x) > Map01Layout.WorldWidth * 0.5f - 6f || Mathf.Abs(p.y) > Map01Layout.WorldDepth * 0.5f - 6f) return false;
            if (Map01Layout.IsInRiver(p, 4f) || Map01Layout.DistanceToRoads(p) < Map01Layout.RoadWidth * 0.5f + 6f) return false;
            foreach (var building in Map01Layout.Buildings)
            {
                var local = p - building.Center;
                if (Mathf.Abs(local.x) < building.Size.x * 0.5f + 6f && Mathf.Abs(local.y) < building.Size.y * 0.5f + 6f) return false;
            }

            // 萤火虫是灯与灯之间的“喘息点”，不和灯或彼此挤在一起。
            if (TooClose(p, placed, 25f)) return false;
            foreach (var lamp in lamps)
            {
                var lampPosition = new Vector2(lamp.transform.position.x, lamp.transform.position.z);
                if (Vector2.Distance(p, lampPosition) < 20f) return false;
            }

            return true;
        }

        private static FireflySwarm Swarm(Transform parent, Terrain terrain, Material material, string name, Vector2 world, System.Random random)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var ground = new Vector3(world.x, 0f, world.y);
            ground.y = terrain.SampleHeight(ground) + terrain.transform.position.y;
            root.transform.position = ground;

            var particlesObject = new GameObject("Particles");
            particlesObject.transform.SetParent(root.transform, false);
            particlesObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var system = particlesObject.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 1f, 0.45f), new Color(1f, 0.9f, 0.35f));
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.rateOverTime = 8f + (float)random.NextDouble() * 4f;

            // 一团萤火虫聚在光圈范围内。
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = FireflyRadius * 0.75f;
            shape.scale = new Vector3(1f, 0.45f, 1f);

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

            var renderer = particlesObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // 微弱的黄绿色光照亮地面，范围与安全半径一致。
            var lightObject = new GameObject("Glow");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.78f, 1f, 0.45f);
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;

            var ringObject = new GameObject("Boundary");
            ringObject.transform.SetParent(root.transform, false);
            var ring = ringObject.AddComponent<LineRenderer>();
            ring.startWidth = 0.05f;
            ring.endWidth = 0.05f;
            ring.sharedMaterial = material;
            ring.startColor = new Color(0.85f, 1f, 0.45f, 0.5f);
            ring.endColor = ring.startColor;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;

            var swarm = root.AddComponent<FireflySwarm>();
            swarm.Configure(FireflyRadius, system, glow, ring);
            return swarm;
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

        /// <summary>安全屋院灯的半径：至少为配置值，并覆盖到安全屋最远的墙角外 SafehouseRadiusMargin 米。</summary>
        public const float SafehouseRadiusMargin = 1.5f;

        public static float SafehouseLampRadius(DarknessConfig config, BuildingSpec safehouse, Vector2 lamp)
        {
            var farthest = 0f;
            for (var sx = -1; sx <= 1; sx += 2)
            {
                for (var sy = -1; sy <= 1; sy += 2)
                {
                    var corner = safehouse.Center + new Vector2(safehouse.Size.x * 0.5f * sx, safehouse.Size.y * 0.5f * sy);
                    farthest = Mathf.Max(farthest, Vector2.Distance(corner, lamp));
                }
            }

            return Mathf.Max(config.safeHouseRadius, farthest + SafehouseRadiusMargin);
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

        internal static SafeLightSource Lamp(GameObject prefab, DarknessConfig config, Transform parent, Terrain terrain, string name, Vector2 world,
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
