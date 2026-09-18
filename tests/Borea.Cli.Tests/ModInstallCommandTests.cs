using Borea.Storage.Instances;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Composition;
using Borea.Core.Instances;
using System.Security.Cryptography;

namespace Borea.Cli.Tests;

public sealed class ModInstallCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task InstallDryRun_PrintsExactRelease_WithoutChangingTheInstance()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--version", "1.0.0", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install flight-tools 1.0.0.", run.Output);
        var instance = Assert.Single(await new FileInstanceRepository(_host.Paths).GetAllAsync());
        Assert.Empty(instance.Mods);
    }

    [Fact]
    public async Task InstallDryRun_StableChannel_SkipsANewerDevRelease()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install flight-tools 2.0.0.", run.Output);
        Assert.DoesNotContain("2.1.0-dev.1", run.Output);
        Assert.DoesNotContain("release status", run.Output);
    }

    [Fact]
    public async Task InstallDryRun_SavedTestingChannel_OffersTestingButNotDev()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-beta.1", releaseStatus: ReleaseStatus.Testing));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.2.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("settings", "set", "channel", "testing");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install flight-tools 2.1.0-beta.1.", run.Output);
    }

    [Fact]
    public async Task InstallDryRun_ChannelOption_OverridesTheSavedChannelForOneCommand()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");

        var overridden = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run", "--channel", "dev");
        var saved = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");
        var show = await _host.RunAsync("settings", "show", "--json");

        Assert.Equal(0, overridden.ExitCode);
        Assert.Contains("Install flight-tools 2.1.0-dev.1.", overridden.Output);
        Assert.DoesNotContain("release status", overridden.Output);
        Assert.Contains("Install flight-tools 2.0.0.", saved.Output);
        Assert.Equal("stable", show.Json.GetProperty("releaseChannel").GetString());
    }

    [Fact]
    public async Task InstallDryRun_ChannelOption_NarrowerThanTheSavedChannel_IsUsed()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");
        await _host.RunAsync("settings", "set", "channel", "dev");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run", "--channel", "stable");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Install flight-tools 2.0.0.", run.Output);
    }

    [Fact]
    public async Task Install_ExactDevVersionOnStable_WarnsAndInstalls()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");
        RecordingInstaller? installer = null;
        _host.InstallerFactory = graph => installer = new RecordingInstaller(graph);

        var run = await _host.RunAsync("install", "flight-tools", "--version", "2.1.0-dev.1", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("warning: Release 2.1.0-dev.1 has the release status dev, which the stable channel does not offer.", run.Output);
        Assert.Equal("2.1.0-dev.1", Assert.Single(installer!.Installed).Version.ToString());
    }

    [Fact]
    public async Task InstallDryRun_NoReleaseInTheChannel_FailsNamingTheChannel()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No release of 'flight-tools' in the stable channel is available.", run.Error);
    }

    [Theory]
    [InlineData("install", "flight-tools")]
    [InlineData("update", "flight-tools")]
    public async Task ChannelOption_NameThatIsNoChannel_IsAUsageError(string command, string modId)
    {
        var run = await _host.RunAsync(command, modId, "--channel", "nightly");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("is not a release channel", run.Error);
        Assert.Equal(0, _host.Builds);
    }

    [Fact]
    public async Task UpdateDryRun_StableChannel_TargetsTheNewestStableRelease()
    {
        var installed = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Releases.Add(installed);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "1.1.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await SaveInstalledAsync(installed);

        var run = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("flight-tools 1.1.0.", run.Output);
        Assert.DoesNotContain("2.0.0-dev.1", run.Output);
    }

    [Fact]
    public async Task UpdateDryRun_ChannelOption_TargetsANewerTestingReleaseForOneCommand()
    {
        var installed = ContentCommandFixtures.Release(version: "1.0.0");
        _host.Mods.Releases.Add(installed);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "1.1.0-beta.1", releaseStatus: ReleaseStatus.Testing));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0-dev.1", releaseStatus: ReleaseStatus.Dev));
        await SaveInstalledAsync(installed);

        var testing = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run", "--channel", "testing");
        var saved = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, testing.ExitCode);
        Assert.Contains("flight-tools 1.1.0-beta.1.", testing.Output);
        Assert.Contains("Nothing to do.", saved.Output);
    }

    [Fact]
    public async Task InstallDryRun_InstalledDevReleaseOnStable_IsNotReplacedByAnOlderStableRelease()
    {
        var installed = ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(installed);
        await SaveInstalledAsync(installed);

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Nothing to do.", run.Output);
        Assert.DoesNotContain("Install flight-tools 2.0.0.", run.Output);
    }

    [Fact]
    public async Task UpdateDryRun_InstalledDevReleaseOnStable_StaysWithoutAWarning()
    {
        var installed = ContentCommandFixtures.Release(version: "2.0.0-dev.1", releaseStatus: ReleaseStatus.Dev);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "1.0.0"));
        _host.Mods.Releases.Add(installed);
        await SaveInstalledAsync(installed);

        var run = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Nothing to do.", run.Output);
        Assert.DoesNotContain("release status", run.Output);
    }

    [Fact]
    public async Task InstallDryRun_RequiredDependency_PrintsBothOperations()
    {
        var dependency = new Borea.Core.Dependencies.ModDependency("library", Borea.Core.Dependencies.ModDependencyKind.Required);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [dependency]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("flight-tools 2.0.0", run.Output);
        Assert.Contains("library 1.0.0", run.Output);
    }

    [Fact]
    public async Task Install_ReportsEachOperationWithItsPlaceInThePlan()
    {
        var dependency = new ModDependency("library", ModDependencyKind.Required);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [dependency]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");
        _host.InstallerFactory = graph => new RecordingInstaller(graph);

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        var lines = run.Error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "Downloading library 1.0.0 (1 of 2)",
            "Extracting library 1.0.0 (1 of 2)",
            "Finishing library 1.0.0 (1 of 2)",
            "Downloading flight-tools 2.0.0 (2 of 2)",
            "Extracting flight-tools 2.0.0 (2 of 2)",
            "Finishing flight-tools 2.0.0 (2 of 2)",
        ], lines);
    }

    [Fact]
    public async Task Install_UnknownExactRelease_Fails()
    {
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--version", "9.0.0", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Release 9.0.0", run.Error);
    }

    [Fact]
    public async Task Install_ExecutesThePlannedRelease()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        RecordingInstaller? installer = null;
        _host.InstallerFactory = graph => installer = new RecordingInstaller(graph);

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("flight-tools", Assert.Single(installer!.Installed).ModId);
    }

    [Fact]
    public async Task Install_CancelledWhileAModUnpacks_FinishesThatModAndStops()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [new ModDependency("library", ModDependencyKind.Required)]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");
        using var cancellation = new CancellationTokenSource();
        RecordingInstaller? installer = null;
        _host.InstallerFactory = graph => installer = new RecordingInstaller(graph, extracting: _ => cancellation.Cancel());

        var run = await _host.RunAsync(cancellation.Token, "install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Stopped. 1 of 2 mods installed.", run.Error);
        Assert.Contains("The command was cancelled.", run.Error);
        Assert.Single(installer!.Installed);
    }

    [Fact]
    public async Task Install_Conflict_FailsWithoutExecuting()
    {
        var conflict = new ModDependency("blocker", ModDependencyKind.Conflict);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [conflict]));
        await SaveInstalledAsync(ContentCommandFixtures.Release(id: "blocker"));
        var calls = 0;
        _host.InstallerFactory = graph => new RecordingInstaller(graph, _ => calls++);

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("conflict:", run.Output);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Remove_RequiredByAnotherMod_FailsWithoutExecuting()
    {
        var library = ContentCommandFixtures.Release(id: "library", version: "1.0.0");
        var dependency = new ModDependency("library", ModDependencyKind.Required);
        await SaveInstalledAsync(library, ContentCommandFixtures.Release(id: "consumer", dependencies: [dependency]));
        var calls = 0;
        _host.UninstallerFactory = graph => new RecordingUninstaller(graph, _ => calls++);

        var run = await _host.RunAsync("remove", "library", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("required by consumer", run.Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Remove_ManagedMod_ExecutesUninstaller()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release());
        RecordingUninstaller? uninstaller = null;
        _host.UninstallerFactory = graph => uninstaller = new RecordingUninstaller(graph);

        var run = await _host.RunAsync("remove", "flight-tools", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("flight-tools", Assert.Single(uninstaller!.Removed));
    }

    [Fact]
    public async Task Update_SkipsYankedNewestRelease()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release(version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "3.0.0", yanked: true));
        RecordingReplacer? replacer = null;
        _host.ReplacerFactory = graph => replacer = new RecordingReplacer(graph);

        var run = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(ModVersion.Parse("2.0.0"), Assert.Single(replacer!.Replacements).Version);
    }

    [Fact]
    public async Task Update_ForeignOwnedRecord_FailsBeforePlanning()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release(), ownership: ModInstallOwnership.Foreign);

        var run = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not own", run.Error);
    }

    [Fact]
    public async Task Install_WithRecommended_SelectsNestedRecommendations()
    {
        var second = new ModDependency("second", ModDependencyKind.Recommends);
        var first = new ModDependency("first", ModDependencyKind.Recommends);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [first]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "first", version: "1.0.0", dependencies: [second]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "second", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--with-recommended", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("first 1.0.0", run.Output);
        Assert.Contains("second 1.0.0", run.Output);
    }

    [Fact]
    public async Task Install_RequiredAlternative_CanBeSelectedExplicitly()
    {
        var alternatives = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second")]);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [alternatives]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "first", version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "second", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");
        var initial = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");
        var option = initial.Output.Split(Environment.NewLine).Single(line => line.StartsWith("choice option:", StringComparison.Ordinal));
        var key = option["choice option: ".Length..option.IndexOf(" = ", StringComparison.Ordinal)];

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--alternative", $"{key}=second", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("second 1.0.0", run.Output);
    }

    [Fact]
    public async Task InstallDryRun_WarningsChoicesAndConflicts_PrintTheirEnglishText()
    {
        var alternatives = ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second")]);
        var dependencies = new[]
        {
            new ModDependency("helper", ModDependencyKind.Recommends),
            new ModDependency("extra", ModDependencyKind.Suggests),
            alternatives,
            new ModDependency("missing-lib", ModDependencyKind.Required, ModVersion.Parse("1.0.0")),
        };
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.1.0-dev.1", releaseStatus: ReleaseStatus.Dev, dependencies: dependencies));
        foreach (var id in new[] { "helper", "extra", "first", "second" })
            _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: id, version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--version", "2.1.0-dev.1", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            string.Join(Environment.NewLine,
            [
                "warning: Game compatibility is Unknown.",
                "warning: Release 2.1.0-dev.1 has the release status dev, which the stable channel does not offer.",
                "Install flight-tools 2.1.0-dev.1.",
                "choice: Select one alternative for Required dependency on any of [first, second].",
                "choice option: flight-tools:dependency:2:alternative = first, second",
                "conflict: Required dependency on mod 'missing-lib' >= 1.0.0",
                string.Empty,
            ]),
            run.Output);
    }

    [Fact]
    public async Task Install_ServiceFailure_ReturnsFailedExitCode()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        _host.InstallerFactory = graph => new FailingInstaller();

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("hash mismatch", run.Error);
    }

    [Fact]
    public async Task InstallDryRun_DoesNotChangeAnyFileUnderTheBoreaRoot()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        var before = FileHashes();

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, FileHashes());
    }

    [Fact]
    public async Task RemoveDryRun_DoesNotChangeAnyFileUnderTheBoreaRoot()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release());
        var before = FileHashes();

        var run = await _host.RunAsync("remove", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, FileHashes());
    }

    [Fact]
    public async Task UpdateDryRun_DoesNotChangeAnyFileUnderTheBoreaRoot()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release(version: "1.0.0"));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(version: "2.0.0"));
        var before = FileHashes();

        var run = await _host.RunAsync("update", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(before, FileHashes());
    }

    [Fact]
    public async Task InstallDryRun_MissingCache_FailsWithoutWriting()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        _host.IndexReader.Read = _ => throw new IOException("No cached index exists.");
        var before = FileHashes();

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("borea index refresh", run.Error);
        Assert.Equal(before, FileHashes());
    }

    [Fact]
    public async Task Install_IncompatibleRelease_NamesRequiredBuild()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(gameMinRevision: 9000));
        _host.InstalledVersion = new FakeInstalledGameVersionProvider
        {
            Installed = new Borea.Core.Game.InstalledGameVersion(new Borea.Core.Game.GameVersion(2026, 9, 1, 5402), "2026.9.1.5402"),
        };
        await _host.RunAsync("instance", "create", "Alpha");

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Requires game 2026.1.1.9000", run.Output);
    }

    [Fact]
    public async Task Install_ForeignOwnedRecord_FailsBeforePlanning()
    {
        await SaveInstalledAsync(ContentCommandFixtures.Release(), ownership: ModInstallOwnership.Foreign);

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha", "--dry-run");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not own", run.Error);
    }

    [Fact]
    public async Task Install_ExternalChangeBetweenOperations_StopsTheSecondWrite()
    {
        var dependency = new ModDependency("library", ModDependencyKind.Required);
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(dependencies: [dependency]));
        _host.Mods.Releases.Add(ContentCommandFixtures.Release(id: "library", version: "1.0.0"));
        await _host.RunAsync("instance", "create", "Alpha");
        RecordingInstaller? installer = null;
        _host.InstallerFactory = graph => installer = new RecordingInstaller(graph);
        _host.InstancesFactory = graph => new ChangingInstanceRepository(graph.Instances, ContentCommandFixtures.Release(id: "external", version: "1.0.0"));

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("changed while", run.Error);
        Assert.Single(installer!.Installed);
    }

    [Fact]
    public async Task Install_ChangeDuringDownload_IsRejectedByGuardedWrite()
    {
        _host.Mods.Releases.Add(ContentCommandFixtures.Release());
        await _host.RunAsync("instance", "create", "Alpha");
        RecordingInstaller? installer = null;
        _host.InstallerFactory = graph => installer = new RecordingInstaller(graph, _ =>
            graph.Instances.UpdateAsync(Assert.Single(graph.Instances.GetAllAsync().GetAwaiter().GetResult()).InstanceId, instance =>
            {
                instance.AddMod(Installed(ContentCommandFixtures.Release(id: "external", version: "1.0.0"), ModInstallOwnership.Borea));
                return true;
            }).GetAwaiter().GetResult());

        var run = await _host.RunAsync("install", "flight-tools", "--instance", "Alpha");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("changed after", run.Error);
        Assert.Empty(installer!.Installed);
    }

    private Dictionary<string, string> FileHashes() => Directory.Exists(_host.Root)
        ? Directory.GetFiles(_host.Root, "*", SearchOption.AllDirectories).Where(path => Path.GetRelativePath(_host.Root, path).Split(Path.DirectorySeparatorChar)[0] != "Logs").ToDictionary(path => Path.GetRelativePath(_host.Root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal)
        : new Dictionary<string, string>(StringComparer.Ordinal);

    private async Task SaveInstalledAsync(ModVersionMetadata first, ModVersionMetadata? second = null, ModInstallOwnership ownership = ModInstallOwnership.Borea)
    {
        var mods = new[] { Installed(first, ownership) }.Concat(second is null ? [] : [Installed(second, ModInstallOwnership.Borea)]).ToList();
        var instance = Borea.Core.Instances.Instance.FromExisting(Guid.NewGuid(), "Alpha", Borea.Core.Instances.InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);
    }

    private static InstalledMod Installed(ModVersionMetadata release, ModInstallOwnership ownership) =>
        new(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release, new string('A', 64), ownership, ownership == ModInstallOwnership.Borea ? Guid.NewGuid().ToString("N") : null);

    private sealed class RecordingInstaller(BoreaServices graph, Action<ModVersionMetadata>? before = null, Action<ModVersionMetadata>? extracting = null) : IModInstaller
    {
        public List<ModVersionMetadata> Installed { get; } = new();
        public async Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            before?.Invoke(release);
            Installed.Add(release);
            var installed = ModInstallCommandTests.Installed(release, ModInstallOwnership.Borea);
            await graph.Instances.UpdateAsync(instanceId, instance => { instance.AddMod(installed); return true; }, cancellationToken);
            return new InstallResult(installed, new DownloadResult(release.Download.Url, release.Download.SizeBytes ?? 0, release.Download.Sha256 ?? string.Empty), Borea.Core.State.ModEntryAddResult.Added);
        }
        public async Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, Borea.Core.Planning.InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            before?.Invoke(release);
            // two byte reports in one phase, so the output shows it collapses them
            progress?.Report(InstallProgress.Of(release, InstallPhase.Downloading, new DownloadProgress(1, 2)));
            progress?.Report(InstallProgress.Of(release, InstallPhase.Downloading, new DownloadProgress(2, 2)));
            progress?.Report(InstallProgress.Of(release, InstallPhase.Extracting));
            extracting?.Invoke(release);
            progress?.Report(InstallProgress.Of(release, InstallPhase.Finishing));
            return await graph.Instances.UpdateAsync(instanceId, current =>
            {
                if (!expectedState.Matches(current))
                    throw new InvalidOperationException("The instance changed after Borea planned the operation.");
                var installed = ModInstallCommandTests.Installed(release, ModInstallOwnership.Borea);
                current.AddMod(installed);
                Installed.Add(release);
                var result = new InstallResult(installed, new DownloadResult(release.Download.Url, release.Download.SizeBytes ?? 0, release.Download.Sha256 ?? string.Empty), Borea.Core.State.ModEntryAddResult.Added);
                return new GuardedInstallResult(result, Borea.Core.Planning.InstallPlanningState.Capture(current));
            }, cancellationToken);
        }
    }

    private sealed class RecordingUninstaller(BoreaServices graph, Action<string>? before = null) : IModUninstaller
    {
        public List<string> Removed { get; } = new();
        public async Task UninstallAsync(Guid instanceId, string modId, CancellationToken cancellationToken = default)
        {
            before?.Invoke(modId);
            Removed.Add(modId);
            await graph.Instances.UpdateAsync(instanceId, instance => instance.RemoveMod(modId), cancellationToken);
        }
    }

    private sealed class RecordingReplacer(BoreaServices graph) : IModReplacer
    {
        public List<ModVersionMetadata> Replacements { get; } = new();
        public async Task<ModReplacementResult> ReplaceAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Replacements.Add(replacement);
            var installed = ModInstallCommandTests.Installed(replacement, ModInstallOwnership.Borea);
            await graph.Instances.UpdateAsync(instanceId, instance => { instance.ReplaceMod(installed); return true; }, cancellationToken);
            return new ModReplacementResult(expectedCurrent, installed, new DownloadResult(replacement.Download.Url, replacement.Download.SizeBytes ?? 0, replacement.Download.Sha256 ?? string.Empty), null);
        }
        public async Task<GuardedModReplacementResult> ReplaceGuardedAsync(Guid instanceId, InstalledMod expectedCurrent, ModVersionMetadata replacement, Borea.Core.Planning.InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return await graph.Instances.UpdateAsync(instanceId, current =>
            {
                if (!expectedState.Matches(current))
                    throw new InvalidOperationException("The instance changed after Borea planned the operation.");
                var installed = ModInstallCommandTests.Installed(replacement, ModInstallOwnership.Borea);
                current.ReplaceMod(installed);
                Replacements.Add(replacement);
                var result = new ModReplacementResult(expectedCurrent, installed, new DownloadResult(replacement.Download.Url, replacement.Download.SizeBytes ?? 0, replacement.Download.Sha256 ?? string.Empty), null);
                return new GuardedModReplacementResult(result, Borea.Core.Planning.InstallPlanningState.Capture(current));
            }, cancellationToken);
        }
    }

    private sealed class FailingInstaller : IModInstaller
    {
        public Task<InstallResult> InstallAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new IOException("The archive hash mismatch stopped the install.");
        public Task<GuardedInstallResult> InstallGuardedAsync(Guid instanceId, ModVersionMetadata release, InstallReason reason, bool enable, Borea.Core.Planning.InstallPlanningState expectedState, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new IOException("The archive hash mismatch stopped the install.");
    }

    private sealed class ChangingInstanceRepository(IInstanceRepository inner, ModVersionMetadata external) : IInstanceRepository
    {
        private int _reads;
        public Task<IReadOnlyList<Instance>> GetAllAsync() => inner.GetAllAsync();
        public async Task<Instance?> GetByIdAsync(Guid instanceId)
        {
            _reads++;
            if (_reads == 3)
                await inner.UpdateAsync(instanceId, instance => { instance.AddMod(Installed(external, ModInstallOwnership.Borea)); return true; });
            return await inner.GetByIdAsync(instanceId);
        }
        public Task<Guid?> GetActiveInstanceIdAsync() => inner.GetActiveInstanceIdAsync();
        public Task SetActiveInstanceAsync(Guid instanceId) => inner.SetActiveInstanceAsync(instanceId);
        public Task ClearActiveInstanceAsync() => inner.ClearActiveInstanceAsync();
        public Task<bool> IsNameAvailableAsync(string name, Guid? excludingInstanceId = null) => inner.IsNameAvailableAsync(name, excludingInstanceId);
        public Task<InstanceCreateResult> CreateAsync(string name, InstanceSource source) => inner.CreateAsync(name, source);
        public Task<InstanceCreateResult> CreateAsync(Instance instance) => inner.CreateAsync(instance);
        public Task RenameAsync(Guid instanceId, string newName) => inner.RenameAsync(instanceId, newName);
        public Task DeleteAsync(Guid instanceId) => inner.DeleteAsync(instanceId);
        public Task SaveAsync(Instance instance) => inner.SaveAsync(instance);
        public Task<TResult> UpdateAsync<TResult>(Guid instanceId, Func<Instance, TResult> update, CancellationToken cancellationToken = default) => inner.UpdateAsync(instanceId, update, cancellationToken);
    }

    public void Dispose() => _host.Dispose();
}
