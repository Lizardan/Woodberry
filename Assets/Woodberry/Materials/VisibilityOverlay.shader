// Темнота поверх кадра с прорезанной видимой областью и лёгким туманом.
//
// Четырёхугольник растянут ровно на видимую область камеры, поэтому его
// собственные UV совпадают с экранными — отдельная выборка по экранным
// координатам не нужна, а значит не нужна и матрица проекции.
//
// Туман берётся по МИРОВЫМ координатам, а не по UV четырёхугольника:
// иначе он был бы приклеен к камере и не «плыл» относительно сцены.
//
// Непрозрачность темноты задаётся не единицей: 0.985 оставляет силуэты
// едва различимыми. Требование «игрок не слепнет в полной темноте»
// выполняется именно здесь, а не подкруткой яркости спрайтов.
Shader "Woodberry/VisibilityOverlay"
{
    Properties
    {
        _MaskTex ("Vision Mask", 2D) = "black" {}
        _FogTex ("Fog", 2D) = "black" {}
        _DarkColor ("Dark Color", Color) = (0.02, 0.024, 0.034, 1)
        _Darkness ("Darkness", Range(0, 1)) = 0.985
        _FogColor ("Fog Color", Color) = (0.32, 0.37, 0.45, 1)
        _FogStrength ("Fog Strength", Range(0, 1)) = 0.09
        _FogScale ("Fog Scale", Float) = 0.30
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.3)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VisibilityOverlay"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_FogTex);
            SAMPLER(sampler_FogTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                float4 _FogTex_ST;
                float4 _DarkColor;
                float4 _FogColor;
                float  _Darkness;
                float  _FogStrength;
                float  _FogScale;
                float  _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 fogUv : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.fogUv = positionWS.xy * _FogScale;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, input.uv).r;

                // Яркость маски проходит в темноту КАК ЕСТЬ. Раньше здесь
                // стоял smoothstep по узкой полосе, и он превращал плавный
                // спад маски в ступеньку: любой тексель ярче порога давал
                // полный свет, любой темнее — полную тьму. Градиент,
                // который считает шейдер маски, до экрана не доходил,
                // и мягкость приходилось делать в геометрии, где она
                // получается полосатой.
                //
                // _EdgeSoftness оставлен: это сглаживание последнего
                // пикселя кромки, защита от пилы по краю текселя. Он
                // намеренно мал и на ширину спада не влияет.
                float reveal = saturate(mask / max(_EdgeSoftness, 0.001));
                reveal = saturate(reveal);
                float darkness = saturate(_Darkness * (1.0 - reveal));

                float fog = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, input.fogUv).r;
                half3 color = _DarkColor.rgb + _FogColor.rgb * fog * _FogStrength;

                return half4(color, darkness);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
