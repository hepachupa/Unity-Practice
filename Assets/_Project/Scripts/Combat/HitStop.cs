using System.Collections;
using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>
    /// 타격감용 히트스톱. 아주 짧게 게임 시간을 거의 멈춘다.
    /// 겹쳐 호출되면 더 긴 쪽으로 연장하고, 끝나면 원래 timeScale로 돌려놓는다.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop instance;
        float until;
        float restoreScale = 1f;
        bool running;

        public static void Do(float duration, float scale = 0.03f)
        {
            if (duration <= 0f) return;
            if (instance == null)
            {
                var go = new GameObject("[HitStop]");
                go.hideFlags = HideFlags.HideInHierarchy;
                instance = go.AddComponent<HitStop>();
            }
            instance.Begin(duration, scale);
        }

        void Begin(float duration, float scale)
        {
            until = Mathf.Max(until, Time.unscaledTime + duration);
            if (running) return;
            restoreScale = Time.timeScale;
            Time.timeScale = scale;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            running = true;
            while (Time.unscaledTime < until) yield return null;
            Time.timeScale = restoreScale;
            running = false;
        }

        void OnDisable()
        {
            if (running) Time.timeScale = restoreScale;
            running = false;
        }
    }
}
