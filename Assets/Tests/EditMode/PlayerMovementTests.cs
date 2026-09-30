using NUnit.Framework;
using UnityEngine;
using Woodberry.Gameplay.Player;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// EditMode-тесты чистого правила движения: без сцены, MonoBehaviour и таймера.
    /// </summary>
    public sealed class PlayerMovementTests
    {
        private const float Speed = 4f;
        private const float Dt = 0.5f;

        [Test]
        public void ComputeDisplacement_WhenNoInput_ThenReturnsZero()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(Vector2.zero, Speed, Dt);

            Assert.That(result, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ComputeDisplacement_WhenInputForward_ThenMovesAlongPositiveZ()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(Vector2.up, Speed, Dt);

            Assert.That(result.x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(result.y, Is.Zero, "движение строго в плоскости XZ");
            Assert.That(result.z, Is.EqualTo(Speed * Dt).Within(1e-5f));
        }

        [Test]
        public void ComputeDisplacement_WhenInputRight_ThenMovesAlongPositiveX()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(Vector2.right, Speed, Dt);

            Assert.That(result.x, Is.EqualTo(Speed * Dt).Within(1e-5f));
            Assert.That(result.z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void ComputeDisplacement_WhenDiagonalInput_ThenNormalizesToSameSpeed()
        {
            Vector3 diagonal =
                PlayerMovement.ComputeDisplacement(new Vector2(1f, 1f), Speed, Dt);
            Vector3 straight = PlayerMovement.ComputeDisplacement(Vector2.up, Speed, Dt);

            Assert.That(
                diagonal.magnitude,
                Is.EqualTo(straight.magnitude).Within(1e-4f),
                "диагональ не должна давать диагональную скорость");
        }

        [Test]
        public void ComputeDisplacement_WhenInputIsOversized_ThenClampsToUnitLength()
        {
            Vector3 result =
                PlayerMovement.ComputeDisplacement(new Vector2(9f, 9f), Speed, Dt);

            Assert.That(result.magnitude, Is.EqualTo(Speed * Dt).Within(1e-4f));
        }

        [Test]
        public void ComputeDisplacement_WhenInputBelowDeadZone_ThenReturnsZero()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(new Vector2(0.05f, 0f), Speed, Dt);

            Assert.That(result, Is.EqualTo(Vector3.zero), "шум ввода не должен двигать игрока");
        }

        [Test]
        public void ComputeDisplacement_WhenSpeedIsZero_ThenReturnsZero()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(Vector2.up, 0f, Dt);

            Assert.That(result, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ComputeDisplacement_WhenDeltaTimeIsZero_ThenReturnsZero()
        {
            Vector3 result = PlayerMovement.ComputeDisplacement(Vector2.up, Speed, 0f);

            Assert.That(result, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ComputeDisplacement_AlwaysKeepsYAtZero()
        {
            Vector3 result =
                PlayerMovement.ComputeDisplacement(new Vector2(0.7f, -0.7f), Speed, Dt);

            Assert.That(result.y, Is.Zero);
        }

        [Test]
        public void ClampDeadZone_WhenInputBelowThreshold_ThenZeroes()
        {
            Assert.That(
                PlayerMovement.ClampDeadZone(new Vector2(PlayerMovement.DeadZone * 0.5f, 0f)),
                Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ClampDeadZone_WhenInputAboveThreshold_ThenPreservesDirection()
        {
            Vector2 input = new Vector2(0.6f, 0.8f);
            Vector2 result = PlayerMovement.ClampDeadZone(input);

            Assert.That(result.x, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(result.y, Is.EqualTo(0.8f).Within(1e-5f));
        }
    }
}
