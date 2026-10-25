using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.UserContent;

/// <summary>
/// Installs, updates and removes one vehicle or save folder that Borea records (RFC 0085). Every outcome
/// other than <see cref="UserContentOutcome.Installed"/>, <see cref="UserContentOutcome.Updated"/> and
/// <see cref="UserContentOutcome.Removed"/> changes nothing. A step that fails throws and leaves the
/// folders under Vehicles and saves and the records as they were.
/// </summary>
public interface IUserContentInstaller
{
    /// <summary>
    /// What <see cref="InstallAsync"/> would do, without a write: <see cref="UserContentOutcome.Ready"/>
    /// names the folder it would use.
    /// </summary>
    /// <param name="name">A new name the player chose, or null for the name of the release.</param>
    Task<UserContentResult> CheckInstallAsync(Guid instanceId, ModVersionMetadata release, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="UpdateAsync"/> would do, without a write and without a download:
    /// <see cref="UserContentOutcome.Ready"/> names the folder it would use.
    /// </summary>
    Task<UserContentResult> CheckUpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, CancellationToken cancellationToken = default);

    /// <param name="beforeMove">
    /// Runs after the archive is downloaded, unpacked and checked, and before anything under Vehicles
    /// or saves changes. The folder moves into place only when it returns.
    /// </param>
    /// <exception cref="DownloadFailedException">Every source failed or served other bytes.</exception>
    /// <exception cref="InvalidOperationException">The archive is not a folder the game reads.</exception>
    Task<UserContentResult> InstallAsync(Guid instanceId, ModVersionMetadata release, string? name, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a recorded folder with another release, and keeps the old folder in the backups.
    /// A name the player chose for the item stays, unless <paramref name="name"/> names another one.
    /// </summary>
    /// <param name="discardChanges">Whether files the player changed since the install may go into the backups.</param>
    Task<UserContentResult> UpdateAsync(Guid instanceId, InstalledContentRecord installed, ModVersionMetadata release, string? name, bool discardChanges, UserContentStep? beforeMove = null, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Moves a recorded folder into the backups and drops its record.</summary>
    Task<UserContentResult> RemoveAsync(Guid instanceId, InstalledContentRecord installed, bool discardChanges, CancellationToken cancellationToken = default);
}

/// <summary>A step that runs inside an install or update, such as the install of the mods the item needs.</summary>
public delegate Task UserContentStep(IProgress<InstallProgress>? progress, CancellationToken cancellationToken);

public enum UserContentOutcome
{
    Installed,
    Updated,
    Removed,

    /// <summary>The instance has a recorded folder of this listing, so an install is an update.</summary>
    AlreadyInstalled,

    /// <summary>
    /// A folder, or a meta.toml name, in the same kind folder equals the name in any letter case, so the
    /// game would show only one of the two. Choose another name.
    /// </summary>
    NameTaken,

    /// <summary>The game would change the name, see <see cref="GameSaveName"/>.</summary>
    InvalidName,

    /// <summary>The player changed files since the install. <see cref="UserContentResult.Changes"/> names them.</summary>
    Changed,

    /// <summary>A KSA or StarMap process runs, and Borea cannot tell which instance it uses.</summary>
    GameRunning,

    /// <summary>The record or its folder is gone.</summary>
    NotInstalled,

    /// <summary>A check found nothing in the way. <see cref="UserContentResult.FolderName"/> is the folder the change would use.</summary>
    Ready,
}

/// <param name="FolderName">The folder the outcome is about, under Vehicles or saves.</param>
/// <param name="Record">The record after the change, or the record that already exists.</param>
/// <param name="Changes">The changed files for <see cref="UserContentOutcome.Changed"/>.</param>
public sealed record UserContentResult(
    UserContentOutcome Outcome,
    string? FolderName = null,
    InstalledContentRecord? Record = null,
    InstalledContentChanges? Changes = null)
{
    /// <summary>The outcomes that changed files or records.</summary>
    public bool IsDone => Outcome is UserContentOutcome.Installed or UserContentOutcome.Updated or UserContentOutcome.Removed;
}
