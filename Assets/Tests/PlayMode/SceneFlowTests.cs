using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Woodberry.Core;
using Woodberry.Core.Input;
using Woodberry.Core.Scenes;

namespace Woodberry.Tests.PlayMode
{
    /// <summary>
    /// Переходы между сценами — то, что нельзя проверить EditMode-тестом,
    /// потому что нужен реальный Play Mode.
    ///
    /// Класс намеренно не использует <c>UnityEditor</c>: PlayMode-сборка
    /// собирается и под player, где этого API нет. Приватные поля читаются
    /// рефлексией, а проверки — по поведению.
    /// </summary>
    public sealed class SceneFlowTests
    {
        private const float LoadTimeout = 5f;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = false;
            CleanUp();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Без этой очистки bootstrap остаётся в DontDestroyOnLoad, а сервисы
            // в реестре — и тесты других классов начинают зависеть от порядка.
            CleanUp();
        }

        private static void CleanUp()
        {
            foreach (var boot in Object.FindObjectsByType<Woodberry.Core.GameBootstrap>())
            {
                Object.DestroyImmediate(boot.gameObject);
            }

            ServiceRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator Bootstrap_RegistersServicesAndSurvivesSceneChange()
        {
            yield return LoadSceneAsync(ScenePaths.Bootstrap);
            yield return WaitForActiveScene("Menu");

            Assert.That(
                ServiceRegistry.TryGet<IInputReader>(out _),
                Is.True,
                "bootstrap обязан зарегистрировать ввод для будущих сцен");
            Assert.That(ServiceRegistry.TryGet<ISceneLoader>(out _), Is.True);

            var boot = Object.FindAnyObjectByType<Woodberry.Core.GameBootstrap>();
            Assert.That(boot, Is.Not.Null, "bootstrap должен пережить выгрузку своей сцены");
            Assert.That(
                boot.gameObject.scene.name,
                Is.EqualTo("DontDestroyOnLoad"),
                "bootstrap обязан быть персистентным, иначе сервисы умрут на первом переходе");
        }

        [UnityTest]
        public IEnumerator Menu_DoesNotEnableGameplayInput()
        {
            yield return LoadSceneAsync(ScenePaths.Bootstrap);
            yield return WaitForActiveScene("Menu");

            var boot = Object.FindAnyObjectByType<Woodberry.Core.GameBootstrap>();
            Assert.That(boot, Is.Not.Null);

            // Геймплейный ввод в меню не нужен и конфликтует с UI-вводом.
            Assert.That(
                boot.InputEnabled,
                Is.False,
                "карта Player не должна быть активна в меню: потребителя у неё нет");
        }

        [UnityTest]
        public IEnumerator Menu_PlayButton_LoadsGameScene_AndPlayerMovesFromRegistryInput()
        {
            yield return LoadSceneAsync(ScenePaths.Bootstrap);
            yield return WaitForActiveScene("Menu");

            // Подменяем сервис заглушкой: так проверяется именно факт того, что
            // игрок берёт ввод из реестра, а не то, какого он типа.
            var stub = new FakeInputReader { Move = Vector2.up };
            ServiceRegistry.Register<IInputReader>(stub);

            var button = FindPlayButton();
            Assert.That(button, Is.Not.Null, "кнопка Play обязана быть назначена в инспекторе");
            Assert.That(button.interactable, Is.True, "кнопка должна быть доступна до нажатия");

            button.onClick.Invoke();
            Assert.That(button.interactable, Is.False, "после нажатия кнопка обязана блокироваться");

            yield return WaitForActiveScene("Game");

            var player = Object.FindAnyObjectByType<Woodberry.Gameplay.Player.PlayerController>();
            Assert.That(player, Is.Not.Null, "в игровой сцене должен быть игрок");

            // Поведенческая проверка: без ручной связки в инспекторе игрок
            // обязан поехать на ввод, взятый из реестра.
            Vector3 start = player.transform.position;
            yield return new WaitForSeconds(0.3f);

            Assert.That(
                player.transform.position.z - start.z,
                Is.GreaterThan(0.01f),
                "игрок обязан двигаться на ввод из реестра");
        }

        [UnityTest]
        public IEnumerator GameBootstrap_IsSingleInstance_AfterTransitions()
        {
            yield return LoadSceneAsync(ScenePaths.Bootstrap);
            yield return WaitForActiveScene("Menu");

            var button = FindPlayButton();
            button.onClick.Invoke();
            yield return WaitForActiveScene("Game");

            var boots = Object.FindObjectsByType<Woodberry.Core.GameBootstrap>();
            Assert.That(
                boots.Length,
                Is.EqualTo(1),
                "composition root должен быть один: два экземпляра регистрируют сервисы дважды");
        }

        private static UnityEngine.UI.Button FindPlayButton()
        {
            var menu = Object.FindAnyObjectByType<Woodberry.UI.Menu.MainMenuController>();
            Assert.That(menu, Is.Not.Null, "в меню должен быть контроллер");

            var field = typeof(Woodberry.UI.Menu.MainMenuController).GetField(
                "_playButton",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            return field.GetValue(menu) as UnityEngine.UI.Button;
        }

        private static IEnumerator LoadSceneAsync(string path)
        {
            SceneManager.LoadScene(path, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Ждём появления сцены с таймаутом, а не фиксированное число кадров:
        /// загрузка идёт через корутину с отложенным кадром.
        /// </summary>
        private static IEnumerator WaitForActiveScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + LoadTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().name == name)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Сцена {name} не загрузилась за {LoadTimeout}с. " +
                $"Активна: {SceneManager.GetActiveScene().name}");
        }
    }
}
