using System;
using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Группа каталога: Portraits/Roles, Backgrounds/Village/Interiors.
    /// Один узел дерева в окне.
    /// </summary>
    public sealed class CatalogGroup
    {
        public string Key { get; set; }

        public string Title { get; set; }

        public ArtAssetKind Kind { get; set; }

        public List<PoolInfo> Pools { get; set; } = new List<PoolInfo>();

        public int FrameCount => Pools.Sum(pool => pool.Frames.Count);
    }
}
