using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Журнал ревью: какой кадр одобрен, какой помечен браком и с каким
    /// комментарием. Хранится в <c>ArtStudio/feedback.json</c>.
    ///
    /// Ключ — путь ассета в проекте (или путь кандидата в ревью). Путь, а не
    /// stand-id: у кадра в проекте stand-id известен не всегда, а путь есть
    /// всегда. Связь путь → stand-id живёт в каталоге, а не здесь.
    ///
    /// Битый файл не должен ронять окно. Это ручной труд — если JSON
    /// потерял хвост, журнал открывается пустым с предупреждением, а не
    /// исключением: потерять возможность продолжить ревью из-за одной
    /// скобки хуже, чем потерять записи до последнего сохранения.
    ///
    /// Запись атомарная: временный файл + переименование. Прерванная
    /// запись не должна оставлять половину JSON.
    /// </summary>
    public sealed class FeedbackStore
    {
        [Serializable]
        private sealed class EntryDto
        {
            public string key;
            public string status;
            public string comment;
            public string at;
        }

        [Serializable]
        private sealed class FileDto
        {
            public List<EntryDto> entries = new List<EntryDto>();
        }

        public sealed class Entry
        {
            public ArtStatus Status;
            public string Comment;
            public string At;
        }

        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public int Count => _entries.Count;

        public IReadOnlyCollection<string> Keys => _entries.Keys;

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

        public LoadResult Load(string path)
        {
            _entries.Clear();

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
                return LoadResult.WithProblem($"feedback.json не прочитан: {e.Message}");
            }

            FileDto file;
            try
            {
                file = JsonUtility.FromJson<FileDto>(text);
            }
            catch (Exception e)
            {
                return LoadResult.WithProblem(
                    $"feedback.json повреждён, журнал открыт пустым: {e.Message}");
            }

            if (file?.entries == null)
            {
                return LoadResult.Ok();
            }

            var problems = new LoadResult();

            foreach (EntryDto dto in file.entries)
            {
                if (string.IsNullOrEmpty(dto.key))
                {
                    problems.Problems.Add("запись без ключа пропущена");
                    continue;
                }

                if (!TryParseStatus(dto.status, out ArtStatus status))
                {
                    problems.Problems.Add(
                        $"неизвестный статус «{dto.status}» у {dto.key}, запись пропущена");
                    continue;
                }

                _entries[dto.key] = new Entry
                {
                    Status = status,
                    Comment = dto.comment,
                    At = dto.at
                };
            }

            return problems;
        }

        public Entry Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return _entries.TryGetValue(key, out Entry entry) ? entry : null;
        }

        public ArtStatus StatusOf(string key)
        {
            return Get(key)?.Status ?? ArtStatus.New;
        }

        public void Set(string key, ArtStatus status, string comment)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            _entries[key] = new Entry
            {
                Status = status,
                Comment = comment,
                At = DateTime.Now.ToString("s")
            };
        }

        public bool Remove(string key)
        {
            return key != null && _entries.Remove(key);
        }

        public IEnumerable<KeyValuePair<string, Entry>> Rejected()
        {
            return _entries.Where(pair => pair.Value.Status == ArtStatus.Rejected);
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
                entries = _entries.Select(pair => new EntryDto
                {
                    key = pair.Key,
                    status = pair.Value.Status.ToString().ToLowerInvariant(),
                    comment = pair.Value.Comment,
                    at = pair.Value.At
                }).ToList()
            };

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

        private static bool TryParseStatus(string value, out ArtStatus status)
        {
            status = ArtStatus.New;

            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (ArtStatus candidate in Enum.GetValues(typeof(ArtStatus)))
            {
                if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    status = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
