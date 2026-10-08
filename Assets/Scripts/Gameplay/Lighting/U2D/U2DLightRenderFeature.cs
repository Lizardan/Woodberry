using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// URP-фича, добавляющая проход 2D-освещения. Порт <c>Light2DRenderFeature</c>
    /// из SardineFish/Unity2DLighting, переписанный под RenderGraph (URP 17 /
    /// Unity 6): у <c>ScriptableRenderPass</c> в этой версии больше нет
    /// <c>Execute</c>, вместо него <c>RecordRenderGraph</c>.
    ///
    /// Проход обязан быть <b>unsafe</b>: пайплайн Unity2DLighting вручную
    /// переключает render target между lightmap, shadowmap и экраном, а это
    /// в RasterCommandBuffer запрещено. Официальный шаблон — пример
    /// <c>UnsafePassRenderFeature</c> из URP Samples.
    ///
    /// Настройки берутся с <see cref="U2DLightSystem"/> в сцене.
    /// </summary>
    public class U2DLightRenderFeature : ScriptableRendererFeature
    {
        /// <summary>Данные, которые переезжают из RecordRenderGraph в лямбду выполнения.</summary>
        private class PassData
        {
            public U2DLightSystem System;
            public Camera Camera;
            public TextureHandle ColorTarget;
            public TextureHandle LightMap;
            public TextureHandle ShadowMap;
            public TextureHandle ShadowMapScratch;
        }

        private class U2DLightPass : ScriptableRenderPass
        {
            public U2DLightPass()
            {
                // До постобработки: во-первых, освещение — часть сцены, а не
                // финальной картинки (виньетка и зерно должны ложиться поверх);
                // во-вторых, после постобработки активный цвет — это backbuffer,
                // у которого нет RTHandle, и Blitter.BlitTexture падает на null.
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }

            /// <summary>
            /// Тело прохода. Вынесено в статический метод, чтобы лямбда была
            /// без замыканий — так рекомендует сам Unity в сэмплах RenderGraph.
            /// </summary>
            private static void ExecutePass(PassData data, UnsafeGraphContext context)
            {
                if (data.System == null || data.Camera == null)
                {
                    return;
                }

                CommandBuffer nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                data.System.RenderLighting(
                    context.cmd,
                    nativeCmd,
                    data.Camera,
                    data.ColorTarget,
                    data.LightMap,
                    data.ShadowMap,
                    data.ShadowMapScratch);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                Camera camera = cameraData.camera;
                if (camera == null || camera.cameraType == CameraType.Preview)
                {
                    return;
                }

                U2DLightSystem system = U2DLightSystem.Instance;
                if (system == null || !system.isActiveAndEnabled)
                {
                    return;
                }

                // cameraColor — настоящая цветовая текстура камеры. Брать
                // activeColorTexture нельзя: на этом событии она ещё указывает
                // на камеру, но после постобработки станет backbuffer без RTHandle.
                TextureHandle colorTarget = resourceData.cameraColor;
                if (!colorTarget.IsValid())
                {
                    colorTarget = resourceData.activeColorTexture;
                }

                if (!colorTarget.IsValid())
                {
                    return;
                }

                U2DLightSystemSettings settings = system.Settings;

                int width = Mathf.Max(1, camera.pixelWidth);
                int height = Mathf.Max(1, camera.pixelHeight);
                int lightW = Mathf.Max(1, Mathf.FloorToInt(width * settings.LightMapResolutionScale));
                int lightH = Mathf.Max(1, Mathf.FloorToInt(height * settings.LightMapResolutionScale));
                int shadowW = Mathf.Max(1, Mathf.FloorToInt(width * settings.ShadowMapResolutionScale));
                int shadowH = Mathf.Max(1, Mathf.FloorToInt(height * settings.ShadowMapResolutionScale));

                TextureHandle lightMap = renderGraph.CreateTexture(new TextureDesc(lightW, lightH)
                {
                    name = "_2DLightMap",
                    clearBuffer = true,
                    clearColor = Color.black,
                    filterMode = FilterMode.Bilinear,
                    // Формат обязан быть HDR. На LDR-формате свет обрезался на 1.0:
                    // ядро пятна выцветало в белый (терялся цвет источника),
                    // «интенсивность» выше единицы ничего не давала, а bloom
                    // не срабатывал, потому что сцена после умножения на такую
                    // карту не дотягивала до его порога.
                    colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
                    msaaSamples = MSAASamples.None,
                });

                TextureHandle shadowMap = renderGraph.CreateTexture(new TextureDesc(shadowW, shadowH)
                {
                    name = "_2DShadowMap",
                    clearBuffer = true,
                    clearColor = Color.black,
                    filterMode = FilterMode.Bilinear,
                    colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    msaaSamples = MSAASamples.None,
                });

                // Промежуточная текстура для двухпроходного размытия теней.
                TextureHandle shadowMapScratch = renderGraph.CreateTexture(new TextureDesc(shadowW, shadowH)
                {
                    name = "_2DShadowMapScratch",
                    clearBuffer = true,
                    clearColor = Color.black,
                    filterMode = FilterMode.Bilinear,
                    colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm,
                    msaaSamples = MSAASamples.None,
                });

                using (IUnsafeRenderGraphBuilder builder =
                       renderGraph.AddUnsafePass<PassData>("U2D Lighting", out PassData data))
                {
                    data.System = system;
                    data.Camera = camera;
                    data.ColorTarget = colorTarget;
                    data.LightMap = lightMap;
                    data.ShadowMap = shadowMap;
                    data.ShadowMapScratch = shadowMapScratch;

                    // Читаем и пишем цвет — композит идёт «на месте».
                    builder.UseTexture(colorTarget, AccessFlags.ReadWrite);
                    builder.UseTexture(lightMap, AccessFlags.ReadWrite);
                    builder.UseTexture(shadowMap, AccessFlags.ReadWrite);
                    builder.UseTexture(shadowMapScratch, AccessFlags.ReadWrite);

                    // Unsafe-проход по определению не мержится и не вырезается.
                    builder.AllowPassCulling(false);

                    // Проход ставит глобальные параметры шейдеров (_ShadowMap,
                    // _LightMap, _GlobalLight, _ExposureLimit). Без явного
                    // разрешения RenderGraph бросит InvalidOperationException.
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (PassData passData, UnsafeGraphContext context) =>
                        ExecutePass(passData, context));
                }
            }
        }

        private U2DLightPass _pass;

        public override void Create()
        {
            _pass = new U2DLightPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null)
            {
                return;
            }

            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection)
            {
                return;
            }

            renderer.EnqueuePass(_pass);
        }
    }
}
