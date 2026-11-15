using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 玩家视野＝跟随镜头的高度（<see cref="FixedObliqueCamera.HeightScale"/>，同一角度拉近或拉远）。
    /// 默认只有原来高度的 <see cref="DimScale"/>（75%，镜头更近、看到的地图更少）；
    /// 教学里的事件选择「长明」之后恢复到 100%，并写进存档一直保持。
    /// 选「昏暗」时保持 75%，存档记 <see cref="DimFlag"/>，以后的事件按它多给物资。
    /// 长明的代价：怪物发现、追踪玩家的距离 ×<see cref="EverbrightDetectionScale"/>。
    /// 从安全屋出发的局改由守灯人给的这一趟 buff（<see cref="RaidBuffs"/>）决定，不看教学关的存档选择。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVision : MonoBehaviour
    {
        public const float DimScale = 0.75f;
        /// <summary>存档剧情标记：选了长明（镜头高度 100%）。</summary>
        public const string EverbrightFlag = "vision_everbright";
        /// <summary>存档剧情标记：选了昏暗（镜头高度 75%，以后的事件多给物资）。</summary>
        public const string DimFlag = "vision_dim";
        /// <summary>长明的代价：怪物发现和追踪这名玩家的距离乘以这个倍数（警戒 9 → 11.7 米，追击 12 → 15.6 米）。</summary>
        public const float EverbrightDetectionScale = 1.3f;

        /// <summary>测试用：不读存档，直接指定是否长明。</summary>
        public static bool? EverbrightOverride;

        private bool everbright;
        private float cameraScale = DimScale;
        private float detectionScale = 1f;

        public bool Everbright => everbright;
        /// <summary>怪物发现、追踪这名玩家的距离倍数。</summary>
        public float DetectionScale => detectionScale;
        /// <summary>怪物看这个目标的距离倍数：长明的玩家更显眼，更容易被发现和追上。</summary>
        public static float DetectionScaleOf(Component target)
        {
            if (target == null) return 1f;
            var vision = target.GetComponent<PlayerVision>();
            return vision != null ? vision.detectionScale : 1f;
        }

        /// <summary>当前应有的镜头高度倍数。</summary>
        public float Scale => cameraScale;

        private void Start()
        {
            Refresh();
        }

        private void OnDisable()
        {
            if (rig != null) rig.HeightScale = 1f;
        }

        /// <summary>按测试覆盖、本趟 buff（从安全屋出发时）或存档重新决定视野。</summary>
        public void Refresh()
        {
            if (EverbrightOverride.HasValue)
            {
                SetEverbright(EverbrightOverride.Value);
                return;
            }

            if (RaidTransfer.Active)
            {
                ApplyBuff(RaidBuffs.Active);
                return;
            }

            SetEverbright(SaveSlots.HasStoryFlag(EverbrightFlag));
        }

        public void SetEverbright(bool value)
        {
            everbright = value;
            cameraScale = value ? 1f : DimScale;
            detectionScale = value ? EverbrightDetectionScale : 1f;
            Apply();
        }

        /// <summary>按守灯人给的这一趟 buff 设置视野与被发现距离。</summary>
        public void ApplyBuff(RaidBuffKind kind)
        {
            var buff = RaidBuffs.Get(kind);
            everbright = kind == RaidBuffKind.Everbright;
            cameraScale = buff.CameraScale;
            detectionScale = buff.DetectionScale;
            Apply();
        }

        private void LateUpdate()
        {
            // 场景里的跟随镜头可能比玩家晚生成或被换掉，每帧对齐一次（只是赋值，开销可以忽略）。
            Apply();
        }

        private FixedObliqueCamera rig;
        private float nextSearch;

        private void Apply()
        {
            if ((rig == null || rig.Target != transform) && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 1f;
                rig = null;
                foreach (var candidate in FindObjectsByType<FixedObliqueCamera>())
                {
                    if (candidate.Target == transform) rig = candidate;
                }
            }

            if (rig != null) rig.HeightScale = Scale;
        }

        /// <summary>当前跟随镜头的高度倍数（测试用）。</summary>
        public float CameraScale => rig != null ? rig.HeightScale : -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => EverbrightOverride = null;
    }
}
