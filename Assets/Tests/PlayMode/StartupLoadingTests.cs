using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Woodberry.Core;
using Woodberry.Core.Bootstrap;
using Woodberry.Core.Input;
using Woodberry.Core.Scenes;

namespace Woodberry.Tests.PlayMode
{
    /// <summary>
    /// Экран загрузки в сцене Bootstrap: пайплайн старта обязан доходить до
    /// конца, показывать прогресс и только потом уводить в меню. Проверяется
    /// здесь, потому что в Editor-режиме сцена не грузится.
    /// </summary>
    public sealed class StartupLoadingTests
    {
        private const float LoadTimeout = 5f;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = false;

            foreach (var boot in Object.FindObjectsByType<Woodberry.Core.GameBootstrap>())
            {
                Object.DestroyImmediate(boot.gameObject);
            }

            ServiceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            foreach (var boot in Object.FindObjectsByType<Woodberry.Core.GameBootstrap>())
            {
                Object.DestroyImmediate(boot.gameObject);
            }

            ServiceRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator BootstrapScene_StartupProgress_IsRegisteredBeforeAnySceneChange()
        {
            SceneManagerLoad(ScenePaths.Bootstrap);
            yield return null;

            Assert.That(
                ServiceRegistry.TryGet<IStartupProgress>(out IStartupProgress progress),
                Is.True,
                "экран загрузки читает прогресс в своём Start, значит сервис нужен сразу");

            Assert.That(progress.IsComplete, Is.False, "на старте инициализация ещё не закончена");
        }

        [UnityTest]
        public IEnumerator BootstrapScene_LoadingScreen_HasProgressAndStatusBeforeMenu()
        {
            SceneManagerLoad(ScenePaths.Bootstrap);
            yield return null;

            // Экран загрузки живёт в сцене Bootstrap и обязан быть на месте,
            // иначе прогресс некуда выводить.
            var view = Object.FindAnyObjectByType<Woodberry.UI.Bootstrap.StartupLoadingView>();
            Assert.That(view, Is.Not.Null, "в сцене Bootstrap должен быть экран загрузки");

            // Приватные поля читаются рефлексией, а не SerializedObject:
            // PlayMode-сборка обязана компилироваться и под player, где
            // UnityEditor недоступен. Класс это прямо запрещает в докстринге.
            Assert.That(ReadPrivate(view, "_statusLabel"), Is.Not.Null,
                "строка статуса обязана быть назначена");
            Assert.That(ReadPrivate(view, "_progressBar"), Is.Not.Null,
                "полоса прогресса обязана быть назначена");
        }

        private static object ReadPrivate(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"поле {fieldName} не найдено");
            return field.GetValue(target);
        }

        [UnityTest]
        public IEnumerator BootstrapScene_Initialization_CompletesThenGoesToMenu()
        {
            SceneManagerLoad(ScenePaths.Bootstrap);

            // Экран загрузки не должен пропускать шаги: ждём завершения
            // инициализации, а не просто смены сцены.
            yield return WaitFor(() =>
                ServiceRegistry.TryGet<IStartupProgress>(out IStartupProgress p) && p.IsComplete,
                "инициализация должна завершиться");

            Assert.That(
                ServiceRegistry.TryGet<IStartupProgress>(out IStartupProgress done),
                Is.True);
            Assert.That(done.Normalized, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(done.CurrentStep, Is.EqualTo("Готово"));

            // Сервисы обязаны появиться именно как следствие шагов.
            Assert.That(ServiceRegistry.TryGet<IInputReader>(out _), Is.True);
            Assert.That(ServiceRegistry.TryGet<ISceneLoader>(out _), Is.True);

            yield return WaitForActiveScene("Menu");
        }

        [UnityTest]
        public IEnumerator StartupRunner_MonotonicProgress_EndsAtOne()
        {
            var progress = new StartupProgress();
            var observed = new List<float>();
            progress.Changed += () => observed.Add(progress.Normalized);

            var runner = new StartupRunner(new IStartupStep[]
            {
                new FixedStep("Первый", new[] { 0f, 0.5f, 1f }),
                new FixedStep("Второй", new[] { 0.5f, 1f })
            });

            yield return runner.Run(progress);

            Assert.That(progress.IsComplete, Is.True);
            Assert.That(progress.Normalized, Is.EqualTo(1f).Within(1e-4f));

            for (int i = 1; i < observed.Count; i++)
            {
                Assert.That(
                    observed[i],
                    Is.GreaterThanOrEqualTo(observed[i - 1]),
                    "прогресс обязан идти только вперёд, иначе полоса дёргается назад");
            }
        }

        [UnityTest]
        public IEnumerator StartupRunner_WithNoSteps_CompletesImmediately()
        {
            var progress = new StartupProgress();

            yield return new StartupRunner(new IStartupStep[0]).Run(progress);

            Assert.That(progress.IsComplete, Is.True);
            Assert.That(progress.Normalized, Is.EqualTo(1f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator StartupRunner_StepName_VisibleToThePlayer()
        {
            var progress = new StartupProgress();
            var names = new List<string>();
            progress.Changed += () =>
            {
                if (names.Count == 0 || names[names.Count - 1] != progress.CurrentStep)
                {
                    names.Add(progress.CurrentStep);
                }
            };

            yield return new StartupRunner(new IStartupStep[]
            {
                new FixedStep("Подключение к сессии", new[] { 1f }),
                new FixedStep("Загрузка уровня", new[] { 1f })
            }).Run(progress);

            Assert.That(names, Does.Contain("Подключение к сессии"));
            Assert.That(names, Does.Contain("Загрузка уровня"));
            Assert.That(names[names.Count - 1], Is.EqualTo("Готово"));
        }

        /// <summary>Шаг-заглушка: отдаёт заранее заданные значения прогресса.</summary>
        private sealed class FixedStep : IStartupStep
        {
            private readonly float[] _values;

            public FixedStep(string displayName, float[] values)
            {
                DisplayName = displayName;
                _values = values;
            }

            public string DisplayName { get; }

            public IEnumerator Run(System.Action<float> reportProgress)
            {
                foreach (float value in _values)
                {
                    reportProgress(value);
                    yield return null;
                }
            }
        }

        private static void SceneManagerLoad(string path)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                path, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + LoadTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(message + " (таймаут " + LoadTimeout + "с)");
        }

        private static IEnumerator WaitForActiveScene(string name)
        {
            yield return WaitFor(
                () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == name,
                $"сцена {name} не загрузилась");
        }
    }
}
