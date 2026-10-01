using NUnit.Framework;
using UnityEngine;
using Woodberry.Gameplay.Player;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Разворот персонажа по направлению движения.
    ///
    /// Ошибка здесь не ломает движение: персонаж едет правильно, а смотрит
    /// не туда. Найти её можно только проверкой угла, поэтому тест сравнивает
    /// не «угол равен чему-то», а куда в итоге смотрит спрайт: он должен
    /// смотреть в сторону движения.
    /// </summary>
    public sealed class PlayerFacingTests
    {
        /// <summary>Куда смотрит спрайт после поворота узла на заданный угол.</summary>
        private static Vector2 RotatedSpriteForward(float degrees)
        {
            return Quaternion.Euler(0f, 0f, degrees) * PlayerFacing.SpriteForward;
        }

        [Test]
        public void ComputeZRotation_WhenMovingDown_KeepsSpriteUnrotated()
        {
            float degrees = PlayerFacing.ComputeZRotation(Vector2.down);

            Assert.That(degrees, Is.EqualTo(0f).Within(0.01f));
            Assert.That(RotatedSpriteForward(degrees).y, Is.LessThan(-0.99f),
                "спрайт обязан смотреть вниз");
        }

        [Test]
        public void ComputeZRotation_WhenMovingUp_TurnsSpriteAround()
        {
            float degrees = PlayerFacing.ComputeZRotation(Vector2.up);
            Vector2 forward = RotatedSpriteForward(degrees);

            Assert.That(forward.y, Is.GreaterThan(0.99f),
                "при движении вверх спрайт обязан смотреть вверх");
            Assert.That(Mathf.Abs(forward.x), Is.LessThan(0.01f));
        }

        [Test]
        public void ComputeZRotation_WhenMovingRight_PointsSpriteRight()
        {
            // Регрессия: в каскаде условий лево и право были перепутаны —
            // при движении вправо спрайт смотрел влево.
            float degrees = PlayerFacing.ComputeZRotation(Vector2.right);
            Vector2 forward = RotatedSpriteForward(degrees);

            Assert.That(forward.x, Is.GreaterThan(0.99f),
                "при движении вправо спрайт обязан смотреть вправо");
            Assert.That(Mathf.Abs(forward.y), Is.LessThan(0.01f));
        }

        [Test]
        public void ComputeZRotation_WhenMovingLeft_PointsSpriteLeft()
        {
            float degrees = PlayerFacing.ComputeZRotation(Vector2.left);
            Vector2 forward = RotatedSpriteForward(degrees);

            Assert.That(forward.x, Is.LessThan(-0.99f),
                "при движении влево спрайт обязан смотреть влево");
            Assert.That(Mathf.Abs(forward.y), Is.LessThan(0.01f));
        }

        [Test]
        public void ComputeZRotation_ForEachDirection_PointsExactlyAlongIt()
        {
            var directions = new[] { Vector2.down, Vector2.up, Vector2.left, Vector2.right };

            foreach (Vector2 direction in directions)
            {
                float degrees = PlayerFacing.ComputeZRotation(direction);
                Vector2 forward = RotatedSpriteForward(degrees);

                Assert.That(
                    Vector2.Angle(forward, direction),
                    Is.LessThan(0.5f),
                    "спрайт смотрит мимо направления движения: " + direction);
            }
        }

        [Test]
        public void ComputeZRotation_ForDiagonals_SnapsToDominantAxis()
        {
            // Диагональ обязана уйти в доминирующую ось, а не дать 45°:
            // у спрайта всего четыре направления.
            float degrees = PlayerFacing.ComputeZRotation(new Vector2(0.8f, 0.6f));
            Vector2 forward = RotatedSpriteForward(degrees);

            Assert.That(forward.x, Is.GreaterThan(0.99f));
        }

        [Test]
        public void ComputeZRotation_WhenFacingIsZero_ReturnsZeroInsteadOfNaN()
        {
            float degrees = PlayerFacing.ComputeZRotation(Vector2.zero);

            Assert.That(float.IsNaN(degrees), Is.False);
            Assert.That(degrees, Is.EqualTo(0f));
        }

        [Test]
        public void Quantize_WhenDiagonal_PicksDominantAxis()
        {
            Assert.That(PlayerFacing.Quantize(new Vector2(0.9f, 0.2f)), Is.EqualTo(Vector2.right));
            Assert.That(PlayerFacing.Quantize(new Vector2(-0.9f, 0.2f)), Is.EqualTo(Vector2.left));
            Assert.That(PlayerFacing.Quantize(new Vector2(0.2f, 0.9f)), Is.EqualTo(Vector2.up));
            Assert.That(PlayerFacing.Quantize(new Vector2(0.2f, -0.9f)), Is.EqualTo(Vector2.down));
        }

        [Test]
        public void Quantize_WhenExactlyDiagonal_PrefersHorizontal()
        {
            // Ровно 45° — вырожденный случай; правило должно быть
            // детерминированным, иначе персонаж дёргается на диагональном вводе.
            Assert.That(PlayerFacing.Quantize(new Vector2(1f, 1f)), Is.EqualTo(Vector2.right));
        }

        [Test]
        public void Quantize_WhenZero_ReturnsDown()
        {
            Assert.That(PlayerFacing.Quantize(Vector2.zero), Is.EqualTo(Vector2.down));
        }
    }
}
