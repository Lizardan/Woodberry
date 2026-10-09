using System;
using System.IO;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Пути рабочей папки арт-студии. Все они считаются от корня проекта,
    /// переданного явно: в редакторе это <c>Application.dataPath/..</c>,
    /// в тестах — временная папка. Хардкод абсолютного пути означал бы,
    /// что на другой машине инструмент молча не найдёт очередь.
    /// </summary>
    public static class ArtStudioPaths
    {
        public const string FolderName = "ArtStudio";

        public const string DialogueRoot = "Assets/Woodberry/Art/Dialogue";

        public const string ConfigFile = "config.json";
        public const string CommandsFile = "commands.jsonl";
        public const string EventsFile = "events.jsonl";
        public const string FeedbackFile = "feedback.json";
        public const string ExtraPoolsFile = "extra_pools.json";
        public const string DriverInfoFile = "driver.json";

        /// <summary>Корень проекта из пути Assets: <c>…/Woodberry</c>.</summary>
        public static string ProjectRootFromDataPath(string dataPath)
        {
            if (string.IsNullOrEmpty(dataPath))
            {
                throw new ArgumentException("dataPath не задан", nameof(dataPath));
            }

            return Path.GetFullPath(Path.Combine(dataPath, ".."));
        }

        /// <summary>Абсолютный путь рабочей папки.</summary>
        public static string Root(string projectRoot)
        {
            return Path.Combine(projectRoot, FolderName);
        }

        public static string Config(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), ConfigFile);
        }

        public static string Commands(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), CommandsFile);
        }

        public static string Events(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), EventsFile);
        }

        public static string Feedback(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), FeedbackFile);
        }

        public static string ExtraPools(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), ExtraPoolsFile);
        }

        public static string DriverInfo(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), DriverInfoFile);
        }

        public static string Logs(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), "logs");
        }

        public static string Review(string projectRoot)
        {
            return Path.Combine(Root(projectRoot), "review");
        }

        public static string ReviewThumbs(string projectRoot)
        {
            return Path.Combine(Review(projectRoot), "thumbs");
        }

        public static string ReviewReplaced(string projectRoot)
        {
            return Path.Combine(Review(projectRoot), "replaced");
        }
    }
}
