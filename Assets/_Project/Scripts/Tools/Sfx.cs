using System.Collections.Generic;
using UnityEngine;

namespace LNStudy.Tools
{
    /// <summary>
    /// 오디오 에셋 없이 코드로 합성한 효과음 (토이 프로젝트용). 처음 쓸 때 한 번 만들어 캐시한다.
    /// Sfx.Play("jump", position, volume, pitch) — 위치는 화면 좌우 패닝에만 쓴다 (2D 사운드).
    /// 이름: jump, land, step, click, clunk, rumble, thud, pop, munch, chime, switch, scrape(루프), creak, skid, call
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        public static float MasterVolume = 0.55f;

        static readonly Dictionary<string, AudioClip> clips = new();
        static AudioSource[] pool;
        static int next;
        static GameObject host;

        public static AudioClip Get(string name)
        {
            if (clips.TryGetValue(name, out var c) && c != null) return c;
            c = Build(name);
            clips[name] = c;
            return c;
        }

        public static void Play(string name, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            var clip = Get(name);
            if (clip == null) return;
            EnsurePool();
            var src = pool[next];
            next = (next + 1) % pool.Length;
            src.pitch = pitch;
            var cam = Camera.main;
            src.panStereo = cam != null ? Mathf.Clamp((position.x - cam.transform.position.x) / 9f, -1f, 1f) * 0.6f : 0f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume) * MasterVolume);
        }

        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            var cam = Camera.main;
            Play(name, cam != null ? cam.transform.position : Vector3.zero, volume, pitch);
        }

        /// <summary>루프 재생용 AudioSource를 대상 오브젝트에 붙여 돌려준다 (상자 끄는 소리 등)</summary>
        public static AudioSource CreateLoop(GameObject owner, string name)
        {
            var src = owner.AddComponent<AudioSource>();
            src.clip = Get(name);
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        static void EnsurePool()
        {
            if (host != null && pool != null) return;
            host = new GameObject("[Sfx]");
            Object.DontDestroyOnLoad(host);
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = host.AddComponent<AudioSource>();
                pool[i].playOnAwake = false;
                pool[i].spatialBlend = 0f;
            }
        }

        // ───────────────────────── Synthesis ─────────────────────────

        static AudioClip Make(string name, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                data[i] = Mathf.Clamp(f(t, t / seconds), -1f, 1f);
            }
            var clip = AudioClip.Create("sfx_" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static readonly System.Random rng = new(1234);
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Sine(float phase) => Mathf.Sin(phase * Mathf.PI * 2f);
        static float Env(float k, float attack, float decayPow) =>
            (k < attack ? k / attack : Mathf.Pow(1f - (k - attack) / (1f - attack), decayPow));

        static AudioClip Build(string name)
        {
            float lp = 0f, lp2 = 0f, phase = 0f;
            switch (name)
            {
                case "jump":
                    return Make(name, 0.16f, (t, k) =>
                    {
                        phase += Mathf.Lerp(320f, 760f, Mathf.Sqrt(k)) / Rate;
                        return (float)System.Math.Tanh(2.2f * Sine(phase)) * 0.35f * Env(k, 0.05f, 2f);
                    });
                case "land":
                    return Make(name, 0.18f, (t, k) =>
                    {
                        lp += 0.08f * (Noise() - lp);
                        phase += Mathf.Lerp(130f, 55f, k) / Rate;
                        return (lp * 1.4f + Sine(phase) * 0.6f) * Env(k, 0.02f, 3f);
                    });
                case "step":
                    return Make(name, 0.05f, (t, k) =>
                    {
                        lp += 0.25f * (Noise() - lp);
                        return lp * 0.5f * Env(k, 0.05f, 3f);
                    });
                case "click":
                    return Make(name, 0.07f, (t, k) =>
                    {
                        phase += 1900f / Rate;
                        return (Sine(phase) * 0.4f + Noise() * 0.2f * (1f - k)) * Env(k, 0.02f, 4f);
                    });
                case "clunk":
                    return Make(name, 0.3f, (t, k) =>
                    {
                        lp += 0.06f * (Noise() - lp);
                        phase += Mathf.Lerp(160f, 90f, k) / Rate;
                        return (Sine(phase) * 0.7f + lp * 1.2f + Sine(phase * 3.1f) * 0.15f) * Env(k, 0.01f, 3f);
                    });
                case "rumble":
                    return Make(name, 1.3f, (t, k) =>
                    {
                        lp += 0.015f * (Noise() - lp);
                        lp2 += 0.04f * (lp - lp2);
                        phase += 48f / Rate;
                        return (lp2 * 4f + Sine(phase) * 0.15f) * Env(k, 0.15f, 1.5f) * (0.8f + 0.2f * Sine(t * 7f));
                    });
                case "thud":
                    return Make(name, 0.35f, (t, k) =>
                    {
                        lp += 0.05f * (Noise() - lp);
                        phase += Mathf.Lerp(95f, 45f, k) / Rate;
                        return (Sine(phase) * 0.9f + lp * 1.5f) * Env(k, 0.01f, 2.5f);
                    });
                case "pop":
                    return Make(name, 0.2f, (t, k) =>
                    {
                        phase += Mathf.Lerp(520f, 1500f, k) / Rate;
                        return (Sine(phase) * 0.45f + Sine(phase * 2f) * 0.15f) * Env(k, 0.03f, 2f);
                    });
                case "munch":
                    return Make(name, 0.12f, (t, k) =>
                    {
                        float n = Noise();
                        lp += 0.35f * (n - lp);
                        lp2 += 0.08f * (n - lp2);
                        float crunch = (lp - lp2) * (Sine(t * 90f) > 0f ? 1f : 0.4f);
                        return crunch * 0.9f * Env(k, 0.05f, 1.5f);
                    });
                case "chime":
                {
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    return Make(name, 1.1f, (t, k) =>
                    {
                        float s = 0f;
                        for (int i = 0; i < notes.Length; i++)
                        {
                            float start = i * 0.11f;
                            if (t < start) continue;
                            float lt = t - start;
                            s += Sine(notes[i] * lt) * Mathf.Exp(-lt * 3.5f) * 0.22f;
                            s += Sine(notes[i] * 2f * lt) * Mathf.Exp(-lt * 6f) * 0.05f;
                        }
                        return s;
                    });
                }
                case "switch":
                    return Make(name, 0.14f, (t, k) =>
                    {
                        phase += (k < 0.45f ? 620f : 930f) / Rate;
                        return Sine(phase) * 0.3f * Env(k, 0.05f, 1.5f);
                    });
                case "call":
                    return Make(name, 0.22f, (t, k) =>
                    {
                        phase += (k < 0.4f ? 700f : k < 0.7f ? 880f : 700f) / Rate;
                        return Sine(phase) * 0.25f * Env(k, 0.05f, 1.2f);
                    });
                case "scrape":
                    return Make(name, 1f, (t, k) =>
                    {
                        float n = Noise();
                        lp += 0.12f * (n - lp);
                        lp2 += 0.02f * (n - lp2);
                        float grit = (lp - lp2) * (0.7f + 0.3f * Sine(t * 23f)) * (rng.NextDouble() < 0.002 ? 3f : 1f);
                        float edge = Mathf.Min(1f, Mathf.Min(k, 1f - k) * 40f); // 루프 이음새 부드럽게
                        return grit * 0.8f * Mathf.Lerp(0.6f, 1f, edge);
                    });
                case "creak":
                    return Make(name, 0.6f, (t, k) =>
                    {
                        phase += (220f + 60f * Sine(t * 9f) + 120f * k) / Rate;
                        float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
                        lp += 0.1f * (saw - lp);
                        return lp * 0.35f * Env(k, 0.1f, 1f);
                    });
                case "skid":
                    return Make(name, 0.16f, (t, k) =>
                    {
                        float n = Noise();
                        lp += 0.5f * (n - lp);
                        lp2 += 0.1f * (n - lp2);
                        return (lp - lp2) * 0.6f * Env(k, 0.05f, 2f);
                    });
            }
            return null;
        }
    }
}
