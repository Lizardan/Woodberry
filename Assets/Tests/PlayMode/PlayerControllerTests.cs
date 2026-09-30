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
            go.AddComponent<CharacterController>();
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
        public IEnumerator PlayerController_WithInput_MovesAlongXZ()
        {
            PlayerController controller = CreatePlayer("MovingPlayer");
            controller.Initialize(new FakeInputReader { Move = Vector2.up });

            Vector3 start = controller.transform.position;
            yield return new WaitForSeconds(0.5f);
            Vector3 delta = controller.transform.position - start;

            Assert.That(delta.y, Is.EqualTo(0f).Within(1e-3f), "движение строго в XZ");
            Assert.That(delta.z, Is.GreaterThan(0.01f), "игрок должен двигаться вперёд");
        }

        [UnityTest]
        public IEnumerator PlayerController_WithNoInput_StaysPut()
        {
            PlayerController controller = CreatePlayer("IdlePlayer");
            controller.Initialize(new FakeInputReader());

            Vector3 start = controller.transform.position;
            yield return new WaitForSeconds(0.3f);

            Assert.That(controller.transform.position.x, Is.EqualTo(start.x).Within(1e-3f));
            Assert.That(controller.transform.position.z, Is.EqualTo(start.z).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator PlayerController_WhenDisabled_ThenUpdateStopsMovingIt()
        {
            PlayerController controller = CreatePlayer("DisabledPlayer");
            controller.Initialize(new FakeInputReader { Move = Vector2.right });

            yield return new WaitForSeconds(0.2f);
            controller.gameObject.SetActive(false);

            Vector3 parked = controller.transform.position;
            yield return new WaitForSeconds(0.3f);

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
            yield return new WaitForSeconds(0.2f);

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
    }
}
