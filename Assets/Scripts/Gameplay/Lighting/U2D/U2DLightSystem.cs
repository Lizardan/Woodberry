using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Композитор всей системы 2D-освещения. Порт <c>LightSystem</c> из
    /// SardineFish/Unity2DLighting.
    ///
    /// Жизненный цикл кадра:
    /// 1. Очистить lightmap (чёрный) и shadowmap (чёрный).
    /// 2. Для каждого источника с тенями: нарисовать их в shadowmap.
    /// 3. Каждый источник складывает свой свет в lightmap аддитивно,
    ///    затеняя его по shadowmap.
    /// 4. Композит: сцена умножается на lightmap (Blend DstColor Zero).
    ///
    /// Текстуры создаёт вызывающая сторона (RenderGraph), система только
    /// заполняет их — это требование URP 17.
    /// </summary>
    public class U2DLightSystem : MonoBehaviour
    {
        private static U2DLightSystem _instance;

        public static U2DLightSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<U2DLightSystem>();
                }
                return _instance;
            }
        }

        [SerializeField]
        private U2DLightSystemSettings _settings = new U2DLightSystemSettings();

        public U2DLightSystemSettings Settings => _settings;

        private static readonly int ExposureLimitId = Shader.PropertyToID("_ExposureLimit");
        private static readonly int ShadowFloorId = Shader.PropertyToID("_ShadowFloor");
        private static readonly int LightMapId = Shader.PropertyToID("_LightMap");
        private static readonly int ShadowMapId = Shader.PropertyToID("_ShadowMap");
        private static readonly int GlobalLightId = Shader.PropertyToID("_GlobalLight");

        private Material _deferredMaterial;

        private Material DeferredMaterial
        {
            get
            {
                if (_deferredMaterial == null)
                {
                    _deferredMaterial = U2DShaderPool.Get("Woodberry/U2DDeferredLighting");
                }
                return _deferredMaterial;
            }
        }

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>
        /// Отрисовывает свет в lightmap, тени в shadowmap и делает композит в
        /// <paramref name="colorTarget"/>. Вызывается из unsafe-пасса RenderGraph.
        /// </summary>
        /// <param name="cmd">Unsafe-буфер RenderGraph: умеет менять render target
        /// и глобальные параметры.</param>
        /// <param name="nativeCmd">Нативный CommandBuffer того же потока команд —
        /// нужен для <see cref="Blitter"/> и <c>DrawMesh</c>, которые перегрузок
        /// под unsafe-буфер не имеют.</param>
        public void RenderLighting(UnsafeCommandBuffer cmd, CommandBuffer nativeCmd, Camera camera,
            TextureHandle colorTarget, TextureHandle lightMap, TextureHandle shadowMap,
            TextureHandle shadowMapScratch)
        {
            Material deferred = DeferredMaterial;
            if (deferred == null || camera == null
                || !colorTarget.IsValid() || !lightMap.IsValid() || !shadowMap.IsValid())
            {
                return;
            }

            var data = new U2DLightRenderingData
            {
                Camera = camera,
                Lightmap = lightMap,
                ShadowMap = shadowMap,
                ShadowMapScratch = shadowMapScratch,
                CameraColorTarget = colorTarget,
                Settings = _settings,
                Cmd = cmd,
                NativeCmd = nativeCmd,
            };

            // Unsafe-проход RenderGraph не настраивает матрицы камеры — DrawMesh
            // рисовал бы в неизвестной проекции. Задаём VP явно: lightMap и
            // shadowMap имеют размер экрана, поэтому подходит матрица камеры.
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 proj = camera.projectionMatrix;
            cmd.SetViewProjectionMatrices(view, proj);

            // 1. Очистить карту света — она накапливается аддитивно.
            cmd.SetRenderTarget(lightMap);
            cmd.ClearRenderTarget(false, true, Color.black);

            var lights = U2DLight2DBase.All;

            for (int i = 0; i < lights.Count; i++)
            {
                U2DLight2DBase light = lights[i];
                if (light == null || !light.isActiveAndEnabled)
                {
                    continue;
                }

                if (light.Shadows != LightShadows.None)
                {
                    // Карта теней одна на всех, поэтому её надо очистить перед
                    // каждым «теневым» светом: затенение читается сразу после
                    // отрисовки этого света.
                    cmd.SetRenderTarget(shadowMap);
                    cmd.ClearRenderTarget(false, true, Color.black);
                    light.RenderShadow(cmd, nativeCmd, ref data, shadowMap);
                    cmd.SetRenderTarget(lightMap);
                }

                light.RenderLight(cmd, ref data);
            }

            // 2. Композит: цвет сцены умножается на (global light + lightmap).
            cmd.SetGlobalFloat(ExposureLimitId, _settings.ExposureLimit);
            cmd.SetGlobalFloat(ShadowFloorId, _settings.ShadowFloor);
            cmd.SetGlobalTexture(ShadowMapId, shadowMap);
            cmd.SetGlobalTexture(LightMapId, lightMap);
            cmd.SetGlobalColor(GlobalLightId, _settings.GlobalLight);

            // Таргет надо вернуть на цвет сцены: до этого мы писали в lightmap.
            cmd.SetRenderTarget(colorTarget);
            Blitter.BlitTexture(nativeCmd, colorTarget, new Vector4(1f, 1f, 0f, 0f), deferred, 0);

            // Вернуть матрицы туда, откуда RenderGraph их ждёт.
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        }
    }
}
