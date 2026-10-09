using System;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Идентификатор кадра диалога: роль (портрет или фон), группа, пул и номер.
    ///
    /// Зачем он нужен редактору. Кадр в проекте лежит как
    /// <c>Portraits/Roles/Priest/Priest_02_Ink.png</c>, а стенд знает тот же
    /// кадр как <c>p_Roles_Priest_b</c>. Редактор обязан держать обе стороны
    /// одной связью: без stand-id заказать перегенерацию нечем, без номера —
    /// не посчитать следующий свободный.
    ///
    /// Почему номер, а не буква. Буква это порядковый номер кадра в пуле
    /// (a = 1), и сводить её к числу один раз надёжнее, чем помнить это
    /// правило в каждом вызове.
    /// </summary>
    public readonly struct ArtAssetId : IEquatable<ArtAssetId>
    {
        public ArtAssetKind Kind { get; }

        /// <summary>Группа каталога стенда: Roles, City, VillageInteriors…</summary>
        public string Group { get; }

        /// <summary>Пул: Priest, Baker, Guild. Один пул = один каталог в проекте.</summary>
        public string Pool { get; }

        /// <summary>Номер кадра в пуле, начиная с 1.</summary>
        public int Number { get; }

        public ArtAssetId(ArtAssetKind kind, string group, string pool, int number)
        {
            if (number < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(number), number,
                    "номер кадра в пуле начинается с 1");
            }

            Kind = kind;
            Group = group;
            Pool = pool;
            Number = number;
        }

        /// <summary>Буква варианта: 1 → "a", 2 → "b". У стенда именно буквами.</summary>
        public string VariantLetter => ((char)('a' + Number - 1)).ToString();

        /// <summary>Номер как в имени файла: 1 → "01".</summary>
        public string VariantNumber => Number.ToString("00");

        /// <summary>
        /// Идентификатор кадра для стенда: <c>p_Роли_Пул_буква</c> или
        /// <c>bg_Группа_Место</c>. У фонов вариант в id не кодируется —
        /// место в каталоге одно, а номера разводит перенос в проект.
        /// </summary>
        public string StandId => Kind == ArtAssetKind.Portrait
            ? $"p_{Group}_{Pool}_{VariantLetter}"
            : $"bg_{Group}_{Pool}";

        public bool Equals(ArtAssetId other)
        {
            return Kind == other.Kind
                   && string.Equals(Group, other.Group, StringComparison.Ordinal)
                   && string.Equals(Pool, other.Pool, StringComparison.Ordinal)
                   && Number == other.Number;
        }

        public override bool Equals(object obj)
        {
            return obj is ArtAssetId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind * 397;
                hash = (hash ^ (Group?.GetHashCode() ?? 0)) * 31;
                hash = (hash ^ (Pool?.GetHashCode() ?? 0)) * 17;
                hash ^= Number;
                return hash;
            }
        }

        public override string ToString()
        {
            return StandId;
        }
    }
}
