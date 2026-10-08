// Порт шейдера "Lighting2D/Shadow" из SardineFish/Unity2DLighting.
// Pass #0 — жёсткая тень (просто пишет белый в карту теней).
// Pass #1 — объёмная мягкая тень: аналитически считает перекрытый телесный угол
//           относительно диска радиуса _LightSize. Математика из оригинала,
//           ссылка на вывод: https://www.geogebra.org/graphing/ysvegxsz
Shader "Woodberry/U2DShadow"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        // Pass #0 — жёсткая тень.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata_base IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float3 color = float3(1, 1, 1);
                return fixed4(color, 1.0);
            }
            ENDCG
        }

        // Pass #1 — объёмная мягкая тень.
        Pass
        {
            BlendOp Add
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define PI 3.14159265358979323846264338327950288419716939937510

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 edgeA  : TEXCOORD0;
                float2 edgeB  : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 edgeA  : TEXCOORD0;
                float2 edgeB  : TEXCOORD1;
                float2 pos    : TEXCOORD2;
            };

            sampler2D _MainTex;
            float _LightSize;

            v2f vert(appdata_t i)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.edgeA = i.edgeA;
                o.edgeB = i.edgeB;
                o.pos = i.vertex;
                return o;
            }

            inline float2 rotate(float2 v, float ang)
            {
                float2 r = float2(cos(ang), sin(ang));
                return float2(r.x * v.x - r.y * v.y, r.x * v.y + r.y * v.x);
            }

            inline float cross2(float2 u, float2 v)
            {
                return cross(float3(u, 0), float3(v, 0)).z;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = distance(float2(0, 0), i.pos);
                float ang = asin(_LightSize / max(d, _LightSize + 0.0001));
                float2 left = normalize(rotate(-i.pos, ang));
                float2 right = normalize(rotate(-i.pos, -ang));
                float2 u = normalize(i.edgeA.xy - i.pos);
                float2 v = normalize(i.edgeB.xy - i.pos);

                // Держим порядок, при котором u лежит слева от v.
                if (cross2(v, u) < 0)
                {
                    float2 t = u;
                    u = v;
                    v = t;
                }

                float leftLeak = saturate(sign(cross2(u, left))) * acos(clamp(dot(left, u), -1, 1));
                float rightLeak = saturate(sign(cross2(right, v))) * acos(clamp(dot(right, v), -1, 1));
                float total = acos(clamp(dot(right, left), -1, 1));

                float3 color = saturate((leftLeak + rightLeak) / max(total, 0.0001));

                return fixed4(1 - color, 1.0);
            }
            ENDCG
        }
    }
}
