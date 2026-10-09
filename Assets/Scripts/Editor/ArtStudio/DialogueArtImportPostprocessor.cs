using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Навешивает норматив импорта на каждый новый кадр диалогового арта.
    ///
    /// Зачем постпроцессор, а не ручной прогон меню. Кадры теперь кладутся
    /// арт-студией пачками, и шаг «настроить импорт» забывается ровно один
    /// раз — на той партии, которую некому проверить. Постпроцессор делает
    /// норматив свойством папки, а не дисциплиной: положенный PNG получает
    /// настройки сам, и повторный ручной прогон ничего не находит.
    ///
    /// <see cref="GetVersion"/> поднят до 1 вместе с введением норматива:
    /// Unity переимпортирует ассеты, чей постпроцессор изменился, — иначе
    /// правка стандарта не дойдёт до уже импортированных файлов.
    /// </summary>
    public sealed class DialogueArtImportPostprocessor : AssetPostprocessor
    {
        private const uint Version = 1;

        public override uint GetVersion()
        {
            return Version;
        }

        private void OnPreprocessTexture()
        {
            TextureImporter importer = (TextureImporter)assetImporter;
            if (!DialogueArtImportStandard.ShouldProcess(assetPath))
            {
                return;
            }

            IReadOnlyList<string> changed = DialogueArtImportStandard.Apply(importer);
            if (changed.Count == 0)
            {
                return;
            }

            Debug.Log(
                $"Woodberry: импорт {assetPath} приведён к нормативу диалогового " +
                $"арта ({string.Join(", ", changed)}).");
        }
    }
}
