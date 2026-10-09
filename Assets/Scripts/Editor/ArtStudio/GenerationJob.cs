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

        /// <summary>Секунд с запуска задания. Нужны для оценки остатка.</summary>
        public double Elapsed { get; set; }

        /// <summary>Последняя строка лога или причина провала.</summary>
        public string Message { get; set; }

        public bool IsFinished =>
            State == JobState.Done || State == JobState.Failed || State == JobState.Cancelled;
    }
}
