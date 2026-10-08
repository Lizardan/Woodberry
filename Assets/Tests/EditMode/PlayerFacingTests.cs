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
    /// смотреть в сторону движения — в любую, а не только в четыре стороны.
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
        public void ComputeZRotation_ForDiagonals_PointsAlongTheDiagonal()
        {
            // Раньше угол округлялся до ближайших 90°, и диагональ уводило
            // в ближайшую ось — персонаж смотрел мимо движения. Спрайт
            // рисуется сверху, поэтому он обязан разворачиваться в саму
            // диагональ, а не выбирать из четырёх сторон.
            var diagonals = new[]
            {
                new Vector2(1f, 1f).normalized,
                new Vector2(1f, -1f).normalized,
                new Vector2(-1f, 1f).normalized,
                new Vector2(-1f, -1f).normalized,
                new Vector2(0.8f, 0.6f).normalized,
            };

            foreach (Vector2 direction in diagonals)
            {
                float degrees = PlayerFacing.ComputeZRotation(direction);
                Vector2 forward = RotatedSpriteForward(degrees);

                Assert.That(
                    Vector2.Angle(forward, direction),
                    Is.LessThan(0.5f),
                    "диагональ обязана разворачивать ровно по движению: " + direction);
            }
        }

        [Test]
        public void ComputeZRotation_WhenFacingIsZero_ReturnsZeroInsteadOfNaN()
        {
            float degrees = PlayerFacing.ComputeZRotation(Vector2.zero);

            Assert.That(float.IsNaN(degrees), Is.False);
            Assert.That(degrees, Is.EqualTo(0f));
        }

        [Test]
        public void StepAngle_MovesTowardTargetWithoutOvershoot()
        {
            float stepped = PlayerFacing.StepAngle(0f, 90f, 10f);

            Assert.That(stepped, Is.EqualTo(10f).Within(0.01f),
                "за вызов угол не должен уходить дальше предела");
        }

        [Test]
        public void StepAngle_WhenTargetIsWithinReach_ReachesItExactly()
        {
            float stepped = PlayerFacing.StepAngle(80f, 90f, 30f);

            Assert.That(stepped, Is.EqualTo(90f).Within(0.01f));
        }

        [Test]
        public void StepAngle_TakesTheShortWayAround()
        {
            // От 350° к 10° ближе через ноль (+20°), а не назад через 340.
            float stepped = PlayerFacing.StepAngle(350f, 10f, 5f);

            Assert.That(stepped, Is.EqualTo(355f).Within(0.01f),
                "поворот обязан идти по кратчайшей дуге, иначе персонаж крутится на 340°");
        }

        [Test]
        public void StepAngle_WhenSpeedIsZero_KeepsCurrentAngle()
        {
            float stepped = PlayerFacing.StepAngle(42f, 90f, 0f);

            Assert.That(stepped, Is.EqualTo(42f).Within(0.01f));
        }
    }
}
