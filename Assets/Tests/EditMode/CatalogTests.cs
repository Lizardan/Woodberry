using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Каталог арта: разбор раскладки и фильтры. Здесь проверяются имена
    /// файлов, которые должен продуцировать конвейер, — ошибка раскладки
    /// ломает и ревью, и перегенерацию.
    /// </summary>
    public sealed class CatalogTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(),
                $"artstudio_catalog_{TestContext.CurrentContext.Test.Name}");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private void WriteFrame(string relative)
        {
            string full = Path.Combine(_root, relative.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "not a real png");
        }

        private static CatalogEntry Entry(string kind, string group, string pool, int number)
        {
            return new CatalogEntry
            {
                AssetPath = $"Assets/Woodberry/Art/Dialogue/{kind}/{group}/{pool}/{pool}_{number:00}_Ink.png",
                Id = new ArtAssetId(
                    kind == "Portraits" ? ArtAssetKind.Portrait : ArtAssetKind.Background,
                    group, pool, number)
            };
        }

        [Test]
        public void Scan_OnStandardLayout_ReadsEveryFrame()
        {
            WriteFrame("Portraits/Roles/Priest/Priest_01_Ink.png");
            WriteFrame("Portraits/Roles/Priest/Priest_02_Ink.png");
            WriteFrame("Backgrounds/Village/Interiors/Barn/Barn_01_Ink.png");

            CatalogScanner.ScanResult result = CatalogScanner.Scan(_root);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Entries.Count, Is.EqualTo(3));
        }

        [Test]
        public void Scan_OnBackgroundWithTwoLevelGroup_JoinsGroupPath()
        {
            WriteFrame("Backgrounds/Village/Interiors/Barn/Barn_01_Ink.png");

            CatalogScanner.ScanResult result = CatalogScanner.Scan(_root);

            CatalogEntry entry = result.Entries[0];
            Assert.That(entry.Group, Is.EqualTo("Village/Interiors"));
            Assert.That(entry.Pool, Is.EqualTo("Barn"));
            Assert.That(entry.Kind, Is.EqualTo(ArtAssetKind.Background));
        }

        [Test]
        public void Scan_WhenFileNameDoesNotMatchPool_ReportsProblem()
        {
            WriteFrame("Portraits/Roles/Priest/Baker_01_Ink.png");

            CatalogScanner.ScanResult result = CatalogScanner.Scan(_root);

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Scan_WhenFileIsTooDeep_ReportsProblem()
        {
            WriteFrame("Portraits/Roles/Priest/Extra/Priest_01_Ink.png");

            CatalogScanner.ScanResult result = CatalogScanner.Scan(_root);

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Scan_WhenRootMissing_ReportsProblem()
        {
            CatalogScanner.ScanResult result =
                CatalogScanner.Scan(Path.Combine(_root, "no-such-folder"));

            Assert.That(result.Entries, Is.Empty);
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Model_Grouping_SplitsByKindAndGroup()
        {
            var entries = new List<CatalogEntry>
            {
                Entry("Portraits", "Roles", "Priest", 1),
                Entry("Portraits", "Roles", "Priest", 2),
                Entry("Portraits", "City", "Guard", 1),
                Entry("Backgrounds", "Village/Interiors", "Barn", 1)
            };

            CatalogModel model = CatalogModel.Build(entries, new FeedbackStore(), new CatalogFilter());

            Assert.That(model.Total, Is.EqualTo(4));
            Assert.That(model.Groups.Count, Is.EqualTo(3));
            Assert.That(model.Shown, Is.EqualTo(4));
        }

        [Test]
        public void Model_Search_MatchesPoolNameIgnoringCase()
        {
            var entries = new List<CatalogEntry>
            {
                Entry("Portraits", "Roles", "Priest", 1),
                Entry("Portraits", "Roles", "Baker", 1)
            };

            CatalogModel model = CatalogModel.Build(entries, new FeedbackStore(),
                new CatalogFilter { Search = "priest" });

            Assert.That(model.Shown, Is.EqualTo(1));
            Assert.That(model.Groups[0].Pools[0].Name, Is.EqualTo("Priest"));
        }

        [Test]
        public void Model_FilterByStatus_UsesFeedbackJournal()
        {
            var entries = new List<CatalogEntry>
            {
                Entry("Portraits", "Roles", "Priest", 1),
                Entry("Portraits", "Roles", "Priest", 2)
            };
            var feedback = new FeedbackStore();
            feedback.Set(entries[0].AssetPath, ArtStatus.Approved, "лицо живое");
            feedback.Set(entries[1].AssetPath, ArtStatus.Rejected, "плоское");

            CatalogModel approved = CatalogModel.Build(entries, feedback,
                new CatalogFilter { Status = ArtStatus.Approved });
            CatalogModel rejected = CatalogModel.Build(entries, feedback,
                new CatalogFilter { Status = ArtStatus.Rejected });

            Assert.That(approved.Shown, Is.EqualTo(1));
            Assert.That(approved.Groups[0].Pools[0].Frames[0].Number, Is.EqualTo(1));
            Assert.That(rejected.Shown, Is.EqualTo(1));
            Assert.That(rejected.Groups[0].Pools[0].Frames[0].Number, Is.EqualTo(2));
        }

        [Test]
        public void Model_Counts_SummariseAllFramesNotShown()
        {
            var entries = new List<CatalogEntry>
            {
                Entry("Portraits", "Roles", "Priest", 1),
                Entry("Portraits", "Roles", "Priest", 2),
                Entry("Portraits", "Roles", "Baker", 1)
            };
            var feedback = new FeedbackStore();
            feedback.Set(entries[0].AssetPath, ArtStatus.Approved, null);
            feedback.Set(entries[1].AssetPath, ArtStatus.Rejected, null);

            CatalogModel model = CatalogModel.Build(entries, feedback,
                new CatalogFilter { Search = "priest" });

            Assert.That(model.Total, Is.EqualTo(3));
            Assert.That(model.Approved, Is.EqualTo(1));
            Assert.That(model.Rejected, Is.EqualTo(1));
            Assert.That(model.New, Is.EqualTo(1));
            Assert.That(model.Shown, Is.EqualTo(2));
        }

        [Test]
        public void Model_OnlyRegenerable_HidesFramesWithoutStandId()
        {
            var withStand = Entry("Portraits", "Roles", "Priest", 1);
            withStand.StandId = "p_Roles_Priest_a";
            var withoutStand = Entry("Portraits", "Roles", "Baker", 1);

            var entries = new List<CatalogEntry> { withStand, withoutStand };

            CatalogModel model = CatalogModel.Build(entries, new FeedbackStore(),
                new CatalogFilter { OnlyRegenerable = true });

            Assert.That(model.Shown, Is.EqualTo(1));
            Assert.That(model.Groups[0].Pools[0].Name, Is.EqualTo("Priest"));
        }

        [Test]
        public void Model_EmptyFilter_KeepsPoolsWithOneFrame()
        {
            var entries = new List<CatalogEntry> { Entry("Portraits", "Roles", "Priest", 1) };

            CatalogModel model = CatalogModel.Build(entries, new FeedbackStore(), new CatalogFilter());

            Assert.That(model.Groups.Count, Is.EqualTo(1));
            Assert.That(model.Groups[0].Pools.Count, Is.EqualTo(1));
            Assert.That(model.Groups[0].Pools[0].Frames.Count, Is.EqualTo(1));
            Assert.That(model.Groups[0].Title, Is.EqualTo("Портреты / Roles"));
        }

        [Test]
        public void ArtAssetId_StandId_UsesLetterForPortraitAndNoneForBackground()
        {
            Assert.That(
                new ArtAssetId(ArtAssetKind.Portrait, "Roles", "Priest", 1).StandId,
                Is.EqualTo("p_Roles_Priest_a"));
            Assert.That(
                new ArtAssetId(ArtAssetKind.Portrait, "Roles", "Priest", 2).StandId,
                Is.EqualTo("p_Roles_Priest_b"));
            Assert.That(
                new ArtAssetId(ArtAssetKind.Background, "VillageInteriors", "Barn", 1).StandId,
                Is.EqualTo("bg_VillageInteriors_Barn"));
        }

        [Test]
        public void ArtAssetId_NumberZero_Throws()
        {
            Assert.That(
                () => new ArtAssetId(ArtAssetKind.Portrait, "Roles", "Priest", 0),
                Throws.Exception);
        }

        [Test]
        public void ArtAssetId_VariantNumber_IsTwoDigits()
        {
            Assert.That(new ArtAssetId(ArtAssetKind.Portrait, "R", "P", 3).VariantNumber,
                Is.EqualTo("03"));
            Assert.That(new ArtAssetId(ArtAssetKind.Portrait, "R", "P", 12).VariantNumber,
                Is.EqualTo("12"));
        }
    }
}
