// Собрать профиль настроения в сцене Game — с сохранением под-ассетов.
//
// Прошлая попытка добавила компоненты через profile.Add<T>(), но на диск
// они не попали: файл профиля остался прежним, а слоты — {fileID: 0}.
// Причина в том, как Volume хранит компоненты. У VolumeProfile компоненты
// это ScriptableObject-ы, и чтобы они жили в том же файле .asset, их надо
// ЯВНО положить туда через AssetDatabase.AddObjectToAsset. Без этого
// объект существует только в памяти до перезагрузки домена, и SaveAssets
// его не пишет.
var log = new List<string>();

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Woodberry/Scenes/Game.unity")
{
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
        "Assets/Woodberry/Scenes/Game.unity",
        UnityEditor.SceneManagement.OpenSceneMode.Single);
}

var volume = UnityEngine.Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
var profile = volume.sharedProfile;
log.Add("profile=" + profile.name);

var profilePath = AssetDatabase.GetAssetPath(profile);
log.Add("assetPath=" + profilePath);

// Компоненты профиля: удалить прежние пустые слоты и собрать заново.
profile.components.Clear();

var vignette = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.Vignette>();
vignette.name = "Vignette";
vignette.active = true;
vignette.intensity.Override(0.34f);
vignette.smoothness.Override(0.62f);
profile.components.Add(vignette);
AssetDatabase.AddObjectToAsset(vignette, profile);

var adjust = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.ColorAdjustments>();
adjust.name = "ColorAdjustments";
adjust.active = true;
adjust.postExposure.Override(-0.08f);
adjust.contrast.Override(12f);
adjust.saturation.Override(-22f);
profile.components.Add(adjust);
AssetDatabase.AddObjectToAsset(adjust, profile);

var bloom = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.Bloom>();
bloom.name = "Bloom";
bloom.active = true;
bloom.intensity.Override(0.45f);
bloom.threshold.Override(0.75f);
profile.components.Add(bloom);
AssetDatabase.AddObjectToAsset(bloom, profile);

var tonemap = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.Tonemapping>();
tonemap.name = "Tonemapping";
tonemap.active = true;
tonemap.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.Neutral);
profile.components.Add(tonemap);
AssetDatabase.AddObjectToAsset(tonemap, profile);

var grain = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.FilmGrain>();
grain.name = "FilmGrain";
grain.active = true;
grain.type.Override(UnityEngine.Rendering.Universal.FilmGrainLookup.Medium1);
grain.intensity.Override(0.22f);
grain.response.Override(0.7f);
profile.components.Add(grain);
AssetDatabase.AddObjectToAsset(grain, profile);

EditorUtility.SetDirty(profile);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

foreach (var c in profile.components)
{
    log.Add("  " + (c == null ? "<null>" : c.name));
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
log.Add("сохранено, слоёв=" + profile.components.Count);

return string.Join("\n", log);
