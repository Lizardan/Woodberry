using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Файл доп. кадров — контракт со стендом: формат и правила записи
    /// обязаны совпадать с <c>roster.py::extra_letters</c>, иначе стенд молча
    /// пропустит кадр. Здесь проверяется и запись, и валидация до неё.
    /// </summary>
    public sealed class ExtraPoolsFileTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                $"extrapools_{TestContext.CurrentContext.Test.Name}.json");
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

        [Test]
        public void Load_WhenFileMissing_OpensEmptyWithNoProblems()
        {
            var file = new ExtraPoolsFile();

            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(file.Count, Is.EqualTo(0));
            Assert.That(result.Problems, Is.Empty,
                "нет файла — нет доп. кадров, это не поломка");
        }

        [Test]
        public void Load_ReadsPoolAndLetters()
        {
            File.WriteAllText(_path,
                "{\"pools\":[{\"pool\":\"p_Craftsmen_Baker\",\"letters\":\"cd\"}]}");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(file.Count, Is.EqualTo(1));
            Assert.That(file.Find("p_Craftsmen_Baker").Letters, Is.EqualTo("cd"));
        }

        [Test]
        public void Load_WhenEntriesSharePool_MergesThem()
        {
            File.WriteAllText(_path,
                "{\"pools\":[{\"pool\":\"p_X_a\",\"letters\":\"c\"}," +
                "{\"pool\":\"p_X_a\",\"letters\":\"d\"}]}");

            var file = new ExtraPoolsFile();
            file.Load(_path);

            Assert.That(file.Count, Is.EqualTo(1),
                "две записи одного пула стенд складывает — список обязан совпасть");
            Assert.That(file.Find("p_X_a").Letters, Is.EqualTo("cd"));
        }

        [Test]
        public void Load_WhenBrokenJson_OpensEmptyAndReportsProblem()
        {
            File.WriteAllText(_path, "{\"pools\":[{\"pool\":\"p_X_a\",\"let");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(file.Count, Is.EqualTo(0));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_WhenEntryHasNoPool_SkipsItWithProblem()
        {
            File.WriteAllText(_path, "{\"pools\":[{\"letters\":\"c\"}]}");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(file.Count, Is.EqualTo(0));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Add_NewPool_CreatesEntry()
        {
            var file = new ExtraPoolsFile();

            Assert.That(file.Add("p_Craftsmen_Baker", "C"), Is.True);
            Assert.That(file.Find("p_Craftsmen_Baker").Letters, Is.EqualTo("c"),
                "буквы пишутся строчными — так их читает стенд");
        }

        [Test]
        public void Add_ExistingPool_MergesWithoutDuplicating()
        {
            var file = new ExtraPoolsFile();
            file.Add("p_X_a", "c");

            Assert.That(file.Add("p_X_a", "dc"), Is.True);

            Assert.That(file.Find("p_X_a").Letters, Is.EqualTo("cd"));
        }

        [Test]
        public void Add_WhenLettersAlreadyPresent_ReturnsFalse()
        {
            var file = new ExtraPoolsFile();
            file.Add("p_X_a", "c");

            Assert.That(file.Add("p_X_a", "c"), Is.False,
                "файл незачем переписывать, если ничего не меняется");
        }

        [Test]
        public void Remove_DropsEntry()
        {
            var file = new ExtraPoolsFile();
            file.Add("p_X_a", "c");

            Assert.That(file.Remove("p_X_a"), Is.True);
            Assert.That(file.Count, Is.EqualTo(0));
            Assert.That(file.Remove("p_X_a"), Is.False);
        }

        [Test]
        public void Save_ThenLoadInNewInstance_RestoresEntries()
        {
            var first = new ExtraPoolsFile();
            first.Add("p_X_a", "c");
            first.Add("p_Y_b", "d");
            first.Save(_path);

            var second = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = second.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(second.Count, Is.EqualTo(2));
            Assert.That(second.Find("p_Y_b").Letters, Is.EqualTo("d"));
        }

        [Test]
        public void Save_OverExistingFile_ReplacesInsteadOfFailing()
        {
            var file = new ExtraPoolsFile();
            file.Add("p_X_a", "c");
            file.Save(_path);

            file.Add("p_Y_b", "d");
            file.Save(_path);

            var restored = new ExtraPoolsFile();
            restored.Load(_path);
            Assert.That(restored.Count, Is.EqualTo(2));
        }

        [Test]
        public void Save_LeavesNoTemporaryFileBehind()
        {
            var file = new ExtraPoolsFile();
            file.Add("p_X_a", "c");
            file.Save(_path);

            Assert.That(File.Exists(_path + ".tmp"), Is.False);
        }

        [Test]
        public void NextFreeLetters_SkipsUsedOnes()
        {
            string letters = ExtraPoolsFile.NextFreeLetters(new List<string> { "a", "b" }, 2);

            Assert.That(letters, Is.EqualTo("cd"),
                "буква — номер кадра, и повторять занятую нельзя");
        }

        [Test]
        public void NextFreeLetters_FillsGapsInsteadOfAppending()
        {
            string letters = ExtraPoolsFile.NextFreeLetters(
                new List<string> { "a", "c" }, 2);

            Assert.That(letters, Is.EqualTo("bd"));
        }

        [Test]
        public void NextFreeLetters_WhenLettersRunOut_ReturnsFewer()
        {
            var used = new List<string>();
            for (char letter = 'a'; letter <= 'y'; letter++)
            {
                used.Add(letter.ToString());
            }

            string letters = ExtraPoolsFile.NextFreeLetters(used, 3);

            Assert.That(letters, Is.EqualTo("z"),
                "букв всего 26: вернуть меньше — честно, а вызывающий обязан это заметить");
        }

        [Test]
        public void NextFreeLetters_WhenNothingUsed_StartsFromA()
        {
            Assert.That(ExtraPoolsFile.NextFreeLetters(null, 3), Is.EqualTo("abc"));
            Assert.That(ExtraPoolsFile.NextFreeLetters(new List<string>(), 0), Is.Empty);
        }

        [Test]
        public void LetterOf_TakesLastSegmentOfPortraitId()
        {
            Assert.That(ExtraPoolsFile.LetterOf("p_Craftsmen_Baker_c"), Is.EqualTo("c"));
            Assert.That(ExtraPoolsFile.LetterOf("bg_City_Guild"), Is.Empty,
                "у фона буквы-кадра нет");
        }

        // -------------------------------------------------------------- доп. места

        private static readonly List<string> Groups = new List<string>
        {
            "City", "VillageInteriors", "VillageExteriors", "VillageWilds"
        };

        [Test]
        public void Load_ReadsPlaces()
        {
            File.WriteAllText(_path,
                "{\"places\":[{\"group\":\"City\",\"place\":\"Bathhouse\"," +
                "\"label\":\"Баня\",\"scene\":\"interior of a stone bathhouse\"}]}");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(file.PlaceCount, Is.EqualTo(1));
            Assert.That(file.FindPlace("bg_City_Bathhouse").Label, Is.EqualTo("Баня"));
        }

        [Test]
        public void Load_WhenPlaceHasNoGroupOrName_SkipsItWithProblem()
        {
            File.WriteAllText(_path,
                "{\"places\":[{\"group\":\"City\",\"label\":\"Без места\"}]}");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(file.PlaceCount, Is.EqualTo(0));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_WhenPlaceRepeats_SkipsSecondWithProblem()
        {
            File.WriteAllText(_path,
                "{\"places\":[{\"group\":\"City\",\"place\":\"X\",\"label\":\"а\",\"scene\":\"s\"}," +
                "{\"group\":\"City\",\"place\":\"X\",\"label\":\"б\",\"scene\":\"s\"}]}");

            var file = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = file.Load(_path);

            Assert.That(file.PlaceCount, Is.EqualTo(1));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddPlace_ThenRemove_WorksById()
        {
            var file = new ExtraPoolsFile();

            Assert.That(file.AddPlace("City", "Bathhouse", "Баня", "interior"), Is.True);
            Assert.That(file.FindPlace("bg_City_Bathhouse").StandId,
                Is.EqualTo("bg_City_Bathhouse"));
            Assert.That(file.AddPlace("City", "Bathhouse", "Дубль", "interior"), Is.False);

            Assert.That(file.RemovePlace("bg_City_Bathhouse"), Is.True);
            Assert.That(file.PlaceCount, Is.EqualTo(0));
        }

        [Test]
        public void Save_KeepsBothPoolsAndPlaces()
        {
            var first = new ExtraPoolsFile();
            first.Add("p_X_a", "c");
            first.AddPlace("VillageInteriors", "Barn", "Амбар", "interior of a barn");
            first.Save(_path);

            var second = new ExtraPoolsFile();
            ExtraPoolsFile.LoadResult result = second.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(second.PlaceCount, Is.EqualTo(1),
                "буквы и места живут в одном файле — потерять половину нельзя");
            Assert.That(second.FindPlace("bg_VillageInteriors_Barn").Scene,
                Is.EqualTo("interior of a barn"));
        }

        [Test]
        public void ValidatePlace_WhenGroupUnknown_ReportsIt()
        {
            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "Ruins", "Bathhouse", "Баня", "scene", Groups, null);

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("неизвестна"));
        }

        [Test]
        public void ValidatePlace_WhenPlaceHasUnderscore_ReportsIt()
        {
            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "City", "Bath_House", "Баня", "scene", Groups, null);

            Assert.That(problems.Count, Is.EqualTo(1),
                "id разбирается по «_», и лишний разделитель ломает путь");
        }

        [Test]
        public void ValidatePlace_WhenPlaceIsCyrillic_ReportsIt()
        {
            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "City", "Баня", "Баня", "scene", Groups, null);

            Assert.That(problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void ValidatePlace_WhenSceneMissing_ReportsIt()
        {
            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "City", "Bathhouse", "Баня", "  ", Groups, null);

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("сцена"));
        }

        [Test]
        public void ValidatePlace_WhenPlaceAlreadyUsed_ReportsIt()
        {
            var used = new List<string> { "bg_City_Bathhouse" };

            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "City", "Bathhouse", "Баня", "scene", Groups, used);

            Assert.That(problems.Count, Is.EqualTo(1));
            Assert.That(problems[0], Does.Contain("уже есть"));
        }

        [Test]
        public void ValidatePlace_WhenEverythingIsFine_ReturnsNoProblems()
        {
            List<string> problems = ExtraPoolsFile.ValidatePlace(
                "City", "Bathhouse", "Баня", "interior of a stone bathhouse",
                Groups, new List<string> { "bg_City_Guild" });

            Assert.That(problems, Is.Empty);
        }
    }
}
