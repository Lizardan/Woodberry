using UnityEngine;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Область обзора как чистое значение: точка, направление, угол и радиус.
    ///
    /// Тип намеренно не MonoBehaviour и не зависит от сцены — вся арифметика
    /// обзора проверяется EditMode-тестами без запуска Play Mode.
    ///
    /// Два вида конуса:
    /// <list type="bullet">
    /// <item>круговой (<see cref="Circle"/>) — то, что игрок видит вокруг себя;</item>
    /// <item>направленный (<see cref="Spot"/>) — сектор с вершиной в заданной
    /// точке. Нужен для источника, который светит вперёд, а не вокруг себя
    /// (фонарь, взгляд врага).</item>
    /// </list>
    ///
    /// Проёмы — окна и двери — конусами не описываются: это дырки в геометрии
    /// стен, и обзор проходит через них той же круговой логикой, что и всюду.
    /// Отдельный конус для окна пробовали и убрали — он давал второе пятно
    /// поверх первого.
    /// </summary>
    public readonly struct VisionCone
    {
        /// <summary>Полный угол, при котором конус считается круговым.</summary>
        public const float FullCircleDegrees = 360f;

        public VisionCone(Vector2 origin, Vector2 direction, float halfAngleDegrees,
                          float radius, float intensity)
        {
            Origin = origin;
            Direction = direction;
            HalfAngleDegrees = Mathf.Clamp(halfAngleDegrees, 0f, 180f);
            Radius = Mathf.Max(0f, radius);
            Intensity = Mathf.Clamp01(intensity);
        }

        /// <summary>Точка, из которой считается угол зрения.</summary>
        public Vector2 Origin { get; }

        /// <summary>Ось конуса. Для кругового не используется.</summary>
        public Vector2 Direction { get; }

        /// <summary>Половина угла раствора в градусах. 180 — полный круг.</summary>
        public float HalfAngleDegrees { get; }

        /// <summary>Дальность обзора в мировых единицах.</summary>
        public float Radius { get; }

        /// <summary>Яркость обзора: 0 — конус выключен и не влияет на маску.</summary>
        public float Intensity { get; }

        /// <summary>Круговой конус: видно во все стороны на заданный радиус.</summary>
        public bool IsOmnidirectional => HalfAngleDegrees >= 180f;

        /// <summary>Конус не влияет ни на что: нулевой радиус или нулевая яркость.</summary>
        public bool IsEmpty => Radius <= 0f || Intensity <= 0f;

        /// <summary>Круговой обзор вокруг точки.</summary>
        public static VisionCone Circle(Vector2 origin, float radius, float intensity = 1f)
        {
            return new VisionCone(origin, Vector2.down, 180f, radius, intensity);
        }

        /// <summary>Направленный обзор: вершина угла в <paramref name="origin"/>.</summary>
        public static VisionCone Spot(Vector2 origin, Vector2 direction,
                                      float halfAngleDegrees, float radius, float intensity = 1f)
        {
            Vector2 axis = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.down;
            return new VisionCone(origin, axis, halfAngleDegrees, radius, intensity);
        }

        /// <summary>
        /// Попадает ли точка в конус. Используется тестами и отладкой;
        /// сам обзор строится рейкастами, а не этой проверкой.
        /// </summary>
        public bool Contains(Vector2 point)
        {
            if (IsEmpty)
            {
                return false;
            }

            Vector2 delta = point - Origin;

            if (delta.sqrMagnitude > Radius * Radius)
            {
                return false;
            }

            if (IsOmnidirectional)
            {
                return true;
            }

            if (delta.sqrMagnitude <= Mathf.Epsilon)
            {
                return true;
            }

            // Допуск обязателен: на самой кромке сектора Vector2.Angle даёт
            // 30.000002 вместо 30, и точка ровно на границе «выпадала» бы из
            // конуса. На стыке двух секторов это дало бы дырку в обзоре.
            const float BoundaryTolerance = 0.05f;

            float angle = Vector2.Angle(Direction, delta);
            return angle <= HalfAngleDegrees + BoundaryTolerance;
        }
    }
}
