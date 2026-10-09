using System.IO;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Кандидат — кадр, который драйвер сгенерировал и положил в
    /// <c>ArtStudio/review/</c>. Это ещё не кадр проекта: в <c>Assets</c> его
    /// переносит драйвер по команде <c>approve</c>.
    ///
    /// ИСТОЧНИК ПРАВДЫ — ДИСК, А НЕ СОБЫТИЯ ДРАЙВЕРА. Пока список строился из
    /// событий, у него было две болезни. Удалённый кандидат оставался в списке
    /// призраком, и «принять» уходило в пустоту. А кандидат, переживший
    /// перезапуск драйвера, пропадал из окна, хотя файл лежал на месте:
    /// события нового прогона начинаются с чистого листа.
    ///
    /// <see cref="WrittenUtcTicks"/> и <see cref="Size"/> отличают
    /// «кадр перегенерирован» от «кадр тот же»: по ним окно снимает устаревшую
    /// пометку брака. Событие драйвера для этого не годится — оно теряется
    /// вместе с перезапуском.
    /// </summary>
    public sealed class ReviewCandidate
    {
        public ReviewCandidate(ArtAssetId id, string path,
            long writtenUtcTicks, long size, int coverage)
        {
            Id = id;
            Path = path;
            WrittenUtcTicks = writtenUtcTicks;
            Size = size;
            Coverage = coverage;
        }

        public ArtAssetId Id { get; }

        /// <summary>Идентификатор стенда: <c>p_Craftsmen_Baker_c</c>.</summary>
        public string StandId => Id.StandId;

        /// <summary>Имя кадра как в проекте: <c>Baker_03</c>, <c>Bathhouse_01</c>.</summary>
        public string DisplayName => $"{Id.Pool}_{Id.VariantNumber}";

        public ArtAssetKind Kind => Id.Kind;

        /// <summary>Абсолютный путь файла в <c>review/</c>.</summary>
        public string Path { get; }

        /// <summary>Когда файл записан: по этому времени видно перегенерацию.</summary>
        public long WrittenUtcTicks { get; }

        public long Size { get; }

        /// <summary>
        /// Покрытие кадра силуэтом, 0..100. У портрета норма 41–62 %;
        /// <c>-1</c> — метрика не измеряется (у фона она всегда 100 и не значит
        /// ничего, показывать её нельзя).
        /// </summary>
        public int Coverage { get; }

        public bool HasCoverage => Coverage >= 0;

        /// <summary>
        /// Имя файла. <c>System.IO.Path</c> зовётся полным именем: свойство
        /// <see cref="Path"/> перекрывает тип, и короткая запись не компилируется.
        /// </summary>
        public string FileName =>
            Path == null ? string.Empty : System.IO.Path.GetFileName(Path);
    }
}
