// Обстановка дома: ковёр, стол, шкаф, ящики.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();
var occluderLayer = LayerMask.NameToLayer("Occluder");

// Импорт предметов: PPU 128, билинейный фильтр, Full Rect.
foreach (var name in new[] { "Prop_Table", "Prop_Crate", "Prop_Shelf", "Prop_Rug" })
{
    var path = "Assets/Woodberry/Art/Environment/Props/" + name + ".png";
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null) { log.Add("НЕТ ИМПОРТЁРА " + path); continue; }

    importer.textureType = TextureImporterType.Sprite;
    importer.spriteImportMode = SpriteImportMode.Single;
    importer.spritePixelsPerUnit = 128f;
    importer.mipmapEnabled = false;
    importer.alphaIsTransparency = true;
    importer.wrapMode = TextureWrapMode.Clamp;
    importer.filterMode = FilterMode.Bilinear;
    importer.textureCompression = TextureImporterCompression.Uncompressed;

    var settings = new TextureImporterSettings();
    importer.ReadTextureSettings(settings);
    settings.spriteMeshType = SpriteMeshType.FullRect;
    settings.spriteAlignment = (int)SpriteAlignment.Center;
    settings.spritePixelsPerUnit = 128f;
    settings.filterMode = FilterMode.Bilinear;
    settings.mipmapEnabled = false;
    importer.SetTextureSettings(settings);
    importer.SaveAndReimport();
}

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);

var house = GameObject.Find("House");
if (house == null) { log.Add("НЕТ House"); return log; }

// Старая обстановка могла остаться от прошлого прогона.
var old = house.transform.Find("Furniture");
if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

var furniture = new GameObject("Furniture");
furniture.transform.SetParent(house.transform, false);

void Place(string name, string spritePath, Vector2 position, float rotationZ,
           Vector2 colliderSize, int sortingOrder)
{
    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
    if (sprite == null) { log.Add("НЕТ СПРАЙТА " + spritePath); return; }

    var go = new GameObject(name);
    go.transform.SetParent(furniture.transform, false);
    go.transform.position = position;
    go.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

    var renderer = go.AddComponent<SpriteRenderer>();
    renderer.sprite = sprite;
    renderer.sortingOrder = sortingOrder;

    if (colliderSize.sqrMagnitude > 0f)
    {
        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = colliderSize;

        // Мебель блокирует обзор так же, как стена: за столом не видно,
        // что происходит с другой стороны. Это второй, «мягкий» случай
        // притупления угла зрения после стен.
        go.layer = occluderLayer;
    }

    log.Add("  " + name + " @" + position);
}

Place("Rug", "Assets/Woodberry/Art/Environment/Props/Prop_Rug.png",
    new Vector2(-1.2f, 0.2f), 6f, Vector2.zero, -70);
Place("Table", "Assets/Woodberry/Art/Environment/Props/Prop_Table.png",
    new Vector2(-2.5f, 1.6f), 3f, new Vector2(0.92f, 0.64f), -60);
Place("Shelf", "Assets/Woodberry/Art/Environment/Props/Prop_Shelf.png",
    new Vector2(3.2f, 2.5f), 0f, new Vector2(0.92f, 0.40f), -60);
Place("CrateA", "Assets/Woodberry/Art/Environment/Props/Prop_Crate.png",
    new Vector2(3.9f, -2.3f), 12f, new Vector2(0.44f, 0.44f), -60);
Place("CrateB", "Assets/Woodberry/Art/Environment/Props/Prop_Crate.png",
    new Vector2(4.2f, -1.5f), -8f, new Vector2(0.44f, 0.44f), -60);

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
AssetDatabase.SaveAssets();

log.Add("обстановка добавлена");
return log;
