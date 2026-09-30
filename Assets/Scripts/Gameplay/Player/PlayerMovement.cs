using UnityEngine;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Чистое правило движения: ввод (XZ) + скорость -> смещение за кадр.
    /// Вынесено отдельно от <see cref="PlayerController"/>, чтобы правило
    /// проверялось EditMode-тестом без сцены, MonoBehaviour и таймера.
    /// </summary>
    public static class PlayerMovement
    {
        /// <summary>Порог, ниже которого ввод считается шумом и игрок стоит.</summary>
        public const float DeadZone = 0.1f;

        /// <summary>
        /// Смещение за кадр в плоскости XZ.
        /// Ввод обрезается по мёртвой зоне, затем нормализуется, поэтому диагональ
        /// не даёт диагональной скорости. Y всегда 0 — вид сверху.
        /// </summary>
        public static Vector3 ComputeDisplacement(Vector2 input, float speed, float deltaTime)
        {
            if (speed <= 0f || deltaTime <= 0f)
            {
                return Vector3.zero;
            }

            Vector2 direction = ClampDeadZone(input);

            if (direction.sqrMagnitude <= 0f)
            {
                return Vector3.zero;
            }

            // Нормализация ограничивает длину сверху единицей, поэтому
            // диагональ (1,1) движется с той же скоростью, что и (1,0).
            Vector3 offset = Vector3.ClampMagnitude(direction, 1f);

            return new Vector3(offset.x, 0f, offset.y) * (speed * deltaTime);
        }

        /// <summary>Убирает мёртвую зону, не меняя направление.</summary>
        public static Vector2 ClampDeadZone(Vector2 input)
        {
            if (input.sqrMagnitude < DeadZone * DeadZone)
            {
                return Vector2.zero;
            }

            return input;
        }
    }
}
