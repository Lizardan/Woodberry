using System;
using NUnit.Framework;
using UnityEngine;
using Woodberry.Core;
using Woodberry.Core.Input;
using Woodberry.Core.Scenes;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Реестр — единственный канал доступа к глобальным сервисам, и он живёт
    /// между сценами. Значит, его поведение надо проверять: утечка состояния
    /// между прогонами ломает тесты и, что хуже, незаметно ломает игру.
    /// </summary>
    public sealed class ServiceRegistryTests
    {
        /// <summary>Заглушка загрузчика сцен: реестру всё равно, что внутри.</summary>
        private sealed class FakeSceneLoader : ISceneLoader
        {
            public SceneId Current => SceneId.Menu;

            public void Load(SceneId scene)
            {
            }
        }
        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceRegistry.Clear();
        }

        [Test]
        public void TryGet_WhenServiceNotRegistered_ReturnsFalse()
        {
            Assert.That(ServiceRegistry.TryGet<IInputReader>(out _), Is.False);
        }

        [Test]
        public void Register_ThenTryGet_ReturnsSameInstance()
        {
            var reader = new InputSystemReader(new InputSystem_Actions());

            ServiceRegistry.Register<IInputReader>(reader);

            Assert.That(ServiceRegistry.TryGet<IInputReader>(out var found), Is.True);
            Assert.That(found, Is.SameAs(reader));
        }

        [Test]
        public void Register_TwoServices_KeepsBothIndependently()
        {
            var reader = new InputSystemReader(new InputSystem_Actions());
            var loader = new FakeSceneLoader();

            ServiceRegistry.Register<IInputReader>(reader);
            ServiceRegistry.Register<ISceneLoader>(loader);

            ServiceRegistry.TryGet<IInputReader>(out var foundReader);
            ServiceRegistry.TryGet<ISceneLoader>(out var foundLoader);

            Assert.That(foundReader, Is.SameAs(reader));
            Assert.That(foundLoader, Is.SameAs(loader));
        }

        [Test]
        public void Register_SameTypeTwice_LastWins()
        {
            var first = new InputSystemReader(new InputSystem_Actions());
            var second = new InputSystemReader(new InputSystem_Actions());

            ServiceRegistry.Register<IInputReader>(first);
            ServiceRegistry.Register<IInputReader>(second);

            ServiceRegistry.TryGet<IInputReader>(out var found);
            Assert.That(found, Is.SameAs(second));
            Assert.That(ServiceRegistry.Count, Is.EqualTo(1));
        }

        [Test]
        public void Unregister_ThenTryGet_ReturnsFalse()
        {
            var reader = new InputSystemReader(new InputSystem_Actions());
            ServiceRegistry.Register<IInputReader>(reader);

            // Тип аргумента обязателен: вывод T из значения искал бы ключ
            // реализации вместо ключа интерфейса.
            ServiceRegistry.Unregister<IInputReader>();

            Assert.That(ServiceRegistry.TryGet<IInputReader>(out _), Is.False);
        }

        [Test]
        public void Unregister_UnknownService_LeavesOthersIntact()
        {
            var reader = new InputSystemReader(new InputSystem_Actions());
            ServiceRegistry.Register<IInputReader>(reader);
            ServiceRegistry.Register<ISceneLoader>(new FakeSceneLoader());

            ServiceRegistry.Unregister<ISceneLoader>();

            ServiceRegistry.TryGet<IInputReader>(out var found);
            Assert.That(found, Is.SameAs(reader), "снятие одного сервиса не должно трогать другие");
        }

        [Test]
        public void TryGetId_ForEverySceneName_ReturnsMatchingId()
        {
            foreach (SceneId id in Enum.GetValues(typeof(SceneId)))
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(ScenePaths.For(id));

                Assert.That(ScenePaths.TryGetId(sceneName, out SceneId found), Is.True, sceneName);
                Assert.That(found, Is.EqualTo(id));
            }
        }

        [Test]
        public void TryGetId_ForUnknownSceneName_ReturnsFalse()
        {
            Assert.That(ScenePaths.TryGetId("NotAScene", out _), Is.False);
        }

        [Test]
        public void Register_WhenServiceIsNull_Throws()
        {
            Assert.That(
                () => ServiceRegistry.Register<IInputReader>(null),
                Throws.ArgumentNullException);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            ServiceRegistry.Register<IInputReader>(new InputSystemReader(new InputSystem_Actions()));
            ServiceRegistry.Register<ISceneLoader>(new FakeSceneLoader());
            Assert.That(ServiceRegistry.Count, Is.EqualTo(2));

            ServiceRegistry.Clear();

            Assert.That(ServiceRegistry.Count, Is.EqualTo(0));
            Assert.That(ServiceRegistry.TryGet<IInputReader>(out _), Is.False);
            Assert.That(ServiceRegistry.TryGet<ISceneLoader>(out _), Is.False);
        }

        [Test]
        public void ScenePaths_ForEverySceneId_ReturnsPathInsideScenesFolder()
        {
            foreach (SceneId id in System.Enum.GetValues(typeof(SceneId)))
            {
                string path = ScenePaths.For(id);

                Assert.That(path, Does.StartWith("Assets/Woodberry/Scenes/"), id.ToString());
                Assert.That(path, Does.EndWith(".unity"), id.ToString());
            }
        }

        [Test]
        public void ScenePaths_ForUnknownSceneId_Throws()
        {
            Assert.That(
                () => ScenePaths.For((SceneId)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
