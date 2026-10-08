UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Woodberry/Scenes/LightingMenu.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
var sb = new System.Text.StringBuilder();
var menu = UnityEngine.Object.FindFirstObjectByType<Woodberry.UI.LightingMenu>();
sb.Append("menu=").Append(menu != null ? "yes" : "no").Append("; ");
var b1 = UnityEngine.GameObject.Find("BtnScene1");
var b2 = UnityEngine.GameObject.Find("BtnScene2");
sb.Append("btn1=").Append(b1 != null).Append("; btn2=").Append(b2 != null).Append("; ");
if (b1 != null) sb.Append("label1='").Append(b1.GetComponentInChildren<UnityEngine.UI.Text>().text).Append("'; ");
if (b2 != null) sb.Append("label2='").Append(b2.GetComponentInChildren<UnityEngine.UI.Text>().text).Append("'; ");
if (menu != null)
{
    var so = new UnityEditor.SerializedObject(menu);
    sb.Append("s1='").Append(so.FindProperty("_scene1Name").stringValue).Append("'; ");
    sb.Append("s2='").Append(so.FindProperty("_scene2Name").stringValue).Append("'; ");
    var p1 = so.FindProperty("_scene1Button");
    var p2 = so.FindProperty("_scene2Button");
    sb.Append("bound1=").Append(p1 != null && p1.objectReferenceValue != null)
      .Append("; bound2=").Append(p2 != null && p2.objectReferenceValue != null);
}
var es = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
sb.Append("; eventSystem=").Append(es != null);
return sb.ToString();
