using System;
using System.Collections;
using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 有裂缝的外墙：玩家带着炸药站在墙边按交互安放，引信烧完后墙被炸开，露出后面的台阶。
    /// 爆炸时离得太近会受伤（伤害走 Health，教学里不会真的死，只会倒下）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialBreachWall : MonoBehaviour
    {
        public enum WallState
        {
            Intact,
            Armed,
            Blown
        }

        [SerializeField] private ItemDefinition explosive;
        [Tooltip("炸开后隐藏的墙体。")]
        [SerializeField] private GameObject[] wallPieces = Array.Empty<GameObject>();
        [Tooltip("安放后出现的炸药包外观。")]
        [SerializeField] private GameObject charge;
        [SerializeField] private Material debrisMaterial;
        [SerializeField, Min(0.5f)] private float armRange = 2.2f;
        [SerializeField, Min(0.5f)] private float fuseSeconds = 3f;
        [SerializeField, Min(0.5f)] private float blastRadius = 3.5f;
        [SerializeField, Min(0f)] private float blastDamage = 25f;
        [SerializeField, Min(0f)] private float noiseRadius = 25f;

        public WallState State { get; private set; }
        public float FuseRemaining { get; private set; }
        public ItemDefinition Explosive => explosive;
        public event Action Armed;
        public event Action Blown;

        public void Configure(ItemDefinition item, GameObject[] pieces, GameObject chargeVisual, Material debris)
        {
            explosive = item;
            wallPieces = pieces ?? Array.Empty<GameObject>();
            charge = chargeVisual;
            debrisMaterial = debris;
        }

        private void Awake()
        {
            if (charge != null) charge.SetActive(false);
        }

        public bool InRange(Vector3 position)
        {
            var delta = position - transform.position;
            delta.y = 0f;
            return delta.magnitude <= armRange;
        }

        public bool CanArm(PlayerCharacter player)
        {
            return State == WallState.Intact && player != null && InRange(player.transform.position)
                   && explosive != null && player.Inventory.Inventory.CountOf(explosive) > 0;
        }

        /// <summary>扣掉一份炸药并点燃引信。</summary>
        public bool TryArm(PlayerCharacter player)
        {
            if (!CanArm(player)) return false;
            player.Inventory.Inventory.Remove(explosive, 1);
            State = WallState.Armed;
            FuseRemaining = fuseSeconds;
            if (charge != null) charge.SetActive(true);
            Armed?.Invoke();
            StartCoroutine(Fuse(player));
            return true;
        }

        private IEnumerator Fuse(PlayerCharacter player)
        {
            while (FuseRemaining > 0f)
            {
                FuseRemaining -= Time.deltaTime;
                yield return null;
            }

            FuseRemaining = 0f;
            Blow(player);
        }

        private void Blow(PlayerCharacter player)
        {
            State = WallState.Blown;
            if (charge != null) charge.SetActive(false);
            foreach (var piece in wallPieces)
            {
                if (piece != null) piece.SetActive(false);
            }

            CombatNoise.Emit(transform.position, noiseRadius);
            SpawnDebris();
            StartCoroutine(Flash());
            if (player != null)
            {
                var delta = player.transform.position - transform.position;
                delta.y = 0f;
                if (delta.magnitude <= blastRadius) player.Health.TakeDamage(blastDamage, gameObject);
            }

            Blown?.Invoke();
        }

        private IEnumerator Flash()
        {
            var go = new GameObject("BlastFlash");
            go.transform.position = transform.position + Vector3.up * 1.5f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.range = 12f;
            const float duration = 0.5f;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                light.intensity = Mathf.Lerp(12f, 0f, t / duration);
                yield return null;
            }

            Destroy(go);
        }

        private void SpawnDebris()
        {
            var random = new System.Random(7);
            for (var i = 0; i < 10; i++)
            {
                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "BlastDebris";
                var size = 0.25f + (float)random.NextDouble() * 0.35f;
                chunk.transform.localScale = Vector3.one * size;
                chunk.transform.position = transform.position + new Vector3(0f, 0.5f + (float)random.NextDouble() * 1.5f, ((float)random.NextDouble() - 0.5f) * 2.5f);
                if (debrisMaterial != null) chunk.GetComponent<Renderer>().sharedMaterial = debrisMaterial;
                var body = chunk.AddComponent<Rigidbody>();
                // 碎块往墙外（−X，台阶一侧）和上方飞，不砸向玩家。
                body.linearVelocity = new Vector3(-2f - (float)random.NextDouble() * 4f, 2f + (float)random.NextDouble() * 3f, ((float)random.NextDouble() - 0.5f) * 4f);
                body.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
                Destroy(chunk, 3f);
            }
        }
    }
}
