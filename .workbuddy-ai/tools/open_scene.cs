// Открыть сцену Game. Тесты и предыдущие прогоны оставляют редактор
// в другой сцене, и тогда Play запускает меню, а не уровень.
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    "Assets/Woodberry/Scenes/Game.unity",
    UnityEditor.SceneManagement.OpenSceneMode.Single);

return scene.name + " открыта, корневых объектов: " + scene.GetRootGameObjects().Length;
