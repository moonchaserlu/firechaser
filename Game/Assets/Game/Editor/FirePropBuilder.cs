using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WildernessExtraction.EditorTools
{
    /// <summary>
    /// 火的道具：玩家火堆、固定火坑（炉火）、火把和熄灭的旧火堆。木柴与火把用几何体拼，石圈优先用已导入的 KayKit 岩石；
    /// 火焰是 URP 粒子。都没有碰撞，不影响导航。KayKit 的 torch 按素材规范不进 Map01，所以火把是自制的。
    /// </summary>
    public static class FirePropBuilder
    {
        public const string CampfirePrefabPath = PrefabsFolder + "/Campfire.prefab";
        public const string FirePitPrefabPath = PrefabsFolder + "/FirePit.prefab";
        public const string TorchPrefabPath = PrefabsFolder + "/Torch.prefab";

        private const string PrefabsFolder = "Assets/Game/Prefabs";
        private const string MaterialsFolder = "Assets/Game/Materials";
        private const string DarknessConfigPath = "Assets/Game/Data/DarknessConfig.asset";
        private const string CampfireConfigPath = "Assets/Game/Data/CampfireConfig.asset";
        private const string RockModelFolder = "Assets/ThirdPartyAssets/itch.io/KayLousberg/ForestNaturePack/1.0_SOURCE/Models/Color2";
        private const string RockMaterialPath = "Assets/Map01/Art/Materials/M_KK_Forest.mat";
        private const string ParticleTexturePath = MaterialsFolder + "/T_FireParticle.png";

        // 岩石模型与其原始宽度（米，见 KayKitBounds.txt），按目标尺寸缩放。
        private static readonly (string model, float size)[] Rocks = { ("Rock_2_A", 0.22f), ("Rock_6_A", 0.23f), ("Rock_1_A", 0.58f) };

        [MenuItem("Wilderness/Rebuild Fire Prefabs")]
        public static void RebuildPrefabs()
        {
            var darkness = AssetDatabase.LoadAssetAtPath<DarknessConfig>(DarknessConfigPath);
            var ring = LitOrUnlit("M_SafeLightRing", new Color(1f, 0.72f, 0.32f), true);
            BuildCampfirePrefab(darkness, AssetDatabase.LoadAssetAtPath<CampfireConfig>(CampfireConfigPath), ring);
            BuildFirePitPrefab(darkness, ring);
            BuildTorchPrefab(darkness, ring);
            AssetDatabase.SaveAssets();
            Debug.Log("[Wilderness] 火堆、火坑、火把预制体已重建。");
        }

        public static GameObject LoadFirePit() => LoadOrBuild(FirePitPrefabPath);
        public static GameObject LoadTorch() => LoadOrBuild(TorchPrefabPath);

        private static GameObject LoadOrBuild(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
            RebuildPrefabs();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>玩家火堆：石圈 + 木柴 + 火焰粒子 + 闪烁点光源 + 边界环，安全半径与寿命来自 CampfireConfig。</summary>
        public static Campfire BuildCampfirePrefab(DarknessConfig darkness, CampfireConfig campfireConfig, Material ring)
        {
            var root = new GameObject("Campfire");
            try
            {
                FireRing(root.transform, false);
                var flame = Flame(root.transform, new Vector3(0f, 0.2f, 0f), 1.3f);
                var light = PointLight(root.transform, new Vector3(0f, 1.6f, 0f), new Color(1f, 0.6f, 0.28f), 5f);
                var source = AddSafeLight(root, darkness, SafeLightKind.Campfire, campfireConfig.lightRadius, light, ring);
                AddFlicker(root, light, source, flame);

                var campfire = root.AddComponent<Campfire>();
                Set(campfire, "config", campfireConfig);
                Set(campfire, "flame", flame);
                Set(campfire, "flameLight", light);
                return PrefabUtility.SaveAsPrefabAsset(root, CampfirePrefabPath).GetComponent<Campfire>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>不会燃尽的固定火坑（庇护所炉火），外观与玩家火堆一致。</summary>
        private static void BuildFirePitPrefab(DarknessConfig darkness, Material ring)
        {
            var root = new GameObject("FirePit");
            try
            {
                FireRing(root.transform, false);
                var flame = Flame(root.transform, new Vector3(0f, 0.2f, 0f), 1.3f);
                var light = PointLight(root.transform, new Vector3(0f, 1.4f, 0f), new Color(1f, 0.55f, 0.25f), 2.5f);
                var source = AddSafeLight(root, darkness, SafeLightKind.SafeHouse, 0f, light, ring);
                AddFlicker(root, light, source, flame);
                PrefabUtility.SaveAsPrefabAsset(root, FirePitPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>插在地上的火把：木杆 + 铁箍 + 浸油布头 + 小火焰，半径取 DarknessConfig.torchRadius。</summary>
        private static void BuildTorchPrefab(DarknessConfig darkness, Material ring)
        {
            var root = new GameObject("Torch");
            try
            {
                var wood = LitOrUnlit("M_CampfireLog", new Color(0.35f, 0.22f, 0.12f), false);
                Part(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.11f, 0.85f, 0.11f), Vector3.zero, wood);
                Part(PrimitiveType.Cylinder, "Band", root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(0.15f, 0.03f, 0.15f), Vector3.zero,
                    LitOrUnlit("M_TorchBand", new Color(0.24f, 0.22f, 0.2f), false));
                Part(PrimitiveType.Cylinder, "Wrap", root.transform, new Vector3(0f, 1.72f, 0f), new Vector3(0.21f, 0.17f, 0.21f), Vector3.zero,
                    LitOrUnlit("M_CharredLog", new Color(0.07f, 0.06f, 0.055f), false));
                for (var i = 0; i < 3; i++)
                {
                    var angle = i * Mathf.PI * 2f / 3f + 0.4f;
                    Stone(root.transform, i, new Vector3(Mathf.Cos(angle) * 0.2f, 0f, Mathf.Sin(angle) * 0.2f), 0.2f);
                }

                var flame = Flame(root.transform, new Vector3(0f, 1.82f, 0f), 0.85f);
                var light = PointLight(root.transform, new Vector3(0f, 2.2f, 0f), new Color(1f, 0.62f, 0.28f), 3f);
                var source = AddSafeLight(root, darkness, SafeLightKind.FixedLamp, darkness.torchRadius, light, ring);
                AddFlicker(root, light, source, flame);
                PrefabUtility.SaveAsPrefabAsset(root, TorchPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>熄灭的旧火堆：同样的石圈和烧黑的木柴，没有光。</summary>
        public static void BuildColdPit(Transform parent)
        {
            FireRing(parent, true);
        }

        private static void FireRing(Transform parent, bool cold)
        {
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI / 4f;
                Stone(parent, i, new Vector3(Mathf.Cos(angle) * 0.52f, 0f, Mathf.Sin(angle) * 0.52f), 0.28f + 0.04f * (i % 3));
            }

            var log = cold ? LitOrUnlit("M_CharredLog", new Color(0.07f, 0.06f, 0.055f), false) : LitOrUnlit("M_CampfireLog", new Color(0.35f, 0.22f, 0.12f), false);
            var bed = cold ? LitOrUnlit("M_FireAsh", new Color(0.12f, 0.12f, 0.12f), false) : LitOrUnlit("M_FireEmber", new Color(1f, 0.38f, 0.1f), true);
            Part(PrimitiveType.Cylinder, "Bed", parent, new Vector3(0f, 0.03f, 0f), new Vector3(0.72f, 0.03f, 0.72f), Vector3.zero, bed);
            for (var i = 0; i < 4; i++)
            {
                // 木柴搭成锥形，底端朝外。
                var yaw = i * 90f + 20f;
                var direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Part(PrimitiveType.Cylinder, $"Log_{i}", parent, direction * 0.17f + Vector3.up * 0.2f, new Vector3(0.1f, 0.3f, 0.1f),
                    new Vector3(-48f, yaw, 0f), log);
            }
        }

        private static void Stone(Transform parent, int index, Vector3 localPosition, float size)
        {
            var rock = Rocks[index % Rocks.Length];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{RockModelFolder}/{rock.model}_Color2.fbx");
            var material = AssetDatabase.LoadAssetAtPath<Material>(RockMaterialPath);
            if (model == null || material == null)
            {
                // Map01 素材层还没生成时退回方块，外观差一些但不缺件。
                Part(PrimitiveType.Cube, "Stone", parent, localPosition + Vector3.up * size * 0.4f, Vector3.one * size * 0.8f, new Vector3(0f, index * 37f, 0f),
                    LitOrUnlit("M_FireStone", new Color(0.3f, 0.29f, 0.28f), false));
                return;
            }

            var stone = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            stone.name = "Stone";
            stone.transform.localPosition = localPosition;
            stone.transform.localRotation = Quaternion.Euler(0f, index * 67f, 0f);
            stone.transform.localScale = Vector3.one * size / rock.size;
            foreach (var renderer in stone.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
        }

        private static GameObject Flame(Transform parent, Vector3 localPosition, float scale)
        {
            var material = FireParticleMaterial();
            var flame = new GameObject("Flame");
            flame.transform.SetParent(parent, false);
            flame.transform.localPosition = localPosition;

            var system = flame.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f * scale, 1.25f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f * scale, 0.7f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = system.emission;
            emission.rateOverTime = 34f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.2f * scale;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = Gradient(
                new[] { (new Color(1f, 0.82f, 0.42f), 0f), (new Color(1f, 0.46f, 0.12f), 0.45f), (new Color(0.75f, 0.14f, 0.03f), 1f) },
                new[] { (0f, 0f), (0.85f, 0.15f), (0.55f, 0.6f), (0f, 1f) });
            Finish(flame, material);

            var sparksObject = new GameObject("Sparks");
            sparksObject.transform.SetParent(flame.transform, false);
            var sparks = sparksObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main = sparks.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f * scale, 2.1f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.3f), new Color(1f, 0.42f, 0.12f));
            main.maxParticles = 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            emission = sparks.emission;
            emission.rateOverTime = 5f;
            shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.15f * scale;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var noise = sparks.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.8f;
            color = sparks.colorOverLifetime;
            color.enabled = true;
            color.color = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });
            Finish(sparksObject, material);
            return flame;
        }

        private static void Finish(GameObject particles, Material material)
        {
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Gradient Gradient((Color color, float time)[] colors, (float alpha, float time)[] alphas)
        {
            var colorKeys = new GradientColorKey[colors.Length];
            for (var i = 0; i < colors.Length; i++) colorKeys[i] = new GradientColorKey(colors[i].color, colors[i].time);
            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (var i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        private static Light PointLight(Transform parent, Vector3 localPosition, Color color, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        private static SafeLightSource AddSafeLight(GameObject root, DarknessConfig darkness, SafeLightKind kind, float radius, Light light, Material ring)
        {
            var boundaryObject = new GameObject("Boundary");
            boundaryObject.transform.SetParent(root.transform, false);
            var boundary = boundaryObject.AddComponent<LineRenderer>();
            boundary.useWorldSpace = false;
            boundary.loop = true;
            boundary.startWidth = 0.08f;
            boundary.endWidth = 0.08f;
            boundary.sharedMaterial = ring;
            boundary.shadowCastingMode = ShadowCastingMode.Off;
            boundary.receiveShadows = false;

            var source = root.AddComponent<SafeLightSource>();
            var serialized = new SerializedObject(source);
            serialized.FindProperty("config").objectReferenceValue = darkness;
            serialized.FindProperty("kind").enumValueIndex = (int)kind;
            serialized.FindProperty("radiusOverride").floatValue = radius;
            serialized.FindProperty("lightSource").objectReferenceValue = light;
            serialized.FindProperty("boundary").objectReferenceValue = boundary;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            source.RefreshVisuals();
            return source;
        }

        private static void AddFlicker(GameObject root, Light light, SafeLightSource source, GameObject flame)
        {
            var flicker = root.AddComponent<FireFlicker>();
            Set(flicker, "flameLight", light);
            Set(flicker, "source", source);
            Set(flicker, "flame", flame);
        }

        private static void Set(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Vector3 euler, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material LitOrUnlit(string name, Color color, bool unlit)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")) { color = color };
            if (!unlit) material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>火焰粒子材质：柔和圆点贴图 + 加色混合。</summary>
        private static Material FireParticleMaterial()
        {
            const string path = MaterialsFolder + "/M_FireParticle.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var dot = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var a = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f);
                    dot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }

            dot.Apply();
            System.IO.File.WriteAllBytes(ParticleTexturePath, dot.EncodeToPNG());
            Object.DestroyImmediate(dot);
            AssetDatabase.ImportAsset(ParticleTexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ParticleTexturePath);
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ParticleTexturePath));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
