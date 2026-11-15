using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 子弹落点表现。打在地面/墙/物件上：一簇带重力的火星 + 一团扩散的灰尘 + 一下闪光；
    /// 打在有血量的目标上：顺着弹道喷出的血滴 + 几颗火星 + 目标整体闪白一下。
    /// 粒子全部画在一个程序生成的网格里（世界坐标），播完自毁并撤掉闪白。只负责显示。
    /// </summary>
    public sealed class BulletImpactEffect : MonoBehaviour
    {
        public const float Duration = 0.45f;
        public const float FlashDuration = 0.11f;

        private const float Gravity = 16f;
        private const int PuffSides = 10;

        private struct Particle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Life;
            public float Age;
            public float Width;
            public Color Head;
            public Color Tail;
            public float Drag;
        }

        private struct Puff
        {
            public Vector3 Position;
            public float Radius;
            public float Growth;
            public float Life;
            public Color Color;
        }

        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<Puff> puffs = new List<Puff>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Renderer> flashed = new List<Renderer>();
        private Material flashMaterial;
        private Mesh mesh;
        private Light flashLight;
        private float lightIntensity;
        private float elapsed;

        /// <summary>命中的是活物时显示血，否则显示火星和灰尘。</summary>
        public bool HitFlesh { get; private set; }

        /// <param name="point">落点。</param>
        /// <param name="normal">落点表面法线；打空时传 -shotDirection。</param>
        /// <param name="shotDirection">子弹飞行方向。</param>
        /// <param name="hitFlesh">打中的是有血量的目标（溅血），否则是环境（火星和灰尘）。</param>
        /// <param name="flashTarget">要闪白的目标；霰弹多颗打中同一目标时只给一颗传，其余传 null。</param>
        /// <param name="withLight">是否带一盏点光（霰弹只给其中一颗，避免一下冒出很多灯）。</param>
        public static BulletImpactEffect Play(Vector3 point, Vector3 normal, Vector3 shotDirection, bool hitFlesh, Health flashTarget, bool withLight)
        {
            var effect = new GameObject("BulletImpact").AddComponent<BulletImpactEffect>();
            effect.HitFlesh = hitFlesh;
            effect.Build(point, normal.sqrMagnitude > 0.0001f ? normal.normalized : -shotDirection.normalized, shotDirection.normalized, withLight);
            if (flashTarget != null) effect.Flash(flashTarget);
            effect.Rebuild(0f);
            return effect;
        }

        private void Build(Vector3 point, Vector3 normal, Vector3 shotDirection, bool withLight)
        {
            mesh = new Mesh { name = "BulletImpact", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = MeleeSlashEffect.SharedMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (HitFlesh) SpawnBlood(point, normal, shotDirection);
            else SpawnSparks(point, normal, shotDirection);

            if (!withLight) return;
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = point + normal * 0.35f + Vector3.up * 0.15f;
            flashLight = lightObject.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.shadows = LightShadows.None;
            flashLight.color = HitFlesh ? new Color(1f, 0.45f, 0.3f) : new Color(1f, 0.7f, 0.35f);
            flashLight.range = HitFlesh ? 5f : 6.5f;
            lightIntensity = HitFlesh ? 10f : 14f;
            flashLight.intensity = lightIntensity;
        }

        private void SpawnSparks(Vector3 point, Vector3 normal, Vector3 shotDirection)
        {
            var bounce = Vector3.Reflect(shotDirection, normal);
            var count = Random.Range(16, 22);
            for (var i = 0; i < count; i++)
            {
                var spray = (bounce * 0.8f + normal * 0.6f + Random.insideUnitSphere * 0.9f).normalized;
                if (Vector3.Dot(spray, normal) < 0.05f) spray = (spray + normal).normalized;
                particles.Add(new Particle
                {
                    Position = point + normal * 0.03f,
                    Velocity = spray * Random.Range(5f, 12f) + Vector3.up * Random.Range(1f, 3.5f),
                    Life = Random.Range(0.18f, 0.42f),
                    Width = Random.Range(0.045f, 0.075f),
                    Head = new Color(1f, 0.96f, 0.75f, 1f),
                    Tail = new Color(1f, 0.45f, 0.08f, 0f),
                    Drag = 2f,
                });
            }

            // 打中瞬间的一颗亮点 + 两团灰尘（尘土是暗灰色的，在火光边缘看得出飞起来）。
            puffs.Add(new Puff { Position = point + normal * 0.06f, Radius = 0.4f, Growth = 0.7f, Life = 0.09f, Color = new Color(1f, 0.9f, 0.65f, 1f) });
            for (var i = 0; i < 2; i++)
            {
                puffs.Add(new Puff
                {
                    Position = point + normal * (0.12f + 0.15f * i) + Random.insideUnitSphere * 0.08f,
                    Radius = 0.2f,
                    Growth = Random.Range(1f, 1.6f),
                    Life = Random.Range(0.32f, Duration),
                    Color = new Color(0.6f, 0.55f, 0.5f, 0.5f),
                });
            }
        }

        private void SpawnBlood(Vector3 point, Vector3 normal, Vector3 shotDirection)
        {
            // 大部分顺着弹道从背后喷出，少部分从入口往回溅。
            var count = Random.Range(22, 30);
            for (var i = 0; i < count; i++)
            {
                var exit = i % 4 != 0;
                var baseDirection = exit ? shotDirection : (normal * 0.7f - shotDirection * 0.3f).normalized;
                var spray = (baseDirection + Random.insideUnitSphere * 0.55f + Vector3.up * 0.25f).normalized;
                var shade = Random.Range(0.55f, 0.85f);
                particles.Add(new Particle
                {
                    Position = point + (exit ? shotDirection * 0.15f : normal * 0.03f),
                    Velocity = spray * Random.Range(exit ? 4f : 2.5f, exit ? 9f : 5f),
                    Life = Random.Range(0.25f, 0.44f),
                    Width = Random.Range(0.07f, 0.12f),
                    Head = new Color(Mathf.Min(1f, shade * 1.15f), 0.06f, 0.05f, 1f),
                    Tail = new Color(shade * 0.6f, 0.02f, 0.02f, 0f),
                    Drag = 1f,
                });
            }

            // 失火者身上带火：再崩出几颗火星。
            for (var i = 0; i < 4; i++)
            {
                particles.Add(new Particle
                {
                    Position = point + normal * 0.05f,
                    Velocity = (normal + Random.insideUnitSphere * 0.8f + Vector3.up * 0.5f).normalized * Random.Range(3f, 6f),
                    Life = Random.Range(0.1f, 0.22f),
                    Width = 0.05f,
                    Head = new Color(1f, 0.85f, 0.5f, 1f),
                    Tail = new Color(1f, 0.35f, 0.05f, 0f),
                    Drag = 2f,
                });
            }

            puffs.Add(new Puff { Position = point + normal * 0.08f, Radius = 0.35f, Growth = 0.8f, Life = 0.09f, Color = new Color(1f, 0.6f, 0.45f, 1f) });
            puffs.Add(new Puff { Position = point + shotDirection * 0.25f, Radius = 0.2f, Growth = 1.4f, Life = 0.3f, Color = new Color(0.75f, 0.05f, 0.04f, 0.85f) });
        }

        /// <summary>给目标模型多叠一层不受光的白色（和近战命中同一个做法）；结束时只摘掉这一层。</summary>
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
            var delta = Time.deltaTime;
            elapsed += delta;
            if (elapsed >= Duration)
            {
                Destroy(gameObject);
                return;
            }

            for (var i = 0; i < particles.Count; i++)
            {
                var particle = particles[i];
                particle.Age += delta;
                particle.Velocity += Vector3.down * (Gravity * delta);
                particle.Velocity *= Mathf.Exp(-particle.Drag * delta);
                particle.Position += particle.Velocity * delta;
                particles[i] = particle;
            }

            if (flashed.Count > 0 && elapsed >= FlashDuration) RemoveFlash();
            Rebuild(elapsed);
        }

        private void Rebuild(float time)
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            var view = Camera.main;
            var toCamera = view != null ? -view.transform.forward : Vector3.up;
            var right = Vector3.Cross(toCamera, Vector3.up);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            var up = Vector3.Cross(right, toCamera).normalized;

            foreach (var puff in puffs)
            {
                var progress = time / puff.Life;
                if (progress >= 1f) continue;
                var radius = puff.Radius * (1f + puff.Growth * (1f - (1f - progress) * (1f - progress)) * 2f);
                var center = puff.Color;
                center.a *= 1f - progress;
                var edge = center;
                edge.a = 0f;
                var start = vertices.Count;
                vertices.Add(puff.Position);
                colors.Add(center);
                for (var side = 0; side < PuffSides; side++)
                {
                    var angle = side * Mathf.PI * 2f / PuffSides;
                    vertices.Add(puff.Position + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
                    colors.Add(edge);
                    triangles.Add(start);
                    triangles.Add(start + 1 + (side + 1) % PuffSides);
                    triangles.Add(start + 1 + side);
                }
            }

            foreach (var particle in particles)
            {
                var progress = particle.Age / particle.Life;
                if (progress >= 1f) continue;
                // 拖尾长度跟速度走，越快越长，像高速相机里拉出的火星线。
                var tail = particle.Position - particle.Velocity * 0.045f;
                var axis = particle.Position - tail;
                var side = Vector3.Cross(axis, toCamera);
                side = side.sqrMagnitude > 0.000001f ? side.normalized * particle.Width * (1f - 0.5f * progress) : right * particle.Width;
                var head = particle.Head;
                head.a *= 1f - progress * progress;
                var start = vertices.Count;
                vertices.Add(particle.Position + side);
                vertices.Add(particle.Position - side);
                vertices.Add(tail);
                colors.Add(head);
                colors.Add(head);
                colors.Add(particle.Tail);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            if (flashLight != null)
            {
                var fade = Mathf.Clamp01(1f - time / 0.2f);
                flashLight.intensity = lightIntensity * fade * fade;
            }

            if (flashMaterial != null)
            {
                var fade = Mathf.Clamp01(1f - time / FlashDuration);
                flashMaterial.color = HitFlesh ? new Color(1f, 0.92f, 0.85f, 0.85f * fade) : Color.clear;
            }
        }

        private void RemoveFlash()
        {
            // 闪白期间目标的材质可能被换过（MeleeEnemy 按状态变色会实例化材质），所以只摘掉叠上去的这一层。
            foreach (var renderer in flashed)
            {
                if (renderer == null) continue;
                var materials = new List<Material>(renderer.sharedMaterials);
                materials.Remove(flashMaterial);
                renderer.sharedMaterials = materials.ToArray();
            }

            flashed.Clear();
        }

        private void OnDestroy()
        {
            RemoveFlash();
            if (flashMaterial != null) Destroy(flashMaterial);
            if (mesh != null) Destroy(mesh);
        }
    }
}
