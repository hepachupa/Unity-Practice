using System.Collections;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>세워져 있던 통나무 다리가 쿵 하고 쓰러져 구덩이를 잇는다 (버튼 이벤트로 Lower 호출).</summary>
    public class LogBridge : MonoBehaviour
    {
        [Tooltip("회전축 (자식 다리를 이 축으로 넘어뜨림)")]
        [SerializeField] Transform pivot;
        [SerializeField] Vector3 raisedEuler = new Vector3(0f, 0f, 88f);
        [SerializeField] Vector3 loweredEuler = Vector3.zero;
        [SerializeField] float fallTime = 1.1f;

        bool lowered;

        void Awake()
        {
            if (pivot != null) pivot.localRotation = Quaternion.Euler(raisedEuler);
        }

        public void Lower()
        {
            if (lowered || pivot == null) return;
            lowered = true;
            StartCoroutine(Fall());
        }

        IEnumerator Fall()
        {
            Quaternion a = Quaternion.Euler(raisedEuler), b = Quaternion.Euler(loweredEuler);
            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float k = t / fallTime;
                pivot.localRotation = Quaternion.SlerpUnclamped(a, b, k * k); // 중력처럼 가속
                yield return null;
            }
            // 쿵: 살짝 튀었다가 안착
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                float bounce = Mathf.Sin(t / 0.25f * Mathf.PI) * 4f;
                pivot.localRotation = b * Quaternion.Euler(raisedEuler.normalized * bounce);
                yield return null;
            }
            pivot.localRotation = b;
            LNStudy.CameraSystem.ThirdPersonCamera.Active?.Shake(0.15f, 0.3f);
            StageHUD.Instance?.ShowToast("다리가 놓였다!");
        }
    }
}
