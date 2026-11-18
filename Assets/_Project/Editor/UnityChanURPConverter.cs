using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LNStudy.EditorTools
{
    /// <summary>
    /// Built-in 파이프라인 전용 Unity-chan 머티리얼을 URP Lit으로 변환한다.
    /// 기존 _MainTex/_Color를 _BaseMap/_BaseColor로 옮기고,
    /// 원래 셰이더 이름(예: "UnityChan/Eye - Transparent", "UnityChan/Hair - Double-sided")으로
    /// 반투명·양면 여부를 판단한다. 이미 변환된 머티리얼은 머티리얼 이름 표로 판단해 설정만 다시 적용한다.
    /// </summary>
    public static class UnityChanURPConverter
    {
        const string MaterialFolder = "Assets/unity-chan!/Unity-chan! Model/Art/Materials";
        const string LitShaderName = "Universal Render Pipeline/Lit";

        // 이미 변환되어 원래 셰이더 이름을 알 수 없을 때 쓰는 기준 (Unity-chan 기본 머티리얼)
        static readonly string[] TransparentByName = { "eye_L1", "eye_R1", "eyeline", "mat_cheek" };
        static readonly string[] DoubleSidedByName = { "body", "hair" };

        [MenuItem("LNStudy/Unity-chan/Convert Materials to URP")]
        public static void Convert()
        {
            var lit = Shader.Find(LitShaderName);
            if (lit == null)
            {
                Debug.LogError("[UnityChanURPConverter] URP Lit 셰이더를 찾지 못했습니다. URP가 설치되어 있는지 확인하세요.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder });
            int converted = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                bool alreadyLit = mat.shader == lit;
                string oldShader = mat.shader != null ? mat.shader.name : "(missing)";

                bool transparent, doubleSided;
                if (alreadyLit)
                {
                    transparent = System.Array.IndexOf(TransparentByName, mat.name) >= 0;
                    doubleSided = System.Array.IndexOf(DoubleSidedByName, mat.name) >= 0;
                }
                else
                {
                    string lower = oldShader.ToLowerInvariant();
                    transparent = lower.Contains("transparent") || lower.Contains("blend");
                    doubleSided = lower.Contains("double-sided") || lower.EndsWith("_ds");
                }

                Undo.RecordObject(mat, "Convert Unity-chan material to URP");

                if (!alreadyLit)
                {
                    // 셰이더를 바꾸기 전에 저장된 값을 직렬화 데이터에서 직접 읽는다
                    // (구 셰이더가 깨져 있어도 머티리얼 파일에는 값이 남아 있다)
                    Texture mainTex = ReadSavedTexture(mat, "_MainTex");
                    Color color = ReadSavedColor(mat, "_Color", Color.white);

                    mat.shader = lit;
                    if (mainTex != null) mat.SetTexture("_BaseMap", mainTex);
                    mat.SetColor("_BaseColor", color);
                    mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Smoothness", 0.15f);
                }

                SetCull(mat, doubleSided ? CullMode.Off : CullMode.Back);
                if (transparent) SetTransparent(mat);
                else SetOpaque(mat);

                EditorUtility.SetDirty(mat);
                converted++;
                Debug.Log($"[UnityChanURPConverter] {mat.name}: {(alreadyLit ? "(설정 재적용)" : oldShader + " → URP Lit")} 반투명={transparent}, 양면={doubleSided}", mat);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UnityChanURPConverter] 완료: {converted}개");
        }

        static Texture ReadSavedTexture(Material mat, string name)
        {
            var so = new SerializedObject(mat);
            var texEnvs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (texEnvs == null) return null;
            for (int i = 0; i < texEnvs.arraySize; i++)
            {
                var entry = texEnvs.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == name)
                    return entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
            }
            return null;
        }

        static Color ReadSavedColor(Material mat, string name, Color fallback)
        {
            var so = new SerializedObject(mat);
            var colors = so.FindProperty("m_SavedProperties.m_Colors");
            if (colors == null) return fallback;
            for (int i = 0; i < colors.arraySize; i++)
            {
                var entry = colors.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == name)
                    return entry.FindPropertyRelative("second").colorValue;
            }
            return fallback;
        }

        static void SetCull(Material mat, CullMode cull)
        {
            mat.SetFloat("_Cull", (float)cull);
            mat.doubleSidedGI = cull == CullMode.Off;
        }

        static void SetOpaque(Material mat)
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1f);
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Geometry;
            mat.SetShaderPassEnabled("DepthOnly", true);
            mat.SetShaderPassEnabled("ShadowCaster", true);
        }

        static void SetTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f); // Alpha
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            // 얼굴 위에 겹쳐 그리는 눈·속눈썹·볼터치는 그림자를 만들지 않는다
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
        }
    }
}
