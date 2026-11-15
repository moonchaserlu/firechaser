using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 开枪时枪口的一团火：朝镜头的放射状火舌 + 沿枪管方向喷出的长火舌 + 一瞬间很亮的暖色点光，把周围的黑暗照亮一下。
    /// 全部程序生成，播完自毁，只负责显示。
    /// </summary>
    public sealed class GunMuzzleFlash : MonoBehaviour
    {
        public const float Duration = 0.09f;

        private const int Rays = 7;
        private const float LightRange = 11f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MeshRenderer flashRenderer;
        private MaterialPropertyBlock block;
        private Mesh mesh;
        private Light flashLight;
        private float strength;
        private float elapsed;

        /// <param name="strength">1 = 步枪，霰弹枪给大一些。</param>
        public static GunMuzzleFlash Play(Vector3 position, Vector3 direction, float strength)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            var effect = new GameObject("GunMuzzleFlash").AddComponent<GunMuzzleFlash>();
            effect.strength = Mathf.Max(0.3f, strength);
            effect.transform.position = position;
            effect.Build(direction.normalized);
            effect.Apply(0f);
            return effect;
        }

        /// <summary>网格建在世界坐标下：一朵朝镜头的星形 + 一条沿射击方向的长火舌（宽面朝镜头）。</summary>
        private void Build(Vector3 direction)
        {
            var view = Camera.main;
            var toCamera = view != null ? -view.transform.forward : Vector3.up;
            var right = Vector3.Cross(toCamera, Vector3.up);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            var up = Vector3.Cross(right, toCamera).normalized;
            var scale = 0.85f + 0.65f * strength;

            var vertices = new Vector3[Rays * 3 + 4];
            var colors = new Color32[vertices.Length];
            var triangles = new int[Rays * 3 + 6];
            var hot = new Color32(255, 250, 220, 255);
            var flame = new Color32(255, 140, 30, 255);
            for (var ray = 0; ray < Rays; ray++)
            {
                var angle = (ray + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / Rays;
                var outward = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var side = Vector3.Cross(toCamera, outward).normalized * 0.07f * scale;
                var length = 0.42f * scale * Random.Range(0.5f, 1f);
                var index = ray * 3;
                vertices[index] = outward * length;
                vertices[index + 1] = side;
                vertices[index + 2] = -side;
                colors[index] = flame;
                colors[index + 1] = hot;
                colors[index + 2] = hot;
                triangles[index] = index;
                triangles[index + 1] = index + 1;
                triangles[index + 2] = index + 2;
            }

            // 长火舌：根部宽、尖端细，沿枪口方向喷出。
            var tongueSide = Vector3.Cross(toCamera, direction);
            if (tongueSide.sqrMagnitude < 0.0001f) tongueSide = right;
            tongueSide = tongueSide.normalized * 0.13f * scale;
            var tongue = Rays * 3;
            var tip = direction * (0.95f * scale * Random.Range(0.85f, 1.15f));
            vertices[tongue] = tongueSide - direction * 0.05f;
            vertices[tongue + 1] = -tongueSide - direction * 0.05f;
            // 火舌中段随机偏向一侧，免得每枪一模一样。
            vertices[tongue + 2] = tip * 0.45f + tongueSide * (Random.value < 0.5f ? 0.6f : -0.6f);
            vertices[tongue + 3] = tip;
            colors[tongue] = hot;
            colors[tongue + 1] = hot;
            colors[tongue + 2] = flame;
            colors[tongue + 3] = new Color32(255, 90, 20, 0);
            var t = Rays * 3;
            triangles[t] = tongue;
            triangles[t + 1] = tongue + 2;
            triangles[t + 2] = tongue + 1;
            triangles[t + 3] = tongue + 1;
            triangles[t + 4] = tongue + 2;
            triangles[t + 5] = tongue + 3;

            mesh = new Mesh { name = "GunMuzzleFlash", hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = triangles, colors32 = colors };
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            flashRenderer = gameObject.AddComponent<MeshRenderer>();
            flashRenderer.sharedMaterial = MeleeSlashEffect.SharedMaterial;
            flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();

            // 光源放在枪口前一点，照亮持枪人和地面。
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = direction * 0.3f + Vector3.up * 0.1f;
            flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.72f, 0.38f);
            flashLight.range = LightRange * Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(0.5f, 2f, strength));
            flashLight.shadows = LightShadows.None;
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
            transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.15f, progress);
            block.SetColor(ColorId, new Color(1f, 1f, 1f, Mathf.Clamp01(fade * 1.6f)));
            flashRenderer.SetPropertyBlock(block);
            // 第一帧最亮，随后快速掉下去，像火光一闪。
            flashLight.intensity = 22f * strength * fade * fade;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
