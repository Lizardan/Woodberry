using System;
using System.Collections;

namespace Woodberry.Core.Bootstrap
{
    /// <summary>
    /// Один шаг стартовой инициализации. Шаги выполняются последовательно,
    /// каждый сообщает свой прогресс через <c>reportProgress</c> (0..1 внутри шага).
    ///
    /// <see cref="IEnumerator"/> выбран не случайно: реальные шаги — загрузка
    /// ассетов, подключение к сессии, прогрев — асинхронны и ждут кадров.
    /// </summary>
    public interface IStartupStep
    {
        /// <summary>Подпись для игрока: «Подключение к сессии», не «Step3».</summary>
        string DisplayName { get; }

        IEnumerator Run(Action<float> reportProgress);
    }
}
