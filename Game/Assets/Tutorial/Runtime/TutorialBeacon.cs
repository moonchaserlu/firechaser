using System;
using UnityEngine;

namespace WildernessExtraction.Tutorial
{
    /// <summary>
    /// 引路灯：某一段完成时点亮，变成有效安全光，同时移开挡路的阻隔、露出藏着的东西，并成为新的复活点。
    /// 未点亮时不提供任何保护。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialBeacon : MonoBehaviour
    {
        [Tooltip("这一段完成时点亮；留空表示开局就亮（出生点的灯）。")]
        [SerializeField] private string litAfterStep = string.Empty;
        [SerializeField] private SafeLightSource lamp;
        [Tooltip("点亮时隐藏（倒下的木梁、铁护栏）。")]
        [SerializeField] private GameObject[] hideWhenLit = Array.Empty<GameObject>();
        [Tooltip("点亮时出现（例如失火者身旁的遗物）。")]
        [SerializeField] private GameObject[] showWhenLit = Array.Empty<GameObject>();
        [Tooltip("倒下后在这里醒来；为空时用灯本身的位置。")]
        [SerializeField] private Transform respawnPoint;

        public string LitAfterStep => litAfterStep;
        public bool IsLit { get; private set; }
        public Vector3 RespawnPosition => respawnPoint != null ? respawnPoint.position : transform.position;
        public SafeLightSource Lamp => lamp;

        public void Configure(string afterStep, SafeLightSource source, GameObject[] hide, GameObject[] show, Transform respawn)
        {
            litAfterStep = afterStep ?? string.Empty;
            lamp = source;
            hideWhenLit = hide ?? Array.Empty<GameObject>();
            showWhenLit = show ?? Array.Empty<GameObject>();
            respawnPoint = respawn;
        }

        private void Start()
        {
            if (string.IsNullOrEmpty(litAfterStep))
            {
                Light();
                return;
            }

            if (lamp != null) lamp.SetLit(false);
            foreach (var target in showWhenLit)
            {
                if (target != null) target.SetActive(false);
            }
        }

        public void Light()
        {
            IsLit = true;
            if (lamp != null) lamp.SetLit(true);
            foreach (var target in hideWhenLit)
            {
                if (target != null) target.SetActive(false);
            }

            foreach (var target in showWhenLit)
            {
                if (target != null) target.SetActive(true);
            }
        }
    }
}
