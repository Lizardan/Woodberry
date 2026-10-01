using System.Collections.Generic;
using UnityEngine;

namespace Woodberry.Gameplay.Perception
{
    /// <summary>
    /// Строит набор углов, по которым пускаются лучи обзора.
    ///
    /// Чистая функция без Unity-сцены: на вход конусы, на выход отсортированный
    /// список углов в градусах. Тестируется EditMode-тестами.
    ///
    /// Почему углы, а не сразу точки: рейкаст живёт в MonoBehaviour, а
    /// геометрия — здесь. Разделение позволяет проверить углы и сборку
    /// полигона без физики.
    /// </summary>
    public static class VisionFanBuilder
    {
        /// <summary>
        /// Добавляет в <paramref name="sink"/> углы лучей для всех конусов.
        ///
        /// Круговой конус даёт полный оборот с шагом <paramref name="stepDegrees"/>,
        /// направленный — только свой сектор. Шаг ограничен снизу, иначе
        /// случайный ноль в инспекторе превратит кадр в бесконечный цикл.
        /// </summary>
        public static void BuildAngles(IReadOnlyList<VisionCone> cones, float stepDegrees, List<float> sink)
        {
            if (cones == null || sink == null)
            {
                return;
            }

            for (int i = 0; i < cones.Count; i++)
            {
                BuildAngles(cones[i], stepDegrees, sink);
            }
        }

        /// <summary>Добавляет углы лучей одного конуса.</summary>
        public static void BuildAngles(VisionCone cone, float stepDegrees, List<float> sink)
        {
            if (sink == null || cone.IsEmpty)
            {
                return;
            }

            float step = Mathf.Max(0.25f, stepDegrees);

            if (cone.IsOmnidirectional)
            {
                for (float angle = 0f; angle < VisionCone.FullCircleDegrees; angle += step)
                {
                    sink.Add(angle);
                }

                return;
            }

            float center = DirectionToDegrees(cone.Direction);
            float half = cone.HalfAngleDegrees;

            // Шаг выравнивается по раствору, а последний луч ставится ровно на
            // правую границу. Простой прибавкой шага правая кромка выходит
            // 29.999998 вместо 30, и между двумя соседними секторами остаётся
            // щель — на экране это тонкая тёмная полоса поперёк обзора.
            int steps = Mathf.Max(1, Mathf.CeilToInt((half * 2f) / step));
            float actualStep = (half * 2f) / steps;

            for (int s = 0; s < steps; s++)
            {
                sink.Add(center - half + actualStep * s);
            }

            sink.Add(center + half);
        }

        /// <summary>
        /// Добавляет лучи по краям найденных попаданий.
        ///
        /// Без этого углы стен получаются срезанными: между двумя соседними
        /// лучами стена успевает закончиться, и граница тени «плывёт» на
        /// несколько градусов. Два луча вплотную к попаданию прижимают
        /// границу к настоящему углу препятствия.
        /// </summary>
        public static void AppendEdgeAngles(List<float> angles, Vector2 origin,
                                            IReadOnlyList<Vector2> hitPoints,
                                            float epsilonDegrees)
        {
            if (angles == null || hitPoints == null)
            {
                return;
            }

            float epsilon = Mathf.Max(0.01f, epsilonDegrees);

            for (int i = 0; i < hitPoints.Count; i++)
            {
                Vector2 delta = hitPoints[i] - origin;

                if (delta.sqrMagnitude <= Mathf.Epsilon)
                {
                    continue;
                }

                float angle = DirectionToDegrees(delta);
                angles.Add(angle - epsilon);
                angles.Add(angle + epsilon);
            }
        }

        /// <summary>
        /// Угол направления в градусах в системе обзора: 0 — вверх, по часовой.
        /// </summary>
        public static float DirectionToDegrees(Vector2 direction)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            float degrees = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            return Normalize(degrees);
        }

        /// <summary>Приводит угол к диапазону [0, 360).</summary>
        public static float Normalize(float degrees)
        {
            float result = degrees % VisionCone.FullCircleDegrees;

            if (result < 0f)
            {
                result += VisionCone.FullCircleDegrees;
            }

            return result;
        }
    }
}
