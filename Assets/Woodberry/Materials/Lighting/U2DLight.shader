// Порт шейдера "Lighting2D/2DLight" из SardineFish/Unity2DLighting.
// Pass #0 — аналитический свет (затухание по расстоянию от центра квада).
// Pass #1 — текстурированный свет (cookie-текстура).
// Оба пасса пишут в lightmap аддитивно (Blend One One).
Shader "Woodberry/U2DLight"
{
    Properties
    {
        _MainTex ("Light Cookie", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One One

        // Pass #0 — аналитический свет.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "U2DLighting.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float4 shadowUV : TEXCOORD2;
            };

            fixed4 _Color;
            float _Attenuation;
            float _Intensity;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.shadowUV = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // distance() в UV-пространстве: 0 в центре квада, 0.707 в углу.
                float dist = distance(i.texcoord, float2(0.5, 0.5));
                dist /= 0.5;
                dist = saturate(dist);
                float illum = 0;

                // Четыре режима затухания. Границы выбраны так, чтобы кривая
                // оставалась непрерывной при _Attenuation = -1, 0 и 1.
                if (_Attenuation <= -1) // (-inf, -1]
                {
                    illum = 0;
                }
                else if (_Attenuation <= 0) // (-1, 0]
                {
                    float t = 1 / (_Attenuation + 1) - 1;
                    illum = exp(-dist * t) - exp(-t) * dist;
                }
                else if (_Attenuation < 1) // (0, 1)
                {
                    float t = 1 / (1 - _Attenuation) - 1;
                    dist = 1 - dist;
                    illum = 1 - (exp(-dist * t) - exp(-t) * dist);
                }
                else // [1, +inf) — жёсткий диск без спада
                {
                    illum = dist >= 1 ? 0 : 1;
                }

                float3 color = illum * _Intensity * _Color.rgb;

                // Затенение берётся из карты теней, снятой в экранных координатах.
                i.shadowUV.xy /= i.shadowUV.w;
                color = APPLY_SHADOW_2D(color, i.shadowUV);

                color += DITHER_OFFSET(i.vertex.xy);

                // float4, а не fixed4: fixed на части платформ клампится
                // в диапазон [-2, 2], и HDR-ядро источника срезалось бы.
                return float4(color, 1.0);
            }
            ENDCG
        }

        // Pass #1 — текстурированный свет (LightCookie).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "U2DLighting.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float4 shadowUV : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _LightCookie;
            fixed4 _Color;
            float _Attenuation;
            float _Intensity;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.shadowUV = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 color = tex2D(_LightCookie, i.texcoord.xy).rgb * _Intensity * _Color.rgb;
                i.shadowUV.xy /= i.shadowUV.w;
                color = APPLY_SHADOW_2D(color, i.shadowUV);
                color += DITHER_OFFSET(i.vertex.xy);
                return float4(color, 1.0);
            }
            ENDCG
        }
    }
}
