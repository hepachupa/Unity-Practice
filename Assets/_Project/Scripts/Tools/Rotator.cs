using UnityEngine;

namespace LNStudy.Tools
{
    /// <summary>환풍기 같은 장식 오브젝트를 일정 속도로 회전시킨다.</summary>
    public class Rotator : MonoBehaviour
    {
        [SerializeField] Vector3 degreesPerSecond = new Vector3(0f, 90f, 0f);
        [SerializeField] Space space = Space.Self;

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, space);
    }
}
