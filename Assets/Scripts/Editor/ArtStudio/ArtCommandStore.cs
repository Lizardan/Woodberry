using System.IO;
using System.Text;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Дописывает команды в <c>commands.jsonl</c>: одна команда = одна строка.
    ///
    /// Почему отдельный класс. Драйвер читает файл по смещению и разбирает
    /// только целые строки. Значит запись обязана быть одной строкой с одним
    /// переводом строки и сбросом на диск — иначе драйвер увидит половину JSON
    /// и команда потеряется молча. Это правило проверяется тестом.
    /// </summary>
    public static class ArtCommandStore
    {
        public static void Append(string path, ArtCommand command)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(
                       path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(command.ToJson());
                writer.Write('\n');
                writer.Flush();
                stream.Flush(true);
            }
        }
    }
}
