using System.Collections.Generic;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Заполняет <see cref="CatalogEntry.StandId"/> по раскладке проекта,
    /// пока каталог стенда недоступен.
    ///
    /// Отдельный класс, а не код окна: это единственное место, где решается
    /// «можно этому кадру заказать перегенерацию», и решение должно
    /// проверяться тестом. Драйвер отвечает каталогом — и его значения
    /// имеют приоритет над этим fallback.
    /// </summary>
    public static class StandCatalogFallback
    {
        public static IReadOnlyList<string> Apply(IEnumerable<CatalogEntry> entries)
        {
            var unknown = new List<string>();

            foreach (CatalogEntry entry in entries)
            {
                if (entry.CanRegenerate)
                {
                    continue;
                }

                if (StandGroupMapper.TryMap(entry.Kind, entry.Group, out string group))
                {
                    entry.StandId = entry.Kind == ArtAssetKind.Portrait
                        ? $"p_{group}_{entry.Pool}_{entry.Id.VariantLetter}"
                        : $"bg_{group}_{entry.Pool}";
                }
                else
                {
                    unknown.Add(entry.AssetPath);
                }
            }

            return unknown;
        }
    }
}
