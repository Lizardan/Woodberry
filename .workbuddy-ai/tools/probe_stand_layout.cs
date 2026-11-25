// Открыть страницу генератора, поднять окно и вернуть его прямоугольник.
var wins = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow));
if (wins == null || wins.Length == 0)
{
    return "окно не открыто";
}

var window = (UnityEditor.EditorWindow)wins[0];
var type = typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

type.GetMethod("SetGeneratorPanel", flags).Invoke(window, new object[] { true });
window.position = new UnityEngine.Rect(60f, 60f, 1100f, 760f);
window.ShowTab();
window.Focus();
window.Repaint();

UnityEngine.UIElements.VisualElement root = window.rootVisualElement;
UnityEngine.UIElements.ProgressBar overall =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.ProgressBar>(root, "generationBar").First();
UnityEngine.UIElements.ProgressBar frame =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.ProgressBar>(root, "frameBar").First();
UnityEngine.UIElements.Button button =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Button>(root, "standButton").First();

UnityEngine.Rect or = overall.worldBound;
UnityEngine.Rect fr = frame.worldBound;
UnityEngine.Rect br = button.worldBound;

return "окно=" + window.position.x + "," + window.position.y
    + "," + window.position.width + "," + window.position.height
    + " | общая полоса: x=" + or.x.ToString("0") + " y=" + or.y.ToString("0")
    + " w=" + or.width.ToString("0") + " h=" + or.height.ToString("0")
    + " | кадровая: x=" + fr.x.ToString("0") + " y=" + fr.y.ToString("0")
    + " w=" + fr.width.ToString("0") + " h=" + fr.height.ToString("0")
    + " | кнопка: w=" + br.width.ToString("0") + " h=" + br.height.ToString("0")
    + " text=\"" + button.text + "\"";
