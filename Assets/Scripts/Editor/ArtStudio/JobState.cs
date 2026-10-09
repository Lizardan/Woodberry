namespace Woodberry.Editor.ArtStudio
{
    /// <summary>Состояние задания очереди.</summary>
    public enum JobState
    {
        Queued = 0,
        Running = 1,
        Done = 2,
        Failed = 3,
        Cancelled = 4
    }
}
