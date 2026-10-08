var sb = new System.Text.StringBuilder();
var sys = Woodberry.Lighting.U2D.U2DLightSystem.Instance;
sb.Append("system=").Append(sys != null ? sys.name : "NULL").Append("; ");
sb.Append("active=").Append(sys != null && sys.isActiveAndEnabled)
  .Append("; ");
sb.Append("lights=").Append(Woodberry.Lighting.U2D.U2DLight2DBase.All.Count)
  .Append("; ");
var shader = UnityEngine.Shader.Find("Woodberry/U2DDeferredLighting");
sb.Append("deferredShader=").Append(shader != null ? "found" : "MISSING").Append("; ");
var sh2 = UnityEngine.Shader.Find("Woodberry/U2DLight");
sb.Append("lightShader=").Append(sh2 != null ? "found" : "MISSING").Append("; ");
var sh3 = UnityEngine.Shader.Find("Woodberry/U2DShadow");
sb.Append("shadowShader=").Append(sh3 != null ? "found" : "MISSING").Append("; ");
sb.Append("playing=").Append(UnityEditor.EditorApplication.isPlaying);
return sb.ToString();
