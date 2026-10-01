// Читаемость персонажа: в кадре он занимал меньше 4% высоты и читался
// пятном. Увеличиваем визуальный узел и слегка поднимаем коллайдер,
// чтобы силуэт не залезал в стены.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var prefabPath = "Assets/Woodberry/Prefabs/Player.prefab";
var contents = PrefabUtility.LoadPrefabContents(prefabPath);
var log = new List<string>();

var visual = contents.transform.Find("Visual");
if (visual != null)
{
    visual.localScale = new Vector3(1.2f, 1.2f, 1f);
    log.Add("Visual scale 1.2");
}

var shadow = contents.transform.Find("Shadow");
if (shadow != null)
{
    shadow.localScale = new Vector3(1.2f, 1.2f, 1f);
}

var collider = contents.GetComponent<CircleCollider2D>();
if (collider != null)
{
    collider.radius = 0.24f;
    log.Add("коллайдер 0.24");
}

PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
PrefabUtility.UnloadPrefabContents(contents);
AssetDatabase.SaveAssets();

log.Add("префаб обновлён");
return log;
