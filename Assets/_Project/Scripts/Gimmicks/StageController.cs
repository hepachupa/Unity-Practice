using LNStudy.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 스테이지 하나의 진행 관리.
    /// - 입구 트리거에 플레이어가 들어오면: 제목 배너, 목표 표시, 체크포인트 갱신
    /// - 클리어 조건: (몬스터 전부 처치) AND (기믹 필요 시 MarkGimmickDone 호출됨)
    /// - 클리어되면 출구 장벽을 열고 onCleared 실행
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class StageController : MonoBehaviour
    {
        [SerializeField] string stageTitle = "Stage";
        [TextArea] [SerializeField] string gimmickHint = "";
        [SerializeField] Health[] monsters;
        [SerializeField] bool requiresGimmick;
        [SerializeField] StageBarrier exitBarrier;
        [SerializeField] Transform checkpoint;
        [SerializeField] bool isFinalStage;

        public UnityEvent onCleared = new();

        public bool Entered { get; private set; }
        public bool GimmickDone { get; private set; }
        public bool Cleared { get; private set; }
        public string Title => stageTitle;

        public int Alive
        {
            get
            {
                int n = 0;
                foreach (var m in monsters) if (m != null && !m.IsDead) n++;
                return n;
            }
        }

        void Reset() => GetComponent<Collider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (Entered || !other.CompareTag("Player")) return;
            Entered = true;

            var combat = other.GetComponentInParent<PlayerCombat>();
            if (combat != null && checkpoint != null) combat.SetCheckpoint(checkpoint.position, checkpoint.rotation);

            StageHUD.Instance?.ShowBanner(stageTitle, 2.5f);
            StageHUD.Instance?.SetStage(this);
        }

        /// <summary>버튼 기믹 등에서 호출</summary>
        public void MarkGimmickDone()
        {
            if (GimmickDone) return;
            GimmickDone = true;
            StageHUD.Instance?.ShowToast("장치가 작동했다!");
        }

        void Update()
        {
            if (Cleared || !Entered) return;
            if (Alive > 0) return;
            if (requiresGimmick && !GimmickDone) return;

            Cleared = true;
            if (exitBarrier != null) exitBarrier.Open();
            StageHUD.Instance?.ShowBanner(isFinalStage ? "모든 스테이지 클리어!" : $"{stageTitle} 클리어!", 3f);
            onCleared.Invoke();
        }

        /// <summary>HUD 목표 문구</summary>
        public string ObjectiveText()
        {
            if (Cleared) return isFinalStage ? "축하해! 모든 스테이지를 클리어했어" : "길이 열렸다 → 다음 스테이지로";
            string s = monsters.Length > 0 ? $"몬스터 처치  {monsters.Length - Alive} / {monsters.Length}" : "";
            if (requiresGimmick)
            {
                if (s.Length > 0) s += "\n";
                s += GimmickDone ? "장치 작동 완료" : gimmickHint;
            }
            return s;
        }
    }
}
