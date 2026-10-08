UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Woodberry/Scenes/Menu.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
var sb = new System.Text.StringBuilder();
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.Append("scene=").Append(scene.name).Append("\n");
foreach (var r in scene.GetRootGameObjects())
{
    Walk(r.transform, 0, sb);
}
return sb.ToString();

static void Walk(UnityEngine.Transform t, int depth, System.Text.StringBuilder sb)
{
    if (depth > 4) return;
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<UnityEngine.Component>())
        if (c != null) comps.Add(c.GetType().Name);
    sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" {")
      .Append(string.Join(",", comps)).Append("}\n");
    for (int i = 0; i < t.childCount && i < 20; i++) Walk(t.GetChild(i), depth + 1, sb);
}
