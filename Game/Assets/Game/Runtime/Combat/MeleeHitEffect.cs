using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 近战命中反馈：命中点一朵放射状火星 + 一瞬间的点光，被打中的模型整体闪白。播完自毁并撤掉闪白。
    /// 全部程序生成，只负责显示，不参与伤害判定。
    /// </summary>
    public sealed class MeleeHitEffect : MonoBehaviour
    {
        public const float Duration = 0.18f;

        private const float BodyHeight = 1f;
        private const float StarRadius = 1.1f;
        private const int Rays = 9;
        private const float LightIntensity = 14f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Renderer> flashed = new List<Renderer>();
        private MaterialPropertyBlock block;
        private MeshRenderer star;
        private Mesh mesh;
        private Light flashLight;
        private Material flashMaterial;
        private float elapsed;

        public static MeleeHitEffect Play(Health target, Vector3 attackerPosition)
        {
            var body = target.transform.position + Vector3.up * BodyHeight;
            var toAttacker = attackerPosition + Vector3.up * BodyHeight - body;
            var effect = new GameObject("MeleeHit").AddComponent<MeleeHitEffect>();
            // 火星落在目标朝向攻击者的一侧，再朝镜头提一点，免得被模型自己挡住。
            var view = Camera.main;
            var towardCamera = view != null ? -view.transform.forward : Vector3.up;
            effect.transform.position = body + Vector3.ClampMagnitude(toAttacker, 0.4f) + towardCamera;
            effect.transform.rotation = view != null ? view.transform.rotation : Quaternion.LookRotation(-towardCamera);
            effect.Build();
            effect.Flash(target);
            effect.Apply(0f);
            return effect;
        }

        private void Build()
        {
            var vertices = new Vector3[Rays * 3];
            var triangles = new int[Rays * 3];
            var colors = new Color32[vertices.Length];
            for (var ray = 0; ray < Rays; ray++)
            {
                var angle = (ray + Random.Range(-0.25f, 0.25f)) * Mathf.PI * 2f / Rays;
                var outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var side = new Vector3(-outward.y, outward.x, 0f) * 0.09f;
                var length = StarRadius * Random.Range(0.55f, 1f);
                var index = ray * 3;
                vertices[index] = outward * length;
                vertices[index + 1] = side;
                vertices[index + 2] = -side;
                colors[index] = new Color32(255, 150, 50, 255);
                colors[index + 1] = Color.white;
                colors[index + 2] = Color.white;
                triangles[index] = index;
                triangles[index + 1] = index + 1;
                triangles[index + 2] = index + 2;
            }

            mesh = new Mesh { name = "MeleeHit", hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = triangles, colors32 = colors };
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            star = gameObject.AddComponent<MeshRenderer>();
            star.sharedMaterial = MeleeSlashEffect.SharedMaterial;
            star.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            star.receiveShadows = false;
            block = new MaterialPropertyBlock();

            flashLight = gameObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.7f, 0.4f);
            flashLight.range = 6f;
            flashLight.shadows = LightShadows.None;
        }

        /// <summary>给目标的模型多叠一层不受光的白色：多出来的材质会把最后一个子网格再画一遍。</summary>
        private void Flash(Health target)
        {
            flashMaterial = new Material(MeleeSlashEffect.SharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                var materials = new List<Material>(renderer.sharedMaterials) { flashMaterial };
                renderer.sharedMaterials = materials.ToArray();
                flashed.Add(renderer);
            }
        }

        private void LateUpdate()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= Duration)
            {
                Destroy(gameObject);
                return;
            }

            Apply(elapsed / Duration);
        }

        private void Apply(float progress)
        {
            var fade = 1f - progress;
            transform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.2f, 1f - fade * fade);
            block.SetColor(ColorId, new Color(1f, 1f, 1f, fade));
            star.SetPropertyBlock(block);
            flashLight.intensity = LightIntensity * fade * fade;
            flashMaterial.color = new Color(1f, 0.95f, 0.85f, 0.85f * fade);
        }

        private void OnDestroy()
        {
            // 闪白期间目标的材质可能被换过（MeleeEnemy 按状态变色会实例化材质），所以只摘掉叠上去的这一层。
            foreach (var renderer in flashed)
            {
                if (renderer == null) continue;
                var materials = new List<Material>(renderer.sharedMaterials);
                materials.Remove(flashMaterial);
                renderer.sharedMaterials = materials.ToArray();
            }

            if (flashMaterial != null) Destroy(flashMaterial);
            if (mesh != null) Destroy(mesh);
        }
    }
}
