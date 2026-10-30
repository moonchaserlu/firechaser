using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 室内有灯即安全：整个室内判定体都算有效光，而不只是灯周围的半径。
    /// 常亮（安全屋），或跟随一盏室内灯的点亮状态（厂房随发电机）。没有灯的室内仍是黑暗。
    /// 有光时在每个室内判定体里铺一组补光，让看到的亮区与安全范围一致；断电时补光随之熄灭。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BuildingReveal))]
    public sealed class IndoorLight : MonoBehaviour, ISafeLightArea
    {
        [SerializeField] private bool alwaysLit;
        [Tooltip("室内灯；它点亮时整个室内算有光。")]
        [SerializeField] private SafeLightSource linkedLamp;
        [Tooltip("可选：室内有光时放大光圈、把整栋屋子罩进去的院灯；无光时回到配置默认半径。")]
        [SerializeField] private SafeLightSource coverLamp;
        [SerializeField, Min(0f)] private float coverLitRadius;

        [Header("室内补光")]
        [SerializeField] private Color fillColor = new Color(1f, 0.82f, 0.58f);
        [SerializeField, Min(0f)] private float fillIntensity = 6f;
        [Tooltip("每盏补光负责的格子边长 = 层高 × 该倍数，再夹在下面的上下限之间。")]
        [SerializeField, Min(0.5f)] private float fillCellPerHeight = 1.6f;
        [SerializeField, Min(2f)] private float fillCellMin = 5f;
        [SerializeField, Min(2f)] private float fillCellMax = 12f;
        [Tooltip("补光范围 = 灯到格子最远角距离 × 该倍数。范围大于房间才能让墙角不被衰减压暗，穿墙由阴影挡住。")]
        [SerializeField, Min(1f)] private float fillRangeScale = 2.2f;
        [Tooltip("补光高度占层高的比例；越高越均匀。")]
        [SerializeField, Range(0.3f, 1f)] private float fillHeightRatio = 0.9f;
        [Tooltip("补光投射阴影，避免光穿墙照亮室外、让室外看起来也安全。")]
        [SerializeField] private LightShadows fillShadows = LightShadows.Soft;

        private BuildingReveal building;
        private readonly List<Light> fills = new List<Light>();
        private bool? coverApplied;

        public bool IsLit => isActiveAndEnabled && (alwaysLit || (linkedLamp != null && linkedLamp.IsLit));

        public void Configure(bool lit, SafeLightSource lamp)
        {
            alwaysLit = lit;
            linkedLamp = lamp;
        }

        /// <summary>室内有光时把 lamp 的半径放大到 radius（安全屋通电后院灯罩住整栋屋子）。</summary>
        public void SetCoverLamp(SafeLightSource lamp, float radius)
        {
            coverLamp = lamp;
            coverLitRadius = radius;
            coverApplied = null;
            if (Application.isPlaying) ApplyFills();
        }

        /// <summary>运行时切换常亮（任务模式下庇护所先断电，装上电池后恢复）。</summary>
        public void SetAlwaysLit(bool lit)
        {
            alwaysLit = lit;
            ApplyFills();
        }

        public bool Contains(Vector3 worldPosition)
        {
            if (building == null) building = GetComponent<BuildingReveal>();
            return building.Contains(worldPosition);
        }

        /// <summary>当前生成的补光，供测试与截图检查。</summary>
        public IReadOnlyList<Light> Fills => fills;

        private void OnEnable()
        {
            LightRegistry.Scene.Register(this);
            if (fills.Count == 0) CreateFills();
            ApplyFills();
        }

        private void OnDisable()
        {
            LightRegistry.Scene.Unregister(this);
            foreach (var fill in fills)
            {
                if (fill != null) fill.enabled = false;
            }

            if (coverLamp != null && coverApplied == true)
            {
                coverApplied = false;
                coverLamp.SetRadius(0f);
            }
        }

        private void LateUpdate()
        {
            ApplyFills();
        }

        private void ApplyFills()
        {
            var lit = IsLit;
            foreach (var fill in fills)
            {
                if (fill != null && fill.enabled != lit) fill.enabled = lit;
            }

            // 只在明暗切换时改院灯半径；0 表示回到配置默认半径。
            if (coverLamp != null && coverApplied != lit)
            {
                coverApplied = lit;
                coverLamp.SetRadius(lit ? coverLitRadius : 0f);
            }
        }

        private void CreateFills()
        {
            if (building == null) building = GetComponent<BuildingReveal>();
            var root = new GameObject("IndoorFill").transform;
            root.SetParent(transform, false);
            root.gameObject.hideFlags = HideFlags.DontSave;

            foreach (var volume in building.InteriorVolumes)
            {
                if (volume == null) continue;
                var size = volume.Size;
                var cellSize = Mathf.Clamp(size.y * fillCellPerHeight, fillCellMin, fillCellMax);
                var columns = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
                var rows = Mathf.Max(1, Mathf.CeilToInt(size.z / cellSize));
                var cell = new Vector2(size.x / columns, size.z / rows);
                // 每格中心一盏。点光在接近范围边缘时会被平滑压到 0，范围只够到墙角会让墙角发黑，
                // 所以范围取到最远角的数倍，让整格落在衰减平缓的一段里；穿墙由阴影挡住。
                var height = size.y * fillHeightRatio;
                var farthest = Mathf.Sqrt(cell.x * cell.x * 0.25f + cell.y * cell.y * 0.25f + height * height);
                var range = farthest * fillRangeScale;
                for (var c = 0; c < columns; c++)
                {
                    for (var r = 0; r < rows; r++)
                    {
                        var local = new Vector3(-size.x * 0.5f + cell.x * (c + 0.5f), height, -size.z * 0.5f + cell.y * (r + 0.5f));
                        var lightObject = new GameObject($"Fill_{volume.name}_{c}_{r}");
                        lightObject.transform.SetParent(root, false);
                        lightObject.transform.position = volume.transform.TransformPoint(local);
                        var fill = lightObject.AddComponent<Light>();
                        fill.type = LightType.Point;
                        fill.color = fillColor;
                        fill.intensity = fillIntensity;
                        fill.range = range;
                        fill.shadows = fillShadows;
                        fill.enabled = false;
                        fills.Add(fill);
                    }
                }
            }
        }
    }
}
