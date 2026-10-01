using System;
using System.Collections;
using Woodberry.Core.Input;
using Woodberry.Core.Scenes;

namespace Woodberry.Core.Bootstrap
{
    /// <summary>
    /// Создаёт и регистрирует глобальные сервисы.
    ///
    /// Вынесен в шаг, а не сделан прямо в <c>Awake</c>, чтобы инициализация
    /// шла одним наблюдаемым потоком, а экран загрузки показывал реальную
    /// последовательность, а не только факт «что-то произошло».
    ///
    /// Сюда же попадёт сетевая сессия и сейв, когда они появятся.
    /// </summary>
    public sealed class RegisterServicesStep : IStartupStep
    {
        private readonly UnityEngine.MonoBehaviour _coroutineHost;

        public RegisterServicesStep(UnityEngine.MonoBehaviour coroutineHost)
        {
            _coroutineHost = coroutineHost;
        }

        public string DisplayName => "Подготовка сервисов";

        public IEnumerator Run(Action<float> reportProgress)
        {
            reportProgress(0.3f);
            yield return null;

            // Сгенерированная обёртка Input System самодостаточна: в неё встроен
            // JSON карт, поэтому ассет в сцене назначать не нужно.
            var reader = new InputSystemReader(new InputSystem_Actions());
            var loader = new UnitySceneLoader(_coroutineHost);

            reportProgress(0.7f);
            yield return null;

            ServiceRegistry.Register<IInputReader>(reader);
            ServiceRegistry.Register<ISceneLoader>(loader);

            reportProgress(1f);
        }
    }
}
