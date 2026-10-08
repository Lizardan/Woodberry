UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Woodberry/Scenes/Game.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var sb = new System.Text.StringBuilder();
var roots = scene.GetRootGameObjects();
foreach (var r in roots)
{
    Dump(r.transform, 0, sb);
}
return sb.ToString();

static void Dump(UnityEngine.Transform t, int depth, System.Text.StringBuilder sb)
{
    if (depth > 3) return;
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>())
    {
        if (c == null) continue;
        comps.Add(c.GetType().Name);
    }
    sb.Append(new string(' ', depth * 2)).Append(t.name)
      .Append(" [L").Append(t.gameObject.layer).Append("] {")
      .Append(string.Join(",", comps)).Append("}\n");
    for (int i = 0; i < t.childCount && i < 12; i++) Dump(t.GetChild(i), depth + 1, sb);
}
