using UnityEngine;

namespace Woodberry.Gameplay.Player
{
    /// <summary>
    /// Правило разворота персонажа: направление движения → угол поворота узла
    /// <c>Visual</c>.
    ///
    /// Вынесено из <see cref="PlayerController"/> отдельной чистой функцией,
    /// потому что ошибка здесь не ловится ничем, кроме глаза: персонаж едет
    /// правильно, а картинка смотрит не туда.
    ///
    /// Персонаж смотрит **в сторону движения**, а не в ближайшую из четырёх
    /// сторон. Спрайт нарисован строго сверху, поэтому поворот в плоскости XY
    /// и есть разворот: голова и плечи поворачиваются вокруг центра корпуса
    /// ровно так, как повернулся бы человек. Четыре фиксированных направления
    /// давали резкий перескок через 90° и не совпадали с диагональным вводом —
    /// поэтому угол берётся из направления как есть, без округления.
    ///
    /// Спрайт нарисован лицом **вниз**, то есть его «вперёд» — это локальная
    /// ось <c>-Y</c>. Поворот на угол θ переводит <c>-Y</c> в
    /// <c>(sin θ, -cos θ)</c>. Приравняв это к направлению движения, получаем
    /// <c>θ = atan2(fx, -fy)</c>.
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

            return Mathf.Atan2(facing.x, -facing.y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Доворачивает текущий угол к целевому не быстрее
        /// <paramref name="maxDegrees"/> за вызов, по кратчайшей дуге.
        ///
        /// Чистая функция, чтобы плавность можно было проверить без сцены.
        /// Именно она убирает «рывок»: угол приходит к цели за несколько
        /// кадров, а не скачком.
        /// </summary>
        public static float StepAngle(float currentDegrees, float targetDegrees, float maxDegrees)
        {
            if (maxDegrees <= 0f)
            {
                return currentDegrees;
            }

            return Mathf.MoveTowardsAngle(currentDegrees, targetDegrees, maxDegrees);
        }
    }
}
