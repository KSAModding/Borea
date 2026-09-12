namespace Borea.Core.Index;

public class IndexStatus
{
    public IndexStatusState State { get; }

    public DateTime? Since { get; }

    public string? Reason { get; }

    public IndexStatus(IndexStatusState state, string? since = null, string? reason = null)
    {
        State = state;
        Reason = reason;
        if (since is not null && DateTime.TryParse(since, out DateTime dateTime))
        {
            Since = dateTime;
        }
        else
        {
            Since = null;
        }
    }
}
