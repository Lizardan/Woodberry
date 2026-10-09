using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Woodberry.Editor.ArtStudio;

namespace Woodberry.Tests.EditMode
{
    /// <summary>
    /// Норматив импорта диалогового арта — контракт, а не настройка «на сейчас»:
    /// он держит единый масштаб 144 кадров и чистый силуэт вырезанных портретов.
    /// Поэтому и границы его применения, и значения, и фактическое состояние
    /// проекта проверяется тестом, а не только глазами.
    /// </summary>
    public sealed class DialogueArtImportStandardTests
    {
        private static string Root => DialogueArtImportStandard.Root;

        [Test]
        public void PixelsPerUnit_OnStandard_EqualsAssetStandardValue()
        {
            Assert.That(
                DialogueArtImportStandard.PixelsPerUnit,
                Is.EqualTo(128f),
                "PPU 128 общий для всего 2D-контента проекта (asset-standards.md). " +
                "Расхождение с тайлами пола даёт разный мировой масштаб.");
        }

        [Test]
        public void ShouldProcess_WhenPathIsDialoguePortraitOrBackground_ReturnsTrue()
        {
            Assert.That(
                DialogueArtImportStandard.ShouldProcess(Root + "/Portraits/Roles/Priest/Priest_02_Ink.png"),
                Is.True);
            Assert.That(
                DialogueArtImportStandard.ShouldProcess(Root + "/Backgrounds/Village/Interiors/Barn/Barn_01_Ink.png"),
                Is.True);
        }

        [Test]
        public void ShouldProcess_WhenPathIsSiblingFolderSharingPrefix_ReturnsFalse()
        {
            // Вхождение строки, а не сегмент пути, захватило бы DialogueExtra.
            Assert.That(
                DialogueArtImportStandard.ShouldProcess("Assets/Woodberry/Art/DialogueExtra/x.png"),
                Is.False);
            Assert.That(
                DialogueArtImportStandard.ShouldProcess("Assets/Woodberry/Art/Dialogue"),
                Is.False);
        }

        [Test]
        public void ShouldProcess_WhenPathIsOutsideDialogueRoot_ReturnsFalse()
        {
            Assert.That(
                DialogueArtImportStandard.ShouldProcess("Assets/Woodberry/Art/Environment/Floor_Interior.png"),
                Is.False);
            Assert.That(
                DialogueArtImportStandard.ShouldProcess("Assets/Scripts/Editor/ArtStudio/Diagram.png"),
                Is.False);
        }

        [Test]
        public void ShouldProcess_WhenFileIsNotAnImage_ReturnsFalse()
        {
            Assert.That(
                DialogueArtImportStandard.ShouldProcess(Root + "/Portraits/Roles/Priest/notes.txt"),
                Is.False);
            Assert.That(
                DialogueArtImportStandard.ShouldProcess(Root + "/Portraits/Roles/Priest/Priest_02_Ink.png.meta"),
                Is.False);
        }

        [Test]
        public void ShouldProcess_WhenPathIsNullOrEmpty_ReturnsFalse()
        {
            Assert.That(DialogueArtImportStandard.ShouldProcess(null), Is.False);
            Assert.That(DialogueArtImportStandard.ShouldProcess(string.Empty), Is.False);
        }

        [Test]
        public void FindDeviations_OnAllCurrentDialogueAssets_ReturnsEmpty()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Root });
            var problems = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    problems.Add($"{path}: импортер не TextureImporter");
                    continue;
                }

                IReadOnlyList<string> deviations = DialogueArtImportStandard.FindDeviations(importer);
                if (deviations.Count > 0)
                {
                    problems.Add($"{path}: {string.Join(", ", deviations)}");
                }
            }

            Assert.That(
                guids.Length,
                Is.GreaterThan(0),
                "под корнем диалогового арта не найдено текстур — тест ничего не проверяет.");

            Assert.That(
                problems,
                Is.Empty,
                "все кадры диалога обязаны иметь одинаковый импорт:\n" +
                string.Join("\n", problems) +
                "\nПочинка: Woodberry → Art Studio → Привести диалоговый арт к нормативу.");
        }

        [Test]
        public void StandardizerRun_OnCurrentProject_ReportsNoDeviations()
        {
            DialogueArtImportStandardizer.Report report =
                DialogueArtImportStandardizer.Run(apply: false);

            Assert.That(report.Total, Is.GreaterThan(0));
            Assert.That(report.Deviations, Is.Empty,
                "отклонения от норматива:\n" + string.Join("\n", report.Deviations));
            Assert.That(report.WrongDimensions, Is.Empty,
                "габарит кадра — часть контракта диалога:\n" +
                string.Join("\n", report.WrongDimensions));
            Assert.That(report.ReimportFailed, Is.EqualTo(0));
        }
    }
}
