// 텍스처 없이 정점 색으로 칠해진 모델(스케치펙 추출 모델 등)을 URP에서 그리는 간단한 툰 셰이더.
// - 메인 라이트: 2단 음영(경계 부드럽기 조절) + 그림자 받기
// - 추가 라이트: Forward+ 클러스터 루프 지원 (램버트)
// - 환경광(SH) + 가장자리 림 라이트
// 그림자 드리우기/깊이는 URP Lit의 패스를 그대로 사용한다.
Shader "LNStudy/VertexColorToon"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Color", Color) = (0.62, 0.58, 0.7, 1)
        _ShadeStep ("Shade Step", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.06
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.8
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadeColor;
                half _ShadeStep;
                half _ShadeSoftness;
                half _AmbientStrength;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 albedo = i.color.rgb * _BaseColor.rgb;

                // 메인 라이트: 2단 음영
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half halfLambert = dot(n, mainLight.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, halfLambert);
                lit *= lerp(0.35h, 1.0h, mainLight.shadowAttenuation);
                half3 color = lerp(albedo * _ShadeColor.rgb, albedo, lit) * mainLight.color;

                // 추가 라이트 (Forward+ 포함)
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    half ndl = saturate(dot(n, l.direction));
                    color += albedo * l.color * (ndl * l.distanceAttenuation * l.shadowAttenuation);
                LIGHT_LOOP_END
                #endif

                // 환경광 + 림
                color += albedo * SampleSH(n) * _AmbientStrength;
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower) * _RimStrength;
                color += _RimColor.rgb * rim;

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
