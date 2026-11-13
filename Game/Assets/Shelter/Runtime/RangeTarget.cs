using UnityEngine;

namespace WildernessExtraction.Shelter
{
    /// <summary>靶子：被打中时记一次命中和伤害，随后回满生命，不会被打坏。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class RangeTarget : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 1000f;
        [SerializeField] private Renderer face;
        [SerializeField] private Color hitColor = new Color(1f, 0.85f, 0.4f);

        private Health health;
        private Color baseColor;
        private float flash;

        public int Hits { get; private set; }
        public float TotalDamage { get; private set; }
        public float LastDamage { get; private set; }

        public void Configure(Renderer targetFace) => face = targetFace;

        public void ResetStats()
        {
            Hits = 0;
            TotalDamage = 0f;
            LastDamage = 0f;
        }

        private void Awake()
        {
            health = GetComponent<Health>();
            health.Configure(maxHealth);
            if (face != null) baseColor = face.material.color;
        }

        private void OnEnable() => GetComponent<Health>().Damaged += OnDamaged;

        private void OnDisable() => GetComponent<Health>().Damaged -= OnDamaged;

        private void OnDamaged(float amount)
        {
            Hits++;
            TotalDamage += amount;
            LastDamage = amount;
            health.Heal(health.Max);
            flash = 0.15f;
        }

        private void Update()
        {
            if (face == null) return;
            if (flash > 0f) flash -= Time.deltaTime;
            face.material.color = flash > 0f ? hitColor : baseColor;
        }
    }
}
