Shader "Custom/ToonFireSparks_Radial"
{
    Properties
    {
        [HDR] _Color ("Spark Color", Color) = (1,0.6,0.2,1)
        _Intensity ("Brightness", Range(0,5)) = 2
        _SoftEdge ("Edge Softness", Range(0.001,1)) = 0.25
        _CoreSize ("Core Size", Range(0,1)) = 0.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Blend One One   
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 _Color;
            float _Intensity;
            float _SoftEdge;
            float _CoreSize;

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.pos = TransformObjectToHClip(v.vertex);

               
                o.uv = v.uv;

                o.color = v.color;

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

               
                float2 uv = i.uv * 2.0 - 1.0;

                
                float dist = length(uv);

               
                float core = smoothstep(_CoreSize, 0.0, dist);

                
                float falloff = smoothstep(1.0, _SoftEdge, dist);

                float alpha = core * falloff;

                float3 col = _Color.rgb * _Intensity * i.color.rgb * alpha;

                return float4(col, alpha);
            }

            ENDHLSL
        }
    }
}