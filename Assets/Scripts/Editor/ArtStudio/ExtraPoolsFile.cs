using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Файл доп. кадров <c>ArtStudio/extra_pools.json</c>: чего не хватает
    /// ростеру стенда — буквы-кадры пулам и новые места для фонов.
    ///
    /// Формат — контракт стенда (<c>roster.py::_load_extra</c>), а не наш:
    /// <code>
    /// {
    ///   "pools":  [{"pool": "p_Craftsmen_Baker", "letters": "cd"}],
    ///   "places": [{"group": "City", "place": "Bathhouse",
    ///               "label": "Баня", "scene": "interior of ..."}]
    /// }
    /// </code>
    /// Префикс пула, а не короткое имя: <c>Forge</c> есть и в городе, и в
    /// деревне.
    ///
    /// Почему у персонажа буква, а у фона место. Буква варианта — это номер
    /// кадра в проекте (<c>a</c> → <c>_01</c>), и человек на новом кадре
    /// считается хешем от <c>пул:буква</c>. У фона буквы нет: номер всегда
    /// <c>_01</c>, поэтому «добавить кадр» фону значит добавить МЕСТО.
    ///
    /// Валидация в окне — удобство, а не истина: окончательное слово за
    /// стендом. Он же сообщает то, чего окно знать не может, — нехватку
    /// различимых поз у пула.
    /// </summary>
    public sealed class ExtraPoolsFile
    {
        [Serializable]
        private sealed class EntryDto
        {
            public string pool;
            public string letters;
        }

        [Serializable]
        private sealed class PlaceDto
        {
            public string group;
            public string place;
            public string label;
            public string scene;
        }

        [Serializable]
        private sealed class FileDto
        {
            public List<EntryDto> pools = new List<EntryDto>();
            public List<PlaceDto> places = new List<PlaceDto>();
        }

        /// <summary>Запись файла: пул и добавленные ему буквы.</summary>
        public sealed class Entry
        {
            public Entry(string pool, string letters)
            {
                Pool = pool;
                Letters = letters;
            }

            public string Pool { get; }

            public string Letters { get; set; }
        }

        /// <summary>Запись файла: новое место фона.</summary>
        public sealed class Place
        {
            public Place(string group, string placeName, string label, string scene)
            {
                Group = group;
                PlaceName = placeName;
                Label = label;
                Scene = scene;
            }

            public string Group { get; }

            /// <summary>Имя места в id: `Bathhouse`.</summary>
            public string PlaceName { get; }

            public string Label { get; }

            public string Scene { get; }

            /// <summary>Идентификатор стенда: <c>bg_City_Bathhouse</c>.</summary>
            public string StandId => $"bg_{Group}_{PlaceName}";
        }

        public sealed class LoadResult
        {
            public readonly List<string> Problems = new List<string>();

            public static LoadResult Ok()
            {
                return new LoadResult();
            }

            public static LoadResult WithProblem(string problem)
            {
                var result = new LoadResult();
                result.Problems.Add(problem);
                return result;
            }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Place> _places = new List<Place>();

        public IReadOnlyList<Entry> Entries => _entries;

        public IReadOnlyList<Place> Places => _places;

        public int Count => _entries.Count;

        public int PlaceCount => _places.Count;

        /// <summary>
        /// Читает файл. Нет файла — нет доп. кадров, и это не проблема.
        /// Битый JSON — проблема, а не исключение: список открывается пустым,
        /// инструмент остаётся рабочим, а причина видна человеку.
        ///
        /// Текст берётся из файла как есть (кроме регистра букв): чистить его
        /// здесь значило бы показывать не то, что лежит на диске. Запись с
        /// опечаткой стенд пропустит и скажет об этом, а человек должен
        /// видеть, что именно пропущено, и убрать это из списка.
        /// </summary>
        public LoadResult Load(string path)
        {
            _entries.Clear();
            _places.Clear();

            if (!File.Exists(path))
            {
                return LoadResult.Ok();
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return LoadResult.WithProblem($"доп. кадры: файл не прочитан: {e.Message}");
            }

            FileDto file;
            try
            {
                file = JsonUtility.FromJson<FileDto>(text);
            }
            catch (Exception e)
            {
                return LoadResult.WithProblem(
                    $"доп. кадры: файл не разобран, список открыт пустым: {e.Message}");
            }

            if (file == null)
            {
                return LoadResult.WithProblem(
                    "доп. кадры: ожидается объект с полями pools/places");
            }

            var problems = new LoadResult();

            foreach (EntryDto dto in file.pools ?? new List<EntryDto>())
            {
                string pool = (dto?.pool ?? string.Empty).Trim();
                string letters = (dto?.letters ?? string.Empty).Trim().ToLowerInvariant();

                if (pool.Length == 0)
                {
                    problems.Problems.Add("доп. кадры: запись без префикса пула пропущена");
                    continue;
                }

                // Записи одного пула сливаются: стенд складывает их буквы, а
                // две строки на один пул читались бы как два пула.
                Entry existing = Find(pool);
                if (existing == null)
                {
                    _entries.Add(new Entry(pool, letters));
                }
                else
                {
                    existing.Letters += letters;
                }
            }

            foreach (PlaceDto dto in file.places ?? new List<PlaceDto>())
            {
                string group = (dto?.group ?? string.Empty).Trim();
                string place = (dto?.place ?? string.Empty).Trim();

                if (group.Length == 0 || place.Length == 0)
                {
                    problems.Problems.Add("доп. кадры: запись места без группы или имени пропущена");
                    continue;
                }

                if (FindPlace($"bg_{group}_{place}") != null)
                {
                    problems.Problems.Add($"доп. кадры: место bg_{group}_{place} повторяется");
                    continue;
                }

                _places.Add(new Place(group, place,
                    (dto?.label ?? string.Empty).Trim(),
                    (dto?.scene ?? string.Empty).Trim()));
            }

            return problems;
        }

        public Entry Find(string pool)
        {
            return string.IsNullOrEmpty(pool)
                ? null
                : _entries.FirstOrDefault(entry =>
                    string.Equals(entry.Pool, pool, StringComparison.Ordinal));
        }

        public Place FindPlace(string standId)
        {
            return string.IsNullOrEmpty(standId)
                ? null
                : _places.FirstOrDefault(place =>
                    string.Equals(place.StandId, standId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Добавляет буквы пулу. false — добавлять нечего (буквы пустые или
        /// уже стоят у пула), и файл трогать незачем.
        /// </summary>
        public bool Add(string pool, string letters)
        {
            if (string.IsNullOrEmpty(pool))
            {
                return false;
            }

            string normalized = Normalize(letters);
            if (normalized.Length == 0)
            {
                return false;
            }

            Entry entry = Find(pool);
            if (entry == null)
            {
                _entries.Add(new Entry(pool, normalized));
                return true;
            }

            var merged = new StringBuilder(entry.Letters);
            bool changed = false;
            foreach (char letter in normalized)
            {
                if (merged.ToString().IndexOf(letter) < 0)
                {
                    merged.Append(letter);
                    changed = true;
                }
            }

            if (!changed)
            {
                return false;
            }

            entry.Letters = merged.ToString();
            return true;
        }

        public bool Remove(string pool)
        {
            return !string.IsNullOrEmpty(pool) &&
                   _entries.RemoveAll(entry =>
                       string.Equals(entry.Pool, pool, StringComparison.Ordinal)) > 0;
        }

        public bool AddPlace(string group, string place, string label, string scene)
        {
            string standId = $"bg_{group}_{place}";
            if (FindPlace(standId) != null)
            {
                return false;
            }

            _places.Add(new Place(group, place, label, scene));
            return true;
        }

        public bool RemovePlace(string standId)
        {
            return !string.IsNullOrEmpty(standId) &&
                   _places.RemoveAll(place =>
                       string.Equals(place.StandId, standId, StringComparison.Ordinal)) > 0;
        }

        public void Save(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new FileDto
            {
                pools = _entries.Select(entry => new EntryDto
                {
                    pool = entry.Pool,
                    letters = entry.Letters
                }).ToList(),
                places = _places.Select(place => new PlaceDto
                {
                    group = place.Group,
                    place = place.PlaceName,
                    label = place.Label,
                    scene = place.Scene
                }).ToList()
            };

            // Атомарная запись, как у журнала ревью: прерванная запись не
            // должна оставить половину JSON — этот файл читает стенд.
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(file, prettyPrint: true));

            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        /// <summary>
        /// Следующие свободные буквы вариантов: <paramref name="count"/> штук
        /// по алфавиту, которых нет среди занятых.
        ///
        /// Почему буквы считает окно, а не человек. Буква — это номер кадра в
        /// проекте (`a` → 01), и вписывать её руками незачем: человек говорит
        /// только «сколько кадров добавить». Возвращает МЕНЬШЕ, чем просили,
        /// если буквы кончились (их 26) — вызывающий обязан это заметить и
        /// сказать, а не молча сгенерировать меньше.
        /// </summary>
        public static string NextFreeLetters(IReadOnlyCollection<string> usedLetters, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            var used = new HashSet<string>(
                usedLetters ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var builder = new StringBuilder(count);
            for (char letter = 'a'; letter <= 'z' && builder.Length < count; letter++)
            {
                if (!used.Contains(letter.ToString()))
                {
                    builder.Append(letter);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Что не так с новым местом — до записи файла. Место становится
        /// новым фоном: группа переводится в папку по фиксированной таблице,
        /// поэтому незнакомая группа — отказ, а не догадка.
        /// </summary>
        public static List<string> ValidatePlace(string group, string place,
            string label, string scene,
            IReadOnlyCollection<string> knownGroups,
            IReadOnlyCollection<string> usedIds)
        {
            var problems = new List<string>();

            // Пробелы — это «не задано», а не значение: иначе поле с одним
            // пробелом прошло бы проверку и уехало в файл как имя места.
            group = (group ?? string.Empty).Trim();
            place = (place ?? string.Empty).Trim();
            label = (label ?? string.Empty).Trim();
            scene = (scene ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(group))
            {
                problems.Add("доп. места: группа не выбрана");
            }
            else if (knownGroups != null && !knownGroups.Contains(group))
            {
                problems.Add($"доп. места: группа «{group}» неизвестна — "
                             + $"известны: {string.Join(", ", knownGroups)}");
            }

            if (string.IsNullOrEmpty(place))
            {
                problems.Add("доп. места: место не задано (латиница и цифры, "
                             + "например Bathhouse)");
            }
            else if (!IsIdPart(place))
            {
                problems.Add("доп. места: место — латиница и цифры без «_»");
            }

            if (string.IsNullOrEmpty(label))
            {
                problems.Add("доп. места: название не задано");
            }

            if (string.IsNullOrEmpty(scene))
            {
                // Промпт — язык стенда, и окно его не сочиняет. Но без него
                // место бессмысленно: генератору нечего рисовать.
                problems.Add("доп. места: сцена не задана — это промпт стенда");
            }

            if (usedIds != null && !string.IsNullOrEmpty(group) && !string.IsNullOrEmpty(place))
            {
                string standId = $"bg_{group}_{place}";
                if (usedIds.Contains(standId))
                {
                    problems.Add($"доп. места: {standId} уже есть");
                }
            }

            return problems;
        }

        /// <summary>Буквы из id стенда: `p_Craftsmen_Baker_c` → `c`.</summary>
        public static string LetterOf(string standId)
        {
            if (string.IsNullOrEmpty(standId) ||
                !standId.StartsWith("p_", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int cut = standId.LastIndexOf('_');
            return cut >= 0 && cut < standId.Length - 1
                ? standId.Substring(cut + 1).ToLowerInvariant()
                : string.Empty;
        }

        /// <summary>Часть id: латиница и цифры, без «_» и без кириллицы.</summary>
        private static bool IsIdPart(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char letter in value)
            {
                if (letter > 127 || !char.IsLetterOrDigit(letter))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Строчные латинские буквы без повторов — то, что пишем в файл.</summary>
        private static string Normalize(string letters)
        {
            if (string.IsNullOrEmpty(letters))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(letters.Length);
            foreach (char letter in letters.Trim().ToLowerInvariant())
            {
                if (letter >= 'a' && letter <= 'z' &&
                    builder.ToString().IndexOf(letter) < 0)
                {
                    builder.Append(letter);
                }
            }

            return builder.ToString();
        }
    }
}
