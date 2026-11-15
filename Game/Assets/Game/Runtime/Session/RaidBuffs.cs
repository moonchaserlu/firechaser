using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    public enum RaidBuffKind
    {
        /// <summary>没选：视野 75%，怪物发现距离不变。</summary>
        None,
        /// <summary>长明：视野（镜头高度）100%，怪物发现、追踪距离 ×1.3。</summary>
        Everbright,
        /// <summary>昏暗：视野保持 75%，怪物发现、追踪距离 ×0.8，更好潜行。</summary>
        Dim
    }

    /// <summary>守灯人给的一种 buff：名字、说明和对视野 / 怪物发现距离的影响。</summary>
    public sealed class RaidBuffDefinition
    {
        public RaidBuffKind Kind;
        /// <summary>中文原文（显示前过 <see cref="Loc.T"/>）。</summary>
        public string Name;
        public string Description;
        public float CameraScale;
        public float DetectionScale;
    }

    /// <summary>
    /// 安全屋守灯人在每次出发前给的 buff。只管一趟：出发时随 <see cref="RaidTransfer"/> 带进地图，
    /// 回到安全屋就清空，下次出发要重新选。地图直接打开（不是从安全屋出发）时不生效，视野仍按教学关的存档选择。
    /// </summary>
    public static class RaidBuffs
    {
        public const float DimDetectionScale = 0.8f;

        private static readonly RaidBuffDefinition[] all =
        {
            new RaidBuffDefinition
            {
                Kind = RaidBuffKind.Everbright,
                Name = "长明",
                Description = "这一趟视野恢复到 100%，但怪物发现和追踪你的距离 ×1.3",
                CameraScale = 1f,
                DetectionScale = PlayerVision.EverbrightDetectionScale
            },
            new RaidBuffDefinition
            {
                Kind = RaidBuffKind.Dim,
                Name = "昏暗",
                Description = "这一趟视野保持 75%，但怪物发现和追踪你的距离 ×0.8",
                CameraScale = PlayerVision.DimScale,
                DetectionScale = DimDetectionScale
            }
        };

        private static readonly RaidBuffDefinition none = new RaidBuffDefinition
        {
            Kind = RaidBuffKind.None,
            Name = "无",
            Description = string.Empty,
            CameraScale = PlayerVision.DimScale,
            DetectionScale = 1f
        };

        /// <summary>守灯人能给的全部 buff（不含“无”）。</summary>
        public static IReadOnlyList<RaidBuffDefinition> All => all;

        /// <summary>安全屋里选好、准备带出门的 buff。</summary>
        public static RaidBuffKind Selected { get; private set; }

        /// <summary>本局生效的 buff：只有从安全屋出发的局才有。</summary>
        public static RaidBuffKind Active => RaidTransfer.Active ? Selected : RaidBuffKind.None;

        public static RaidBuffDefinition Get(RaidBuffKind kind)
        {
            foreach (var buff in all)
            {
                if (buff.Kind == kind) return buff;
            }

            return none;
        }

        public static void Select(RaidBuffKind kind) => Selected = kind;

        /// <summary>回到安全屋时清空：buff 只管一趟。</summary>
        public static void Clear() => Selected = RaidBuffKind.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();
    }
}
