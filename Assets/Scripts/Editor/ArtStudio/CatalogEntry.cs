namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Кадр диалогового арта, какой он лежит в проекте.
    ///
    /// <see cref="StandId"/> заполняется не всегда: из имени файла группу
    /// стенда восстановить нельзя (Village/Interiors в пути против
    /// VillageInteriors в id), поэтому до ответа драйвера <c>catalog</c>
    /// поле пустое. Перегенерировать кадр без stand-id нечем, и окно обязано
    /// показывать это, а не молча давать мёртвую кнопку.
    /// </summary>
    public sealed class CatalogEntry
    {
        public string AssetPath { get; set; }

        public ArtAssetId Id { get; set; }

        public string StandId { get; set; }

        /// <summary>Русское название роли из каталога стенда, если известно.</summary>
        public string Label { get; set; }

        public ArtAssetKind Kind => Id.Kind;

        public string Pool => Id.Pool;

        public string Group => Id.Group;

        public int Number => Id.Number;

        public bool CanRegenerate => !string.IsNullOrEmpty(StandId);
    }
}
