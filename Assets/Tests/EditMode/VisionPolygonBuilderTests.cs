using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Gameplay.Perception;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Сборка полигона обзора из углов и дистанций попаданий.
    ///
    /// Здесь проверяется то, ради чего обзор вообще считается рейкастами:
    /// препятствие обязано обрезать форму, а не пропустить взгляд сквозь себя.
    /// </summary>
    public sealed class VisionPolygonBuilderTests
    {
        private readonly List<Vector2> _points = new List<Vector2>();

        [SetUp]
        public void SetUp()
        {
            _points.Clear();
        }

        [Test]
        public void PointAt_ZeroDegrees_PointsUp()
        {
            Vector2 point = VisionPolygonBuilder.PointAt(Vector2.zero, 0f, 2f);

            Assert.That(point.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(point.y, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void PointAt_NinetyDegrees_PointsRight()
        {
            Vector2 point = VisionPolygonBuilder.PointAt(Vector2.zero, 90f, 2f);

            Assert.That(point.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(point.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void BuildPoints_WhenRayMisses_UsesFullRadius()
        {
            var angles = new List<float> { 0f };
            var distances = new List<float> { -1f };

            VisionPolygonBuilder.BuildPoints(Vector2.zero, angles, distances, 5f, _points);

            Assert.That(_points.Count, Is.EqualTo(1));
            Assert.That(_points[0].y, Is.EqualTo(5f).Within(0.001f),
                "промах луча обязан давать вершину на полном радиусе");
        }

        [Test]
        public void BuildPoints_WhenRayHits_ClampsToHitDistance()
        {
            var angles = new List<float> { 90f };
            var distances = new List<float> { 1.5f };

            VisionPolygonBuilder.BuildPoints(Vector2.zero, angles, distances, 5f, _points);

            Assert.That(_points[0].x, Is.EqualTo(1.5f).Within(0.001f),
                "вершина обязана стоять на препятствии, а не за ним");
        }

        [Test]
        public void BuildPoints_WhenHitIsFartherThanRadius_ClampsToRadius()
        {
            var angles = new List<float> { 0f };
            var distances = new List<float> { 99f };

            VisionPolygonBuilder.BuildPoints(Vector2.zero, angles, distances, 4f, _points);

            Assert.That(_points[0].y, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void BuildPoints_DoesNotClearSink_SoFansCanBeAppended()
        {
            var angles = new List<float> { 0f };
            var distances = new List<float> { -1f };

            VisionPolygonBuilder.BuildPoints(Vector2.zero, angles, distances, 3f, _points);
            VisionPolygonBuilder.BuildPoints(Vector2.zero, angles, distances, 3f, _points);

            Assert.That(_points.Count, Is.EqualTo(2));
        }

        [Test]
        public void NormalizeAngles_SortsAscendingAndRemovesDuplicates()
        {
            var angles = new List<float> { 20f, -10f, 20.05f, 350f, 20f };

            VisionPolygonBuilder.NormalizeAngles(angles, 0.5f);

            Assert.That(angles, Is.EqualTo(new List<float> { 20f, 350f }));
        }

        [Test]
        public void NormalizeAngles_KeepsGenuinelyDistinctNeighbours()
        {
            var angles = new List<float> { 0f, 1f, 2f };

            VisionPolygonBuilder.NormalizeAngles(angles, 0.5f);

            Assert.That(angles.Count, Is.EqualTo(3),
                "лучи, разнесённые дальше допуска, обязаны остаться: иначе кромки тени срезаются");
        }

        [Test]
        public void NormalizeAngles_WhenEmpty_DoesNotThrow()
        {
            var angles = new List<float>();

            Assert.DoesNotThrow(() => VisionPolygonBuilder.NormalizeAngles(angles, 0.5f));
            Assert.That(angles, Is.Empty);
        }
    }
}
