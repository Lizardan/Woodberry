// Порт шейдера "GaussianBlur/Blur" из SardineFish/Unity2DLighting.
// Размытие с перетаскиваемыми весами: направление задаётся снаружи через
// _BlurDirection, поэтому один шейдер обслуживает и горизонтальный, и
// вертикальный проход.
//
// Вызывается через Blitter.BlitTexture — значит вершины разворачивает Vert
// из Blit.hlsl, а источник читается из _BlitTexture.
Shader "Woodberry/U2DGaussian"
{
    Properties
    {
        _MainTex ("LightTexture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Gaussian ("Gaussian", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "PreviewType" = "Plane"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_Gaussian);
            SAMPLER(sampler_Gaussian);

            float4 _Color;
            float4 _Gaussian_TexelSize;
            int    _BlurRadius;
            float2 _BlurDirection;

            float GaussianWeight(int r, int R)
            {
                return SAMPLE_TEXTURE2D(_Gaussian, sampler_Gaussian, float2(r, R) * _Gaussian_TexelSize.xy).r;
            }

            float3 Gaussian(float2 uv, int r, int R)
            {
                if (r == 0)
                {
                    return GaussianWeight(r, R)
                         * SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel).rgb;
                }

                float2 d = (float)r * _BlurDirection * _BlitTexture_TexelSize.xy;
                return GaussianWeight(r, R) * (
                    SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + d, _BlitMipLevel).rgb +
                    SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - d, _BlitMipLevel).rgb
                );
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 color = float3(0, 0, 0);
                int R = max(_BlurRadius - 1, 1);

                for (int i = 0; i < _BlurRadius; i++)
                {
                    color += Gaussian(input.texcoord.xy, i, R);
                }

                return float4(color * _Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
