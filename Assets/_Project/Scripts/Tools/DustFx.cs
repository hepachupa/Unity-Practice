using UnityEngine;

namespace LNStudy.Tools
{
    /// <summary>
    /// 먼지·부스러기·반짝이를 한 개의 월드 공간 파티클 시스템으로 뿌린다 (Emit 방식, 오브젝트 생성 없음).
    /// 머티리얼은 처음 Init을 부른 컴포넌트가 넘긴 것을 쓴다 (빌드에서 셰이더가 빠지지 않도록 에셋으로 넘김).
    /// </summary>
    public static class DustFx
    {
        static ParticleSystem ps;
        static Material material;

        public static readonly Color DustColor = new Color(0.86f, 0.8f, 0.68f, 0.75f);
        public static readonly Color CrumbColor = new Color(0.45f, 0.27f, 0.15f, 1f);
        public static readonly Color SparkleColor = new Color(1f, 0.88f, 0.45f, 1f);

        public static void Init(Material mat)
        {
            if (mat != null && material == null) material = mat;
        }

        static bool Ensure()
        {
            if (ps != null) return true;
            if (material == null) return false;
            var go = new GameObject("[DustFx]");
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.gravityModifier = 0f;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.12f;
            limit.limit = 0.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return true;
        }

        /// <summary>바닥 먼지: 옆으로 퍼지며 살짝 떠오름</summary>
        public static void Puff(Vector3 pos, int count, float size = 0.35f, float spread = 1.6f, Color? color = null)
        {
            if (!Ensure()) return;
            var p = new ParticleSystem.EmitParams { startColor = color ?? DustColor };
            for (int i = 0; i < count; i++)
            {
                float side = Random.Range(-1f, 1f);
                p.position = pos + new Vector3(side * 0.15f, Random.Range(0f, 0.08f), Random.Range(-0.2f, 0.2f));
                p.velocity = new Vector3(side * spread, Random.Range(0.3f, 0.9f), Random.Range(-0.4f, 0.4f));
                p.startSize = size * Random.Range(0.7f, 1.3f);
                p.startLifetime = Random.Range(0.4f, 0.75f);
                ps.Emit(p, 1);
            }
        }

        /// <summary>사방으로 튀는 조각·반짝이 (중력 없이 감속)</summary>
        public static void Burst(Vector3 pos, int count, Color color, float speed = 2.5f, float size = 0.12f, float life = 0.6f)
        {
            if (!Ensure()) return;
            var p = new ParticleSystem.EmitParams { startColor = color };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                p.position = pos;
                p.velocity = dir * speed * Random.Range(0.5f, 1f);
                p.startSize = size * Random.Range(0.6f, 1.4f);
                p.startLifetime = life * Random.Range(0.7f, 1.2f);
                ps.Emit(p, 1);
            }
        }
    }
}
