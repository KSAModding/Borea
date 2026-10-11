namespace Borea.Core.Stewardship;

/// <summary>
/// The steward actions on one takedown or id dispute report of content-index, with the token of the signed-in account.
/// POLICY.md of content-index asks a steward to answer on the report, to close it with a reason, and lets any steward reopen it on new evidence.
/// GitHub refuses each one with <see cref="StewardFailure.Forbidden"/> while the Borea App has no Issues permission.
/// </summary>
public interface IReportActions
{
    /// <summary>
    /// Whether the installation of the Borea App on the owner of content-index has the Issues permission to write, so an action can work.
    /// False when signed out, or when the check fails. GitHub can still refuse an action, for example when the account cannot write the issues.
    /// </summary>
    Task<bool> CanActAsync(CancellationToken cancellationToken = default);

    /// <summary>Posts the comment on the report, open or closed.</summary>
    /// <exception cref="ArgumentException">The report is no report of content-index, or the comment is empty.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the comment.</exception>
    Task CommentAsync(IndexReport report, string comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the comment and closes the report with the reason. When the last comment on the report is already this comment
    /// of the signed-in account, such as after a close that failed after its comment, it only closes, so a retry does not post the comment twice.
    /// </summary>
    /// <exception cref="ArgumentException">The report is no report of content-index, or the comment is empty.</exception>
    /// <exception cref="ReportRefusedException">The report is closed already. Nothing was sent.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the change.</exception>
    Task CloseAsync(IndexReport report, IndexReportCloseReason reason, string comment, CancellationToken cancellationToken = default);

    /// <summary>Posts the comment, which names the new evidence, and reopens the report. A retry does not post the comment twice.</summary>
    /// <exception cref="ArgumentException">The report is no report of content-index, or the comment is empty.</exception>
    /// <exception cref="ReportRefusedException">The report is open already. Nothing was sent.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the change.</exception>
    Task ReopenAsync(IndexReport report, string comment, CancellationToken cancellationToken = default);
}

/// <summary>Why Borea does not close or reopen a report.</summary>
public enum ReportRefusal
{
    /// <summary>Somebody closed the report since Borea read it.</summary>
    Closed,

    /// <summary>Somebody reopened the report since Borea read it.</summary>
    Open,
}

public sealed class ReportRefusedException(ReportRefusal refusal) : Exception(refusal.ToString())
{
    public ReportRefusal Refusal { get; } = refusal;
}
