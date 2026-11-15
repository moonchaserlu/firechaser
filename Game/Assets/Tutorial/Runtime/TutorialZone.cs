using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>地面上的圆形区域：玩家中心进入时，教学导演记下 fact。只看 XZ 平面距离，不依赖物理触发。</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialZone : MonoBehaviour
    {
        [SerializeField] private string fact = string.Empty;
        [SerializeField, Min(0.5f)] private float radius = 2f;

        public string Fact => fact;
        public float Radius => radius;

        public void Configure(string zoneFact, float zoneRadius)
        {
            fact = zoneFact;
            radius = zoneRadius;
        }

        public bool Contains(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
