using System;
using System.Collections.Generic;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Каталог арта, готовый для показа: группа → пул → кадры.
    ///
    /// Собирается один раз из скана диска и журнала ревью, поэтому фильтр
    /// и поиск не ходят по файловой системе на каждый символ в строке
    /// поиска: при сотнях папок это заметно.
    ///
    /// Скрытие пула при фильтре — правило окна, а не модели: пул без
    /// подходящих кадров не показывается, но продолжает существовать в
    /// полном каталоге.
    /// </summary>
    public sealed class CatalogModel
    {
        public IReadOnlyList<CatalogGroup> Groups { get; private set; } =
            new List<CatalogGroup>();

        public int Total { get; private set; }

        public int Shown { get; private set; }

        public int Approved { get; private set; }

        public int Rejected { get; private set; }

        public int New { get; private set; }

        public static CatalogModel Build(
            IReadOnlyList<CatalogEntry> entries,
            FeedbackStore feedback,
            CatalogFilter filter)
        {
            var model = new CatalogModel();

            List<CatalogEntry> all = entries
                .OrderBy(entry => entry.Id.Kind)
                .ThenBy(entry => entry.Id.Group, StringComparer.Ordinal)
                .ThenBy(entry => entry.Id.Pool, StringComparer.Ordinal)
                .ThenBy(entry => entry.Id.Number)
                .ToList();

            model.Total = all.Count;
            model.Approved = all.Count(entry => feedback.StatusOf(entry.AssetPath) == ArtStatus.Approved);
            model.Rejected = all.Count(entry => feedback.StatusOf(entry.AssetPath) == ArtStatus.Rejected);
            model.New = all.Count(entry => feedback.StatusOf(entry.AssetPath) == ArtStatus.New);

            List<CatalogEntry> visible = filter?.IsEmpty == true
                ? all
                : all.Where(entry => Matches(entry, feedback, filter)).ToList();

            model.Shown = visible.Count;

            var pools = new List<PoolInfo>();

            foreach (CatalogEntry entry in visible)
            {
                PoolInfo pool = pools.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, entry.Pool, StringComparison.OrdinalIgnoreCase)
                    && candidate.Kind == entry.Kind
                    && string.Equals(candidate.Group, entry.Group, StringComparison.OrdinalIgnoreCase));

                if (pool == null)
                {
                    pool = new PoolInfo
                    {
                        Name = entry.Pool,
                        Group = entry.Group,
                        Kind = entry.Kind
                    };
                    pools.Add(pool);
                }

                pool.Frames.Add(entry);
            }

            var groups = new List<CatalogGroup>();

            foreach (IGrouping<string, PoolInfo> grouping in pools
                         .GroupBy(pool => $"{pool.Kind}|{pool.Group}", StringComparer.Ordinal))
            {
                PoolInfo first = grouping.First();
                groups.Add(new CatalogGroup
                {
                    Key = grouping.Key,
                    Title = GroupTitle(first.Kind, first.Group),
                    Kind = first.Kind,
                    Pools = grouping.ToList()
                });
            }

            model.Groups = groups;
            return model;
        }

        private static bool Matches(
            CatalogEntry entry,
            FeedbackStore feedback,
            CatalogFilter filter)
        {
            if (filter == null)
            {
                return true;
            }

            if (filter.Kind != null && entry.Kind != filter.Kind)
            {
                return false;
            }

            ArtStatus status = feedback.StatusOf(entry.AssetPath);
            if (filter.Status != null && status != filter.Status)
            {
                return false;
            }

            if (filter.OnlyRegenerable && !entry.CanRegenerate)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                string needle = filter.Search.Trim();
                bool found =
                    Contains(entry.Pool, needle)
                    || Contains(entry.Group, needle)
                    || Contains(entry.StandId, needle)
                    || Contains(entry.AssetPath, needle)
                    || Contains(entry.Label, needle);

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GroupTitle(ArtAssetKind kind, string group)
        {
            string prefix = kind == ArtAssetKind.Portrait ? "Портреты" : "Фоны";
            string[] parts = group.Split('/');
            string readable = string.Join(" / ", parts.Select(Capitalize));
            return $"{prefix} / {readable}";
        }

        private static string Capitalize(string value)
        {
            return string.IsNullOrEmpty(value)
                ? value
                : char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
