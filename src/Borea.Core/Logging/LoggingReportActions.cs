using System.Globalization;
using Borea.Core.Stewardship;

namespace Borea.Core.Logging;

public sealed class LoggingReportActions : IReportActions
{
    private readonly IBoreaLog _log;

    public IReportActions Inner { get; }

    public LoggingReportActions(IReportActions inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<bool> CanActAsync(CancellationToken cancellationToken = default) => Inner.CanActAsync(cancellationToken);

    public Task CommentAsync(IndexReport report, string comment, CancellationToken cancellationToken = default) =>
        LogAsync(report, "Comment", Inner.CommentAsync(report, comment, cancellationToken));

    public Task CloseAsync(IndexReport report, IndexReportCloseReason reason, string comment, CancellationToken cancellationToken = default) =>
        LogAsync(report, reason == IndexReportCloseReason.Completed ? "Close as completed" : "Close as not planned", Inner.CloseAsync(report, reason, comment, cancellationToken));

    public Task ReopenAsync(IndexReport report, string comment, CancellationToken cancellationToken = default) =>
        LogAsync(report, "Reopen", Inner.ReopenAsync(report, comment, cancellationToken));

    private async Task LogAsync(IndexReport report, string action, Task run)
    {
        ArgumentNullException.ThrowIfNull(report);
        var target = $"report {IndexReport.Repository} #{report.Number.ToString(CultureInfo.InvariantCulture)}";
        try
        {
            await run.ConfigureAwait(false);
            _log.Write($"{action} of {target} done.");
        }
        catch (Exception exception) when (exception is StewardException or ReportRefusedException)
        {
            _log.Write($"{action} of {target} failed. {exception.Message}");
            throw;
        }
    }
}
