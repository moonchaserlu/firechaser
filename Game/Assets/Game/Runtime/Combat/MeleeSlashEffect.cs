using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 一次挥砍的刀光：攻击者腰高处扫过的一道月牙弧，头亮尾淡，播完自毁。
    /// 全部程序生成（不依赖预制体和贴图），不受光照影响，黑暗里也看得清。只负责显示，不参与伤害判定。
    /// </summary>
    public sealed class MeleeSlashEffect : MonoBehaviour
    {
        public const float Duration = 0.26f;

        /// <summary>玩家刀光：亮白刀芒配暖色边。</summary>
        public static readonly Color PlayerColor = new Color(1f, 0.42f, 0.05f);
        /// <summary>失火者的爪击：暗红。</summary>
        public static readonly Color EnemyColor = new Color(0.95f, 0.16f, 0.08f);

        private const int Segments = 28;
        private const int Rows = 3;
        private const float BodyHeight = 1f;
        // 斜俯视长焦下实际判定范围只有几十像素，刀光比判定略大一圈才读得出来。
        private const float RadiusMargin = 0.4f;
        private const float AngleMargin = 30f;

        private static Material sharedMaterial;

        private readonly Color32[] colors = new Color32[(Segments + 1) * Rows];
        private Transform follow;
        private Mesh mesh;
        private Color edgeColor;
        private float elapsed;

        /// <summary>刀光和命中特效共用的材质：Sprites/Default 在 Always Included Shaders 里，打包后也能找到。</summary>
        internal static Material SharedMaterial => sharedMaterial != null ? sharedMaterial : sharedMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };

        /// <param name="attacker">刀光跟随它的位置。</param>
        /// <param name="direction">挥砍中线朝向。</param>
        /// <param name="sweepSign">+1 从左扫到右，-1 反过来。</param>
        public static MeleeSlashEffect Play(Transform attacker, Vector3 direction, float range, float angle, int sweepSign, Color color)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = attacker.forward;
            var effect = new GameObject("MeleeSlash").AddComponent<MeleeSlashEffect>();
            effect.follow = attacker;
            effect.edgeColor = color;
            effect.transform.SetPositionAndRotation(attacker.position + Vector3.up * BodyHeight, Quaternion.LookRotation(direction, Vector3.up));
            effect.Build(range + RadiusMargin, angle + AngleMargin, sweepSign);
            effect.Apply(0f);
            return effect;
        }

        private void Build(float radius, float angle, int sweepSign)
        {
            var vertices = new Vector3[colors.Length];
            var triangles = new int[Segments * (Rows - 1) * 6];
            var triangle = 0;
            for (var column = 0; column <= Segments; column++)
            {
                var along = column / (float)Segments;
                var direction = Quaternion.Euler(0f, sweepSign * (along - 0.5f) * angle, 0f) * Vector3.forward;
                // 月牙：两头尖、中间最宽。
                var thickness = Mathf.Sin(along * Mathf.PI);
                vertices[column * Rows] = direction * (radius * (1f - 0.6f * thickness));
                vertices[column * Rows + 1] = direction * (radius * (1f - 0.14f * thickness));
                vertices[column * Rows + 2] = direction * radius;
                if (column == Segments) continue;
                for (var row = 0; row < Rows - 1; row++)
                {
                    var a = column * Rows + row;
                    var b = a + Rows;
                    triangles[triangle++] = a;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = a + 1;
                    triangles[triangle++] = b + 1;
                }
            }

            mesh = new Mesh { name = "MeleeSlash", hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = triangles };
            mesh.MarkDynamic();
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = SharedMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + Vector3.up * BodyHeight;
            elapsed += Time.deltaTime;
            if (elapsed >= Duration)
            {
                Destroy(gameObject);
                return;
            }

            Apply(elapsed / Duration);
        }

        /// <summary>progress 0–1：刀头先快速扫过全弧，刀尾随后追上，把弧线收掉。</summary>
        private void Apply(float progress)
        {
            var head = 1f - Mathf.Pow(1f - Mathf.Clamp01(progress / 0.55f), 2f);
            var tail = Mathf.Pow(Mathf.Clamp01((progress - 0.25f) / 0.75f), 1.5f);
            var core = Color.Lerp(Color.white, edgeColor, 0.15f);
            for (var column = 0; column <= Segments; column++)
            {
                var along = column / (float)Segments;
                var alpha = along > head || along < tail || head <= tail ? 0f : Mathf.Pow((along - tail) / (head - tail), 0.6f);
                colors[column * Rows] = WithAlpha(edgeColor, 0f);
                colors[column * Rows + 1] = WithAlpha(edgeColor, alpha * 0.8f);
                colors[column * Rows + 2] = WithAlpha(core, alpha);
            }

            mesh.colors32 = colors;
        }

        private static Color32 WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
