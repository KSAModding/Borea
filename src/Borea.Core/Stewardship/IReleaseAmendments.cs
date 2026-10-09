using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// The steward amendments of the release files of content-index-releases. Each amendment of one listing goes to a steward branch
/// cut from the base branch, in one commit, and its pull request waits in the queue until validate passes.
/// </summary>
public interface IReleaseAmendments
{
    /// <summary>The stamped versions of the listing on the base branch, newest first.</summary>
    /// <exception cref="ReleaseAmendmentRefusedException">The listing has no stamped release, or a file name is no version.</exception>
    /// <exception cref="StewardException">GitHub could not be read, or the account is no steward of content-index-releases.</exception>
    Task<IReadOnlyList<string>> ReleasesAsync(string listingId, CancellationToken cancellationToken = default);

    /// <summary>Every stamped release file of the listing on the base branch, newest first, with the game release list. It writes nothing.</summary>
    /// <exception cref="ReleaseAmendmentRefusedException">The listing has no stamped release, a file name is no version, or a file is no UTF-8 text.</exception>
    /// <exception cref="StewardException">GitHub could not be read, or the account is no steward of content-index-releases.</exception>
    Task<ReleaseFiles> ReleaseFilesAsync(string listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the mod.toml of the release archive declares, read from the download URL or a mirror whose bytes match the stamped sha256, as the
    /// checks of content-index-releases read it before they accept the removal of an authored dependency entry. It writes nothing.
    /// </summary>
    /// <exception cref="ReleaseAmendmentRefusedException">The archive could not be downloaded or read (<see cref="ReleaseAmendmentRefusal.UnreadableArchive"/>).</exception>
    Task<IReadOnlyList<LocalModDependency>> DeclaredDependenciesAsync(ReleaseFile file, CancellationToken cancellationToken = default);

    /// <summary>Reads the selected release files at the tip of the base branch and amends them as tools/amend.py would. It writes nothing.</summary>
    /// <exception cref="ReleaseAmendmentRefusedException">The amendment is refused, as tools/amend.py or the checks would refuse it.</exception>
    /// <exception cref="StewardException">GitHub could not be read, or the account is no steward of content-index-releases.</exception>
    Task<ReleaseAmendmentPreview> PreviewAsync(ReleaseAmendmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the files again, and only when the amendment is still the one of <paramref name="preview"/>, commits the changed files
    /// to a steward branch and opens the pull request.
    /// </summary>
    /// <exception cref="ReleaseAmendmentChangedException">A selected file changed on the base branch since the preview. Nothing was written.</exception>
    /// <exception cref="ReleaseAmendmentRefusedException">The amendment is refused, or no file changes. Nothing was written.</exception>
    /// <exception cref="StewardException">A request failed.</exception>
    Task<ReleaseAmendmentPullRequest> OpenAsync(ReleaseAmendmentPreview preview, CancellationToken cancellationToken = default);
}

/// <param name="Title">The title of the pull request, such as "Amend MyMod 1.2.0".</param>
public sealed record ReleaseAmendmentPullRequest(int Number, Uri Url, string Title);

/// <summary>The release files changed on the base branch since the preview, so the steward checks <see cref="Current"/> first.</summary>
public sealed class ReleaseAmendmentChangedException(ReleaseAmendmentPreview current)
    : Exception("The release files changed on the base branch since the preview.")
{
    /// <summary>The amendment on the tip of the base branch now.</summary>
    public ReleaseAmendmentPreview Current { get; } = current;
}
