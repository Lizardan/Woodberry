using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Общая посылка данных в шейдеры 2D-освещения.
    /// Порт <c>LightRenderingData</c> из SardineFish/Unity2DLighting.
    ///
    /// Все таргеты — <see cref="TextureHandle"/>: их создаёт RenderGraph, а
    /// неявные конверсии позволяют отдавать их и в <see cref="CommandBuffer"/>,
    /// и в шейдеры как <c>RTHandle</c>.
    /// </summary>
    public struct U2DLightRenderingData
    {
        public Camera Camera;
        public TextureHandle Lightmap;
        public TextureHandle ShadowMap;

        /// <summary>Промежуточная текстура для двухпроходного размытия теней.</summary>
        public TextureHandle ShadowMapScratch;

        public TextureHandle CameraColorTarget;
        public U2DLightSystemSettings Settings;

        /// <summary>Unsafe-буфер: смена render target, глобалы.</summary>
        public UnsafeCommandBuffer Cmd;

        /// <summary>Нативный буфер: <see cref="Blitter"/>, <c>DrawMesh</c>.</summary>
        public CommandBuffer NativeCmd;
    }
}
