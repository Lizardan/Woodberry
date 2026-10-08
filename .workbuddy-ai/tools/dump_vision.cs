var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
var sb = new System.Text.StringBuilder();
foreach (var r in scene.GetRootGameObjects())
{
    Walk(r.transform, sb);
}
return sb.ToString();

static void Walk(UnityEngine.Transform t, System.Text.StringBuilder sb)
{
    bool interesting = false;
    foreach (var c in t.GetComponents<UnityEngine.Component>())
    {
        if (c == null) continue;
        string n = c.GetType().Name;
        if (n.Contains("Vision") || n.Contains("Mask") || n.Contains("Perception")) interesting = true;
    }
    if (t.gameObject.layer == 9) interesting = true;
    if (interesting)
    {
        var comps = new System.Collections.Generic.List<string>();
        foreach (var c in t.GetComponents<UnityEngine.Component>())
            if (c != null) comps.Add(c.GetType().Name);
        sb.Append(t.name).Append(" [L").Append(t.gameObject.layer).Append("] {")
          .Append(string.Join(",", comps)).Append("}\n");
    }
    for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), sb);
}
