using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Woodberry.Core.Scenes
{
    /// <summary>
    /// Реализация поверх <see cref="SceneManager"/>. Единственный класс в
    /// проекте, который знает про загрузку сцен.
    ///
    /// Сам не <c>MonoBehaviour</c>: экземпляр создаёт composition root, а
    /// корутину крутит переданный хост.
    /// </summary>
    public sealed class UnitySceneLoader : ISceneLoader
    {
        private readonly MonoBehaviour _coroutineHost;

        public UnitySceneLoader(MonoBehaviour coroutineHost)
        {
            _coroutineHost = coroutineHost != null
                ? coroutineHost
                : throw new ArgumentNullException(nameof(coroutineHost));
        }

        /// <summary>
        /// Сцена, которая сейчас активна. Присваивается после того, как загрузка
        /// действительно завершилась: если сцены нет в Build Settings, значение
        /// остаётся прежним, и это правда.
        /// </summary>
        public SceneId Current { get; private set; } = SceneId.Bootstrap;

        public void Load(SceneId scene)
        {
            if (_coroutineHost == null)
            {
                Debug.LogError(
                    $"{nameof(UnitySceneLoader)}: хост корутины уничтожен, " +
                    $"переход в {scene} невозможен.");
                return;
            }

            _coroutineHost.StartCoroutine(LoadRoutine(scene));
        }

        private IEnumerator LoadRoutine(SceneId scene)
        {
            // Выгрузка отложена на кадр, чтобы текущий ввод (например, клик по
            // кнопке Play) успел обработаться до того, как его объекты исчезнут.
            // Иначе кнопка продолжает обрабатывать событие уже в выгруженной сцене.
            yield return null;

            // LoadScene синхронный: когда он вернулся, сцена уже активна.
            SceneManager.LoadScene(ScenePaths.For(scene), LoadSceneMode.Single);
            Current = scene;
        }
    }
}
