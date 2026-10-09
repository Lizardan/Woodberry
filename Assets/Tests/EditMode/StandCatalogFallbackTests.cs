using System.Collections.Generic;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Перевод пути в идентификатор стенда. Здесь ошибка означает, что
    /// перегенерация уйдёт с неверным id и получит молчаливый отказ.
    /// </summary>
    public sealed class StandCatalogFallbackTests
    {
        private static CatalogEntry Frame(
            ArtAssetKind kind,
            string group,
            string pool,
            int number)
        {
            return new CatalogEntry
            {
                AssetPath = $"Assets/Woodberry/Art/Dialogue/{kind}/{group}/{pool}/{pool}_{number:00}_Ink.png",
                Id = new ArtAssetId(kind, group, pool, number)
            };
        }

        [Test]
        public void Mapper_OnPortraitGroup_ReturnsSameGroup()
        {
            Assert.That(
                StandGroupMapper.TryMap(ArtAssetKind.Portrait, "Roles", out string group),
                Is.True);
            Assert.That(group, Is.EqualTo("Roles"));
        }

        [Test]
        public void Mapper_OnBackgroundGroup_CollapsesTwoLevelPath()
        {
            Assert.That(
                StandGroupMapper.TryMap(ArtAssetKind.Background, "Village/Interiors",
                    out string group),
                Is.True);
            Assert.That(group, Is.EqualTo("VillageInteriors"));
        }

        [Test]
        public void Mapper_OnUnknownBackgroundGroup_ReturnsFalse()
        {
            Assert.That(
                StandGroupMapper.TryMap(ArtAssetKind.Background, "Village/Nonsense", out _),
                Is.False);
        }

        [Test]
        public void Mapper_OnEmptyGroup_ReturnsFalse()
        {
            Assert.That(StandGroupMapper.TryMap(ArtAssetKind.Background, "", out _), Is.False);
        }

        [Test]
        public void Fallback_FillsStandIdForPortraitFromNumber()
        {
            var entry = Frame(ArtAssetKind.Portrait, "Roles", "Priest", 2);

            IReadOnlyList<string> unknown = StandCatalogFallback.Apply(new[] { entry });

            Assert.That(unknown, Is.Empty);
            Assert.That(entry.StandId, Is.EqualTo("p_Roles_Priest_b"));
        }

        [Test]
        public void Fallback_FillsStandIdForBackgroundFromPool()
        {
            var entry = Frame(ArtAssetKind.Background, "Village/Exteriors", "Bridge", 1);

            StandCatalogFallback.Apply(new[] { entry });

            Assert.That(entry.StandId, Is.EqualTo("bg_VillageExteriors_Bridge"));
        }

        [Test]
        public void Fallback_WhenGroupUnknown_ReportsPathAndLeavesEmpty()
        {
            var entry = Frame(ArtAssetKind.Background, "Village/Nothing", "Barn", 1);

            IReadOnlyList<string> unknown = StandCatalogFallback.Apply(new[] { entry });

            Assert.That(unknown.Count, Is.EqualTo(1));
            Assert.That(entry.StandId, Is.Null);
        }

        [Test]
        public void Fallback_WhenStandIdAlreadyKnown_DoesNotOverwrite()
        {
            var entry = Frame(ArtAssetKind.Portrait, "Roles", "Priest", 1);
            entry.StandId = "p_Roles_Priest_from_driver";

            StandCatalogFallback.Apply(new[] { entry });

            Assert.That(entry.StandId, Is.EqualTo("p_Roles_Priest_from_driver"),
                "каталог драйвера authoritative: fallback не имеет права его править");
        }
    }
}
