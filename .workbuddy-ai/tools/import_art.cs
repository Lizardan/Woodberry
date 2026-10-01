// Настройка импорта всего нового арта. Значения — по docs/specs/asset-standards.md.
// Файлы только что скопированы на диск, поэтому сначала заставляем Unity
// их увидеть: без Refresh у них ещё нет импортёра и настроить нечего.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();

void Apply(string path, System.Action<TextureImporter> configure)
{
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null)
    {
        log.Add("MISSING IMPORTER: " + path);
        return;
    }
    configure(importer);
    importer.SaveAndReimport();
    log.Add("ok  " + path);
}

void Common(TextureImporter importer, float ppu, bool repeat, bool fullRect, bool bilinear)
{
    importer.textureType = TextureImporterType.Sprite;
    importer.spriteImportMode = SpriteImportMode.Single;
    importer.spritePixelsPerUnit = ppu;
    importer.mipmapEnabled = false;
    importer.alphaIsTransparency = true;
    importer.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
    importer.filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point;
    importer.textureCompression = TextureImporterCompression.Uncompressed;
    importer.maxTextureSize = 2048;

    var settings = new TextureImporterSettings();
    importer.ReadTextureSettings(settings);
    settings.spriteMeshType = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
    settings.spriteAlignment = (int)SpriteAlignment.Center;
    settings.spritePixelsPerUnit = ppu;
    settings.filterMode = bilinear ? FilterMode.Bilinear : FilterMode.Point;
    settings.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
    settings.mipmapEnabled = false;
    importer.SetTextureSettings(settings);
}

// Спрайт-листы персонажа: нарезка по сетке, кадры идут вдоль X.
void Sheet(string path, int columns, int rows, int framePx)
{
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null) { log.Add("MISSING IMPORTER: " + path); return; }

    Common(importer, 128f, false, true, true);
    importer.spriteImportMode = SpriteImportMode.Multiple;

    var metas = new List<SpriteMetaData>();
    string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
    int index = 0;
    // Unity считает Y снизу вверх — порядок кадров не важен, важны прямоугольники.
    for (int row = 0; row < rows; row++)
    {
        for (int col = 0; col < columns; col++)
        {
            metas.Add(new SpriteMetaData
            {
                name = baseName + "_" + index.ToString("00"),
                rect = new Rect(col * framePx, (rows - 1 - row) * framePx, framePx, framePx),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                border = Vector4.zero,
            });
            index++;
        }
    }

    importer.spritesheet = metas.ToArray();
    importer.SaveAndReimport();
    log.Add("ok  " + path + "  frames=" + metas.Count);
}

const string Art = "Assets/Woodberry/Art/";

// Персонаж: покадровые листы, кадр 128x128 при PPU 128 = 1 юнит.
Sheet(Art + "Characters/Player_TopDown_Idle.png", 4, 1, 128);
Sheet(Art + "Characters/Player_TopDown_Walk.png", 8, 1, 128);

// Тень — одиночный мягкий спрайт.
Apply(Art + "Characters/Player_TopDown_Shadow.png",
    i => Common(i, 128f, false, true, true));

// Тайлы: бесшовные, wrapMode Repeat, Full Rect обязателен для drawMode Tiled.
Apply(Art + "Environment/Floors/Floor_Interior.png",
    i => Common(i, 128f, true, true, true));
Apply(Art + "Environment/Ground/Ground_Forest.png",
    i => Common(i, 128f, true, true, true));
Apply(Art + "Environment/Walls/Wall_Plank.png",
    i => Common(i, 128f, true, true, true));
Apply(Art + "Environment/Walls/Wall_Window.png",
    i => Common(i, 128f, false, true, true));

// Кроны и кусты.
foreach (var name in new[] { "Tree_A", "Tree_B", "Tree_C", "Bush_A", "Bush_B" })
    Apply(Art + "Environment/Foliage/" + name + ".png",
        i => Common(i, 128f, false, true, true));

// Туман: тайлится по обеим осям.
Apply(Art + "VFX/Fog_Noise.png",
    i => Common(i, 128f, true, true, true));

AssetDatabase.Refresh();

// Проверяем, что кадры реально нарезались.
var report = new List<string>();
foreach (var path in new[]
{
    Art + "Characters/Player_TopDown_Idle.png",
    Art + "Characters/Player_TopDown_Walk.png",
    Art + "Characters/Player_TopDown_Shadow.png",
})
{
    var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToList();
    report.Add(System.IO.Path.GetFileName(path) + ": " + sprites.Count + " спрайтов, "
        + (sprites.Count > 0 ? sprites[0].rect.width + "x" + sprites[0].rect.height : "-"));
}

return new { log, report };
