using System;
using System.Collections.Generic;
using System.Globalization;
using Borea.App.Localization;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// Turns install reports into the two lines under a progress bar: what is
/// happening ("Downloading MeasureTools 1.1.10 (2 of 3)") and how far along it
/// is ("12.4 of 38.0 MB, about 2 min left"). One instance follows one install.
/// </summary>
internal sealed class InstallProgressText
{
    /// <summary>How far back the download rate looks, so a short stall does not swing the estimate.</summary>
    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(5);

    /// <summary>Below this much history the rate is a guess, and no estimate is shown.</summary>
    private static readonly TimeSpan MinimumRateSpan = TimeSpan.FromSeconds(1);

    private readonly LocalizationService _localization;
    private readonly TimeProvider _time;
    private readonly Queue<(TimeSpan At, long Bytes)> _samples = new();
    private readonly long _started;
    private (int Step, string ModId)? _operation;

    public InstallProgressText(LocalizationService localization, TimeProvider? time = null)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _time = time ?? TimeProvider.System;
        _started = _time.GetTimestamp();
    }

    public string? Status { get; private set; }

    public string? Detail { get; private set; }

    public double Percent { get; private set; }

    public bool HasPercent { get; private set; }

    public bool IsPaused { get; private set; }

    public void Report(InstallProgress value)
    {
        // a new operation of the plan starts its own rate
        if (_operation != (value.Step, value.ModId))
        {
            _operation = (value.Step, value.ModId);
            _samples.Clear();
            Percent = 0;
        }

        IsPaused = value.Download is { IsPaused: true };
        Status = StatusOf(value);

        if (value is { Phase: InstallPhase.Downloading, Download: { } bytes })
        {
            Percent = bytes.PercentComplete;
            HasPercent = bytes.TotalBytes > 0;
            Detail = DetailOf(bytes);
        }
        else
        {
            HasPercent = false;
            Detail = null;
        }
    }

    private string StatusOf(InstallProgress value)
    {
        var name = $"{value.ModId} {value.Version}";
        var text = value.Phase switch
        {
            InstallPhase.Downloading when IsPaused => _localization.FormatInstallPaused(name),
            InstallPhase.Downloading => _localization.FormatInstallDownloading(name),
            InstallPhase.Extracting => _localization.FormatInstallExtracting(name),
            InstallPhase.Configuring => _localization.FormatInstallConfiguring(name),
            _ => _localization.FormatInstallFinishing(name),
        };

        return value.StepCount > 1 ? _localization.FormatInstallStep(text, value.Step, value.StepCount) : text;
    }

    private string? DetailOf(DownloadProgress bytes)
    {
        var size = bytes.TotalBytes <= 0
            ? Megabytes(bytes.BytesDownloaded)
            : _localization.FormatInstallSize(Number(bytes.BytesDownloaded), Megabytes(bytes.TotalBytes));
        if (bytes.IsPaused)
        {
            _samples.Clear();
            return size;
        }

        var now = _time.GetElapsedTime(_started);
        _samples.Enqueue((now, bytes.BytesDownloaded));
        while (_samples.Count > 2 && now - _samples.Peek().At > RateWindow)
            _samples.Dequeue();

        var left = bytes.TotalBytes > 0 ? TimeLeft(now, bytes) : null;
        return left is null ? size : $"{size}, {left}";
    }

    private string? TimeLeft(TimeSpan now, DownloadProgress bytes)
    {
        var (firstAt, firstBytes) = _samples.Peek();
        var span = now - firstAt;
        var gained = bytes.BytesDownloaded - firstBytes;
        if (span < MinimumRateSpan || gained <= 0)
            return null;

        var seconds = (bytes.TotalBytes - bytes.BytesDownloaded) / (gained / span.TotalSeconds);
        if (seconds < 1)
            return null;

        // rounded coarsely on purpose, a precise countdown only draws attention to its jitter
        return seconds < 60
            ? _localization.FormatInstallSecondsLeft(Math.Max(5, (int)Math.Ceiling(seconds / 5) * 5))
            : _localization.FormatInstallMinutesLeft((int)Math.Ceiling(seconds / 60));
    }

    // "12.4 of 38.0 MB": the unit once, after the total. Decimal megabytes, so the label is accurate.
    private const double BytesPerMegabyte = 1_000_000;

    internal static string Number(long bytes) =>
        (bytes / BytesPerMegabyte).ToString("0.0", CultureInfo.CurrentCulture);

    private static string Megabytes(long bytes) => Number(bytes) + " MB";
}
