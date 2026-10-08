using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Собирает полигон обзора из углов лучей и дистанций попаданий.
    ///
    /// Чистая геометрия: ни физики, ни сцены. Рейкаст делает MonoBehaviour,
    /// а форма строится здесь, поэтому она проверяется EditMode-тестами.
    ///
    /// Ключевое свойство: если луч не во что не попал, вершина кладётся на
    /// полный радиус. Тогда препятствие «притупляет» угол обзора — за стеной
    /// полигон обрезан по её кромке, и увидеть сквозь стену нельзя.
    /// </summary>
    public static class VisionPolygonBuilder
    {
        /// <summary>
        /// Переводит углы и дистанции в точки полигона.
        /// </summary>
        /// <param name="origin">Точка, из которой пускались лучи.</param>
        /// <param name="angles">Углы в градусах (система <see cref="VisionFanBuilder.DirectionToDegrees"/>).</param>
        /// <param name="distances">Дистанция до попадания для каждого угла; отрицательная — промах.</param>
        /// <param name="fallbackRadius">Радиус, на который кладётся вершина при промахе.</param>
        /// <param name="sink">Куда сложить точки. Не очищается — вызывающий решает.</param>
        public static void BuildPoints(Vector2 origin, IReadOnlyList<float> angles,
                                       IReadOnlyList<float> distances, float fallbackRadius,
                                       List<Vector2> sink)
        {
            if (angles == null || distances == null || sink == null)
            {
                return;
            }

            int count = Mathf.Min(angles.Count, distances.Count);
            float radius = Mathf.Max(0f, fallbackRadius);

            for (int i = 0; i < count; i++)
            {
                float distance = distances[i];
                float clamped = distance < 0f ? radius : Mathf.Min(distance, radius);
                sink.Add(PointAt(origin, angles[i], clamped));
            }
        }

        /// <summary>Точка на заданном угле и дистанции от начала.</summary>
        public static Vector2 PointAt(Vector2 origin, float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(
                origin.x + Mathf.Sin(radians) * distance,
                origin.y + Mathf.Cos(radians) * distance);
        }

        /// <summary>
        /// Считает две точки касания лучей из <paramref name="origin"/> к окружности.
        ///
        /// Нужно, чтобы кромка тени от круглого препятствия (дерева) не
        /// «плавала»: без них силуэт круга приближается сеткой лучей веера,
        /// и при движении граница перескакивает между соседними лучами
        /// на несколько градусов — это и есть дрожание кромки.
        ///
        /// Касательные — это точный силуэт круга из данной точки, поэтому лучи
        /// по ним прибивают кромку к настоящей геометрии. Если точка внутри
        /// окружности или на её центре, касательных нет — ничего не добавляем.
        /// </summary>
        public static void CircleTangentPoints(Vector2 origin, Vector2 center,
                                               float radius, List<Vector2> sink)
        {
            if (sink == null)
            {
                return;
            }

            float r = Mathf.Max(0f, radius);
            Vector2 delta = center - origin;
            float distanceSquared = delta.sqrMagnitude;

            if (r <= 0f || distanceSquared <= r * r)
            {
                return;
            }

            float distance = Mathf.Sqrt(distanceSquared);

            // Угол при центре между направлением на origin и точкой касания.
            float atCenter = Mathf.Acos(Mathf.Clamp(r / distance, 0f, 1f));

            // Направление из центра на origin в системе обзора (0 — вверх).
            float toOrigin = Mathf.Atan2(-delta.x, -delta.y);

            float first = toOrigin + atCenter;
            float second = toOrigin - atCenter;

            sink.Add(new Vector2(center.x + Mathf.Sin(first) * r,
                                 center.y + Mathf.Cos(first) * r));
            sink.Add(new Vector2(center.x + Mathf.Sin(second) * r,
                                 center.y + Mathf.Cos(second) * r));
        }

        /// <summary>
        /// Убирает дубли углов с точностью до <paramref name="toleranceDegrees"/>.
        ///
        /// Дубли появляются из-за того, что лучи по краям попаданий добавляются
        /// поверх основного веера, а направленные конусы могут пересекаться.
        /// Дубли не ломают картинку, но раздувают меш и триангуляцию.
        /// Список сортируется по углу — это требование веера.
        /// </summary>
        public static void NormalizeAngles(List<float> angles, float toleranceDegrees)
        {
            if (angles == null || angles.Count == 0)
            {
                return;
            }

            for (int i = 0; i < angles.Count; i++)
            {
                angles[i] = VisionFanBuilder.Normalize(angles[i]);
            }

            angles.Sort();

            float tolerance = Mathf.Max(0.001f, toleranceDegrees);
            int write = 1;

            for (int read = 1; read < angles.Count; read++)
            {
                if (angles[read] - angles[write - 1] > tolerance)
                {
                    angles[write] = angles[read];
                    write++;
                }
            }

            if (write < angles.Count)
            {
                angles.RemoveRange(write, angles.Count - write);
            }
        }
    }
}
