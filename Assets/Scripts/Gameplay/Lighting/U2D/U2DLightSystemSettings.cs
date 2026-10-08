using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Woodberry.Lighting.U2D
{
    /// <summary>
    /// Настройки системы 2D-освещения. Порт <c>LightSystemSettings</c> из
    /// SardineFish/Unity2DLighting.
    /// </summary>
    [Serializable]
    public class U2DLightSystemSettings
    {
        /// <summary>
        /// Индекс слоя препятствий по умолчанию. Тот же слой <c>Occluder</c>,
        /// которым система обзора режет видимость (ADR 0008): непрозрачность
        /// для света и для взгляда — одно и то же свойство объекта.
        /// </summary>
        public const int DefaultOccluderLayer = 8;

        [SerializeField]
        [ColorUsage(false, true)]
        private Color _globalLight = Color.black;

        [SerializeField]
        [Tooltip("Слои, которые отбрасывают тень. Должен совпадать со слоем Occluder, " +
                 "иначе игрок начнёт затенять свет сам себя, а окна перестанут его пропускать.")]
        private LayerMask _shadowCasterMask = 1 << DefaultOccluderLayer;

        [SerializeField]
        [Tooltip("Верхний предел суммарной освещённости. Отрицательное значение отключает ограничение.")]
        private float _exposureLimit = -1f;

        [SerializeField]
        [Tooltip("Доля света, остающаяся в тени. 0 — тень абсолютно чёрная (читается " +
                 "как дыра в геометрии), 0.2 — силуэт предмета в тени угадывается.")]
        [Range(0f, 0.6f)]
        private float _shadowFloor = 0.2f;

        [SerializeField]
        [Range(0.25f, 1f)]
        private float _lightMapResolutionScale = 1f;

        [SerializeField]
        private RenderTextureFormat _lightMapFormat = RenderTextureFormat.ARGB32;

        [SerializeField]
        [Range(0.25f, 1f)]
        private float _shadowMapResolutionScale = 1f;

        [SerializeField]
        private RenderTextureFormat _shadowMapFormat = RenderTextureFormat.R8;

        public Color GlobalLight
        {
            get => _globalLight;
            set => _globalLight = value;
        }

        public float ExposureLimit
        {
            get => _exposureLimit;
            set => _exposureLimit = value;
        }

        public float ShadowFloor
        {
            get => _shadowFloor;
            set => _shadowFloor = value;
        }

        public LayerMask ShadowCasterMask
        {
            get => _shadowCasterMask;
            set => _shadowCasterMask = value;
        }

        public float LightMapResolutionScale
        {
            get => _lightMapResolutionScale;
            set => _lightMapResolutionScale = value;
        }

        public RenderTextureFormat LightMapFormat
        {
            get => _lightMapFormat;
            set => _lightMapFormat = value;
        }

        public float ShadowMapResolutionScale
        {
            get => _shadowMapResolutionScale;
            set => _shadowMapResolutionScale = value;
        }

        public RenderTextureFormat ShadowMapFormat
        {
            get => _shadowMapFormat;
            set => _shadowMapFormat = value;
        }
    }
}
