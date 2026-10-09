using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Журнал ревью — ручной труд пользователя. Главное, что проверяется:
    /// запись переживает перезапуск и битый файл не роняет инструмент.
    /// </summary>
    public sealed class FeedbackStoreTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                $"feedback_{TestContext.CurrentContext.Test.Name}.json");
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static FeedbackStore NewStore()
        {
            return new FeedbackStore();
        }

        [Test]
        public void Load_WhenFileMissing_OpensEmptyWithNoProblems()
        {
            var store = NewStore();

            FeedbackStore.LoadResult result = store.Load(_path);

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(result.Problems, Is.Empty);
        }

        [Test]
        public void SetAndSave_ThenLoadInNewStore_RestoresEntries()
        {
            var first = NewStore();
            first.Set("Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_01_Ink.png",
                ArtStatus.Approved, "лицо живое");
            first.Set("Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_02_Ink.png",
                ArtStatus.Rejected, "плоское");
            first.Save(_path);

            var second = NewStore();
            FeedbackStore.LoadResult result = second.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(second.Count, Is.EqualTo(2));
            Assert.That(
                second.StatusOf("Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_01_Ink.png"),
                Is.EqualTo(ArtStatus.Approved));
            Assert.That(
                second.Get("Assets/Woodberry/Art/Dialogue/Portraits/Roles/Priest/Priest_02_Ink.png").Comment,
                Is.EqualTo("плоское"));
        }

        [Test]
        public void Load_WhenFileIsTruncated_OpensEmptyAndReportsProblem()
        {
            File.WriteAllText(_path, "{\"entries\":[{\"key\":\"Assets/a.png\",\"stat");

            var store = NewStore();
            FeedbackStore.LoadResult result = store.Load(_path);

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(result.Problems.Count, Is.EqualTo(1),
                "потеря журнала до последнего сохранения — это факт, и о нём надо сказать");
        }

        [Test]
        public void Load_WhenStatusIsUnknown_SkipsEntryAndReportsProblem()
        {
            File.WriteAllText(_path,
                "{\"entries\":[{\"key\":\"Assets/a.png\",\"status\":\"maybe\",\"comment\":\"\"}]}");

            var store = NewStore();
            FeedbackStore.LoadResult result = store.Load(_path);

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_WhenEntryHasNoKey_SkipsEntry()
        {
            File.WriteAllText(_path, "{\"entries\":[{\"status\":\"approved\"}]}");

            var store = NewStore();
            store.Load(_path);

            Assert.That(store.Count, Is.EqualTo(0));
        }

        [Test]
        public void StatusOf_WhenNoEntry_ReturnsNew()
        {
            var store = NewStore();

            Assert.That(store.StatusOf("Assets/never.png"), Is.EqualTo(ArtStatus.New));
        }

        [Test]
        public void Rejected_ReturnsOnlyRejectedEntries()
        {
            var store = NewStore();
            store.Set("a", ArtStatus.Rejected, "брак");
            store.Set("b", ArtStatus.Approved, "ок");
            store.Set("c", ArtStatus.Rejected, "тоже брак");

            var rejected = new System.Collections.Generic.List<string>();
            foreach (var pair in store.Rejected())
            {
                rejected.Add(pair.Key);
            }

            Assert.That(rejected, Is.EquivalentTo(new[] { "a", "c" }));
        }

        [Test]
        public void Remove_DropsEntry()
        {
            var store = NewStore();
            store.Set("a", ArtStatus.Rejected, "брак");

            Assert.That(store.Remove("a"), Is.True);
            Assert.That(store.StatusOf("a"), Is.EqualTo(ArtStatus.New));
        }

        [Test]
        public void Save_LeavesNoTemporaryFileBehind()
        {
            var store = NewStore();
            store.Set("a", ArtStatus.Approved, null);
            store.Save(_path);

            Assert.That(File.Exists(_path + ".tmp"), Is.False,
                "временный файл обязан быть поглощён переименованием");
        }

        [Test]
        public void Save_OverExistingFile_ReplacesInsteadOfFailing()
        {
            var store = NewStore();
            store.Set("a", ArtStatus.Approved, null);
            store.Save(_path);

            store.Set("b", ArtStatus.Rejected, "брак");
            store.Save(_path);

            var restored = NewStore();
            restored.Load(_path);
            Assert.That(restored.Count, Is.EqualTo(2));
        }
    }
}
