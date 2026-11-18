using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// Built-in 머티리얼을 URP로 옮긴다.
    /// - Standard / Legacy Diffuse·Specular·Bumped·Transparent·Self-Illumin → URP Lit
    /// - Legacy Particles → URP Particles/Unlit (Additive 유지), Mobile/Unlit·Unlit → URP Unlit
    /// - Skybox는 파이프라인과 무관하게 동작하므로 건너뛴다
    /// 셰이더를 바꾸면 사라지는 값(_MainTex, _Glossiness, _Mode 등)은 직렬화 데이터에서 직접 읽어 다시 넣는다.
    /// 이미 URP Lit인 머티리얼은 건너뛴다 (여러 번 실행해도 안전).
    /// </summary>
    public static class StandardToURPConverter
    {
        const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string DroneMaterialFolder = "Assets/Sci_fi_Drones/Materials";
        public const string ScifiKitFolder = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Materials";

        [MenuItem("LNStudy/Rendering/Convert Creepy_Cat Scifi Kit Leftover Materials to URP")]
        static void ConvertScifiKit() => ConvertFolder(ScifiKitFolder);

        [MenuItem("LNStudy/Rendering/Convert Sci-fi Drone Materials to URP")]
        static void ConvertDrones() => ConvertFolder(DroneMaterialFolder);

        [MenuItem("LNStudy/Rendering/Convert Selected Materials to URP")]
        static void ConvertSelection()
        {
            int n = 0;
            foreach (var obj in Selection.objects)
                if (obj is Material m && Convert(m)) n++;
            AssetDatabase.SaveAssets();
            Debug.Log($"[StandardToURP] 선택 머티리얼 {n}개 변환");
        }

        public static int ConvertFolder(string folder)
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null && Convert(mat)) n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[StandardToURP] {folder}: {n}개 변환");
            return n;
        }

        public static bool Convert(Material mat)
        {
            var lit = Shader.Find(LitShaderName);
            if (lit == null) { Debug.LogError("[StandardToURP] URP Lit 셰이더 없음"); return false; }
            if (mat.shader == null) return false;
            string oldName = mat.shader.name;
            if (oldName.StartsWith("Universal Render Pipeline/") || oldName.StartsWith("Shader Graphs/")
                || oldName.StartsWith("Skybox/") || oldName.StartsWith("Hidden/"))
                return false;

            var saved = SavedProps.Read(mat);
            if (oldName.Contains("Particles")) return ConvertParticle(mat, oldName, saved);
            if (oldName.StartsWith("Mobile/Unlit") || oldName.StartsWith("Unlit/")) return ConvertUnlit(mat, oldName, saved);

            Undo.RecordObject(mat, "Convert to URP Lit");
            mat.shader = lit;

            // 기본색
            mat.SetTexture("_BaseMap", saved.Tex("_MainTex"));
            mat.SetColor("_BaseColor", saved.GetColor("_Color", Color.white));
            mat.SetTextureScale("_BaseMap", saved.TexScale("_MainTex"));
            mat.SetTextureOffset("_BaseMap", saved.TexOffset("_MainTex"));

            // 노멀
            var normal = saved.Tex("_BumpMap");
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", saved.Float("_BumpScale", 1f));
            SetKeyword(mat, "_NORMALMAP", normal != null);

            // 메탈릭 / 스무스니스 (Standard: 메탈릭 맵 알파 = 스무스니스, URP 기본과 같음)
            var metal = saved.Tex("_MetallicGlossMap");
            mat.SetTexture("_MetallicGlossMap", metal);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_Metallic", saved.Float("_Metallic", 0f));
            mat.SetFloat("_Smoothness", metal != null
                ? saved.Float("_GlossMapScale", 1f)
                : saved.Float("_Glossiness", oldName.Contains("Specular") ? saved.Float("_Shininess", 0.4f) : 0.5f));
            mat.SetFloat("_SmoothnessTextureChannel", saved.Float("_SmoothnessTextureChannel", 0f));
            SetKeyword(mat, "_METALLICSPECGLOSSMAP", metal != null);

            // AO
            var occ = saved.Tex("_OcclusionMap");
            mat.SetTexture("_OcclusionMap", occ);
            mat.SetFloat("_OcclusionStrength", saved.Float("_OcclusionStrength", 1f));
            SetKeyword(mat, "_OCCLUSIONMAP", occ != null);

            // 발광
            var emisMap = saved.Tex("_EmissionMap");
            Color emisColor = saved.GetColor("_EmissionColor", Color.black);
            if (oldName.Contains("Self-Illumin"))
            {
                // 레거시 자체발광: 기본 텍스처 자체가 빛난다
                emisMap = saved.Tex("_Illum") ?? saved.Tex("_MainTex");
                emisColor = saved.GetColor("_Color", Color.white) * saved.Float("_EmissionLM", 1f);
                if (emisColor.maxColorComponent < 0.001f) emisColor = Color.white;
            }
            bool emissive = emisColor.maxColorComponent > 0.001f;
            mat.SetTexture("_EmissionMap", emisMap);
            mat.SetColor("_EmissionColor", emisColor);
            SetKeyword(mat, "_EMISSION", emissive);
            mat.globalIlluminationFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive
                                                   : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // 렌더 모드: 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
            int mode = Mathf.RoundToInt(saved.Float("_Mode", 0f));
            if (oldName.Contains("Transparent/Cutout")) mode = 1;
            else if (oldName.Contains("Transparent")) mode = 2;
            ApplySurface(mat, mode, saved.Float("_Cutoff", 0.5f));

            EditorUtility.SetDirty(mat);
            Debug.Log($"[StandardToURP] {mat.name}: {oldName} → URP Lit (mode={mode}, normal={normal != null}, metal={metal != null}, emission={emissive})", mat);
            return true;
        }

        static bool ConvertParticle(Material mat, string oldName, SavedProps saved)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) return false;
            Undo.RecordObject(mat, "Convert particle to URP");
            mat.shader = shader;
            mat.SetTexture("_BaseMap", saved.Tex("_MainTex"));
            // 레거시 파티클은 _TintColor 기본값이 0.5 회색이고 셰이더에서 2배 한다
            Color tint = saved.GetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)) * 2f;
            mat.SetColor("_BaseColor", tint);

            bool additive = oldName.Contains("Additive");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            SetKeyword(mat, "_SURFACE_TYPE_TRANSPARENT", true);
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            Debug.Log($"[StandardToURP] {mat.name}: {oldName} → URP Particles/Unlit (additive={additive})", mat);
            return true;
        }

        static bool ConvertUnlit(Material mat, string oldName, SavedProps saved)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return false;
            Undo.RecordObject(mat, "Convert unlit to URP");
            var tex = saved.Tex("_MainTex");
            var scale = saved.TexScale("_MainTex");
            var offset = saved.TexOffset("_MainTex");
            mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", scale);
            mat.SetTextureOffset("_BaseMap", offset);
            mat.SetColor("_BaseColor", saved.GetColor("_Color", Color.white));
            EditorUtility.SetDirty(mat);
            Debug.Log($"[StandardToURP] {mat.name}: {oldName} → URP Unlit", mat);
            return true;
        }

        static void ApplySurface(Material mat, int mode, float cutoff)
        {
            bool transparent = mode >= 2;
            bool cutout = mode == 1;

            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            mat.SetFloat("_Cutoff", cutoff);
            SetKeyword(mat, "_ALPHATEST_ON", cutout);
            SetKeyword(mat, "_SURFACE_TYPE_TRANSPARENT", transparent);

            if (transparent)
            {
                mat.SetFloat("_Blend", mode == 3 ? 1f : 0f); // Transparent → Premultiply, Fade → Alpha
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", mode == 3 ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                SetKeyword(mat, "_ALPHAPREMULTIPLY_ON", mode == 3);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                SetKeyword(mat, "_ALPHAPREMULTIPLY_ON", false);
                mat.renderQueue = cutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            }
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        /// <summary>셰이더 교체 전에 머티리얼 파일에 저장된 값을 모두 읽어 둔다.</summary>
        class SavedProps
        {
            readonly Dictionary<string, (Texture tex, Vector2 scale, Vector2 offset)> texs = new();
            readonly Dictionary<string, float> floats = new();
            readonly Dictionary<string, Color> colors = new();

            public static SavedProps Read(Material mat)
            {
                var p = new SavedProps();
                var so = new SerializedObject(mat);

                var t = so.FindProperty("m_SavedProperties.m_TexEnvs");
                for (int i = 0; t != null && i < t.arraySize; i++)
                {
                    var e = t.GetArrayElementAtIndex(i);
                    p.texs[e.FindPropertyRelative("first").stringValue] = (
                        e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture,
                        e.FindPropertyRelative("second.m_Scale").vector2Value,
                        e.FindPropertyRelative("second.m_Offset").vector2Value);
                }

                var f = so.FindProperty("m_SavedProperties.m_Floats");
                for (int i = 0; f != null && i < f.arraySize; i++)
                {
                    var e = f.GetArrayElementAtIndex(i);
                    p.floats[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").floatValue;
                }

                var c = so.FindProperty("m_SavedProperties.m_Colors");
                for (int i = 0; c != null && i < c.arraySize; i++)
                {
                    var e = c.GetArrayElementAtIndex(i);
                    p.colors[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").colorValue;
                }
                return p;
            }

            public Texture Tex(string n) => texs.TryGetValue(n, out var v) ? v.tex : null;
            public Vector2 TexScale(string n) => texs.TryGetValue(n, out var v) ? v.scale : Vector2.one;
            public Vector2 TexOffset(string n) => texs.TryGetValue(n, out var v) ? v.offset : Vector2.zero;
            public float Float(string n, float def) => floats.TryGetValue(n, out var v) ? v : def;
            public Color GetColor(string n, Color def) => colors.TryGetValue(n, out var v) ? v : def;
        }
    }
}
