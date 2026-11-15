using System.Collections.Generic;
using UnityEngine;

namespace WildernessExtraction.Map01
{
    /// <summary>
    /// 屋顶显隐的观察者。只有 <see cref="IsLocal"/> 为 true 的观察者（本机操控的玩家）会让屋顶隐藏；
    /// 联网后由所有权决定 IsLocal，屋顶状态从不同步。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevealViewer : MonoBehaviour
    {
        private static readonly List<RevealViewer> Active = new List<RevealViewer>();

        [SerializeField] private bool isLocal = true;

        /// <summary>最近启用的本机观察者；没有时为 null。</summary>
        public static RevealViewer Local
        {
            get
            {
                for (var i = Active.Count - 1; i >= 0; i--)
                {
                    if (Active[i] != null && Active[i].isLocal)
                    {
                        return Active[i];
                    }
                }

                return null;
            }
        }

        public bool IsLocal
        {
            get => isLocal;
            set => isLocal = value;
        }

        private void OnEnable()
        {
            Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }
    }
}
