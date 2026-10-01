using System;
using System.Collections;
using System.Collections.Generic;

namespace Woodberry.Core.Bootstrap
{
    /// <summary>
    /// Выполняет шаги стартовой инициализации последовательно и переводит
    /// общий прогресс. Шаги делят полосу поровну: это сознательное упрощение,
    /// и оно сломается, как только появится шаг, заметно превосходящий
    /// остальные по времени (например, загрузка уровня). Тогда понадобится
    /// вес у шага.
    /// </summary>
    public sealed class StartupRunner
    {
        private readonly IReadOnlyList<IStartupStep> _steps;

        public StartupRunner(IReadOnlyList<IStartupStep> steps)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        public IEnumerator Run(StartupProgress progress)
        {
            if (_steps.Count == 0)
            {
                progress.Complete();
                yield break;
            }

            float share = 1f / _steps.Count;

            for (int i = 0; i < _steps.Count; i++)
            {
                IStartupStep step = _steps[i];
                float start = i * share;

                progress.BeginStep(step.DisplayName, start);

                float last = 0f;
                IEnumerator routine = step.Run(value =>
                {
                    float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);

                    if (clamped - last < 0.001f)
                    {
                        return;
                    }

                    last = clamped;
                    progress.Report(clamped, start, share);
                });

                // Шаг вправе быть null, если решил, что делать нечего.
                while (routine != null && routine.MoveNext())
                {
                    yield return routine.Current;
                }
            }

            progress.Complete();
        }
    }
}
