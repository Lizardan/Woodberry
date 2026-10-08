var rendererData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
if (rendererData == null) return "PC_Renderer.asset not found";
var so = new UnityEditor.SerializedObject(rendererData);
var features = so.FindProperty("m_RendererFeatures");
var sb = new System.Text.StringBuilder();
sb.Append("count=").Append(features.arraySize).Append("; ");
for (int i = 0; i < features.arraySize; i++)
{
    var f = features.GetArrayElementAtIndex(i);
    sb.Append("[").Append(i).Append("]=");
    sb.Append(f.objectReferenceValue != null ? f.objectReferenceValue.GetType().Name : "null");
    sb.Append(" | ");
}
return sb.ToString();
