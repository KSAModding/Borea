using System.Globalization;
using Borea.App.Localization;
using Borea.App.ViewModels;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class InstallProgressTextTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly LocalizationService _localization = new(CultureInfo.GetCultureInfo("en"));
    private readonly ManualClock _clock = new();

    public InstallProgressTextTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Resources.Culture = _originalUiCulture;
    }

    private static InstallProgress Downloading(long bytes, long total, int step = 1, int stepCount = 1, string modId = "MeasureTools") =>
        new(modId, ModVersion.Parse("1.1.10"), InstallPhase.Downloading, new DownloadProgress(bytes, total), step, stepCount);

    [Fact]
    public void Status_NamesThePhaseTheContentAndItsPlaceInThePlan()
    {
        var text = new InstallProgressText(_localization, _clock);

        text.Report(Downloading(0, 10 * Megabyte, step: 2, stepCount: 3));
        Assert.Equal("Downloading MeasureTools 1.1.10 (2 of 3)", text.Status);

        text.Report(Downloading(0, 10 * Megabyte) with { Phase = InstallPhase.Extracting, Download = null });
        Assert.Equal("Unpacking MeasureTools 1.1.10", text.Status);
        Assert.Null(text.Detail);
    }

    [Fact]
    public void Detail_ShowsTheSizeAtOnceAndTheTimeLeftOnceTheRateIsKnown()
    {
        var text = new InstallProgressText(_localization, _clock);

        text.Report(Downloading(Megabyte, 38 * Megabyte));
        Assert.Equal("1.0 of 38.0 MB", text.Detail);

        // one megabyte a second, 36 to go
        _clock.Advance(TimeSpan.FromSeconds(1));
        text.Report(Downloading(2 * Megabyte, 38 * Megabyte));

        Assert.Equal("2.0 of 38.0 MB, about 40 s left", text.Detail);
        Assert.Equal(2d / 38 * 100, text.Percent, precision: 6);
    }

    [Fact]
    public void Detail_LongDownloadsCountInMinutes()
    {
        var text = new InstallProgressText(_localization, _clock);

        text.Report(Downloading(0, 200 * Megabyte));
        _clock.Advance(TimeSpan.FromSeconds(2));
        text.Report(Downloading(Megabyte, 200 * Megabyte));

        Assert.EndsWith("about 7 min left", text.Detail);
    }

    [Fact]
    public void Detail_WithoutAKnownSize_ShowsTheBytesSoFar()
    {
        var text = new InstallProgressText(_localization, _clock);

        text.Report(Downloading(3 * Megabyte, 0));

        Assert.Equal("3.0 MB", text.Detail);
        Assert.Equal(0, text.Percent);
    }

    [Fact]
    public void NextOperation_StartsItsOwnRate()
    {
        var text = new InstallProgressText(_localization, _clock);
        text.Report(Downloading(0, 10 * Megabyte, 1, 2, "library"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        text.Report(Downloading(10 * Megabyte, 10 * Megabyte, 1, 2, "library"));

        text.Report(Downloading(Megabyte, 50 * Megabyte, 2, 2));

        Assert.Equal("1.0 of 50.0 MB", text.Detail);
        Assert.Equal("Downloading MeasureTools 1.1.10 (2 of 2)", text.Status);
    }

    [Fact]
    public void German_UsesTheGermanWording()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de");
        var localization = new LocalizationService(CultureInfo.GetCultureInfo("de"));
        var text = new InstallProgressText(localization, _clock);

        text.Report(Downloading(Megabyte, 38 * Megabyte, 1, 2));

        Assert.Equal("MeasureTools 1.1.10 wird heruntergeladen (1 von 2)", text.Status);
        Assert.Equal("1,0 von 38,0 MB", text.Detail);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
