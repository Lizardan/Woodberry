using System.Collections.Generic;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Перевод группы в имени пути в группу в идентификаторе стенда.
    ///
    /// Зачем он нужен. Портреты совпадают один к одному: путь
    /// <c>Portraits/Roles/Priest</c> и стенд <c>p_Roles_Priest_a</c>. Фоны —
    /// нет: путь <c>Backgrounds/Village/Interiors/Barn</c>, а стенд ждёт
    /// <c>bg_VillageInteriors_Barn</c>. Без перевода запросить перегенерацию
    /// фона нечем.
    ///
    /// Почему это fallback, а не источник правды. Таблицу соответствия
    /// дублировать в C# рискованно: однажды группа в стенде переименуется,
    /// и C# продолжит молча слать неверные id. Поэтому окно предпочитает
    /// каталог от драйвера, а этот класс — только чтобы работать до его
    /// ответа.
    /// </summary>
    public static class StandGroupMapper
    {
        /// <summary>
        /// Группы фонов, которые знает перенос. Новое место обязано лежать в
        /// одной из них: `copy_dialogue_art.destination()` переводит группу в
        /// папку по фиксированной таблице и на незнакомой падает. Поэтому
        /// список — не украшение окна, а граница допустимого.
        /// </summary>
        public static IReadOnlyCollection<string> BackgroundStandGroups =>
            BackgroundGroups.Values;

        /// <summary>Группы фонов: путь в проекте → группа стенда.</summary>
        private static readonly Dictionary<string, string> BackgroundGroups =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "City", "City" },
                { "Village/Interiors", "VillageInteriors" },
                { "Village/Exteriors", "VillageExteriors" },
                { "Village/Wilds", "VillageWilds" }
            };

        /// <summary>
        /// true — группа узнана. Портреты проходят как есть: их группы в пути
        /// и на стенде совпадают.
        /// </summary>
        public static bool TryMap(ArtAssetKind kind, string pathGroup, out string standGroup)
        {
            standGroup = null;

            if (string.IsNullOrEmpty(pathGroup))
            {
                return false;
            }

            if (kind == ArtAssetKind.Portrait)
            {
                standGroup = pathGroup;
                return true;
            }

            return BackgroundGroups.TryGetValue(pathGroup, out standGroup);
        }
    }
}
