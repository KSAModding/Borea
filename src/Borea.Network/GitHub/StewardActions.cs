namespace Borea.Network.GitHub;

/// <summary>What the steward actions on GitHub share: the failure that reaches the App, and how a retry finds the comment that it posted before.</summary>
internal static class StewardActions
{
    public static async Task ActAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
    }

    /// <summary>Compares a comment on GitHub with the comment that Borea sends, without the kind of line end and the spaces around the text.</summary>
    public static bool SameText(string? posted, string body) =>
        posted is not null && string.Equals(posted.ReplaceLineEndings("\n").Trim(), body.ReplaceLineEndings("\n"), StringComparison.Ordinal);
}
