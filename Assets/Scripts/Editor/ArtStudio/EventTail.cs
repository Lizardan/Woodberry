using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Дочитывает <c>events.jsonl</c> с того места, где читатель встал.
    ///
    /// Почему чтение по смещению, а не «прочитать файл целиком»: файл пишет
    /// другой процесс, и следующая строка может быть записана в момент
    /// чтения. Читатель обязан брать только целые строки и оставлять
    /// недописанный хвост до следующего раза — иначе половина JSON-строки
    /// упадёт разбором, а с ней и прогресс генерации.
    ///
    /// Побитая строка — не повод молчать и не повод падать: она попадает в
    /// проблемы, а смещение двигается дальше. Событие без <c>kind</c>
    /// считается битым по той же причине.
    /// </summary>
    public sealed class EventTail
    {
        private long _offset;

        public long Offset => _offset;

        public void Reset()
        {
            _offset = 0;
        }

        /// <summary>
        /// Возвращает события, добавленные с прошлого вызова. <paramref name="problems"/>
        /// получает побитые строки — их показывает интерфейс, а не глотает.
        /// </summary>
        public IReadOnlyList<ArtEvent> ReadNew(string path, ICollection<string> problems)
        {
            var events = new List<ArtEvent>();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return events;
            }

            byte[] data = ReadFromOffset(path);
            if (data.Length == 0)
            {
                return events;
            }

            // Только полные строки. '\n' не может быть частью многобайтового
            // символа в UTF-8, поэтому граница по нему безопасна даже если
            // файл дописывают в момент чтения.
            int lastNewline = -1;
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (data[i] == (byte)'\n')
                {
                    lastNewline = i;
                    break;
                }
            }

            if (lastNewline < 0)
            {
                return events;
            }

            _offset += lastNewline + 1;
            string block = Encoding.UTF8.GetString(data, 0, lastNewline + 1);

            foreach (string raw in block.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (ArtEvent.TryParse(line, out ArtEvent parsed))
                {
                    events.Add(parsed);
                }
                else
                {
                    problems.Add($"строка события не разобрана: {Preview(line)}");
                }
            }

            return events;
        }

        private byte[] ReadFromOffset(string path)
        {
            using (var stream = new FileStream(
                       path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length <= _offset)
                {
                    return System.Array.Empty<byte>();
                }

                stream.Seek(_offset, SeekOrigin.Begin);

                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[8192];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                    }

                    return buffer.ToArray();
                }
            }
        }

        private static string Preview(string line)
        {
            const int limit = 120;
            return line.Length <= limit
                ? line
                : line.Substring(0, limit) + "…";
        }
    }
}
