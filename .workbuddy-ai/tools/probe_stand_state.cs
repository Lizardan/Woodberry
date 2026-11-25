// Состояние стенда глазами окна: порт, драйвер, фаза кнопки.
var wins = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow));
if (wins == null || wins.Length == 0)
{
    return "окно не открыто";
}

var window = (UnityEditor.EditorWindow)wins[0];
var type = typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

object client = type.GetField("_client", flags).GetValue(window);
if (client == null)
{
    return "клиент не создан";
}

var clientType = client.GetType();
var upProp = clientType.GetProperty("IsServerUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
var aliveProp = clientType.GetProperty("IsDriverAlive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

var stateProp = clientType.GetProperty("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
object state = stateProp.GetValue(client);
var serverStateField = state.GetType().GetProperty("ServerState");

var button = UnityEngine.UIElements.UQueryExtensions
    .Query<UnityEngine.UIElements.Button>(window.rootVisualElement, "standButton").First();
object phase = type.GetField("_standPhase", flags).GetValue(window);

return "порт открыт=" + upProp.GetValue(client)
    + " | драйвер жив=" + aliveProp.GetValue(client)
    + " | ServerState=\"" + serverStateField.GetValue(state) + "\""
    + " | кнопка=\"" + button.text + "\" enabled=" + button.enabledSelf
    + " | phase=" + phase;
