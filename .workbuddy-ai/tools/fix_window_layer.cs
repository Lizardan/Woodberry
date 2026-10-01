// Окна переводим на слой Occluder и поднимаем разрешение маски обзора.
//
// Почему окно обязано резать обзор игроку: пока оно пропускало лучи,
// наружу выходили ДВА пятна — круг обзора самого игрока, просочившийся
// через проём, и конус окна. Владелец проекта описал это как «два конуса».
// Требование же звучит однозначно: снаружи видно только через окно, и угол
// считается из окна. Значит, собственный обзор игрока окно не пропускает,
// а наружу смотрит только конус окна.
//
// Свой собственный конус окно при этом не гасит: его вершина лежит внутри
// оконного коллайдера, и VisibilityOverlay перезапускает такой луч сразу
// за коллайдером.
AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var log = new List<string>();
var occluderLayer = LayerMask.NameToLayer("Occluder");

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

    window.gameObject.layer = occluderLayer;
    var collider = window.GetComponent<BoxCollider2D>();
    log.Add(name + " -> слой " + occluderLayer
        + ", коллайдер " + (collider != null ? collider.size.ToString("F2") : "НЕТ"));
}

// Маска обзора: было 384 при экране 1080 — жёсткая кромка ложилась на
// крупные тексели и при движении «ползла» ступеньками.
var overlay = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Perception.VisibilityOverlay>();
var so = new SerializedObject(overlay);
var heightProp = so.FindProperty("_maskHeight");
if (heightProp != null)
{
    heightProp.intValue = 720;
    so.ApplyModifiedPropertiesWithoutUndo();
    log.Add("высота маски: 720");
}
else
{
    log.Add("НЕ НАЙДЕНО поле _maskHeight");
}

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
AssetDatabase.SaveAssets();

return log;
