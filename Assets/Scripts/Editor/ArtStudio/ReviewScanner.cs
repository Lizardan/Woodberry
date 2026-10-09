using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Читает кандидатов из <c>ArtStudio/review/</c> — папки, куда драйвер
    /// кладёт сгенерированные кадры до решения человека.
    ///
    /// Почему скан, а не события драйвера. Событие живёт один прогон:
    /// перезапустился драйвер — и готовые кандидаты исчезли из окна, хотя
    /// файлы лежат. Наоборот, удалённый кандидат оставался в списке призраком.
    /// Диск — единственный источник, который переживает и перезапуск, и
    /// удаление.
    ///
    /// Раскладка папок повторяет раскладку проекта: один пул = одна папка
    /// (<c>review/Craftsmen_Baker/</c>), поэтому пул кандидата виден человеку
    /// без обращения к каталогу стенда. Служебные папки (<c>replaced/</c> —
    /// архив заменённых кадров, <c>thumbs/</c> — превью) пропускаются: это не
    /// кандидаты на решение.
    /// </summary>
    public static class ReviewScanner
    {
        /// <summary>Папка внутри рабочей папки студии.</summary>
        public const string FolderName = "review";

        private static readonly HashSet<string> SkippedFolders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "replaced", "thumbs" };

        public sealed class ScanResult
        {
            public readonly List<ReviewCandidate> Candidates = new List<ReviewCandidate>();
            public readonly List<string> Problems = new List<string>();
        }

        public static ScanResult Scan(string reviewRoot)
        {
            var result = new ScanResult();

            if (!Directory.Exists(reviewRoot))
            {
                // Нет папки — нет кандидатов. Это не поломка: до первой
                // генерации её просто нет.
                return result;
            }

            foreach (string file in Directory
                         .EnumerateFiles(reviewRoot, "*.png", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(reviewRoot, file).Replace('\\', '/');
                if (relative.Split('/').Any(part => SkippedFolders.Contains(part)))
                {
                    continue;
                }

                if (!TryParseId(Path.GetFileNameWithoutExtension(file), out ArtAssetId id))
                {
                    result.Problems.Add($"имя кандидата не разобрано: {relative}");
                    continue;
                }

                FileInfo info = new FileInfo(file);
                result.Candidates.Add(new ReviewCandidate(
                    id,
                    file,
                    info.LastWriteTimeUtc.Ticks,
                    info.Length,
                    id.Kind == ArtAssetKind.Portrait ? Coverage(file) : -1));
            }

            return result;
        }

        /// <summary>
        /// Разбирает имя файла кандидата: <c>&lt;id стенда&gt;_&lt;стиль&gt;.png</c>.
        ///
        /// Стиль отрезается по числу сегментов id, а не по имени из
        /// <c>config.json</c>: иначе смена стиля ломала бы разбор кандидатов,
        /// сгенерированных раньше, — а они лежат в той же папке и никуда не
        /// деваются. Ровно так же устроен <c>copy_dialogue_art.destination()</c>:
        /// id разбирается по «_», поэтому «_» в группе и пуле запрещён.
        /// </summary>
        public static bool TryParseId(string nameWithoutExtension, out ArtAssetId id)
        {
            id = default;

            if (string.IsNullOrEmpty(nameWithoutExtension))
            {
                return false;
            }

            string[] parts = nameWithoutExtension.Split('_');

            if (parts[0] == "p" && parts.Length >= 4)
            {
                string letter = parts[3];
                if (letter.Length != 1)
                {
                    return false;
                }

                int number = char.ToLowerInvariant(letter[0]) - 'a' + 1;
                if (number < 1 || number > 99)
                {
                    return false;
                }

                id = new ArtAssetId(ArtAssetKind.Portrait, parts[1], parts[2], number);
                return true;
            }

            if (parts[0] == "bg" && parts.Length >= 3)
            {
                id = new ArtAssetId(ArtAssetKind.Background, parts[1], parts[2], 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Покрытие кадра силуэтом, 0..100. Читается с диска: кандидата
        /// <see cref="AssetDatabase"/> не знает, он лежит вне <c>Assets</c>.
        /// Ошибка чтения — это <c>-1</c> («не измерено»), а не 0: ноль значил
        /// бы «пустой кадр» и уехал бы человеку как вердикт.
        /// </summary>
        private static int Coverage(string path)
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(path)))
                    {
                        return -1;
                    }

                    Color32[] pixels = texture.GetPixels32();
                    if (pixels.Length == 0)
                    {
                        return -1;
                    }

                    int opaque = 0;
                    foreach (Color32 pixel in pixels)
                    {
                        if (pixel.a > 10)
                        {
                            opaque++;
                        }
                    }

                    return Mathf.RoundToInt(opaque * 100f / pixels.Length);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
