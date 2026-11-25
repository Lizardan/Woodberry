// Что окно думает про очередь и прогресс.
var wins = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow));
var window = (UnityEditor.EditorWindow)wins[0];
var type = typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow);
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

object client = type.GetField("_client", flags).GetValue(window);
object state = client.GetType().GetProperty("State").GetValue(client);
object jobs = state.GetType().GetProperty("Jobs").GetValue(state);
var jobsType = jobs.GetType();

System.Collections.IList list = (System.Collections.IList)jobsType.GetProperty("Jobs").GetValue(jobs);
var sb = new System.Text.StringBuilder();
sb.Append("окон=").Append(wins.Length)
  .Append(" | всего=").Append(jobsType.GetProperty("Total").GetValue(jobs))
  .Append(" готово=").Append(jobsType.GetProperty("Finished").GetValue(jobs))
  .Append(" идёт=").Append(jobsType.GetProperty("IsIdle").GetValue(jobs))
  .Append(" | running=").Append(jobsType.GetProperty("Running").GetValue(jobs) == null ? "нет" : "есть")
  .Append(" | задачи:");

for (int i = 0; i < list.Count; i++)
{
    object j = list[i];
    var jt = j.GetType();
    sb.Append(" [").Append(jt.GetProperty("StandId").GetValue(j))
      .Append(" ").Append(jt.GetProperty("State").GetValue(j))
      .Append(" ").Append(jt.GetProperty("Percent").GetValue(j)).Append("%]");
}

sb.Append(" | _generationRunning=").Append(type.GetField("_generationRunning", flags).GetValue(window));
sb.Append(" _wasGenerating=").Append(type.GetField("_wasGenerating", flags).GetValue(window));

return sb.ToString();
