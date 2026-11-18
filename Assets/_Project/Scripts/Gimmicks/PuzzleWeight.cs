using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 압력 발판이 재는 "무게". Rigidbody 질량과 별개로 퍼즐용 값을 준다
    /// (캐릭터 Rigidbody 질량은 이동·잡기 물리에 쓰이므로 건드리지 않기 위해).
    /// 이 컴포넌트가 없으면 발판은 Grabbable의 Rigidbody 질량을 무게로 본다.
    /// </summary>
    public class PuzzleWeight : MonoBehaviour
    {
        [SerializeField] float weight = 10f;
        public float Value => weight;
    }
}
