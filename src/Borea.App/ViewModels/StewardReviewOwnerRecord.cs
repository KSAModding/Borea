using System.Globalization;
using Borea.Core.Stewardship;

namespace Borea.App.ViewModels;

/// <summary>
/// A change of a pack owner record: who owns the pack on the base branch and after the merge, by login and account id, and whether
/// the new account id still has the login that the record names.
/// </summary>
public sealed class StewardReviewOwnerRecord(MainViewModel owner, PullRequestOwnerRecord record)
{
    public PullRequestOwnerRecord Record { get; } = record;

    public string Path => Record.Path;

    public string KindText => owner.StewardQueueKindText(StewardQueueKind.OwnerRecord);

    public string? FirstClaimText => Record.IsFirstClaim ? owner.Localization.StewardOwnerRecordFirstClaim : null;

    public string BeforeText => Record.Before switch
    {
        null => owner.Localization.StewardOwnerRecordNoneBefore,
        { Owner: { } before } => owner.Localization.FormatStewardOwnerRecordBefore(before.Login, IdText(before.Id)),
        _ => owner.Localization.StewardOwnerRecordUnreadableBefore,
    };

    public string AfterText => Record.After switch
    {
        null => owner.Localization.StewardOwnerRecordRemoved,
        { Owner: { } after } => owner.Localization.FormatStewardOwnerRecordAfter(after.Login, IdText(after.Id)),
        _ => owner.Localization.StewardOwnerRecordUnreadableAfter,
    };

    /// <summary>The text of a record that names no owner, so the steward sees what is there.</summary>
    public string? RawBefore => Record.Before is { Owner: null } before ? before.Text ?? owner.Localization.StewardReviewNotText : null;

    public string? RawAfter => Record.After is { Owner: null } after ? after.Text ?? owner.Localization.StewardReviewNotText : null;

    /// <summary>Whether GitHub still gives the new account id the login of the record.</summary>
    public string? AccountText => (Record.Account, Record.After?.Owner) switch
    {
        ({ State: PackOwnerAccountState.Same }, { } after) => owner.Localization.FormatStewardOwnerAccountSame(IdText(after.Id), after.Login),
        ({ State: PackOwnerAccountState.OtherLogin, Login: { } now }, { } after) => owner.Localization.FormatStewardOwnerAccountOtherLogin(IdText(after.Id), now, after.Login),
        ({ State: PackOwnerAccountState.Gone }, { } after) => owner.Localization.FormatStewardOwnerAccountGone(IdText(after.Id)),
        ({ }, { } after) => owner.Localization.FormatStewardOwnerAccountUnknown(IdText(after.Id)),
        _ => null,
    };

    public bool IsAccountSame => Record.Account?.State == PackOwnerAccountState.Same;

    public bool IsAccountWrong => Record.Account?.State is PackOwnerAccountState.OtherLogin or PackOwnerAccountState.Gone;

    private static string IdText(long id) => id.ToString(CultureInfo.InvariantCulture);
}
