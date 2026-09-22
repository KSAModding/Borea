using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Cli.Tests;

public sealed class SelfUpdateCommandTests
{
    private const string NewVersion = "999.0.0";

    private static BoreaRelease Release(string version) =>
        new(ModVersion.Parse(version), "v" + version, "https://github.com/KSAModding/Borea/releases/tag/v" + version);

    [Fact]
    public async Task UpdateSelf_ANewerRelease_StagesItAndHandsOver()
    {
        using var host = new CliHost { ReleaseCheck = new FakeReleaseCheck(Release(NewVersion)) };
        var updater = new FakeSelfUpdater();
        host.SelfUpdater = updater;

        var run = await host.RunAsync("update-self");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(NewVersion, updater.Staged?.Version.ToString());
        Assert.True(updater.Installed);
        Assert.True(updater.HandedOver);
        Assert.Contains($"Borea {NewVersion} is installed in", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSelf_Check_ReportsTheReleaseAndChangesNothing()
    {
        using var host = new CliHost { ReleaseCheck = new FakeReleaseCheck(Release(NewVersion)) };
        var updater = new FakeSelfUpdater();
        host.SelfUpdater = updater;

        var run = await host.RunAsync("update-self", "--check", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("available", run.Json.GetProperty("state").GetString());
        Assert.Equal(NewVersion, run.Json.GetProperty("available").GetString());
        Assert.Null(updater.Staged);
    }

    [Fact]
    public async Task UpdateSelf_NoNewerRelease_SaysSoAndStagesNothing()
    {
        using var host = new CliHost { ReleaseCheck = new FakeReleaseCheck() };
        var updater = new FakeSelfUpdater();
        host.SelfUpdater = updater;

        var run = await host.RunAsync("update-self", "--json");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("current", run.Json.GetProperty("state").GetString());
        Assert.Equal("Stable", run.Json.GetProperty("channel").GetString());
        Assert.Null(updater.Staged);
    }

    [Fact]
    public async Task UpdateSelf_APackageManagedBuild_PointsAtTheManagerAndFails()
    {
        using var host = new CliHost { ReleaseCheck = new FakeReleaseCheck(Release(NewVersion)) };
        var updater = new FakeSelfUpdater { Readiness = new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged, "winget") };
        host.SelfUpdater = updater;

        var run = await host.RunAsync("update-self");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("winget installed this Borea build, so update it with winget.", run.Error, StringComparison.Ordinal);
        Assert.Null(updater.Staged);
    }

    [Fact]
    public async Task UpdateSelf_AFailedStage_SaysWhatWentWrongAndFails()
    {
        using var host = new CliHost { ReleaseCheck = new FakeReleaseCheck(Release(NewVersion)) };
        host.SelfUpdater = new FakeSelfUpdater
        {
            Failure = new SelfUpdateFailedException(SelfUpdateFailure.Checksum, "The archive does not match the checksums of the release."),
        };

        var run = await host.RunAsync("update-self");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("error: The archive does not match the checksums of the release.", run.Error, StringComparison.Ordinal);
    }

    private sealed class FakeReleaseCheck(params BoreaRelease[] releases) : IBoreaReleaseCheck
    {
        public BoreaUpdateChannel? Asked { get; private set; }

        public Task<IReadOnlyList<BoreaRelease>> GetReleasesAsync(BoreaUpdateChannel channel = BoreaUpdateChannel.Stable, CancellationToken cancellationToken = default)
        {
            Asked = channel;
            return Task.FromResult<IReadOnlyList<BoreaRelease>>(releases);
        }
    }

    private sealed class FakeSelfUpdater : ISelfUpdater
    {
        public SelfUpdateReadiness Readiness { get; set; } = SelfUpdateReadiness.Ready;

        public SelfUpdateFailedException? Failure { get; set; }

        public StagedSelfUpdate? Staged { get; private set; }

        public bool Installed { get; private set; }

        public bool HandedOver { get; private set; }

        public SelfUpdateReadiness GetReadiness() => Readiness;

        public Task<StagedSelfUpdate> StageAsync(BoreaRelease release, IProgress<SelfUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                throw Failure;

            progress?.Report(new SelfUpdateProgress(SelfUpdatePhase.Downloading, 50, 100));
            Staged = new StagedSelfUpdate(
                release.Version,
                "/opt/Borea",
                "/opt/Borea/borea",
                "/opt/Borea/borea.old",
                () => Installed = true,
                () => HandedOver = true);
            return Task.FromResult(Staged);
        }
    }
}
