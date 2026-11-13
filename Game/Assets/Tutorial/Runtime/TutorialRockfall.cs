using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 庇护所门前的落石：玩家走到门口时从上方砸下，把来路彻底堵死（碰撞 + 挖掉导航面，怪物也过不来）。
    /// 落下后复活点改到门前，不会再被送回石头另一边。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialRockfall : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float fallSeconds = 0.6f;
        [SerializeField, Min(0f)] private float dropHeight = 7f;
        [Tooltip("落石之后倒下时在这里醒来（庇护所一侧）。")]
        [SerializeField] private Transform safeSide;

        public bool HasFallen { get; private set; }
        public Vector3 SafeSidePosition => safeSide != null ? safeSide.position : transform.position;

        public void Configure(Transform safe) => safeSide = safe;

        private void Awake()
        {
            SetRocksActive(false);
        }

        public void Fall()
        {
            if (HasFallen) return;
            HasFallen = true;
            SetRocksActive(true);
            StartCoroutine(Drop());
        }

        private IEnumerator Drop()
        {
            var rocks = new Transform[transform.childCount];
            var targets = new Vector3[rocks.Length];
            for (var i = 0; i < rocks.Length; i++)
            {
                rocks[i] = transform.GetChild(i);
                targets[i] = rocks[i].localPosition;
                rocks[i].localPosition = targets[i] + Vector3.up * (dropHeight + i * 0.6f);
            }

            var elapsed = 0f;
            while (elapsed < fallSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / fallSeconds);
                for (var i = 0; i < rocks.Length; i++)
                {
                    rocks[i].localPosition = targets[i] + Vector3.up * ((dropHeight + i * 0.6f) * (1f - t * t));
                }

                yield return null;
            }

            for (var i = 0; i < rocks.Length; i++) rocks[i].localPosition = targets[i];
        }

        private void SetRocksActive(bool active)
        {
            foreach (Transform rock in transform)
            {
                rock.gameObject.SetActive(active);
            }

            var obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = active;
            var blocker = GetComponent<Collider>();
            if (blocker != null) blocker.enabled = active;
        }
    }
}
