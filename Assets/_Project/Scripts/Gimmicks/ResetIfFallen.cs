using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>상자 등이 구덩이로 떨어지면 처음 자리로 되돌린다 (퍼즐이 막히지 않게).</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ResetIfFallen : MonoBehaviour
    {
        [SerializeField] float minY = -4f;

        Rigidbody body;
        Vector3 startPos;
        Quaternion startRot;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            startPos = transform.position;
            startRot = transform.rotation;
        }

        void FixedUpdate()
        {
            if (transform.position.y > minY) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = startPos;
            body.rotation = startRot;
            transform.SetPositionAndRotation(startPos, startRot);
            Sfx.Play("pop", startPos, 0.6f, 0.8f);
            DustFx.Burst(startPos, 14, DustFx.SparkleColor, 2f, 0.15f);
        }
    }
}
