using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Woodberry.Core.Bootstrap;
using Woodberry.Core.Input;
using Woodberry.Core.Scenes;

namespace Woodberry.Core
{
    /// <summary>
    /// Composition root. Единственное место, где создаются и регистрируются
    /// глобальные сервисы.
    ///
    /// Живёт в сцене <c>Bootstrap.unity</c>, первой в Build Settings, и делает
    /// <c>DontDestroyOnLoad</c> — поэтому переживает переходы Menu → Game.
    /// Объекты других сцен получают сервисы из <see cref="ServiceRegistry"/>:
    /// сериализованная ссылка на объект другой сцены Unity не сохраняет.
    ///
    /// Последовательность: собирает сервисы шагами стартовой инициализации,
    /// показывает прогресс, и только когда всё готово — уходит в меню.
    /// Переход раньше готовности означал бы запуск игры на недособранных
    /// зависимостях, и отказ был бы отложенным и невнятным.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>
        /// Защита от второго composition root. Сцена Bootstrap обязана грузиться
        /// ровно один раз; если её загрузили повторно, лишний экземпляр
        /// уничтожает себя, иначе получим две регистрации сервисов и две
        /// попытки уйти в меню. Это единственное статическое состояние в проекте,
        /// и оно не хранит сервисы — только факт первичности.
        /// </summary>
        private static bool s_isPrimary;

        /// <summary>
        /// Сбрасывает статик перед стартом подсистем.
        ///
        /// В проекте отключён и domain reload, и scene reload
        /// (`m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 3`), то
        /// есть между запусками Play Mode статические поля живут. Без этого
        /// сброса второй запуск увидел бы <c>s_isPrimary == true</c>, убил бы
        /// единственный composition root, и приложение стартовало бы без
        /// сервисов и без перехода в меню. Отказ выглядел бы как «ничего не
        /// работает», и найти его было бы трудно.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_isPrimary = false;
        }

        private StartupProgress _progress;
        private IInputReader _reader;
        private bool _inputEnabled;
        private bool _isPrimaryInstance;

        /// <summary>Сервис ввода, готовый к выдаче геймплею.</summary>
        public IInputReader Input => _reader;

        /// <summary>
        /// Активна ли геймплейная карта ввода. В меню она выключена: потребителя
        /// у неё нет, и она конфликтует с UI-вводом.
        /// </summary>
        public bool InputEnabled => _inputEnabled;

        /// <summary>Сцена, в которую bootstrap уводит приложение на старте.</summary>
        [SerializeField]
        private SceneId _startScene = SceneId.Menu;

        private void Awake()
        {
            if (s_isPrimary)
            {
                Debug.LogError(
                    $"{nameof(GameBootstrap)}: экземпляр уже существует. " +
                    "Сцена Bootstrap обязана загружаться один раз. Лишний экземпляр удалён.",
                    this);
                Destroy(gameObject);
                return;
            }

            s_isPrimary = true;
            _isPrimaryInstance = true;

            DontDestroyOnLoad(gameObject);

            // Регистрируем до шагов: экран загрузки читает прогресс в своём Start,
            // а порядок Start между объектами сцены не гарантирован.
            _progress = new StartupProgress();
            ServiceRegistry.Register<IStartupProgress>(_progress);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            if (_isPrimaryInstance)
            {
                StartCoroutine(RunStartup());
            }
        }

        private void OnDisable()
        {
            if (_isPrimaryInstance)
            {
                DisableInput();
            }
        }

        private void OnDestroy()
        {
            if (!_isPrimaryInstance)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            DisableInput();

            // Тип-ориентированный Unregister: единственный composition root
            // гарантирован защитой от второго экземпляра в Awake.
            ServiceRegistry.Unregister<IInputReader>();
            ServiceRegistry.Unregister<ISceneLoader>();
            ServiceRegistry.Unregister<IStartupProgress>();

            _isPrimaryInstance = false;
            s_isPrimary = false;
        }

        private IEnumerator RunStartup()
        {
            var runner = new StartupRunner(BuildSteps());
            yield return runner.Run(_progress);

            if (!ServiceRegistry.TryGet<ISceneLoader>(out ISceneLoader loader))
            {
                Debug.LogError(
                    $"{nameof(GameBootstrap)}: загрузчик сцен не зарегистрирован после " +
                    "инициализации. Приложение не может продолжить.", this);
                yield break;
            }

            loader.Load(_startScene);
        }

        /// <summary>
        /// Шаги стартовой инициализации. Пока реальная работа только одна —
        /// это честно: придумывать искусственные задержки ради красивой полосы
        /// нельзя, иначе она будет врать игроку. По мере появления загрузки
        /// ассетов, сессии и сейва шаги добавляются здесь.
        /// </summary>
        private IReadOnlyList<IStartupStep> BuildSteps()
        {
            return new IStartupStep[]
            {
                new RegisterServicesStep(this)
            };
        }

        /// <summary>
        /// Геймплейный ввод включается только в игровой сцене. В меню карта
        /// <c>Player</c> активна не должна: потребителя у неё нет, и она
        /// конфликтует с UI-вводом. ADR 0005 требует разделять геймплейные
        /// и UI-действия.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!ScenePaths.TryGetId(scene.name, out SceneId id))
            {
                return;
            }

            if (id != SceneId.Game)
            {
                DisableInput();
                return;
            }

            // Тип указывается явно намеренно: вывод T из поля `_reader` дал бы
            // InputSystemReader вместо IInputReader, и поиск шёл бы по ключу
            // реализации, которого в реестре нет. Сервисы регистрируются
            // по интерфейсу — значит и ищутся по интерфейсу.
            if (!ServiceRegistry.TryGet<IInputReader>(out _reader))
            {
                Debug.LogError(
                    $"{nameof(GameBootstrap)}: IInputReader не зарегистрирован, " +
                    "в игровой сцене игрок останется без ввода.", this);
                return;
            }

            EnableInput();
        }

        private void EnableInput()
        {
            if (_reader == null || _inputEnabled)
            {
                return;
            }

            _reader.EnableGameplayInput();
            _inputEnabled = true;
        }

        private void DisableInput()
        {
            if (_reader == null || !_inputEnabled)
            {
                return;
            }

            _reader.DisableGameplayInput();
            _inputEnabled = false;
        }
    }
}
