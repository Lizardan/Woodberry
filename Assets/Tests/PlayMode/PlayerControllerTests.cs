using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using Woodberry.Core.Input;
using Woodberry.Gameplay.Player;

namespace Woodberry.Tests.PlayMode
{
    /// <summary>
    /// Заглушка ввода: даёт тесту задать вектор и кнопки без участия
    /// реального Input System, чтобы PlayMode-тест не зависел от устройств.
    /// </summary>
    internal sealed class FakeInputReader : IInputReader
    {
        public Vector2 Move { get; set; }
        public bool SprintHeld { get; set; }
        public bool InteractPressed { get; set; }

        public void EnableGameplayInput()
        {
        }

        public void DisableGameplayInput()
        {
        }
    }

    /// <summary>
    /// PlayMode-тесты контроллера: объект живёт в реальном игровом цикле,
    /// поэтому проверяем инициализацию, движение и корректное отключение.
    /// </summary>
    public sealed class PlayerControllerTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private PlayerController CreatePlayer(string name)
        {
            var go = new GameObject(name);

            // PlayerController требует Rigidbody2D и CircleCollider2D сам.
            // Явный AddComponent здесь не нужен и был бы дублем RequireComponent.
            PlayerController controller = go.AddComponent<PlayerController>();
            _spawned.Add(go);
            return controller;
        }

        [SetUp]
        public void SetUp()
        {
            // Два утверждения об изоляции, а не одно:
            // ignoreFailingMessages — глобальный статический флаг, он утекает
            // в остальные тесты прогона и отключает проверку логов у всех.
            // Реестр — тоже глобальное состояние: если SceneFlowTests отработал
            // раньше и оставил сервис, тест «без ввода» перестал бы быть таким.
            LogAssert.ignoreFailingMessages = false;
            Woodberry.Core.ServiceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator PlayerController_WithInput_MovesAlongXY()
        {
            PlayerController controller = CreatePlayer("MovingPlayer");
            controller.Initialize(new FakeInputReader { Move = Vector2.up });

            Vector3 start = controller.transform.position;

            // Движение теперь в FixedUpdate, поэтому ждём физический тик,
            // а не условные полсекунды кадрового времени.
            yield return WaitForPhysicsSteps(20);

            Vector3 delta = controller.transform.position - start;

            Assert.That(delta.x, Is.EqualTo(0f).Within(1e-3f), "движение строго в плоскости XY");
            Assert.That(delta.y, Is.GreaterThan(0.01f), "игрок должен двигаться вверх по экрану");
        }

        [UnityTest]
        public IEnumerator PlayerController_WithNoInput_StaysPut()
        {
            PlayerController controller = CreatePlayer("IdlePlayer");
            controller.Initialize(new FakeInputReader());

            Vector3 start = controller.transform.position;
            yield return WaitForPhysicsSteps(10);

            Assert.That(controller.transform.position.x, Is.EqualTo(start.x).Within(1e-3f));
            Assert.That(controller.transform.position.y, Is.EqualTo(start.y).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator PlayerController_RigidBody_IsKinematicWithNoGravity()
        {
            PlayerController controller = CreatePlayer("BodyConfigPlayer");
            yield return null;

            Rigidbody2D body = controller.GetComponent<Rigidbody2D>();

            Assert.That(body, Is.Not.Null, "контроллер требует Rigidbody2D");
            Assert.That(body.bodyType, Is.EqualTo(RigidbodyType2D.Kinematic),
                "динамическое тело даёт дребезг и рассинхрон в кооперативе");
            Assert.That(body.gravityScale, Is.Zero, "иначе игрок провалится вниз");
        }

        [UnityTest]
        public IEnumerator PlayerController_WhenDisabled_ThenUpdateStopsMovingIt()
        {
            PlayerController controller = CreatePlayer("DisabledPlayer");
            controller.Initialize(new FakeInputReader { Move = Vector2.right });

            yield return WaitForPhysicsSteps(5);
            controller.gameObject.SetActive(false);

            Vector3 parked = controller.transform.position;
            yield return WaitForPhysicsSteps(10);

            Assert.That(
                controller.transform.position.x,
                Is.EqualTo(parked.x).Within(1e-3f),
                "отключённый контроллер не должен двигать объект");
        }

        [UnityTest]
        public IEnumerator PlayerController_WithoutInput_LogsWarningAndStaysPut()
        {
            // Ожидаем ровно одно предупреждение о неназначенном вводе.
            // Раньше тест глушил LogAssert и потому не мог ничего обнаружить.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("IInputReader"));
            LogAssert.ignoreFailingMessages = false;

            PlayerController controller = CreatePlayer("NoInputPlayer");
            yield return WaitForPhysicsSteps(5);

            Assert.That(
                controller.transform.position,
                Is.EqualTo(Vector3.zero).Using(Vector3ComparerWithEqualsOperator.Instance),
                "без ввода игрок не должен двигаться");

            // Если ввод не подставился и Предупреждения нет, тест падает здесь.
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PlayerController_ResolvesInputFromServiceRegistry_WhenNotInitialized()
        {
            // Путь, которым реально пользуется игровая сцена: Initialize не
            // вызывали, ввод приходит из реестра. Раньше здесь создавался
            // GameBootstrap, но он уводит приложение в меню и выгружает
            // сцену — в тесте это ломало прогон.
            var reader = new Woodberry.Core.Input.InputSystemReader(
                new Woodberry.Core.Input.InputSystem_Actions());
            Woodberry.Core.ServiceRegistry.Register<Woodberry.Core.Input.IInputReader>(reader);

            try
            {
                PlayerController controller = CreatePlayer("RegistryDrivenPlayer");
                yield return null;

                var field = typeof(PlayerController).GetField(
                    "_input",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.That(
                    field.GetValue(controller),
                    Is.SameAs(reader),
                    "без явного Initialize контроллер обязан взять ввод из реестра");
            }
            finally
            {
                Woodberry.Core.ServiceRegistry.Unregister<Woodberry.Core.Input.IInputReader>();
            }
        }

/// <summary>
        /// Ждёт заданное число физических тиков. Движение живёт в FixedUpdate,
        /// поэтому ждать по кадрам бессмысленно: на быстром железе успеет
        /// пройти больше тиков, чем на медленном, и тест станет плавающим.
        /// </summary>
        private static IEnumerator WaitForPhysicsSteps(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// Поворот спрайта по направлению движения: без него персонаж всегда
        /// смотрит в одну сторону и шагает боком.
        ///
        /// Берёт префаб, а не пустой объект: поворот живёт на узле
        /// <c>Visual</c> внутри рига, и на голом <c>GameObject</c> его нет.
        /// </summary>
        [UnityTest]
        public IEnumerator Facing_ChangesDirection_AcrossAllFourFacings()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Woodberry/Prefabs/Player.prefab");
            Assert.That(prefab, Is.Not.Null, "Player.prefab не найден");

            GameObject go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            _spawned.Add(go);

            PlayerController controller = go.GetComponent<PlayerController>();
            var input = new FakeInputReader();
            controller.Initialize(input);

            var visual = controller.transform.Find("Visual");
            Assert.That(visual, Is.Not.Null, "узел Visual обязателен: он разворачивает спрайт");

            var seen = new System.Collections.Generic.List<float>();
            foreach (Vector2 direction in new[]
                     {
                         new Vector2(0f, -1f),   // вниз
                         new Vector2(1f, 0f),    // вправо
                         new Vector2(0f, 1f),    // вверх
                         new Vector2(-1f, 0f)    // влево
                     })
            {
                input.Move = direction;
                yield return WaitForPhysicsSteps(3);
                seen.Add(visual.localEulerAngles.z);
            }

            Assert.That(seen[0], Is.EqualTo(0f).Within(1f), "вниз — без поворота");
            Assert.That(seen[2], Is.EqualTo(180f).Within(1f), "вверх — разворот на 180");

            // Лево и право должны различаться: одинаковый угол означал бы либо
            // зеркальную ошибку, либо что поворот не применяется вовсе.
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(seen[1], seen[3])),
                Is.GreaterThan(1f),
                "влево и вправо должны давать разный поворот");
        }
    }
}
