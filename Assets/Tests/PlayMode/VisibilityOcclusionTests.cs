using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Woodberry.Gameplay.Perception;

namespace Woodberry.Tests.PlayMode
{
    /// <summary>
    /// Механика обзора: стены режут видимость, проёмы — пропускают.
    ///
    /// По скриншоту это не проверяется: сцена тёмная, и «стена обрезала
    /// обзор» неотличимо от «стена не попала в кадр». Поэтому тесты меряют
    /// форму построенного полигона, а не смотрят на картинку.
    ///
    /// Проёмов два вида и они равноправны: дверной и оконный. У окна нет
    /// собственной логики обзора — это просто дырка в стене, и свет идёт
    /// через неё ровно так же, как через дверь. Отдельный источник обзора
    /// для окна был и убран: он давал второе пятно поверх первого.
    /// </summary>
    public sealed class VisibilityOcclusionTests
    {
        private const float VisionRadius = 5f;
        private const float WallX = 2f;
        private const float WallHalfWidth = 0.2f;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenWallStandsInTheWay_DoesNotRevealBehindIt()
        {
            var (overlay, _) = CreateScene(withWall: true);

            // Физика должна успеть зарегистрировать коллайдеры до рейкастов.
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            Assert.That(overlay.LastPolygonPoints.Count, Is.GreaterThan(0),
                "полигон обзора не построен — проверять нечего");

            float nearFace = WallX - WallHalfWidth;
            float farthest = 0f;

            foreach (Vector2 point in overlay.LastPolygonPoints)
            {
                Assert.That(point.x, Is.LessThanOrEqualTo(nearFace + 0.05f),
                    "вершина обзора оказалась за стеной: " + point);

                farthest = Mathf.Max(farthest, point.x);
            }

            Assert.That(farthest, Is.GreaterThan(nearFace - 0.2f),
                "обзор обязан доходить до стены: иначе тест не отличает обрезку от пустого полигона");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenNothingBlocks_RevealsFullRadius()
        {
            var (overlay, _) = CreateScene(withWall: false);

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            float farthest = 0f;

            foreach (Vector2 point in overlay.LastPolygonPoints)
            {
                farthest = Mathf.Max(farthest, point.magnitude);
            }

            Assert.That(farthest, Is.EqualTo(VisionRadius).Within(0.15f),
                "без препятствий обзор обязан доходить до полного радиуса");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenSourceIsSilent_BuildsNoPolygon()
        {
            var (overlay, source) = CreateScene(withWall: false);
            source.Radius = 0f;

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            Assert.That(overlay.ActiveSourceCount, Is.EqualTo(0),
                "источник с нулевым радиусом не должен попадать в обзор");
            Assert.That(overlay.LastPolygonPoints, Is.Empty);
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenWallHasOpening_VisionPassesThroughIt()
        {
            // Окно и дверь ведут себя одинаково: проём пропускает обзор,
            // стена рядом с ним — нет. Никакой отдельной логики у окна нет.
            var (overlay, _) = CreateSceneWithOpening();

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            Assert.That(overlay.LastPolygonPoints.Count, Is.GreaterThan(0));

            bool passedThrough = false;

            foreach (Vector2 point in overlay.LastPolygonPoints)
            {
                if (point.x <= WallX)
                {
                    continue;
                }

                // Проверять надо не конечную точку луча, а место, где он
                // пересёк плоскость стены. Луч, прошедший через проём у самого
                // его края, уходит дальше вбок, и его конец оказывается далеко
                // за пределами проёма — по конечной точке вывод сделать нельзя.
                // Источник стоит в начале координат, поэтому параметр простой.
                float yAtWall = point.y * (WallX / point.x);

                if (Mathf.Abs(yAtWall) <= 1.05f)
                {
                    passedThrough = true;
                    continue;
                }

                Assert.Fail("обзор прошёл сквозь стену мимо проёма: точка " + point
                    + " пересекает стену при y=" + yAtWall.ToString("F2"));
            }

            Assert.That(passedThrough, Is.True,
                "через проём обзор обязан проходить наружу, как через открытую дверь");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenOriginSitsInsideAnOccluder_StillSeesPastIt()
        {
            // Луч, стартовавший внутри коллайдера, обязан продолжить путь,
            // а не погаснуть на нём. Так бывает, когда источник обзора
            // прижимают к стене: попадание в начале луча — это не
            // препятствие, а место старта.
            var (overlay, _) = CreateSceneWithColliderAtOrigin();

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            float farthest = 0f;
            foreach (Vector2 point in overlay.LastPolygonPoints)
            {
                farthest = Mathf.Max(farthest, point.magnitude);
            }

            Assert.That(farthest, Is.EqualTo(VisionRadius).Within(0.2f),
                "луч, стартовавший внутри коллайдера, обязан продолжить путь, а не остановиться");
        }

        [UnityTest]
        public IEnumerator LateUpdate_WhenOriginIsInsideAnOccluder_StillStopsAtRealObstacle()
        {
            // Обратная сторона предыдущей проверки: старт внутри коллайдера
            // не должен «проскакивать» настоящее препятствие.
            var (overlay, _) = CreateSceneWithColliderAtOrigin(realObstacleX: 3f);

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            float farthest = 0f;
            foreach (Vector2 point in overlay.LastPolygonPoints)
            {
                farthest = Mathf.Max(farthest, point.x);
            }

            Assert.That(farthest, Is.EqualTo(3f).Within(0.25f),
                "настоящее препятствие обязано остаться препятствием");
        }

        [UnityTest]
        public IEnumerator MaskCamera_IsChildOfTargetCamera_SoItCannotLagBehind()
        {
            // Регрессия на дрожание границы обзора при движении.
            //
            // Камера маски копировала позицию основной в LateUpdate, а порядок
            // LateUpdate между компонентами не определён. Когда обзор
            // отрабатывал раньше слежения за игроком, маска снималась со
            // вчерашней позиции камеры, а кадр рисовался с сегодняшней —
            // видимая область уезжала относительно мира на кадр движения.
            //
            // Дочерний узел берёт трансформ родителя в момент отрисовки,
            // поэтому рассинхрон невозможен. Тест держит именно это свойство:
            // вернуть копирование позиции в LateUpdate он не даст.
            Camera camera = CreateCamera();
            PlayerVisionSource source = CreateSource(Vector2.zero);
            CreateOverlay(camera, source);

            yield return null;
            yield return null;

            Transform mask = camera.transform.Find("VisionMaskCamera");

            Assert.That(mask, Is.Not.Null, "камера маски не создана");
            Assert.That(mask.parent, Is.EqualTo(camera.transform),
                "камера маски обязана быть дочерней основной камере");
            Assert.That(mask.localPosition, Is.EqualTo(Vector3.zero),
                "дочерняя камера обязана стоять ровно в начале координат родителя");
        }

        private GameObject SpawnBox(string name, Vector2 position, Vector2 size, int layer)
        {
            var go = Spawn(name);
            go.transform.position = position;
            go.layer = layer;
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = size;
            return go;
        }

        private (VisibilityOverlay overlay, PlayerVisionSource source) CreateScene(bool withWall)
        {
            Camera camera = CreateCamera();
            PlayerVisionSource source = CreateSource(Vector2.zero);

            if (withWall)
            {
                SpawnBox("TestWall", new Vector2(WallX, 0f),
                    new Vector2(WallHalfWidth * 2f, 40f), LayerMask.NameToLayer("Occluder"));
            }

            return (CreateOverlay(camera, source), source);
        }

        /// <summary>Стена с проёмом по центру: как окно, так и открытая дверь.</summary>
        private (VisibilityOverlay overlay, PlayerVisionSource source) CreateSceneWithOpening()
        {
            Camera camera = CreateCamera();
            PlayerVisionSource source = CreateSource(Vector2.zero);

            int occluder = LayerMask.NameToLayer("Occluder");
            SpawnBox("WallTop", new Vector2(WallX, 6f), new Vector2(0.4f, 10f), occluder);
            SpawnBox("WallBottom", new Vector2(WallX, -6f), new Vector2(0.4f, 10f), occluder);

            return (CreateOverlay(camera, source), source);
        }

        private (VisibilityOverlay overlay, PlayerVisionSource source) CreateSceneWithColliderAtOrigin(
            float realObstacleX = float.NaN)
        {
            Camera camera = CreateCamera();
            PlayerVisionSource source = CreateSource(Vector2.zero);

            int occluder = LayerMask.NameToLayer("Occluder");
            SpawnBox("OwnCollider", Vector2.zero, new Vector2(0.5f, 0.5f), occluder);

            if (!float.IsNaN(realObstacleX))
            {
                SpawnBox("RealObstacle", new Vector2(realObstacleX, 0f),
                    new Vector2(0.4f, 40f), occluder);
            }

            return (CreateOverlay(camera, source), source);
        }

        private Camera CreateCamera()
        {
            var go = Spawn("TestCamera");
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            return camera;
        }

        private PlayerVisionSource CreateSource(Vector2 position)
        {
            var go = Spawn("TestVisionSource");
            go.transform.position = position;
            var source = go.AddComponent<PlayerVisionSource>();
            source.Radius = VisionRadius;
            return source;
        }

        private VisibilityOverlay CreateOverlay(Camera camera, PlayerVisionSource source)
        {
            var go = Spawn("TestOverlay");
            var overlay = go.AddComponent<VisibilityOverlay>();
            overlay.Initialize(camera, new[] { (IVisionSource)source }, LayerMask.GetMask("Occluder"));
            return overlay;
        }

        private GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }
    }
}
