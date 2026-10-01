using System;

namespace Woodberry.Core.Bootstrap
{
    /// <summary>
    /// Прогресс стартовой инициализации. Живёт дольше любой сцены, поэтому
    /// это сервис, а не поле компонента: экран загрузки читает его из
    /// <see cref="ServiceRegistry"/>.
    ///
    /// UI не решает, когда переходить в следующую сцену. Он только показывает
    /// прогресс; решение принимает composition root.
    /// </summary>
    public interface IStartupProgress
    {
        /// <summary>Общий прогресс 0..1.</summary>
        float Normalized { get; }

        /// <summary>Что происходит сейчас, человеческим языком.</summary>
        string CurrentStep { get; }

        /// <summary>true, когда все шаги выполнены.</summary>
        bool IsComplete { get; }

        /// <summary>
        /// Подписка на изменение. Вызывается редко — на смену шага и на
        /// существенное движение полосы, а не каждый кадр.
        /// </summary>
        event Action Changed;
    }
}
