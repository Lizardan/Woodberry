using System;

namespace Woodberry.Core.Bootstrap
{
    /// <summary>
    /// Реализация <see cref="IStartupProgress"/>. Core не знает про UI и
    /// ничего не рисует: он только считает и сообщает.
    /// </summary>
    public sealed class StartupProgress : IStartupProgress
    {
        private const float NotifyEpsilon = 0.005f;

        private float _normalized;
        private string _currentStep = string.Empty;
        private bool _isComplete;
        private float _lastNotified;

        public float Normalized => _normalized;

        public string CurrentStep => _currentStep;

        public bool IsComplete => _isComplete;

        public event Action Changed;

        /// <summary>Смена шага: имя обновляется всегда, прогресс сбрасывается.</summary>
        public void BeginStep(string displayName, float stepStartNormalized)
        {
            _currentStep = displayName ?? string.Empty;
            _normalized = stepStartNormalized;
            _lastNotified = stepStartNormalized;
            Changed?.Invoke();
        }

        /// <summary>
        /// Движение полосы внутри шага. Событие поднимается не на каждый кадр,
        /// а только при заметном изменении: иначе подписчик пересобирает строку
        /// впустую и это видно как мусор в GC.
        /// </summary>
        public void Report(float stepProgress, float stepStartNormalized, float stepShare)
        {
            float clamped = stepProgress < 0f ? 0f : (stepProgress > 1f ? 1f : stepProgress);
            float value = stepStartNormalized + stepShare * clamped;

            if (value - _lastNotified < NotifyEpsilon)
            {
                return;
            }

            _normalized = value;
            _lastNotified = value;
            Changed?.Invoke();
        }

        public void Complete()
        {
            _isComplete = true;
            _normalized = 1f;
            _lastNotified = 1f;
            _currentStep = "Готово";
            Changed?.Invoke();
        }
    }
}
