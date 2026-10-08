string path = "Assets/Settings/PC_Renderer.asset";
var rendererData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(path);
if (rendererData == null) return "PC_Renderer.asset not found";

var so = new UnityEditor.SerializedObject(rendererData);
var features = so.FindProperty("m_RendererFeatures");
var map = so.FindProperty("m_RendererFeatureMap");

bool exists = false;
for (int i = 0; i < features.arraySize; i++)
{
    var f = features.GetArrayElementAtIndex(i);
    if (f.objectReferenceValue is Woodberry.Lighting.U2D.U2DLightRenderFeature)
    {
        exists = true;
        break;
    }
}

if (exists) return "already present";

var feature = UnityEngine.ScriptableObject.CreateInstance<Woodberry.Lighting.U2D.U2DLightRenderFeature>();
feature.name = "U2DLightRenderFeature";
UnityEditor.AssetDatabase.AddObjectToAsset(feature, rendererData);

features.arraySize += 1;
features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

if (map != null)
{
    map.arraySize += 1;
    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = 0;
}

so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.EditorUtility.SetDirty(rendererData);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

return "added: count=" + so.FindProperty("m_RendererFeatures").arraySize;
