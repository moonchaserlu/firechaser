using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>可提供“黑暗安全”的区域。装饰灯和枪口闪光不实现该接口。</summary>
    public interface ISafeLightArea
    {
        /// <summary>光源当前是否点亮（熄灭的火堆或失效的灯返回 false）。</summary>
        bool IsLit { get; }

        bool Contains(Vector3 worldPosition);
    }

    /// <summary>
    /// 登记当前有效光源，供黑暗判定查询，避免每帧全场查找。
    /// 光源在 OnEnable/OnDisable 时自行登记和注销，场景重载后自然清空。
    /// </summary>
    public sealed class LightRegistry
    {
        private readonly List<ISafeLightArea> areas = new List<ISafeLightArea>();

        /// <summary>当前场景使用的注册表。测试可另建实例。</summary>
        public static LightRegistry Scene { get; } = new LightRegistry();

        public int Count => areas.Count;

        public void Register(ISafeLightArea area)
        {
            if (area != null && !areas.Contains(area))
            {
                areas.Add(area);
            }
        }

        public void Unregister(ISafeLightArea area)
        {
            areas.Remove(area);
        }

        /// <summary>位置处于任一点亮光源范围内即安全；重叠不叠加效果。</summary>
        public bool IsLit(Vector3 worldPosition)
        {
            foreach (var area in areas)
            {
                if (area.IsLit && area.Contains(worldPosition))
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            areas.Clear();
        }

        // 关闭 Domain Reload 时静态实例会跨 Play 保留，进入 Play 前清空。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Scene.Clear();
        }
    }
}
