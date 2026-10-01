// Разведка: как назначен рендер-пайплайн и что лежит в сцене Game.
var report = new Dictionary<string, object>();

report["GraphicsSettings.defaultRP"] = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline == null
    ? "null"
    : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name;

report["QualitySettings.renderPipeline"] = QualitySettings.renderPipeline == null
    ? "null"
    : QualitySettings.renderPipeline.name;

report["qualityLevel"] = QualitySettings.names[QualitySettings.GetQualityLevel()];

// Все URP-ассеты проекта
var rpAssets = new List<string>();
foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
{
    var path = AssetDatabase.GUIDToAssetPath(guid);
    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(path);
    rpAssets.Add(path + "  name=" + (asset == null ? "?" : asset.name));
}
report["urpAssets"] = rpAssets;

// Все 2D-рендереры проекта
var r2d = new List<string>();
foreach (var guid in AssetDatabase.FindAssets("t:Renderer2DData"))
{
    r2d.Add(AssetDatabase.GUIDToAssetPath(guid));
}
report["renderer2DData"] = r2d;

// Все 3D-рендереры
var r3d = new List<string>();
foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
{
    r3d.Add(AssetDatabase.GUIDToAssetPath(guid));
}
report["universalRendererData"] = r3d;

// Что открыто сейчас
report["activeScene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
report["loadedScenes"] = UnityEngine.SceneManagement.SceneManager.sceneCount;

// Содержимое сцены Game, если она в списке загрузки
var gameScenePath = "Assets/Woodberry/Scenes/Game.unity";
var roots = new List<string>();
var loaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(gameScenePath);
if (loaded.IsValid() && loaded.isLoaded)
{
    foreach (var go in loaded.GetRootGameObjects())
        roots.Add(go.name + "  [" + string.Join(",", go.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name)) + "]");
}
else
{
    roots.Add("(сцена Game не загружена)");
}
report["gameSceneRoots"] = roots;

return report;
