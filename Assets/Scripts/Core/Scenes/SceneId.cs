using System;
using System.IO;

namespace Woodberry.Core.Scenes
{
    /// <summary>
    /// Сцены приложения. Имя файла сцены хранится рядом с идентификатором,
    /// чтобы строки не расходились по коду.
    /// </summary>
    public enum SceneId
    {
        Bootstrap = 0,
        Menu = 1,
        Game = 2
    }

    /// <summary>Единственное место, где сцены сопоставлены с файлами.</summary>
    public static class ScenePaths
    {
        public const string Folder = "Assets/Woodberry/Scenes/";

        public const string Bootstrap = Folder + "Bootstrap.unity";
        public const string Menu = Folder + "Menu.unity";
        public const string Game = Folder + "Game.unity";

        public static string For(SceneId id)
        {
            switch (id)
            {
                case SceneId.Bootstrap:
                    return Bootstrap;
                case SceneId.Menu:
                    return Menu;
                case SceneId.Game:
                    return Game;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "Неизвестная сцена");
            }
        }

        /// <summary>
        /// Обратное сопоставление: имя сцены, как его сообщает Unity, → идентификатор.
        /// Нужно, чтобы реагировать на смену сцены, не разбирая пути вручную.
        /// </summary>
        public static bool TryGetId(string sceneName, out SceneId id)
        {
            foreach (SceneId candidate in (SceneId[])Enum.GetValues(typeof(SceneId)))
            {
                if (Path.GetFileNameWithoutExtension(For(candidate)) == sceneName)
                {
                    id = candidate;
                    return true;
                }
            }

            id = SceneId.Bootstrap;
            return false;
        }
    }
}
