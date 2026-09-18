using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.App.ViewModels;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class InstallChoicesTests
{
    private const string OwnId = ViewModelHarness.FakeSpaceDock.OwnId;
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task Install_Recommendations_InstallTheKeptOneAndSkipTheDeselectedOne()
    {
        using var harness = await CreateWithGameAsync();
        var release = Release(OwnId, dependencies: [Recommends("kept"), Recommends("dropped"), new ModDependency("extra", ModDependencyKind.Suggests)]);
        harness.SpaceDock.Releases.AddRange([release, Release("kept"), Release("dropped"), Release("extra")]);
        var instance = await ActivateInstanceAsync(harness);
        var row = new VersionItem(harness.ViewModel, release);

        await row.InstallCommand.ExecuteAsync(null);

        var choices = row.Choices!;
        Assert.True(row.IsConfirmingInstall);
        Assert.Equal(harness.Localization.ContentAdd, row.ConfirmInstallText);
        Assert.Equal(2, choices.Recommended.Count);
        Assert.All(choices.Recommended, choice => Assert.True(choice.IsSelected));
        Assert.StartsWith("extra", Assert.Single(choices.Suggested), StringComparison.Ordinal);
        Assert.Empty(await ModIdsAsync(harness, instance));

        choices.Recommended.Single(choice => choice.Text.StartsWith("dropped", StringComparison.Ordinal)).IsSelected = false;
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.Choices);
        Assert.Null(row.InstallError);
        Assert.Equal([OwnId, "kept"], await ModIdsAsync(harness, instance));
    }

    [Fact]
    public async Task Install_RecommendationWithoutARelease_StartsDeselected()
    {
        using var harness = await CreateWithGameAsync();
        var release = Release(OwnId, dependencies: [Recommends("unlisted")]);
        harness.SpaceDock.Releases.Add(release);
        var instance = await ActivateInstanceAsync(harness);
        var row = new VersionItem(harness.ViewModel, release);

        await row.InstallCommand.ExecuteAsync(null);

        var choices = row.Choices!;
        Assert.False(Assert.Single(choices.Recommended).IsSelected);
        Assert.Null(choices.BlockedText);
        Assert.Null(row.InstallError);

        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.Choices);
        Assert.Equal([OwnId], await ModIdsAsync(harness, instance));
    }

    [Fact]
    public async Task Install_RequiredAlternative_InstallsTheChosenMod()
    {
        using var harness = await CreateWithGameAsync();
        var release = Release(OwnId, dependencies: [ModDependency.OfAlternatives(ModDependencyKind.Required, [new ModDependencyAlternative("first"), new ModDependencyAlternative("second")])]);
        harness.SpaceDock.Releases.AddRange([release, Release("first"), Release("second")]);
        var instance = await ActivateInstanceAsync(harness);
        var row = new VersionItem(harness.ViewModel, release);

        await row.InstallCommand.ExecuteAsync(null);

        var choices = row.Choices!;
        var group = Assert.Single(choices.Alternatives);
        Assert.Equal(["first", "second"], group.Options.Select(option => option.Name));
        Assert.False(choices.IsComplete);
        Assert.Null(choices.BlockedText);

        group.Options[0].IsSelected = true;
        group.Options[1].IsSelected = true;

        Assert.False(group.Options[0].IsSelected);
        Assert.True(choices.IsComplete);
        await row.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(row.Choices);
        Assert.Null(row.InstallError);
        Assert.Equal([OwnId, "second"], await ModIdsAsync(harness, instance));
    }

    [Fact]
    public async Task Install_ChoicesCancelled_InstallsNothing()
    {
        using var harness = await CreateWithGameAsync();
        var release = Release(OwnId, dependencies: [Recommends("kept")]);
        harness.SpaceDock.Releases.AddRange([release, Release("kept")]);
        var instance = await ActivateInstanceAsync(harness);
        var row = new VersionItem(harness.ViewModel, release);
        await row.InstallCommand.ExecuteAsync(null);
        Assert.NotNull(row.Choices);

        row.CancelInstallCommand.Execute(null);

        Assert.Null(row.Choices);
        Assert.Null(row.PendingPlan);
        Assert.False(row.IsConfirmingInstall);
        Assert.Empty(await ModIdsAsync(harness, instance));
        Assert.DoesNotContain(harness.Requests, uri => uri.Host == ArchiveHost);
    }

    [Fact]
    public async Task Install_BlockedPlanWithOnlyASuggestion_ShowsTheConflict()
    {
        using var harness = await CreateWithGameAsync();
        var release = Release(OwnId, dependencies: [new ModDependency("extra", ModDependencyKind.Suggests), new ModDependency("missing", ModDependencyKind.Required)]);
        harness.SpaceDock.Releases.AddRange([release, Release("extra")]);
        await ActivateInstanceAsync(harness);
        var row = new VersionItem(harness.ViewModel, release);

        await row.InstallCommand.ExecuteAsync(null);

        Assert.Null(row.Choices);
        Assert.Contains("missing", row.InstallError);
    }

    private static ModDependency Recommends(string id) => new(id, ModDependencyKind.Recommends);

    private static ModVersionMetadata Release(string id, string version = "1.0.0", IReadOnlyList<ModDependency>? dependencies = null) => new(
        specVersion: 1,
        modId: id,
        version: ModVersion.Parse(version),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{id}/{version}.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: dependencies ?? []);

    private static async Task<List<string>> ModIdsAsync(ViewModelHarness harness, Instance instance)
        => (await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods.Select(mod => mod.ModId).Order(StringComparer.Ordinal).ToList();

    private static async Task<Instance> ActivateInstanceAsync(ViewModelHarness harness)
    {
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return instance;
    }

    /// <summary>A harness whose game is compatible with every release of these tests, so a plan without choices has no warning.</summary>
    private static Task<ViewModelHarness> CreateWithGameAsync() =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var game = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Game")).FullName;
                File.Copy(Path.Combine(AppContext.BaseDirectory, "GameVersionFixture.dll"), Path.Combine(game, "KSA.dll"));
                return services.SettingsRepository.SaveAsync(services.Settings.WithGameDirectory(game));
            },
            respond: ServeArchive);

    /// <summary>Serves a zip with a mod.toml at its root, named for the mod the archive URL names.</summary>
    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host != ArchiveHost)
            return null;

        var modId = request.RequestUri.Segments[1].TrimEnd('/');
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{modId}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }
}
