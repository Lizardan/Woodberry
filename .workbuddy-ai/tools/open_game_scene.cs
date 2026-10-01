// Открыть сцену Game и снять её текущее состояние: иерархия, камера, масштаб.
UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

var path = "Assets/Woodberry/Scenes/Game.unity";
var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
    path, UnityEditor.SceneManagement.OpenSceneMode.Single);

var report = new Dictionary<string, object>();
var roots = new List<string>();

void Walk(GameObject go, int depth, List<string> sink)
{
    var comps = go.GetComponents<Component>()
        .Select(c => c == null ? "MISSING" : c.GetType().Name);
    var line = new string(' ', depth * 2) + go.name
        + "  pos=" + go.transform.position.ToString("F2")
        + "  scale=" + go.transform.localScale.ToString("F2")
        + "  [" + string.Join(",", comps) + "]";

    var sr = go.GetComponent<SpriteRenderer>();
    if (sr != null && sr.sprite != null)
        line += "  sprite=" + sr.sprite.name + " size=" + sr.size + " order=" + sr.sortingOrder;

    sink.Add(line);

    foreach (Transform child in go.transform)
        Walk(child.gameObject, depth + 1, sink);
}

foreach (var go in scene.GetRootGameObjects())
    Walk(go, 0, roots);

report["hierarchy"] = roots;

var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
if (cam != null)
{
    report["camera"] = new Dictionary<string, object>
    {
        { "name", cam.name },
        { "orthographic", cam.orthographic },
        { "orthoSize", cam.orthographicSize },
        { "position", cam.transform.position.ToString("F2") },
        { "bg", cam.backgroundColor.ToString() },
        { "clearFlags", cam.clearFlags.ToString() },
    };
}

report["timeScale"] = Time.timeScale;

return report;
