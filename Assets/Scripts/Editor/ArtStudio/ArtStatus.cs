using System;
using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>Статус кадра с точки зрения ревью.</summary>
    public enum ArtStatus
    {
        /// <summary>Ещё не смотрели: ни approve, ни брак.</summary>
        New = 0,

        Approved = 1,

        Rejected = 2
    }
}
