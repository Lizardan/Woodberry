using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Приводит уже лежащий в проекте диалоговый арт к нормативу импорта.
    ///
    /// Зачем отдельно от постпроцессора. Постпроцессор ловит только новые
    /// файлы, а 122 из 144 текущих уже разъехались: часть настроена руками,
    /// часть осталась на дефолте Unity. Этот проход их чинит и служит
    /// проверкой: повторный запуск обязан не найти ничего.
    ///
    /// Идемпотентен намеренно. Меню зовут и как инструмент, и как тест —
    /// «изменено 0, по нормативу 144» это и есть результат прогона.
    /// </summary>
    public static class DialogueArtImportStandardizer
    {
        private const string PortraitMark = "/Portraits/";
        private const string BackgroundMark = "/Backgrounds/";
        private const int PortraitWidth = 480;
        private const int PortraitHeight = 720;
        private const int BackgroundWidth = 856;
        private const int BackgroundHeight = 480;

        public sealed class Report
        {
            public int Total;
            public int Changed;
            public int AlreadyStandard;
            public int ReimportFailed;
            public readonly List<string> Deviations = new List<string>();
            public readonly List<string> WrongDimensions = new List<string>();
        }

        [MenuItem("Woodberry/Art Studio/Привести диалоговый арт к нормативу")]
        private static void ApplyFromMenu()
        {
            Report report = Run(apply: true);

            Debug.Log(
                "Woodberry: диалоговый арт приведён к нормативу — " +
                $"всего {report.Total}, изменено {report.Changed}, " +
                $"уже по нормативу {report.AlreadyStandard}.");

            LogProblems(report);
        }

        [MenuItem("Woodberry/Art Studio/Проверить норматив диалогового арта")]
        private static void CheckFromMenu()
        {
            Report report = Run(apply: false);

            Debug.Log(
                "Woodberry: проверка норматива диалогового арта — " +
                $"всего {report.Total}, по нормативу {report.AlreadyStandard}, " +
                $"отклонений {report.Deviations.Count}.");

            LogProblems(report);
        }

        private static void LogProblems(Report report)
        {
            foreach (string deviation in report.Deviations)
            {
                Debug.LogWarning($"Woodberry: неверный импорт — {deviation}");
            }

            foreach (string dimension in report.WrongDimensions)
            {
                Debug.LogWarning($"Woodberry: габарит не по стандарту — {dimension}");
            }

            if (report.ReimportFailed > 0)
            {
                Debug.LogError(
                    $"Woodberry: не переимпортировалось {report.ReimportFailed} ассетов.");
            }
        }

        /// <summary>
        /// Проходит по всем текстурам под <see cref="DialogueArtImportStandard.Root"/>.
        /// При <paramref name="apply"/> пишет норматив в импортер и переимпортирует,
        /// иначе только собирает расхождения.
        /// </summary>
        public static Report Run(bool apply)
        {
            var report = new Report();
            string[] guids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { DialogueArtImportStandard.Root });
            string[] paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            if (apply)
            {
                // Пакетный режим: без него Unity переимпортировала бы ассет
                // после каждого SaveAndReimport, и на 122 файлах проход
                // растянулся бы на минуты импортов-по-одному.
                AssetDatabase.StartAssetEditing();
            }

            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                        "Диалоговый арт", path, (float)i / paths.Length);
                    if (cancelled)
                    {
                        break;
                    }

                    report.Total++;
                    InspectAsset(path, report, apply);
                }
            }
            finally
            {
                if (apply)
                {
                    AssetDatabase.StopAssetEditing();
                }

                EditorUtility.ClearProgressBar();
            }

            return report;
        }

        private static void InspectAsset(string path, Report report, bool apply)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                // На t:Texture2D отвечает только TextureImporter; другой тип
                // здесь означал бы, что под корнем норматива лежит не картинка.
                report.ReimportFailed++;
                return;
            }

            IReadOnlyList<string> deviations = DialogueArtImportStandard.FindDeviations(importer);
            if (deviations.Count > 0)
            {
                report.Deviations.Add($"{path}: {string.Join(", ", deviations)}");
            }
            else
            {
                report.AlreadyStandard++;
            }

            if (apply && deviations.Count > 0)
            {
                DialogueArtImportStandard.Apply(importer);
                importer.SaveAndReimport();
                report.Changed++;
            }

            CheckDimensions(path, importer, report);
        }

        /// <summary>
        /// Габарит кадра — часть контракта диалога: персонаж садится на фон
        /// 90 % высоты, и разный размер ломает посадку у всех сразу. Чинить
        /// здесь нельзя — обрезка это потеря арта, правит стенд
        /// (<c>check_sizes.py --fix</c>). Поэтому только предупреждение.
        /// </summary>
        private static void CheckDimensions(string path, TextureImporter importer, Report report)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            string normalized = path.Replace('\\', '/');

            if (normalized.Contains(PortraitMark))
            {
                if (width != PortraitWidth || height != PortraitHeight)
                {
                    report.WrongDimensions.Add(
                        $"{path}: {width}x{height} вместо {PortraitWidth}x{PortraitHeight}");
                }
            }
            else if (normalized.Contains(BackgroundMark))
            {
                if (width != BackgroundWidth || height != BackgroundHeight)
                {
                    report.WrongDimensions.Add(
                        $"{path}: {width}x{height} вместо {BackgroundWidth}x{BackgroundHeight}");
                }
            }
        }
    }
}
