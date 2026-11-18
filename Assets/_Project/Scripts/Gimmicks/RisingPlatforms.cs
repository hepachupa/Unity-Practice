using System.Collections;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>물속에 잠긴 발판들이 차례로 떠오른다 (버튼 이벤트로 Activate 호출).</summary>
    public class RisingPlatforms : MonoBehaviour
    {
        [SerializeField] Transform[] platforms;
        [Tooltip("물속에 잠겨 있는 깊이")]
        [SerializeField] float sunkenDepth = 4f;
        [SerializeField] float riseTime = 0.9f;
        [SerializeField] float interval = 0.25f;

        Vector3[] targets;
        bool activated;

        void Awake()
        {
            targets = new Vector3[platforms.Length];
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] == null) continue;
                targets[i] = platforms[i].position;
                platforms[i].position = targets[i] + Vector3.down * sunkenDepth;
            }
        }

        public void Activate()
        {
            if (activated) return;
            activated = true;
            StartCoroutine(RiseAll());
        }

        IEnumerator RiseAll()
        {
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] != null) StartCoroutine(Rise(platforms[i], targets[i]));
                yield return new WaitForSeconds(interval);
            }
        }

        IEnumerator Rise(Transform p, Vector3 target)
        {
            Vector3 start = p.position;
            for (float t = 0f; t < riseTime; t += Time.deltaTime)
            {
                float k = t / riseTime;
                k = 1f - Mathf.Pow(1f - k, 3f); // 감속
                // 마지막에 살짝 출렁
                float bob = Mathf.Sin(k * Mathf.PI) * 0.15f;
                p.position = Vector3.LerpUnclamped(start, target, k) + Vector3.up * bob;
                yield return null;
            }
            p.position = target;
        }
    }
}
