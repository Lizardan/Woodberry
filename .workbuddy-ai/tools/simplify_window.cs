// Окно перестаёт быть отдельным источником обзора.
//
// Требование: у окна не должно быть своей логики. Окно — просто проём
// в стене, и обзор проходит через него ровно так же, как через дверной.
// Значит, с окон снимается WindowVision, коллайдер уходит с слоя Occluder
// (обзор он больше не режет), а список источников обзора у VisibilityOverlay
// сокращается до одного игрока.
//
// Коллайдер при этом остаётся на обычном слое: окно не пропускает игрока,
// но пропускает обзор. Это не «особая логика окна», а обычная коллизия.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
{
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
}

var house = GameObject.Find("House");
if (house == null) { log.Add("НЕТ House"); return log; }

foreach (var name in new[] { "WindowNorth", "WindowEast" })
{
    var window = house.transform.Find(name);
    if (window == null) { log.Add("НЕТ " + name); continue; }

    var vision = window.GetComponent<Woodberry.Gameplay.Perception.WindowVision>();
    if (vision != null)
    {
        UnityEngine.Object.DestroyImmediate(vision);
        log.Add(name + ": WindowVision снят");
    }

    window.gameObject.layer = 0;   // Default: обзор сквозь окно проходит
    log.Add(name + ": слой 0, коллайдер "
        + (window.GetComponent<BoxCollider2D>() != null ? "на месте" : "ОТСУТСТВУЕТ"));
}

// Источник обзора остаётся один — игрок.
var player = GameObject.Find("Player");
var playerVision = player.GetComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();

var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
var so = new SerializedObject(overlay);
var sources = so.FindProperty("_sources");
sources.arraySize = 1;
sources.GetArrayElementAtIndex(0).objectReferenceValue = playerVision;
so.ApplyModifiedPropertiesWithoutUndo();
log.Add("источников обзора: 1 (игрок)");

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
AssetDatabase.SaveAssets();

return log;
