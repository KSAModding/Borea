namespace Borea.Core.History;

public enum TaskState
{
    /// <summary>The task has not changed anything yet, for example while it plans.</summary>
    Waiting,
    Running,

    /// <summary>The download of a running install waits for its resume.</summary>
    Paused,
    Finished,
    Stopped,
    Failed,
}
