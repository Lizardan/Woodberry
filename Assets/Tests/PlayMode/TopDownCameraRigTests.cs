using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using Woodberry.CameraRig;

namespace Woodberry.Tests.PlayMode
{
    /// <summary>
    /// Регрессия на Critical Stage 00: <c>SetTarget</c> нигде не вызывался,
    /// поэтому камера молча стояла на месте. Тест фиксирует контракт —
    /// после назначения цели камера следует за ней и догоняет её без рывка.
    /// </summary>
    public sealed class TopDownCameraRigTests
    {
        private GameObject _target;
        private GameObject _rig;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_target != null)
            {
                Object.DestroyImmediate(_target);
            }

            if (_rig != null)
            {
                Object.DestroyImmediate(_rig);
            }
        }

        [UnityTest]
        public IEnumerator SetTarget_SnapsCameraToTarget_WithoutOvershoot()
        {
            CreateRig(out Camera camera, out TopDownCameraRig rig);

            _target.transform.position = new Vector3(10f, 0f, 4f);
            rig.SetTarget(_target.transform);

            yield return null;

            Assert.That(
                rig.transform.position,
                Is.EqualTo(new Vector3(10f, 12f, 6f)).Using(Vector3ComparerWithEqualsOperator.Instance),
                "после SetTarget камера должна встать над целью, а не остаться в нуле");

            Assert.That(
                camera.transform.eulerAngles.x,
                Is.EqualTo(90f).Within(0.01f),
                "камера должна смотреть строго сверху вниз");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenTargetMoves_CameraMovesTowardTarget()
        {
            CreateRig(out _, out TopDownCameraRig rig);
            rig.SetTarget(_target.transform);
            yield return null;

            Vector3 before = rig.transform.position;

            _target.transform.position = new Vector3(3f, 0f, 3f);
            yield return null;

            Vector3 desired = new Vector3(3f, 12f, 5f);
            float distanceBefore = Vector3.Distance(before, desired);
            float distanceAfter = Vector3.Distance(rig.transform.position, desired);

            Assert.That(
                distanceAfter,
                Is.LessThan(distanceBefore),
                "при смещении цели камера обязана сближаться с ней");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenTargetStaysStill_CameraDoesNotDrift()
        {
            CreateRig(out _, out TopDownCameraRig rig);
            rig.SetTarget(_target.transform);
            yield return null;

            Vector3 before = rig.transform.position;
            yield return new WaitForSeconds(0.2f);

            Assert.That(
                rig.transform.position,
                Is.EqualTo(before).Using(Vector3ComparerWithEqualsOperator.Instance),
                "стоящая цель не должна уводить камеру дребезгом (мёртвая зона)");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenTargetLost_DoesNotThrow()
        {
            CreateRig(out _, out TopDownCameraRig rig);
            rig.SetTarget(_target.transform);
            yield return null;

            Object.DestroyImmediate(_target);
            _target = null;

            LogAssert.ignoreFailingMessages = false;
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        private void CreateRig(out Camera camera, out TopDownCameraRig rig)
        {
            _target = new GameObject("CameraTarget");

            _rig = new GameObject("TopDownCamera");
            camera = _rig.AddComponent<Camera>();
            rig = _rig.AddComponent<TopDownCameraRig>();
        }
    }
}
