using System;
using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>Чем фильтровать список кадров в окне.</summary>
    public sealed class CatalogFilter
    {
        public string Search { get; set; }

        public ArtStatus? Status { get; set; }

        public ArtAssetKind? Kind { get; set; }

        /// <summary>Показывать только пулы, которым нужна перегенерация.</summary>
        public bool OnlyRegenerable { get; set; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Search)
            && Status == null
            && Kind == null
            && !OnlyRegenerable;
    }
}
