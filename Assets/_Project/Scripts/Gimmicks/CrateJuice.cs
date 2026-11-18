using LNStudy.CameraSystem;
using LNStudy.Tools;
using UnityEngine;

namespace LNStudy.Gimmicks
{
    /// <summary>
    /// 상자 손맛: 바닥에 끌릴 때 긁히는 소리(속도에 따라 크기·높이) + 아래 모서리 먼지,
    /// 떨어지면 쿵 소리·먼지·화면 흔들림.
    /// 머핀 타임(횡스크롤 상자)과 학교 방어전(사물함 바리케이드)에서 같이 쓴다 → 수평 속도 전체 기준,
    /// 피벗이 바닥에 있는 메시(사물함)는 pivotAtBottom.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrateJuice : MonoBehaviour
    {
        [SerializeField] Material dustMaterial;
        [SerializeField] float halfHeight = 0.5f;
        [SerializeField] LayerMask groundMask = 1;
        [SerializeField] bool pivotAtBottom;

        Rigidbody body;
        AudioSource scrape;
        float dustTimer;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            DustFx.Init(dustMaterial);
            scrape = Sfx.CreateLoop(gameObject, "scrape");
        }

        Vector3 Bottom => pivotAtBottom ? transform.position : transform.position + Vector3.down * halfHeight;

        bool Grounded() =>
            Physics.Raycast(Bottom + Vector3.up * 0.2f, Vector3.down, 0.32f, groundMask, QueryTriggerInteraction.Ignore);

        void Update()
        {
            Vector3 planar = body.linearVelocity;
            planar.y = 0f;
            float speed = planar.magnitude;
            bool sliding = speed > 0.15f && Grounded();
            float targetVol = sliding ? Mathf.Clamp01(speed / 2f) * 0.45f * Sfx.MasterVolume * 1.6f : 0f;
            scrape.volume = Mathf.MoveTowards(scrape.volume, targetVol, Time.deltaTime * 3f);
            scrape.pitch = 0.75f + Mathf.Clamp01(speed / 3f) * 0.5f;
            if (scrape.volume > 0.001f && !scrape.isPlaying) scrape.Play();
            else if (scrape.volume <= 0.001f && scrape.isPlaying) scrape.Pause();

            if (sliding)
            {
                dustTimer += Time.deltaTime * speed;
                if (dustTimer > 0.35f)
                {
                    dustTimer = 0f;
                    Vector3 back = -planar.normalized * 0.4f;
                    DustFx.Puff(Bottom + back + Vector3.up * 0.03f, 2, 0.3f, 0.5f);
                }
            }
        }

        void OnCollisionEnter(Collision c)
        {
            float vy = c.relativeVelocity.y;
            if (Mathf.Abs(vy) < 2f || c.rigidbody != null && c.rigidbody.GetComponent<LNStudy.Player.PlayerMotor>() != null) return;
            float k = Mathf.Clamp01(Mathf.Abs(vy) / 8f);
            Sfx.Play("thud", transform.position, 0.4f + k * 0.6f, 0.9f);
            DustFx.Puff(Bottom, 6 + (int)(k * 8), 0.45f, 1.6f);
            SideFollowCamera.Shake(0.04f + k * 0.08f, 0.2f);
            ThirdPersonCamera.Active?.Shake(0.03f + k * 0.06f, 0.2f);
        }
    }
}
