using LNStudy.CameraSystem;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 신호를 받으면 열린 위치·각도로, 신호가 끊기면 닫힌 위치로 움직이는 장치 (문, 엘리베이터, 도개교).
    /// - AddSignal / RemoveSignal: 발판 onPressed / onReleased에 연결. 신호 수가 requiredSignals 이상이면 열림
    ///   (발판 두 개를 동시에 눌러야 하는 퍼즐은 requiredSignals = 2)
    /// - OpenForever: 한 번 열리면 계속 열림 (가벼운 스위치 등)
    /// Rigidbody(Kinematic)가 있으면 MovePosition/MoveRotation으로 움직여 위에 탄 캐릭터를 같이 밀어 올린다.
    /// </summary>
    public class PuzzleMover : MonoBehaviour
    {
        [Tooltip("닫힌 상태 기준 열린 위치까지의 이동 (로컬, 부모 기준)")]
        [SerializeField] Vector3 openOffset = new Vector3(0f, 3f, 0f);
        [Tooltip("닫힌 상태 기준 열린 각도까지의 회전 (오일러)")]
        [SerializeField] Vector3 openRotation;
        [Tooltip("열리는 데 걸리는 시간 (초)")]
        [SerializeField] float openTime = 1.2f;
        [SerializeField] float closeTime = 1.6f;
        [SerializeField] int requiredSignals = 1;
        [Tooltip("끝까지 열리면 그 뒤로는 신호가 끊겨도 열린 채로 (엘리베이터에서 내리는 순간 다시 내려가는 것 방지)")]
        [SerializeField] bool stayOpenWhenReached;
        [Tooltip("움직이는 동안 켤 소품 (톱니 불빛 등, 선택)")]
        [SerializeField] Light activeLight;
        [Header("Feedback")]
        [SerializeField] bool feedback = true;
        [SerializeField] string startSfx = "rumble";
        [SerializeField] string stopSfx = "thud";
        [SerializeField] float stopShake = 0.06f;

        public bool IsOpen => forever || signals >= requiredSignals;
        public float Progress => t;

        int signals;
        bool forever;
        float t;
        Vector3 closedPos;
        Quaternion closedRot;
        Rigidbody body;
        bool moving;

        void Awake()
        {
            closedPos = transform.localPosition;
            closedRot = transform.localRotation;
            body = GetComponent<Rigidbody>();
            if (body != null) body.isKinematic = true;
        }

        void OnStopped()
        {
            if (!string.IsNullOrEmpty(stopSfx)) Sfx.Play(stopSfx, transform.position, 0.8f, 1f);
            if (stopShake > 0f) SideFollowCamera.Shake(stopShake, 0.22f);
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            DustFx.Puff(new Vector3(b.center.x, b.min.y + 0.05f, Mathf.Min(b.center.z, 0f)), 8, 0.45f, 1.8f);
        }

        public void AddSignal() => signals++;
        public void RemoveSignal() => signals = Mathf.Max(0, signals - 1);
        public void OpenForever() => forever = true;

        void FixedUpdate()
        {
            float target = IsOpen ? 1f : 0f;
            float dur = IsOpen ? openTime : closeTime;
            float prev = t;
            t = Mathf.MoveTowards(t, target, Time.fixedDeltaTime / Mathf.Max(0.05f, dur));
            if (stayOpenWhenReached && t >= 1f) forever = true;

            bool nowMoving = !Mathf.Approximately(prev, t);
            if (feedback && nowMoving != moving)
            {
                if (nowMoving) { if (!string.IsNullOrEmpty(startSfx)) Sfx.Play(startSfx, transform.position, 0.7f, Random.Range(0.9f, 1.05f)); }
                else OnStopped();
            }
            moving = nowMoving;
            if (activeLight != null) activeLight.enabled = !Mathf.Approximately(t, target) || IsOpen;
            if (Mathf.Approximately(prev, t) && body == null) return;

            float e = Mathf.SmoothStep(0f, 1f, t);
            Vector3 lp = closedPos + openOffset * e;
            Quaternion lr = closedRot * Quaternion.Euler(openRotation * e);
            Transform parent = transform.parent;
            Vector3 wp = parent != null ? parent.TransformPoint(lp) : lp;
            Quaternion wr = parent != null ? parent.rotation * lr : lr;

            if (body != null)
            {
                body.MovePosition(wp);
                body.MoveRotation(wr);
            }
            else
            {
                transform.localPosition = lp;
                transform.localRotation = lr;
            }
        }
    }
}
