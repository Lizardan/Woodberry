// Снимок текущих настроек обзора и освещения в сцене Game.
// Нужен перед правкой мягкости кромки: чтобы знать, от чего отталкиваться.
var log = new List<string>();

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
{
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
}

log.Add("scene=" + scene.path);

var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
if (overlay == null)
{
    log.Add("overlay НЕ НАЙДЕН");
    return string.Join("\n", log);
}

var so = new SerializedObject(overlay);
foreach (var field in new[] { "_rayStepDegrees", "_edgeEpsilonDegrees", "_gradientStart",
                               "_edgeSoftness", "_maskHeight", "_darkness" })
{
    var prop = so.FindProperty(field);
    log.Add(field + " = " + (prop == null ? "НЕТ" : prop.floatValue.ToString()));
}

var vis = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.PlayerVisionSource>();
var vso = new SerializedObject(vis);
log.Add("_radius = " + vso.FindProperty("_radius").floatValue);
log.Add("_intensity = " + vso.FindProperty("_intensity").floatValue);

log.Add("isPlaying = " + Application.isPlaying);
log.Add("screen = " + Screen.width + "x" + Screen.height);

var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
log.Add("camera orthoSize = " + cam.orthographicSize + " aspect=" + cam.aspect);

// Материал оверлея: значения до рантайм-клонирования, то есть ассет.
var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Woodberry/Materials/VisibilityOverlay.mat");
if (mat != null)
{
    log.Add("mat._EdgeSoftness = " + mat.GetFloat("_EdgeSoftness"));
    log.Add("mat._Darkness = " + mat.GetFloat("_Darkness"));
    log.Add("mat._FogStrength = " + mat.GetFloat("_FogStrength"));
}

return string.Join("\n", log);
