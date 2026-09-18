namespace Borea.Core.History;

public enum TaskState
{
    /// <summary>The task has not changed anything yet, for example while it plans.</summary>
    Waiting,
    Running,
    Finished,
    Stopped,
    Failed,
}
