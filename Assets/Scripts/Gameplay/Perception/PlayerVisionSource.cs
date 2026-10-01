using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Круговой обзор игрока: то, что видно вокруг него внутри помещения.
    ///
    /// Радиус — не «дальность взгляда», а радиус освещённого пятна. Граница
    /// обзора обрезается стенами: рейкаст останавливается на коллайдере
    /// препятствия, поэтому сквозь стену игрок не видит ничего.
    /// </summary>
    public sealed class PlayerVisionSource : MonoBehaviour, IVisionSource
    {
        [Tooltip("Радиус обзора в мировых единицах.")]
        [SerializeField]
        private float _radius = 4.5f;

        [Tooltip("Яркость обзора. 0 — источник выключен.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _intensity = 1f;

        [Tooltip("Точка обзора. Пусто — берётся позиция этого объекта.")]
        [SerializeField]
        private Transform _origin;

        /// <summary>Радиус обзора. Меняется из конфига уровня.</summary>
        public float Radius
        {
            get => _radius;
            set => _radius = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Точка, из которой считается обзор. Открыта наружу: по ней
        /// проверяется, что полигон обзора стоит там же, где стоит персонаж.
        /// </summary>
        public Vector2 Origin => _origin != null ? _origin.position : transform.position;

        public void CollectCones(List<VisionCone> sink)
        {
            if (sink == null || _radius <= 0f || _intensity <= 0f)
            {
                return;
            }

            sink.Add(VisionCone.Circle(Origin, _radius, _intensity));
        }
    }
}
