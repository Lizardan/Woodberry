var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var sb = new System.Text.StringBuilder();
sb.Append("scene=").Append(scene.name).Append("; ");
int sprites = 0, lights = 0, shadows = 0, meshes = 0;
foreach (var go in scene.GetRootGameObjects())
{
    foreach (var t in go.GetComponentsInChildren<UnityEngine.Transform>(true))
    {
        if (t.GetComponent<UnityEngine.SpriteRenderer>()) sprites++;
        if (t.GetComponent<Woodberry.Lighting.U2D.U2DLight2D>()) lights++;
        if (t.GetComponent<Woodberry.Lighting.FL2D.FL2DLight>()) lights++;
        if (t.GetComponent<Woodberry.Lighting.FL2D.FL2DShadowMesh>()) shadows++;
        if (t.GetComponent<UnityEngine.Collider2D>()) meshes++;
    }
}
sb.Append("sprites=").Append(sprites).Append("; lights=").Append(lights)
  .Append("; shadowMeshes=").Append(shadows).Append("; colliders=").Append(meshes);
var sys = Woodberry.Lighting.U2D.U2DLightSystem.Instance;
sb.Append("; u2dSystem=").Append(sys != null ? "yes" : "no");
var mgr = UnityEngine.Object.FindFirstObjectByType<Woodberry.Lighting.FL2D.FL2DManager>();
sb.Append("; fl2dManager=").Append(mgr != null ? "yes" : "no");
return sb.ToString();
