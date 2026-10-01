// Подгонка значений после первого визуального прогона.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();

void SetPrivate(UnityEngine.Object target, string field, System.Action<SerializedProperty> apply)
{
    var so = new SerializedObject(target);
    var prop = so.FindProperty(field);
    if (prop == null) { log.Add("НЕТ ПОЛЯ " + field); return; }
    apply(prop);
    so.ApplyModifiedPropertiesWithoutUndo();
}

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
{
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
}

// Радиус обзора меньше комнаты: иначе круг обзора не касается стен,
// и главное требование — «не видит сквозь стены» — просто не включается.
var player = GameObject.Find("Player");
var vision = player.GetComponent<Woodberry.Gameplay.Perception.PlayerVisionSource>();
SetPrivate(vision, "_radius", p => p.floatValue = 3.4f);

var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
SetPrivate(overlay, "_darkness", p => p.floatValue = 0.965f);

// Пост-обработка: виньетка и недодержка вместе с темнотой давали чёрный
// кадр. Виньетка должна подчёркивать края, а не съедать их.
var volume = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
var profile = volume.sharedProfile;

var vignette = profile.components.OfType<UnityEngine.Rendering.Universal.Vignette>().First();
vignette.intensity.Override(0.34f);
vignette.smoothness.Override(0.62f);

var adjustments = profile.components.OfType<UnityEngine.Rendering.Universal.ColorAdjustments>().First();
adjustments.postExposure.Override(-0.08f);
adjustments.contrast.Override(12f);
adjustments.saturation.Override(-22f);

var bloom = profile.components.OfType<UnityEngine.Rendering.Universal.Bloom>().First();
bloom.intensity.Override(0.45f);
bloom.threshold.Override(0.75f);

EditorUtility.SetDirty(profile);
AssetDatabase.SaveAssets();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

log.Add("радиус обзора 3.4, темнота 0.965, виньетка 0.34, экспозиция -0.08");

return log;
