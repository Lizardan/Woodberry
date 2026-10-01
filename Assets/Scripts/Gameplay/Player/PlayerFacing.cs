using UnityEngine;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Правило разворота персонажа: направление движения → угол поворота узла
    /// <c>Visual</c>.
    ///
    /// Вынесено из <see cref="PlayerController"/> отдельной чистой функцией,
    /// потому что ошибка здесь не ловится ничем, кроме глаза: персонаж едет
    /// правильно, а картинка смотрит не туда. Ровно такая ошибка и была —
    /// влево и вправо оказались перепутаны, и заметить это можно было только
    /// на живом кадре.
    ///
    /// Спрайт нарисован лицом **вниз**, то есть его «вперёд» — это локальная
    /// ось <c>-Y</c>. Поворот на угол θ переводит <c>-Y</c> в
    /// <c>(sin θ, -cos θ)</c>. Приравняв это к направлению движения, получаем
    /// <c>θ = atan2(fx, -fy)</c>. Дальше угол округляется до ближайших 90° —
    /// у нас четыре направления, и промежуточные значения не нужны.
    /// </summary>
    public static class PlayerFacing
    {
        /// <summary>Ось, в которую смотрит нарисованный спрайт: вниз.</summary>
        public static readonly Vector2 SpriteForward = Vector2.down;

        /// <summary>
        /// Угол поворота узла <c>Visual</c> в градусах для направления движения.
        /// Нулевое направление считается «вниз» — это состояние покоя.
        /// </summary>
        public static float ComputeZRotation(Vector2 facing)
        {
            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                return 0f;
            }

            float degrees = Mathf.Atan2(facing.x, -facing.y) * Mathf.Rad2Deg;

            // Округление до ближайших 90° вместо каскада if: каскад легко
            // записать зеркально, и именно так лево с правом поменялись
            // местами. Здесь направление задано одной формулой.
            return Mathf.Round(degrees / 90f) * 90f;
        }

        /// <summary>
        /// Приводит направление к одному из четырёх: вниз, вправо, вверх, влево.
        /// Диагональ уходит в доминирующую ось.
        /// </summary>
        public static Vector2 Quantize(Vector2 facing)
        {
            if (facing.sqrMagnitude <= Mathf.Epsilon)
            {
                return Vector2.down;
            }

            if (Mathf.Abs(facing.x) >= Mathf.Abs(facing.y))
            {
                return facing.x >= 0f ? Vector2.right : Vector2.left;
            }

            return facing.y >= 0f ? Vector2.up : Vector2.down;
        }
    }
}
