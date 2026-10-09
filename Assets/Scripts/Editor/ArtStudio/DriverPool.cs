using System.Collections.Generic;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Пул со стороны стенда: какие кадры у него есть и какие ещё свободны.
    ///
    /// Появляется из события <c>catalog</c>. Каталог драйвера главнее
    /// <see cref="StandCatalogFallback"/>: стенд знает свои группы, а C# —
    /// только догадку до его ответа (см. <see cref="StandGroupMapper"/>).
    /// </summary>
    public sealed class DriverPool
    {
        public string Prefix { get; set; }

        public ArtAssetKind Kind { get; set; }

        public IReadOnlyList<string> All { get; set; } = new List<string>();

        public IReadOnlyList<string> Free { get; set; } = new List<string>();

        /// <summary>
        /// Префикс id стенда для пула проекта. Портрет — <c>p_&lt;группа&gt;_&lt;пул&gt;</c>,
        /// фон — <c>bg_&lt;группа&gt;_&lt;место&gt;</c>; ровно то, что склеивает драйвер.
        /// </summary>
        public static string PrefixFor(ArtAssetKind kind, string standGroup, string pool)
        {
            return kind == ArtAssetKind.Portrait
                ? $"p_{standGroup}_{pool}"
                : $"bg_{standGroup}_{pool}";
        }
    }
}
