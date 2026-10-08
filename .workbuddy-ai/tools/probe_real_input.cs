var sb = new System.Text.StringBuilder();

Woodberry.Core.ServiceRegistry.TryGet<Woodberry.Core.Input.IInputReader>(out var regReader);
sb.Append("reader=").Append(regReader == null ? "null" : regReader.GetType().Name);

bool enabled = false;
if (regReader != null)
{
    var f = regReader.GetType().GetField("_enabled",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    if (f != null)
    {
        enabled = (bool)f.GetValue(regReader);
    }
}
sb.Append("; enabled=").Append(enabled);

var pc = UnityEngine.Object.FindFirstObjectByType<Woodberry.Gameplay.Player.PlayerController>();
if (pc == null)
{
    sb.Append("; player=NULL");
}
else
{
    var fi = typeof(Woodberry.Gameplay.Player.PlayerController).GetField("_input",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    object bound = fi == null ? null : fi.GetValue(pc);
    sb.Append("; playerInput=").Append(bound == null ? "null" : bound.GetType().Name);
    sb.Append("; sameInstance=").Append(ReferenceEquals(bound, regReader));
    sb.Append("; moveNow=").Append(bound == null ? "n/a" :
        ((Woodberry.Core.Input.IInputReader)bound).Move.ToString("0.00"));
}

sb.Append("; scene=").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
sb.Append("; playing=").Append(UnityEditor.EditorApplication.isPlaying);
return sb.ToString();
