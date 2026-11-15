using System;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 一发子弹的曳光：一段头亮尾淡的光条从枪口飞到落点（不是瞬间出现的整条线），
    /// 身后留一条很淡、很快散掉的弹道线。光条飞到落点时回调，用来在那一刻播命中特效。
    /// 伤害仍在开枪那一帧用射线结算，这里只负责显示。
    /// </summary>
    public sealed class BulletTracer : MonoBehaviour
    {
        /// <summary>光条飞行速度（米/秒）。比真实子弹慢得多，斜俯视下才看得见它飞出去。</summary>
        public const float Speed = 110f;
        public const float StreakLength = 2.6f;
        public const float TrailFade = 0.12f;

        private const float Width = 0.07f;
        private const float GlowWidth = 0.22f;
        private const float TrailWidth = 0.025f;

        private readonly Vector3[] vertices = new Vector3[12];
        private readonly Color32[] colors = new Color32[12];
        private Vector3 from;
        private Vector3 to;
        private float distance;
        private float traveled;
        private float sinceArrival = -1f;
        private Action arrived;
        private Mesh mesh;

        public static BulletTracer Play(Vector3 from, Vector3 to, Action onArrived = null)
        {
            var effect = new GameObject("BulletTracer").AddComponent<BulletTracer>();
            effect.from = from;
            effect.to = to;
            effect.distance = Vector3.Distance(from, to);
            effect.arrived = onArrived;
            effect.Build();
            // 第一帧就让光条露出一截，开枪当帧就看得到。
            effect.Advance(Mathf.Min(effect.distance, StreakLength * 0.6f));
            return effect;
        }

        private void Build()
        {
            var triangles = new int[18];
            for (var quad = 0; quad < 3; quad++)
            {
                var v = quad * 4;
                var t = quad * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            mesh = new Mesh { name = "BulletTracer", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MeleeSlashEffect.SharedMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void LateUpdate()
        {
            Advance(Speed * Time.deltaTime);
        }

        private void Advance(float step)
        {
            if (sinceArrival < 0f)
            {
                traveled = Mathf.Min(distance, traveled + step);
                if (traveled >= distance)
                {
                    sinceArrival = 0f;
                    var callback = arrived;
                    arrived = null;
                    callback?.Invoke();
                }
            }
            else
            {
                sinceArrival += Time.deltaTime;
                // 到了以后光条尾巴继续追上落点，弹道线淡掉。
                traveled += step;
            }

            if (sinceArrival >= TrailFade && traveled - StreakLength >= distance)
            {
                Destroy(gameObject);
                return;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            var direction = distance > 0.0001f ? (to - from) / distance : Vector3.forward;
            var head = from + direction * Mathf.Min(traveled, distance);
            var tail = from + direction * Mathf.Clamp(traveled - StreakLength, 0f, distance);
            var view = Camera.main;
            var toCamera = view != null ? -view.transform.forward : Vector3.up;
            var side = Vector3.Cross(direction, toCamera);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;

            var trailAlpha = sinceArrival < 0f ? 0.28f : 0.28f * Mathf.Clamp01(1f - sinceArrival / TrailFade);
            // 0：淡弹道线（枪口到光条头），1：外圈光晕，2：光条芯。
            SetQuad(0, from, head, side * TrailWidth, new Color(1f, 0.75f, 0.45f, trailAlpha * 0.4f), new Color(1f, 0.8f, 0.5f, trailAlpha));
            SetQuad(1, tail, head, side * GlowWidth, new Color(1f, 0.45f, 0.1f, 0f), new Color(1f, 0.55f, 0.15f, 0.45f));
            SetQuad(2, tail, head, side * Width, new Color(1f, 0.6f, 0.2f, 0f), new Color(1f, 0.97f, 0.85f, 1f));
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.RecalculateBounds();
        }

        private void SetQuad(int quad, Vector3 tail, Vector3 head, Vector3 halfWidth, Color tailColor, Color headColor)
        {
            var v = quad * 4;
            vertices[v] = tail + halfWidth * 0.3f;
            vertices[v + 1] = tail - halfWidth * 0.3f;
            vertices[v + 2] = head + halfWidth;
            vertices[v + 3] = head - halfWidth;
            colors[v] = tailColor;
            colors[v + 1] = tailColor;
            colors[v + 2] = headColor;
            colors[v + 3] = headColor;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
