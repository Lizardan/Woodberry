using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Читает раскладку диалогового арта с диска.
    ///
    /// Ожидаемая раскладка (см. `docs/specs/asset-standards.md`):
    /// <c>Assets/Woodberry/Art/Dialogue/Portraits/&lt;Группа&gt;/&lt;Пул&gt;/&lt;Пул&gt;_&lt;NN&gt;_&lt;Стиль&gt;.png</c>
    /// и такая же для <c>Backgrounds</c>.
    ///
    /// Файл, который не совпал с раскладкой, не игнорируется молча: он
    /// попадает в <see cref="ScanResult.Problems"/>. Полусонная генерация
    /// кладёт кадры с не тем именем, и узнать об этом лучше из отчёта
    /// инструмента, чем через месяц по пустому диалогу.
    /// </summary>
    public static class CatalogScanner
    {
        public const string DialogueRoot = "Assets/Woodberry/Art/Dialogue";

        public sealed class ScanResult
        {
            public readonly List<CatalogEntry> Entries = new List<CatalogEntry>();
            public readonly List<string> Problems = new List<string>();
        }

        public static ScanResult Scan(string dialogueRootAbsolutePath)
        {
            var result = new ScanResult();

            if (!Directory.Exists(dialogueRootAbsolutePath))
            {
                result.Problems.Add($"каталог не найден: {dialogueRootAbsolutePath}");
                return result;
            }

            foreach (string file in Directory
                         .EnumerateFiles(dialogueRootAbsolutePath, "*.png", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!TryReadEntry(dialogueRootAbsolutePath, file, result))
                {
                    continue;
                }
            }

            return result;
        }

        private static bool TryReadEntry(
            string root,
            string file,
            ScanResult result)
        {
            string relative = file.Substring(root.Length)
                .Replace('\\', '/')
                .TrimStart('/');

            string[] parts = relative.Split('/');

            // Глубину не фиксируем: у фонов группа занимает две папки
            // (Village/Interiors), и жёсткая проверка «ровно четыре сегмента»
            // отклоняла всю легальную раскладку фонов. Пул и файл считаются от
            // конца пути, поэтому лишняя вложенность ловится не здесь, а
            // сверкой имени файла с именем пула.
            if (parts.Length < 4)
            {
                result.Problems.Add($"не хватает папок группы и пула: {relative}");
                return false;
            }

            ArtAssetKind kind;
            if (string.Equals(parts[0], "Portraits", StringComparison.OrdinalIgnoreCase))
            {
                kind = ArtAssetKind.Portrait;
            }
            else if (string.Equals(parts[0], "Backgrounds", StringComparison.OrdinalIgnoreCase))
            {
                kind = ArtAssetKind.Background;
            }
            else
            {
                result.Problems.Add($"не Portraits и не Backgrounds: {relative}");
                return false;
            }

            // У фонов группа занимает две папки: Village/Interiors. У портретов
            // одна. Путь до пула = parts[1..^2], поэтому пул и группа считаются
            // от конца, а не от начала.
            string group = string.Join("/", parts.Skip(1).Take(parts.Length - 3));
            string pool = parts[parts.Length - 2];
            string fileName = parts[parts.Length - 1];

            if (!TryParseFileName(fileName, pool, out int number))
            {
                result.Problems.Add($"имя файла не по стандарту: {relative}");
                return false;
            }

            result.Entries.Add(new CatalogEntry
            {
                AssetPath = ToAssetPath(file),
                Id = new ArtAssetId(kind, group, pool, number)
            });

            return true;
        }

        /// <summary>
        /// Разбирает <c>&lt;Пул&gt;_&lt;NN&gt;_&lt;Стиль&gt;.png</c>. Точное
        /// совпадение пула обязательно: кадр чужого пула в чужой папке —
        /// это сломанная раскладка, и молча переименовывать её нельзя.
        /// </summary>
        private static bool TryParseFileName(string fileName, string expectedPool, out int number)
        {
            number = 0;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string[] segments = withoutExtension.Split('_');
            if (segments.Length != 3)
            {
                return false;
            }

            if (!string.Equals(segments[0], expectedPool, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(segments[1], out number) && number > 0;
        }

        private static string ToAssetPath(string absolutePath)
        {
            string normalized = absolutePath.Replace('\\', '/');
            int marker = normalized.IndexOf("/Assets/", StringComparison.Ordinal);
            return marker >= 0 ? normalized.Substring(marker + 1) : normalized;
        }
    }
}
