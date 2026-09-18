namespace Borea.Storage.History;

internal sealed class TaskHistoryDocumentDto
{
    public int FormatVersion { get; set; }

    public List<TaskHistoryEntryDto?>? Tasks { get; set; }
}

internal sealed class TaskHistoryVersionDto
{
    public int FormatVersion { get; set; }
}

internal sealed class TaskHistoryEntryDto
{
    public string? Kind { get; set; }

    public string? State { get; set; }

    public string? Subject { get; set; }

    public Guid? InstanceId { get; set; }

    public string? InstanceName { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public string? FailureReason { get; set; }

    public string? ContentId { get; set; }

    public string? Version { get; set; }
}
