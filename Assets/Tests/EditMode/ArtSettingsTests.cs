using System.IO;
using NUnit.Framework;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Настройки читаются из файла в рабочей папке, поэтому главное —
    /// не зашитый в код путь и живучесть к битому файлу.
    /// </summary>
    public sealed class ArtSettingsTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(),
                $"artsettings_{TestContext.CurrentContext.Test.Name}.json");
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
        public void Load_WhenFileMissing_ReturnsDefaults()
        {
            ArtSettings.LoadResult result = ArtSettings.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Settings.StandDir, Is.EqualTo(ArtSettings.DefaultStandDir));
            Assert.That(result.Settings.Python, Is.EqualTo(ArtSettings.DefaultPython));
            Assert.That(result.Settings.Style, Is.EqualTo(ArtSettings.DefaultStyle));
        }

        [Test]
        public void Save_ThenLoad_RestoresValues()
        {
            var settings = new ArtSettings
            {
                StandDir = @"D:\other\stand",
                Python = @"D:\other\python.exe",
                ComfyHost = "127.0.0.1:9999",
                Style = "painterly",
                Light = "moon_blue"
            };

            settings.Save(_path);
            ArtSettings.LoadResult result = ArtSettings.Load(_path);

            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Settings.StandDir, Is.EqualTo(@"D:\other\stand"));
            Assert.That(result.Settings.Python, Is.EqualTo(@"D:\other\python.exe"));
            Assert.That(result.Settings.ComfyHost, Is.EqualTo("127.0.0.1:9999"));
            Assert.That(result.Settings.Style, Is.EqualTo("painterly"));
            Assert.That(result.Settings.Light, Is.EqualTo("moon_blue"));
        }

        [Test]
        public void Load_WhenFileIsBroken_ReturnsDefaultsAndReportsProblem()
        {
            File.WriteAllText(_path, "{\"standDir\": ");

            ArtSettings.LoadResult result = ArtSettings.Load(_path);

            Assert.That(result.Settings.StandDir, Is.EqualTo(ArtSettings.DefaultStandDir));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void Load_WhenStandDirEmpty_SubstitutesDefaultAndReportsProblem()
        {
            File.WriteAllText(_path, "{\"standDir\":\"\"}");

            ArtSettings.LoadResult result = ArtSettings.Load(_path);

            Assert.That(result.Settings.StandDir, Is.EqualTo(ArtSettings.DefaultStandDir));
            Assert.That(result.Problems.Count, Is.EqualTo(1));
        }

        [Test]
        public void FindSetupProblems_WhenPathsMiss_ReportsBoth()
        {
            var settings = new ArtSettings
            {
                StandDir = @"Z:\нет\такого",
                Python = @"Z:\нет\такого\python.exe"
            };

            System.Collections.Generic.IReadOnlyList<string> problems = settings.FindSetupProblems();

            Assert.That(problems.Count, Is.EqualTo(2));
        }
    }
}
