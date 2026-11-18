using UnityEngine;
using UnityEngine.Events;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 여러 라멘 버튼이 "동시에" 눌려 있어야 발동 (각 버튼은 resetAfter 초 뒤 다시 꺼짐 → 시간 안에 모두 맞혀야 함).
    /// 한 번 발동하면 모든 버튼을 눌린 상태로 고정한다.
    /// </summary>
    public class ButtonGroup : MonoBehaviour
    {
        [SerializeField] RamenButton[] buttons;
        public UnityEvent onAllPressed = new();

        public bool Completed { get; private set; }
        public int PressedCount
        {
            get
            {
                int n = 0;
                foreach (var b in buttons) if (b != null && b.IsPressed) n++;
                return n;
            }
        }
        public int Total => buttons != null ? buttons.Length : 0;

        void Update()
        {
            if (Completed || buttons == null || buttons.Length == 0) return;
            if (PressedCount < buttons.Length) return;

            Completed = true;
            foreach (var b in buttons)
            {
                // 더 이상 꺼지지 않게
                var so = b.GetComponent<RamenButton>();
                if (so != null) so.enabled = false;
            }
            onAllPressed.Invoke();
        }
    }
}
