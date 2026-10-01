// Геометрия обзора: заливает маску белым там, где игрок видит.
//
// Смешивание аддитивное (Blend One One), а не обычная прозрачность.
// Источников обзора несколько, их области пересекаются, и при обычном
// альфа-смешивании перекрытие дало бы шов. Сумма с последующим ограничением
// по единице в шейдере темноты такого шва не даёт.
//
// Цвет вершины задаёт затухание к краю обзора: в центре белый, у границы
// серый. Это единственное место, где задаётся мягкость кромки, — сама
// граница считается в Woodberry/VisibilityOverlay.
Shader "Woodberry/VisionMask"
{
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
            Name "VisionMask"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.color.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
