using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Woodberry.Core;
using Woodberry.Core.Bootstrap;

namespace Woodberry.UI.Bootstrap
{
    /// <summary>
    /// Экран стартовой загрузки: показывает, что происходит, и сколько.
    ///
    /// Презентация и ничего больше. Core сам решает, когда инициализация
    /// закончена и пора уходить в меню, — этот компонент не вызывает загрузку
    /// сцены и не влияет на её момент.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartupLoadingView : MonoBehaviour
    {
        [Tooltip("Строка состояния: «Подготовка сервисов».")]
        [SerializeField]
        private Text _statusLabel;

        [Tooltip("Полоса прогресса. Image должен быть в режиме Filled.")]
        [SerializeField]
        private Image _progressBar;

        [Tooltip("Процент. Можно оставить пустым, если не нужен.")]
        [SerializeField]
        private Text _percentLabel;

        private IStartupProgress _progress;

        // Кэш последней отрисованной строки: пересборка строки каждый кадр —
        // это мусор в GC, а прогресс меняется медленно.
        private string _cachedStatus;
        private string _cachedPercent;

        private void Start()
        {
            if (!ServiceRegistry.TryGet<IStartupProgress>(out _progress))
            {
                Debug.LogError(
                    $"{nameof(StartupLoadingView)}: IStartupProgress не зарегистрирован. " +
                    "Сцена открыта не через Bootstrap.", this);
                return;
            }

            _progress.Changed += HandleProgressChanged;
            HandleProgressChanged();
        }

        private void OnDisable()
        {
            if (_progress != null)
            {
                _progress.Changed -= HandleProgressChanged;
            }
        }

        private void HandleProgressChanged()
        {
            if (_progress == null)
            {
                return;
            }

            if (_statusLabel != null && _cachedStatus != _progress.CurrentStep)
            {
                _cachedStatus = _progress.CurrentStep;
                _statusLabel.text = _cachedStatus;
            }

            if (_progressBar != null)
            {
                _progressBar.fillAmount = _progress.Normalized;
            }

            if (_percentLabel != null)
            {
                int percent = Mathf.RoundToInt(_progress.Normalized * 100f);
                string text = percent.ToString(CultureInfo.InvariantCulture) + "%";

                if (_cachedPercent != text)
                {
                    _cachedPercent = text;
                    _percentLabel.text = text;
                }
            }
        }
    }
}
