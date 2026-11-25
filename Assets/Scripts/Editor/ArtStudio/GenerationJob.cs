namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Одно задание очереди: сгенерировать один кадр и проверить его.
    /// </summary>
    public sealed class GenerationJob
    {
        public string StandId { get; set; }

        public string Pool { get; set; }

        public ArtAssetKind Kind { get; set; }

        public string Label { get; set; }

        public JobState State { get; set; } = JobState.Queued;

        /// <summary>Прогресс кадра 0..100. У готового задания всегда 100.</summary>
        public int Percent { get; set; }

        /// <summary>
        /// Кадр отрисован моделью и лежит в raw. Проверка и приёмка ещё идут,
        /// поэтому кадр не готов, но в общей полосе он уже почти готов —
        /// иначе прогон «рисует 5 кадров» выглядел бы как пустой.
        /// </summary>
        public bool Drawn { get; set; }

        /// <summary>
        /// Этап работы над кадром: prepare, sample. Пустая строка — старый
        /// драйвер или этап вне кадра; полоса кадра тогда считается по
        /// проценту, а не по этапу.
        /// </summary>
        public string Stage { get; set; }

        /// <summary>Секунд с запуска задания. Нужны для оценки остатка.</summary>
        public double Elapsed { get; set; }

        /// <summary>
        /// Подпись фазы текущего кадра: «шаг 4/10», «старт кадра». Отдельно
        /// от <see cref="Message"/> — сообщение это вердикт (причина провала),
        /// а фаза живёт ровно пока кадр считается и счётчика не касается.
        /// </summary>
        public string ProgressText { get; set; }

        /// <summary>Последняя строка лога или причина провала.</summary>
        public string Message { get; set; }

        public bool IsFinished =>
            State == JobState.Done || State == JobState.Failed || State == JobState.Cancelled;
    }
}
