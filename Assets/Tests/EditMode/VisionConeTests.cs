using NUnit.Framework;
using UnityEngine;
using Woodberry.Gameplay.Perception;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Конус обзора как чистое значение. Проверяется без сцены, физики и
    /// Play Mode — это и было целью вынесения обзора из MonoBehaviour.
    /// </summary>
    public sealed class VisionConeTests
    {
        [Test]
        public void Circle_IsOmnidirectional_AndCoversPointBehindOrigin()
        {
            VisionCone cone = VisionCone.Circle(Vector2.zero, 5f);

            Assert.That(cone.IsOmnidirectional, Is.True);
            Assert.That(cone.Contains(new Vector2(0f, -4f)), Is.True,
                "круговой обзор обязан покрывать точку позади начала");
            Assert.That(cone.Contains(new Vector2(4f, 0f)), Is.True);
        }

        [Test]
        public void Contains_WhenPointBeyondRadius_IsFalse()
        {
            VisionCone cone = VisionCone.Circle(Vector2.zero, 5f);

            Assert.That(cone.Contains(new Vector2(0f, 5.5f)), Is.False);
        }

        [Test]
        public void Contains_WhenPointOutsideSpotAngle_IsFalse()
        {
            // Сектор смотрит вверх (0,1), раствор 30 градусов.
            VisionCone cone = VisionCone.Spot(Vector2.zero, Vector2.up, 30f, 10f);

            Assert.That(cone.Contains(new Vector2(0f, 5f)), Is.True,
                "точка по оси конуса обязана попадать");
            Assert.That(cone.Contains(new Vector2(5f, 0f)), Is.False,
                "точка строго вбок от оси обязана быть вне сектора");
            Assert.That(cone.Contains(new Vector2(0f, -5f)), Is.False,
                "точка строго назад обязана быть вне сектора");
        }

        [Test]
        public void Spot_WhenPointOnAngleBoundary_IsContained()
        {
            VisionCone cone = VisionCone.Spot(Vector2.zero, Vector2.up, 30f, 10f);
            Vector2 onBoundary = Quaternion.Euler(0f, 0f, 30f) * Vector2.up * 5f;

            Assert.That(cone.Contains(onBoundary), Is.True,
                "граница сектора включительно: иначе на кромке появляется дырка");
        }

        [Test]
        public void IsEmpty_WhenRadiusOrIntensityIsZero_IsTrue()
        {
            Assert.That(VisionCone.Circle(Vector2.zero, 0f).IsEmpty, Is.True);
            Assert.That(VisionCone.Circle(Vector2.zero, 5f, 0f).IsEmpty, Is.True);
            Assert.That(VisionCone.Circle(Vector2.zero, 5f, 1f).IsEmpty, Is.False);
        }

        [Test]
        public void Spot_WhenDirectionIsZero_FallsBackToDownInsteadOfNaN()
        {
            VisionCone cone = VisionCone.Spot(Vector2.zero, Vector2.zero, 30f, 5f);

            Assert.That(float.IsNaN(cone.Direction.x), Is.False);
            Assert.That(float.IsNaN(cone.Direction.y), Is.False);
            Assert.That(cone.Direction.sqrMagnitude, Is.EqualTo(1f).Within(0.001f));
        }
    }
}
