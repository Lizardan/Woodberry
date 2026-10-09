using System;
using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Пул — каталог одного персонажа или места со всеми его кадрами.
    /// Раскладка проекта пул уже выражает: одна папка = один пул.
    /// </summary>
    public sealed class PoolInfo
    {
        public string Group { get; set; }

        public string Name { get; set; }

        public ArtAssetKind Kind { get; set; }

        public List<CatalogEntry> Frames { get; } = new List<CatalogEntry>();

        public bool CanRegenerate => Frames.Any(frame => frame.CanRegenerate);
    }
}
