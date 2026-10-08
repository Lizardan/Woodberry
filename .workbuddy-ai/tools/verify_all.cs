var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.Append("scene=").Append(scene.name).Append("; ");

var menu = UnityEngine.Object.FindFirstObjectByType<Woodberry.UI.LightingMenu>();
sb.Append("lightingMenu=").Append(menu != null ? "yes" : "no").Append("; ");

var b1 = UnityEngine.GameObject.Find("Scene1Button");
var b2 = UnityEngine.GameObject.Find("Scene2Button");
sb.Append("btn1=").Append(b1 != null).Append("; btn2=").Append(b2 != null).Append("; ");
if (b1 != null) sb.Append("label1='").Append(b1.GetComponentInChildren<UnityEngine.UI.Text>().text).Append("'; ");
if (b2 != null) sb.Append("label2='").Append(b2.GetComponentInChildren<UnityEngine.UI.Text>().text).Append("'; ");

if (menu != null)
{
    var so = new UnityEditor.SerializedObject(menu);
    sb.Append("s1='").Append(so.FindProperty("_scene1Name").stringValue)
      .Append("'; s2='").Append(so.FindProperty("_scene2Name").stringValue).Append("'; ");
    sb.Append("bound1=").Append(so.FindProperty("_scene1Button").objectReferenceValue != null)
      .Append("; bound2=").Append(so.FindProperty("_scene2Button").objectReferenceValue != null).Append("; ");
}

// Ввод: сервис зарегистрирован и включён?
bool hasInput = Woodberry.Core.ServiceRegistry.TryGet<Woodberry.Core.Input.IInputReader>(out var reader);
sb.Append("inputRegistered=").Append(hasInput).Append("; ");

var pc = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Player.PlayerController>();
sb.Append("player=").Append(pc != null).Append("; ");
if (pc != null)
{
    var f = pc.GetType().GetField("_input", System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance);
    var val = f != null ? f.GetValue(pc) : null;
    sb.Append("playerInput=").Append(val != null ? "bound" : "NULL").Append("; ");

    var fb = pc.GetType().GetField("_bodyMissing", System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance);
    sb.Append("bodyMissing=").Append(fb != null && (bool)fb.GetValue(pc)).Append("; ");
}

var boot = UnityEngine.Object.FindFirstObjectByType<Woodberry.Lighting.LightingDemoBootstrap>();
sb.Append("demoBootstrap=").Append(boot != null);

return sb.ToString();
