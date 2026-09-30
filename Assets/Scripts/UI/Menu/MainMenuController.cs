using UnityEngine;
using UnityEngine.UI;
using Woodberry.Core;
using Woodberry.Core.Scenes;

namespace Woodberry.UI.Menu
{
    /// <summary>
    /// Презентация главного меню: читает состояние, пишет намерение.
    /// Никакой игровой логики — кнопка Play только сообщает загрузчику сцен,
    /// куда идти. Сам <c>SceneManager</c> UI не знает.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Tooltip("Кнопка Play. Назначается в инспекторе, чтобы не искать в иерархии.")]
        [SerializeField]
        private Button _playButton;

        private ISceneLoader _sceneLoader;

        private void Start()
        {
            if (!ServiceRegistry.TryGet<ISceneLoader>(out _sceneLoader))
            {
                Debug.LogError(
                    $"{nameof(MainMenuController)}: ISceneLoader не зарегистрирован. " +
                    "Сцена открыта не через Bootstrap — переход в игру невозможен.",
                    this);
                DisableControls();
            }
        }

        private void OnEnable()
        {
            if (_playButton != null)
            {
                _playButton.onClick.AddListener(HandlePlayClicked);
            }
        }

        private void OnDisable()
        {
            // Отписка здесь, а не в OnDestroy, по правилу парности. Сцена
            // выгружается в середине обработки клика, поэтому подписка должна
            // сниматься до разрушения объекта.
            if (_playButton != null)
            {
                _playButton.onClick.RemoveListener(HandlePlayClicked);
            }
        }

        private void HandlePlayClicked()
        {
            if (_sceneLoader == null)
            {
                return;
            }

            // Гасим повторные клики: пока сцена грузится, второй клик запустил бы
            // вторую загрузку поверх первой.
            DisableControls();
            _sceneLoader.Load(SceneId.Game);
        }

        private void DisableControls()
        {
            if (_playButton != null)
            {
                _playButton.interactable = false;
            }
        }
    }
}
