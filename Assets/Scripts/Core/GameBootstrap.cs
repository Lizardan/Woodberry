using UnityEngine;
using UnityEngine.SceneManagement;
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
    /// Здесь же будет жить всё, что грузится на старте: сессия, сейв,
    /// загрузчики уровней. Пока их нет — реестр содержит только ввод и загрузчик сцен.
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

        private ISceneLoader _sceneLoader;
        private InputSystemReader _reader;
        private bool _inputEnabled;
        private bool _isPrimaryInstance;

        /// <summary>Загрузчик сцен. Зарегистрирован для меню и геймплея.</summary>
        public ISceneLoader SceneLoader => _sceneLoader;

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

            // Сгенерированная обёртка самодостаточна: в неё встроен JSON карт,
            // поэтому ассет в сцене назначать не нужно.
            _reader = new InputSystemReader(new InputSystem_Actions());
            _sceneLoader = new UnitySceneLoader(this);

            ServiceRegistry.Register<IInputReader>(_reader);
            ServiceRegistry.Register<ISceneLoader>(_sceneLoader);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnEnable()
        {
            if (_isPrimaryInstance)
            {
                EnableInput();
            }
        }

        private void Start()
        {
            if (_isPrimaryInstance)
            {
                // Уходим из Bootstrap-сцены сразу: она нужна только чтобы собрать
                // сервисы, а её объекты уже переехали в DontDestroyOnLoad.
                _sceneLoader.Load(_startScene);
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

            _isPrimaryInstance = false;
            s_isPrimary = false;
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

            if (id == SceneId.Game)
            {
                EnableInput();
            }
            else
            {
                DisableInput();
            }
        }

        private void EnableInput()
        {
            if (_reader == null || _inputEnabled)
            {
                return;
            }

            _reader.Enable();
            _inputEnabled = true;
        }

        private void DisableInput()
        {
            if (_reader == null || !_inputEnabled)
            {
                return;
            }

            _reader.Disable();
            _inputEnabled = false;
        }
    }
}
