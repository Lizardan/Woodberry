// Порт шейдера "Lighting2D/DeferredLighting" из SardineFish/Unity2DLighting.
// Pass #0 — композит: lightmap * сцена, поверх глобальной засветки.
// Pass #1 — гауссово размытие 9x9 (используется для мягких теней).
// Pass #2 — близкий копировальщик.
//
// Оба рабочих прохода вызываются через Blitter.BlitTexture, а тот рисует
// процедурный треугольник на весь экран и требует вершинный шейдер Vert из
// Blit.hlsl (свои UV он берёт из _BlitScaleBias, а не из appdata_base).
Shader "Woodberry/U2DDeferredLighting"
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
        ZTest Always

        // Pass #0 — deferred-композит. Умножаем сцену на свет.
        Pass
        {
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_LightMap);
            SAMPLER(sampler_LightMap);

            float  _ExposureLimit;
            float3 _GlobalLight;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                float3 ambient = _GlobalLight;
                float3 light = ambient + SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, uv).rgb;

                // _ExposureLimit < 0 отключает ограничение.
                if (_ExposureLimit >= 0)
                {
                    light = clamp(light, 0, _ExposureLimit);
                }

                return float4(light, 1.0);
            }
            ENDHLSL
        }

        // Pass #1 — гауссово размытие 9x9 с фиксированными весами.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float2 _BlurDirection;
            int    _BlurRadius;

            inline float3 Filter(float2 uv, float2 d)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d.xy * _BlitTexture_TexelSize.x, _BlitMipLevel).rgb
                     + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d.xy * _BlitTexture_TexelSize.x, _BlitMipLevel).rgb
                     + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d.yx * _BlitTexture_TexelSize.y, _BlitMipLevel).rgb
                     + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d.yx * _BlitTexture_TexelSize.y, _BlitMipLevel).rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // _BlurDirection задаёт ось прохода: (1,0) или (0,1).
                float2 step = _BlurDirection * max(_BlurRadius, 1);

                float3 col = 0.29234
                           * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel).rgb;
                col += 0.111768  * Filter(uv, step);
                col += 0.0499491 * Filter(uv, step * 2.0);
                col += 0.013032  * Filter(uv, step * 3.0);
                col += 0.00198168 * Filter(uv, step * 4.0);

                return float4(col, 1.0);
            }
            ENDHLSL
        }

        // Pass #2 — копирование.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, _BlitMipLevel);
            }
            ENDHLSL
        }
    }
}
