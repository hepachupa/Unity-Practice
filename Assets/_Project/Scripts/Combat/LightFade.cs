using UnityEngine;

namespace LNStudy.Combat
{
    /// <summary>명중 섬광처럼 잠깐 켜졌다 꺼지는 라이트.</summary>
    [RequireComponent(typeof(Light))]
    public class LightFade : MonoBehaviour
    {
        [SerializeField] float duration = 0.12f;

        Light lightComp;
        float startIntensity, t;

        void Awake()
        {
            lightComp = GetComponent<Light>();
            startIntensity = lightComp.intensity;
        }

        void Update()
        {
            t += Time.deltaTime;
            lightComp.intensity = Mathf.Lerp(startIntensity, 0f, t / duration);
            if (t >= duration) lightComp.enabled = false;
        }
    }
}
