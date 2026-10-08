using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Двухпроходное гауссово размытие. Порт <c>GaussianBlur</c> из
    /// SardineFish/Unity2DLighting: сначала горизонталь, затем вертикаль.
    ///
    /// Работает поверх текстур RenderGraph: в оригинале промежуточная текстура
    /// бралась через <c>GetTemporaryRT</c>, но ни <see cref="RasterCommandBuffer"/>,
    /// ни <see cref="UnsafeCommandBuffer"/> в RenderGraph временных текстур не
    /// выделяют — их создаёт сама RenderGraph, поэтому scratch приходит снаружи.
    /// </summary>
    public static class U2DGaussianBlur
    {
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int BlurDirectionId = Shader.PropertyToID("_BlurDirection");

        public static void Blur(int radius, UnsafeCommandBuffer cmd, CommandBuffer nativeCmd,
            TextureHandle src, TextureHandle dst, TextureHandle scratch, Material material)
        {
            if (material == null || radius <= 0 || !scratch.IsValid() || !src.IsValid() || !dst.IsValid())
            {
                return;
            }

            cmd.SetGlobalInt(BlurRadiusId, radius);

            // Горизонтальный проход — в промежуточную текстуру.
            cmd.SetGlobalVector(BlurDirectionId, new Vector2(1f, 0f));
            cmd.SetRenderTarget(scratch);
            Blitter.BlitTexture(nativeCmd, src, new Vector4(1f, 1f, 0f, 0f), material, 0);

            // Вертикальный проход — обратно в приёмник.
            cmd.SetGlobalVector(BlurDirectionId, new Vector2(0f, 1f));
            cmd.SetRenderTarget(dst);
            Blitter.BlitTexture(nativeCmd, scratch, new Vector4(1f, 1f, 0f, 0f), material, 0);
        }
    }
}
