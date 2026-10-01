using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Gameplay.Perception;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Веер лучей: набор углов, по которым строится полигон обзора.
    /// Чистая арифметика, проверяется без сцены.
    /// </summary>
    public sealed class VisionFanBuilderTests
    {
        private readonly List<float> _angles = new List<float>();

        [SetUp]
        public void SetUp()
        {
            _angles.Clear();
        }

        [Test]
        public void BuildAngles_ForCircle_CoversFullTurnWithoutDuplicatingZero()
        {
            VisionFanBuilder.BuildAngles(VisionCone.Circle(Vector2.zero, 5f), 10f, _angles);

            Assert.That(_angles.Count, Is.EqualTo(36));
            Assert.That(_angles[0], Is.EqualTo(0f).Within(0.001f));
            Assert.That(_angles, Has.No.All.EqualTo(360f),
                "360 не должен попадать в список: это тот же угол, что и 0");
        }

        [Test]
        public void BuildAngles_ForSpot_StaysInsideSector()
        {
            // Ось вверх, раствор 20 градусов (половина угла).
            VisionFanBuilder.BuildAngles(
                VisionCone.Spot(Vector2.zero, Vector2.up, 20f, 5f), 5f, _angles);

            Assert.That(_angles.Count, Is.GreaterThan(0));

            foreach (float angle in _angles)
            {
                float delta = Mathf.DeltaAngle(0f, angle);
                Assert.That(Mathf.Abs(delta), Is.LessThanOrEqualTo(20.001f),
                    "луч вышел за раствор сектора: " + angle);
            }
        }

        [Test]
        public void BuildAngles_ForSpot_PlacesRaysExactlyOnBothEdges()
        {
            VisionFanBuilder.BuildAngles(
                VisionCone.Spot(Vector2.zero, Vector2.up, 30f, 5f), 7f, _angles);

            Assert.That(_angles.Contains(-30f), Is.True, "левый край сектора пропущен");
            Assert.That(_angles.Contains(30f), Is.True, "правый край сектора пропущен");
        }

        [Test]
        public void BuildAngles_WhenStepIsZero_DoesNotLoopForever()
        {
            // Ноль в инспекторе не должен превращать кадр в бесконечный цикл.
            VisionFanBuilder.BuildAngles(VisionCone.Circle(Vector2.zero, 5f), 0f, _angles);

            Assert.That(_angles.Count, Is.GreaterThan(0));
            Assert.That(_angles.Count, Is.LessThan(2000));
        }

        [Test]
        public void DirectionToDegrees_IsZeroUpAndGrowsClockwise()
        {
            Assert.That(VisionFanBuilder.DirectionToDegrees(Vector2.up), Is.EqualTo(0f).Within(0.01f));
            Assert.That(VisionFanBuilder.DirectionToDegrees(Vector2.right), Is.EqualTo(90f).Within(0.01f));
            Assert.That(VisionFanBuilder.DirectionToDegrees(Vector2.down), Is.EqualTo(180f).Within(0.01f));
            Assert.That(VisionFanBuilder.DirectionToDegrees(Vector2.left), Is.EqualTo(270f).Within(0.01f));
        }

        [Test]
        public void AppendEdgeAngles_AddsRaysOnBothSidesOfEveryHit()
        {
            var hits = new List<Vector2> { new Vector2(0f, 3f) };
            _angles.Add(0f);

            VisionFanBuilder.AppendEdgeAngles(_angles, Vector2.zero, hits, 0.5f);

            Assert.That(_angles.Count, Is.EqualTo(3));
            Assert.That(_angles.Contains(-0.5f), Is.True);
            Assert.That(_angles.Contains(0.5f), Is.True);
        }

        [Test]
        public void AppendEdgeAngles_WhenHitIsAtOrigin_SkipsItInsteadOfDividingByZero()
        {
            var hits = new List<Vector2> { Vector2.zero };

            VisionFanBuilder.AppendEdgeAngles(_angles, Vector2.zero, hits, 0.5f);

            Assert.That(_angles, Is.Empty,
                "попадание в самое начало не даёт направления — луч добавлять не из чего");
        }
    }
}
