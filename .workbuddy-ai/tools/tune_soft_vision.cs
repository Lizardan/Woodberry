// Настроить мягкость границы обзора под референс (Scorched Sun).
//
// Что видно на референсе: яркое широкое пятно вокруг игрока, уход
// в темноту плавный, но не «туман» — за пределами пятна по-прежнему
// черно. Радиус обзора там заметно меньше кадра, поэтому спад успевает
// развернуться целиком внутри экрана.
//
// Тут мешало два обстоятельства: радиус 3.4 при высоте кадра 7.6 мировой
// единицы (пятно упиралось в границу кадра), и _gradientStart = 0.7 —
// спад занимал последние 30% радиуса.
var log = new List<string>();

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
{
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
}

var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
if (overlay == null) { log.Add("overlay НЕ НАЙДЕН"); return string.Join("\n", log); }

var so = new SerializedObject(overlay);

void SetFloat(string field, float value)
{
    var prop = so.FindProperty(field);
    if (prop == null) { log.Add("НЕТ ПОЛЯ " + field); return; }
    prop.floatValue = value;
    log.Add(field + " = " + value);
}

// Спад от 40% радиуса с показателем 1.6: светлое ядро и длинный мягкий
// хвост. На референсе ядро занимает примерно треть пятна.
SetFloat("_gradientStart", 0.4f);
SetFloat("_falloffPower", 1.6f);
// Ослабление только в последних 8% радиуса: убирает пилу, но не делает
// кромку размытой. Это не «туман»: за пятном по-прежнему черно.
SetFloat("_edgeSoftness", 0.08f);

so.ApplyModifiedPropertiesWithoutUndo();

// Радиус приводим к масштабу кадра: на референсе пятно заметно меньше
// экрана, поэтому спад читается целиком, а не обрезается кадром.
var player = GameObject.Find("Player");
if (player != null)
{
    var vis = player.GetComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();
    if (vis != null)
    {
        var vso = new SerializedObject(vis);
        vso.FindProperty("_radius").floatValue = 4.2f;
        vso.ApplyModifiedPropertiesWithoutUndo();
        log.Add("_radius = 4.2");
    }
}

// Материал маски: значения по умолчанию совпадают с компонентом, но
// материал — отдельный ассет, и его тоже надо привести в порядок.
var maskMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Woodberry/Materials/VisionMask.mat");
if (maskMat != null)
{
    maskMat.SetFloat("_FalloffStart", 0.4f);
    maskMat.SetFloat("_FalloffPower", 1.6f);
    maskMat.SetFloat("_EdgeSoftness", 0.08f);
    EditorUtility.SetDirty(maskMat);
    log.Add("VisionMask.mat обновлён");
}
else
{
    log.Add("VisionMask.mat НЕ НАЙДЕН");
}

var overlayMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Woodberry/Materials/VisibilityOverlay.mat");
if (overlayMat != null)
{
    overlayMat.SetFloat("_EdgeSoftness", 0.08f);
    EditorUtility.SetDirty(overlayMat);
    log.Add("VisibilityOverlay.mat обновлён");
}
else
{
    log.Add("VisibilityOverlay.mat НЕ НАЙДЕН");
}

EditorUtility.SetDirty(overlay);
AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
log.Add("сцена сохранена");

return string.Join("\n", log);
