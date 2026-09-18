namespace Borea.Core.History;

/// <summary>One task that ended, as the history of Borea.App keeps it.</summary>
/// <param name="Subject">What the task acted on, such as a mod name. Null when the kind says it all.</param>
/// <param name="InstanceName">The name the instance had when the task ran.</param>
/// <param name="ContentId">The mod or pack that a failed install or update plans again.</param>
/// <param name="Version">The exact release that a failed install plans again. Null for the newest release.</param>
public sealed record TaskHistoryEntry(
    TaskKind Kind,
    TaskState State,
    string? Subject,
    Guid? InstanceId,
    string? InstanceName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? FailureReason = null,
    string? ContentId = null,
    string? Version = null)
{
    public const int MaxEntries = 100;
}
