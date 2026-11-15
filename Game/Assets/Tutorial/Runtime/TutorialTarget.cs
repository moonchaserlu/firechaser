using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>射击场的悬挂空油罐：一枪打碎，打碎后隐藏并不再挡子弹。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class TutorialTarget : MonoBehaviour
    {
        private Health health;

        public bool IsBroken => Health.IsDead;
        public Health Health => health != null ? health : health = GetComponent<Health>();

        private void Awake()
        {
            Health.Configure(1f);
            Health.Died += Break;
        }

        private void OnDestroy()
        {
            if (health != null) health.Died -= Break;
        }

        private void Break()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        }
    }
}
