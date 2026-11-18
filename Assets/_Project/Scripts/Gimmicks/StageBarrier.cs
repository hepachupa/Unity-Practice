using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace LNStudy.Gimmicks
{
    /// <summary>스테이지 사이를 막는 마법 장벽. Open()이 불리면 아래로 꺼지며 사라지고 길이 열린다.</summary>
    public class StageBarrier : MonoBehaviour
    {
        [SerializeField] float openTime = 1.2f;
        [SerializeField] float sinkDistance = 6f;

        public bool IsOpen { get; private set; }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var o in GetComponentsInChildren<NavMeshObstacle>()) o.enabled = false;
            StartCoroutine(Sink());
        }

        IEnumerator Sink()
        {
            Vector3 start = transform.position;
            var lights = GetComponentsInChildren<Light>();
            float[] li = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) li[i] = lights[i].intensity;

            for (float t = 0f; t < openTime; t += Time.deltaTime)
            {
                float k = t / openTime;
                transform.position = start + Vector3.down * (sinkDistance * k * k);
                for (int i = 0; i < lights.Length; i++) lights[i].intensity = li[i] * (1f - k);
                yield return null;
            }
            gameObject.SetActive(false);
        }
    }
}
