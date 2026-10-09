using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Кнопка «Арт-студия» на главном тулбаре редактора — рядом с
    /// Play-контролами, а не в разделе меню. Unity 6 даёт для этого публичный
    /// API <see cref="MainToolbarElementAttribute"/>: статический метод-фабрика
    /// с атрибутом объявляет элемент, рантайм тулбара сам его монтирует.
    ///
    /// Меню `Woodberry/Art Studio` остаётся как запасной вход — атрибут
    /// тулбара и пункт меню независимы.
    /// </summary>
    internal static class ArtStudioToolbar
    {
        [MainToolbarElement(
            "Woodberry/Art Studio",
            defaultDockPosition = MainToolbarDockPosition.Middle,
            defaultDockIndex = 0)]
        private static MainToolbarElement CreateArtStudioButton()
        {
            var content = new MainToolbarContent(
                "Арт-студия",
                "Открыть арт-студию: каталог диалогового арта, ревью, генерация");

            return new MainToolbarButton(content, ArtStudioWindow.Open);
        }
    }
}