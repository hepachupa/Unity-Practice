Shader "Custom/StylizedFire"
{
    Properties
    {
        _Alpha ("Transparency", Range(0,1)) = 0.6
        _Emission ("Emission Strength", Range(0,5)) = 2
        _RandomScale ("Random Scale", Float) = 4
        _FlickerSpeed ("Flicker Speed", Float) = 6
        _FlickerAmount ("Flicker Amount", Float) = 0.25

        _WindStrength ("Wind Strength", Float) = 0.1
        _WindSpeed ("Wind Speed", Float) = 2
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            float _Alpha;
            float _Emission;
            float _RandomScale;
            float _FlickerSpeed;
            float _FlickerAmount;

            float _WindStrength;
            float _WindSpeed;

            float rand(float3 co)
            {
                return frac(sin(dot(co ,float3(12.9898,78.233,45.164))) * 43758.5453);
            }

            v2f vert (appdata v)
            {
                v2f o;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                float heightMask = saturate(v.vertex.y);

                float sway = sin(_Time.y * _WindSpeed + worldPos.x * 2) * _WindStrength;

                worldPos.x += sway * heightMask;

                o.pos = UnityWorldToClipPos(worldPos);
                o.worldPos = worldPos;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r = rand(floor(i.worldPos * _RandomScale));

                float3 red = float3(1,0.2,0);
                float3 orange = float3(1,0.5,0);
                float3 yellow = float3(1,0.85,0.2);

                float3 color;

                if (r < 0.33)
                    color = red;
                else if (r < 0.66)
                    color = orange;
                else
                    color = yellow;

                float flicker = 1 + sin(_Time.y * _FlickerSpeed) * _FlickerAmount;

                color *= _Emission * flicker;

                return float4(color, _Alpha);
            }
            ENDCG
        }
    }
}