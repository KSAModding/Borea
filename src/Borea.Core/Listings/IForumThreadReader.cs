namespace Borea.Core.Listings;

/// <summary>Reads a thread of the KSA forums.</summary>
public interface IForumThreadReader
{
    /// <summary>The prefixes in front of the thread title, such as "Gameplay". Empty when the thread has none or could not be read.</summary>
    Task<IReadOnlyList<string>> GetPrefixesAsync(string threadUrl, CancellationToken cancellationToken = default);
}
