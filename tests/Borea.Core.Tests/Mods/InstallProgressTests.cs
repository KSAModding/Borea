using Borea.Core.Mods;

namespace Borea.Core.Tests.Mods;

public sealed class InstallProgressTests
{
    [Fact]
    public void Of_TakesTheIdAndVersionOfTheRelease()
    {
        var release = TestFixtures.SampleVersionMetadata(version: "1.2.3");

        var progress = InstallProgress.Of(release, InstallPhase.Extracting);

        Assert.Equal((release.ModId, release.Version, InstallPhase.Extracting, 1, 1), (progress.ModId, progress.Version, progress.Phase, progress.Step, progress.StepCount));
        Assert.Null(progress.Download);
    }

    [Fact]
    public void ForDownload_TurnsBytesIntoDownloadingReports()
    {
        var release = TestFixtures.SampleVersionMetadata();
        var reports = new List<InstallProgress>();
        IProgress<InstallProgress> listener = new SynchronousProgress<InstallProgress>(reports.Add);

        listener.ForDownload(release)!.Report(new DownloadProgress(5, 10));
        listener.Report(release, InstallPhase.Finishing);

        Assert.Equal([InstallPhase.Downloading, InstallPhase.Finishing], reports.Select(report => report.Phase));
        Assert.Equal(50, reports[0].Download!.Value.PercentComplete);
    }

    [Fact]
    public void WithoutAListener_NothingIsBuiltOrReported()
    {
        IProgress<InstallProgress>? none = null;

        Assert.Null(none.ForDownload(TestFixtures.SampleVersionMetadata()));
        none.Report(TestFixtures.SampleVersionMetadata(), InstallPhase.Extracting);
    }
}
