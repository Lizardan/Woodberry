using UnityEngine;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Чистое правило движения: ввод + скорость -> смещение за кадр.
    /// Вынесено отдельно от <see cref="PlayerController"/>, чтобы правило
    /// проверялось EditMode-тестом без сцены, MonoBehaviour и таймера.
    ///
    /// Игра двухмерная: движение в плоскости XY, поэтому и возвращается Vector2.
    /// При переходе на вид сверху в 3D оси были бы XZ — это единственное отличие
    /// от прежней версии, и оно намеренно держится в одной строке.
    /// </summary>
    public static class PlayerMovement
    {
        /// <summary>Порог, ниже которого ввод считается шумом и игрок стоит.</summary>
        public const float DeadZone = 0.1f;

        /// <summary>
        /// Смещение за кадр в плоскости XY.
        /// Ввод обрезается по мёртвой зоне, затем нормализуется, поэтому диагональ
        /// не даёт диагональной скорости.
        /// </summary>
        public static Vector2 ComputeDisplacement(Vector2 input, float speed, float deltaTime)
        {
            if (speed <= 0f || deltaTime <= 0f)
            {
                return Vector2.zero;
            }

            Vector2 direction = ClampDeadZone(input);

            if (direction.sqrMagnitude <= 0f)
            {
                return Vector2.zero;
            }

            // Нормализация ограничивает длину сверху единицей, поэтому
            // диагональ (1,1) движется с той же скоростью, что и (1,0).
            return Vector2.ClampMagnitude(direction, 1f) * (speed * deltaTime);
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
