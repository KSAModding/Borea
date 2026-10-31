using System.Globalization;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// A mod author's request to take their mod out of a pack (RFC 0080). The steward retracts every accepted version of the pack that
/// pins the mod, with one retracted entry for each version and a reason that names the request, in one pull request.
/// </summary>
/// <param name="Report">The number of the takedown in content-index that asks for it.</param>
public sealed record PackLeave(string PackId, string ModId, int Report)
{
    // orders by SemVer precedence, and a file name that is no version after the others by its text
    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create((left, right) =>
    {
        var leftParsed = ModVersion.TryParse(left, out var leftVersion);
        var rightParsed = ModVersion.TryParse(right, out var rightVersion);
        var compare = leftParsed && rightParsed ? leftVersion.CompareTo(rightVersion) : rightParsed.CompareTo(leftParsed);
        return compare != 0 ? compare : string.CompareOrdinal(left, right);
    });

    /// <summary>The account that filed the report, whose ownership proof of the listing of the mod is checked, or null when GitHub names none.</summary>
    public GitHubAccount? Reporter { get; init; }

    /// <summary>The steward confirmed by the forums account of the listing that the reporter owns the mod, because the proof did not show it.</summary>
    public bool ConfirmedByForums { get; init; }

    public Uri ReportUrl => new($"https://github.com/{IndexReport.Repository}/issues/{Report.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>The reason of each retracted entry, which names the request.</summary>
    public string Reason => $"The author of {ModId} asked to take it out of this pack in {ReportUrl.AbsoluteUri}";

    /// <summary>The title of the pull request and the commit message.</summary>
    public string Title => $"Retract every version of {PackId} that pins {ModId}";

    /// <summary>The branch of content-index the change goes to.</summary>
    public string Branch =>
        $"{IndexStatusChange.BranchPrefix}{IndexStatusEntry.Retracted}-{PackId.ToLowerInvariant()}-without-{ModId.ToLowerInvariant()}";

    /// <summary>The retracted entry of one version, whose reason names the report.</summary>
    public IndexStatusChange ChangeOf(string version) => IndexStatusChange.Retract(PackId, version, Reason);

    /// <summary>
    /// The versions of the pack to retract, the ones that pin the mod and are retracted already, and why nothing can be retracted.
    /// It works as tools/check_status.py keys a retracted entry: by the pack id without case and the version as its file name spells it.
    /// </summary>
    /// <param name="versions">Each version of the pack with the ids of the mods it pins.</param>
    /// <param name="entries">The entries of index-status.toml.</param>
    public PackLeavePlan Plan(IReadOnlyList<PackVersionPins> versions, IReadOnlyList<IndexStatusEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(entries);
        var pinning = versions.Where(version => version.Mods.Contains(ModId, ModIds.Comparer)).Select(version => version.Version).Order(VersionComparer).ToList();
        var retracted = pinning.Where(version => entries.Any(entry => entry.State == IndexStatusEntry.Retracted && ModIds.Equals(entry.Id, PackId) && entry.Version == version)).ToList();
        var open = pinning.Except(retracted, StringComparer.Ordinal).ToList();
        return new PackLeavePlan(open, retracted, open.Count == 0 ? IndexStatusRefusal.NotPinned : null);
    }

    /// <summary>The body of the pull request. It says how the steward knows that the reporter owns the mod, and mentions the owners of the pack.</summary>
    /// <param name="versions">The versions that the pull request retracts.</param>
    /// <param name="proven">The ownership proof of the listing names the reporter, and not only the steward's look at the forums account.</param>
    /// <param name="owners">The logins that own the pack, without the steward who opens it.</param>
    public string Body(IReadOnlyCollection<string> versions, bool proven, IReadOnlyCollection<string> owners)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(owners);
        var reporter = Reporter is { } account ? "@" + account.Login : "The reporter";
        var body = new StringBuilder($"Sets `{IndexStatusEntry.Retracted}` on {(versions.Count == 1 ? "version" : "versions")} ")
            .Append(string.Join(", ", versions.Select(version => $"`{version}`")))
            .Append($" of `{PackId}` in `{IndexStatusDocument.Path}`, because {(versions.Count == 1 ? "it pins" : "each of them pins")} `{ModId}`.")
            .Append("\n\nReason: ").Append(Reason)
            .Append("\n\n").Append(proven
                ? $"{reporter} owns `{ModId}` by the ownership proof of its listing."
                : $"A steward confirmed by the forums account of its listing that {(Reporter is null ? "the reporter" : reporter)} owns `{ModId}`.")
            .Append("\n\nCloses #").Append(Report.ToString(CultureInfo.InvariantCulture));
        if (OwnerMention.Of(owners, PackId) is { } mention)
            body.Append("\n\n").Append(mention);

        return body.ToString();
    }
}

/// <param name="Version">The version as the file name of the pack spells it.</param>
/// <param name="Mods">The ids that its [[mods]] entries pin.</param>
public sealed record PackVersionPins(string Version, IReadOnlyList<string> Mods);

/// <param name="Versions">The versions to retract, which pin the mod and are not retracted yet, oldest first.</param>
/// <param name="Retracted">The versions that pin the mod and are retracted already, oldest first.</param>
/// <param name="Refusal">Why nothing can be retracted, or null.</param>
public sealed record PackLeavePlan(IReadOnlyList<string> Versions, IReadOnlyList<string> Retracted, IndexStatusRefusal? Refusal);

/// <summary>What a steward should know before the pull request of a <see cref="PackLeave"/> opens.</summary>
/// <param name="Refusal">Why Borea would not open it, or null.</param>
/// <param name="Versions">The versions it retracts, oldest first.</param>
/// <param name="Retracted">The versions that pin the mod and are retracted already.</param>
/// <param name="Ownership">The ownership proof of the reporter for the listing of the mod, with the forums thread of the listing.</param>
/// <param name="OpenPullRequests">Open pull requests that change index-status.toml too, or null when they could not be read.</param>
/// <param name="Owners">The logins that own the pack, without the signed-in steward.</param>
/// <param name="IsOwner">The signed-in steward owns the pack.</param>
public sealed record PackLeaveCheck(
    IndexStatusRefusal? Refusal,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Retracted,
    ListingOwnership Ownership,
    IReadOnlyList<IndexStatusPullRequest>? OpenPullRequests,
    IReadOnlyList<string> Owners,
    bool IsOwner)
{
    /// <summary>The ownership proof of the listing names the reporter, so the steward need not confirm it by the forums account.</summary>
    public bool IsProven => Ownership.State == ListingOwnershipState.Verified;
}
