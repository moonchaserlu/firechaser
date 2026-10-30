using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction
{
    /// <summary>
    /// 怪物死亡后变成可搜索的箱子：等死亡动画倒地后隐藏模型，在原地生成箱子并按掉落表装入物品。
    /// 只订阅 MeleeEnemy.StateChanged，不改 AI、伤害和导航；箱子走现有 LootContainer 搜索流程。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLootDrop : MonoBehaviour
    {
        [SerializeField] private LootContainer boxPrefab;
        [SerializeField] private LootTable lootTable;
        [SerializeField] private string boxName = "失火者的遗物";
        [SerializeField, Min(0f)] private float searchDuration = 1.5f;
        [Tooltip("从死亡到变成箱子的等待（秒），留给死亡动画倒地。")]
        [SerializeField, Min(0f)] private float delay = 1.1f;
        [Tooltip("变成箱子时隐藏的模型。")]
        [SerializeField] private GameObject hideOnDrop;

        private MeleeEnemy enemy;
        private Vector3 groundPosition;
        private float yaw;

        public LootContainer Spawned { get; private set; }

        public void Configure(LootContainer prefab, LootTable table, GameObject model)
        {
            boxPrefab = prefab;
            lootTable = table;
            hideOnDrop = model;
        }

        private void Awake()
        {
            enemy = GetComponent<MeleeEnemy>();
            groundPosition = transform.position;
            yaw = transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            if (enemy != null) enemy.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            if (enemy != null) enemy.StateChanged -= OnStateChanged;
        }

        private void LateUpdate()
        {
            // 活着时记下脚下位置和朝向；MeleeEnemy 死亡时会把根节点转 90° 并抬高，不能再用那时的变换。
            if (enemy == null || enemy.CurrentState == MeleeEnemy.State.Dead) return;
            groundPosition = transform.position;
            yaw = transform.eulerAngles.y;
        }

        private void OnStateChanged(MeleeEnemy.State state)
        {
            if (state != MeleeEnemy.State.Dead || Spawned != null || boxPrefab == null) return;
            StartCoroutine(DropAfterDelay());
        }

        private IEnumerator DropAfterDelay()
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Spawned = Instantiate(boxPrefab, groundPosition, Quaternion.Euler(0f, yaw, 0f));
            // 每只怪物各自的种子：同一局里不同箱子掉落不同，同一局重放可复现。
            var seed = RunLootSeed.Current ^ Mathf.RoundToInt(groundPosition.x * 73f + groundPosition.z * 197f);
            var items = lootTable != null ? lootTable.CreateDrops(new System.Random(seed)) : new List<ItemDefinition>();
            Spawned.InitializeWithItems(boxName, ContainerType.Corpse, searchDuration, items);
            if (hideOnDrop != null) hideOnDrop.SetActive(false);
        }
    }
}
