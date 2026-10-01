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

            _target.transform.position = new Vector3(10f, 4f, 0f);
            rig.SetTarget(_target.transform);

            yield return null;

            Assert.That(
                rig.transform.position,
                Is.EqualTo(new Vector3(10f, 5f, 0f)).Using(Vector3ComparerWithEqualsOperator.Instance),
                "после SetTarget камера должна встать над целью, а не остаться в нуле");

            Assert.That(
                rig.transform.eulerAngles,
                Is.EqualTo(Vector3.zero),
                "камера обязана смотреть строго вниз: в 2D это нулевой наклон");
        }

        [UnityTest]
        public IEnumerator Awake_CameraIsOrthographic_LookingStraightDown()
        {
            CreateRig(out Camera camera, out _);
            yield return null;

            // Перспектива сверху разъезжается к краям и ломает ровную сетку,
            // по которой игроки кооперативно ориентируются.
            Assert.That(camera.orthographic, Is.True, "камера обязана быть ортографической");
            Assert.That(camera.orthographicSize, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenTargetMoves_CameraCatchesUp()
        {
            CreateRig(out _, out TopDownCameraRig rig);
            rig.SetTarget(_target.transform);
            yield return null;

            _target.transform.position = new Vector3(3f, 3f, 0f);

            // Проверяем именно ДОГОН, а не «сдвинулась за кадр».
            //
            // Прежняя версия этого теста утверждала только «расстояние до цели
            // уменьшилось» — и пропускала дефект, при котором камера делала
            // один шаг и замирала, потому что мёртвая зона сравнивалась со
            // смещением цели. Один шаг условие «сближается» выполняет, а
            // следованием это не является.
            Vector3 desired = new Vector3(3f, 4f, 0f);
            float timeout = 2f;

            while (timeout > 0f
                   && Vector3.Distance(rig.transform.position, desired) > 0.05f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.That(
                Vector3.Distance(rig.transform.position, desired),
                Is.LessThan(0.05f),
                "камера обязана доехать до цели, а не остановиться на полпути");
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
