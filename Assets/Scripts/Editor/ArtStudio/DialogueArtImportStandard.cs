using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Норматив импорта диалогового арта — единственный источник правды о том,
    /// с какими настройками должны входить в проект портреты и фоны.
    ///
    /// Зачем один класс на всё. Аудит 2026-10-08 показал 122 отклонения на
    /// 144 файлах: рядом лежащие кадры одной роли шли с PPU 128 и 125 (один и
    /// тот же 480x720 рисовался на 6.25 % крупнее), а 121 файл остался на
    /// дефолте Unity — тип <c>Default</c>, сжатие BC, меш по контуру прозрачности
    /// и разрешённый апскейл NPOT до степени двойки. Настройки, размазанные по
    /// файлам, разъезжаются молча: сжатый арт даёт кайму по краю вырезанного
    /// силуэта, апскейл съедает 32 пикселя и лишнюю память, а разный PPU ломает
    /// единый масштаб всех диалоговых кадров.
    ///
    /// Почему <c>Uncompressed</c>, а не HQ-сжатие. Портрет — вырезанный
    /// силуэт, у него альфа по кромке фигуры. Блочное сжатие альфы даёт серую
    /// кайму ровно там, где портрет садится на фон. Цена норматива — 1.38 МБ
    /// на портрет и 1.65 МБ на фон, около 207 МБ на текущие 144 кадра; при
    /// росте пула выше ~400 кадров норматив меняется на CompressedHQ (BC7),
    /// и это решение записано в docs/specs/asset-standards.md, а не принимается
    /// втихую.
    ///
    /// Почему Mip Maps выключены, а Aniso выставлен явно. Кадр диалога стоит
    /// в UI один к одному: мипмап не может включиться, зато Filter Bilinear
    /// в паре с мипмапами размывает кромку. Aniso при выключенных мипмапах не
    /// действует, но фиксируется явно, чтобы значение не зависело от того,
    /// ставили галочку когда-то или нет.
    ///
    /// Почему Ignore Master Texture Limit не трогаем. Глобальный лимит —
    /// предохранитель памяти, а дефолтные 2048 уже выше любого нашего кадра
    /// (максимум 856x480). Отключать его значило бы снять защиту ради галочки,
    /// которая на этих размерах ничего не даёт.
    /// </summary>
    public static class DialogueArtImportStandard
    {
        /// <summary>Корень диалогового арта: только под ним применяется норматив.</summary>
        public const string Root = "Assets/Woodberry/Art/Dialogue";

        /// <summary>PPU стандарта проекта: 480x720 = 3.75x5.625 юнитов, 856x480 = 6.6875x3.75.</summary>
        public const float PixelsPerUnit = 128f;

        public const int MaxTextureSize = 2048;
        public const int AnisoLevel = 1;

        private static readonly string[] ImageExtensions =
        {
            ".png", ".tga", ".jpg", ".jpeg", ".psd", ".tif", ".tiff", ".bmp", ".gif"
        };

        private static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Относится ли ассет к диалоговому арту. Проверка по сегментам пути,
        /// а не по вхождению строки: <c>Assets/Woodberry/Art/DialogueExtra/x.png</c>
        /// норматив получить не должен.
        /// </summary>
        public static bool ShouldProcess(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith(Root + "/", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (string extension in ImageExtensions)
            {
                if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Приводит импортер к нормативу. Возвращает имена изменённых полей.</summary>
        public static IReadOnlyList<string> Apply(TextureImporter importer)
        {
            return Inspect(importer, true);
        }

        /// <summary>
        /// Сверяет импортер с нормативом, ничего не меняя. Пустой список —
        /// ассет уже по стандарту. Нужен и тесту, и отчёту в консоли: без
        /// проверки «всё хорошо» и «не смотрели» выглядят одинаково.
        /// </summary>
        public static IReadOnlyList<string> FindDeviations(TextureImporter importer)
        {
            return Inspect(importer, false);
        }

        private static IReadOnlyList<string> Inspect(TextureImporter importer, bool apply)
        {
            var changed = new List<string>();

            // МЕШ СПРАЙТА ПИШЕТСЯ ПЕРВЫМ, И ПОРЯДОК ОБЯЗАТЕЛЕН.
            //
            // В Unity 6 прямого свойства TextureImporter.spriteMeshType нет:
            // оно читается и пишется только через объект настроек, а само
            // сериализованное поле в .meta осталось. Ловушка в том, что
            // SetTextureSettings перезаписывает импортер ВСЕМИ полями
            // объекта, включая те, что задаются прямыми свойствами. Объект
            // читается ДО правок, поэтому запись настроек после правки
            // прямых свойств возвращает им старые значения — замерено на
            // первом прогоне: textureType, spriteMode, PPU, alpha и npotScale
            // вернулись к тому, чем были. Поэтому объект пишется первым, а
            // прямые свойства выставляются после него.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            bool meshDeviates = settings.spriteMeshType != SpriteMeshType.FullRect;
            if (meshDeviates)
            {
                changed.Add("Mesh Type");

                if (apply)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                }
            }

            Check(changed, "Texture Type", importer.textureType, TextureImporterType.Sprite,
                v => importer.textureType = v, apply);
            Check(changed, "Sprite Mode", importer.spriteImportMode, SpriteImportMode.Single,
                v => importer.spriteImportMode = v, apply);
            Check(changed, "Pixels Per Unit", importer.spritePixelsPerUnit, PixelsPerUnit,
                v => importer.spritePixelsPerUnit = v, apply);
            Check(changed, "Pivot", importer.spritePivot, CenterPivot,
                v => importer.spritePivot = v, apply);
            Check(changed, "Filter Mode", importer.filterMode, FilterMode.Bilinear,
                v => importer.filterMode = v, apply);
            Check(changed, "Mip Maps", importer.mipmapEnabled, false,
                v => importer.mipmapEnabled = v, apply);
            Check(changed, "Aniso Level", importer.anisoLevel, AnisoLevel,
                v => importer.anisoLevel = v, apply);
            Check(changed, "sRGB", importer.sRGBTexture, true,
                v => importer.sRGBTexture = v, apply);
            Check(changed, "Alpha Source", importer.alphaSource, TextureImporterAlphaSource.FromInput,
                v => importer.alphaSource = v, apply);
            Check(changed, "Alpha Is Transparency", importer.alphaIsTransparency, true,
                v => importer.alphaIsTransparency = v, apply);
            Check(changed, "Read/Write", importer.isReadable, false,
                v => importer.isReadable = v, apply);
            Check(changed, "Compression", importer.textureCompression, TextureImporterCompression.Uncompressed,
                v => importer.textureCompression = v, apply);
            Check(changed, "Crunch Compression", importer.crunchedCompression, false,
                v => importer.crunchedCompression = v, apply);
            Check(changed, "Wrap Mode", importer.wrapMode, TextureWrapMode.Clamp,
                v => importer.wrapMode = v, apply);
            Check(changed, "Max Size", importer.maxTextureSize, MaxTextureSize,
                v => importer.maxTextureSize = v, apply);
            Check(changed, "NPOT Scale", importer.npotScale, TextureImporterNPOTScale.None,
                v => importer.npotScale = v, apply);

            return changed;
        }

        private static void Check<T>(
            ICollection<string> changed,
            string label,
            T actual,
            T expected,
            Action<T> assign,
            bool apply)
        {
            if (EqualityComparer<T>.Default.Equals(actual, expected))
            {
                return;
            }

            if (apply)
            {
                assign(expected);
            }

            changed.Add(label);
        }
    }
}
