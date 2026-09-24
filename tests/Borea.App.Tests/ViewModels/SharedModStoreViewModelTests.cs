using System.IO.Compression;
using System.Net;
using System.Text;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class SharedModStoreViewModelTests
{
    private const string ModId = "StoredMod";
    private const string ArchiveHost = "archives.test";

    [Fact]
    public async Task OpenInstance_ModChangedItsStoredFiles_GivesTheInstanceItsOwnCopyAndSaysSoOnce()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var viewModel = harness.ViewModel;
        var instanceId = await InstallLinkedAsync(harness);
        File.WriteAllText(Path.Combine(ModFolder(harness, instanceId), "settings.cfg"), "written by the mod");
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenModStoreCheckedAsync();
        await viewModel.WhenModStoreCheckedAsync();

        Assert.Null(new DirectoryInfo(ModFolder(harness, instanceId)).LinkTarget);
        Assert.Equal("written by the mod", File.ReadAllText(Path.Combine(ModFolder(harness, instanceId), "settings.cfg")));
        Assert.Equal(ModStorage.Private, Assert.Single((await harness.Services.Instances.GetByIdAsync(instanceId))!.Mods).Storage);
        var toast = Assert.Single(viewModel.Toasts.Items);
        Assert.Equal(harness.Localization.FormatSharedModStoreBrokenOut(ModId), toast.Message);
    }

    [Fact]
    public async Task Play_ModChangedItsStoredFiles_GivesTheInstanceItsOwnCopyOnceTheGameCloses()
    {
        using var harness = await ViewModelHarness.CreateAsync(RecordStarMapAsync, respond: ServeArchive, processStarter: new ClosingGameStarter());
        var viewModel = harness.ViewModel;
        var instanceId = await InstallLinkedAsync(harness);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenModStoreCheckedAsync();
        File.WriteAllText(Path.Combine(ModFolder(harness, instanceId), "settings.cfg"), "written by the mod");

        await viewModel.PlayCommand.ExecuteAsync(null);
        await viewModel.WhenGameExitCheckedAsync();

        Assert.Null(new DirectoryInfo(ModFolder(harness, instanceId)).LinkTarget);
        Assert.Equal(ModStorage.Private, Assert.Single((await harness.Services.Instances.GetByIdAsync(instanceId))!.Mods).Storage);
        Assert.Single(viewModel.Toasts.Items, toast => toast.Message == harness.Localization.FormatSharedModStoreBrokenOut(ModId));
    }

    [Fact]
    public async Task TurnOff_GivesEveryInstanceItsOwnCopyAndSavesTheSetting()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var viewModel = harness.ViewModel;
        var instanceId = await InstallLinkedAsync(harness);
        await viewModel.LoadAsync();
        Assert.True(viewModel.UseSharedModStore);

        viewModel.UseSharedModStore = false;
        await viewModel.WhenSharedModStoreSavedAsync();

        Assert.Null(viewModel.SharedModStoreError);
        Assert.False(viewModel.UseSharedModStore);
        Assert.False(harness.Services.Settings.SharedModStore);
        Assert.False((await harness.Services.SettingsRepository.GetAsync())!.SharedModStore);
        Assert.Null(new DirectoryInfo(ModFolder(harness, instanceId)).LinkTarget);
        Assert.True(File.Exists(Path.Combine(ModFolder(harness, instanceId), "mod.toml")));
        Assert.Empty(Directory.EnumerateDirectories(harness.Services.Paths.GetStaticModFilesRoot()));
    }

    [WindowsFact("Only Windows refuses to copy a file that is open.")]
    public async Task TurnOff_CopyFails_KeepsTheSettingOffAndSaysHowToFinish()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: ServeArchive);
        var viewModel = harness.ViewModel;
        var instanceId = await InstallLinkedAsync(harness);
        await viewModel.LoadAsync();
        using var held = new FileStream(Path.Combine(ModFolder(harness, instanceId), "mod.toml"), FileMode.Open, FileAccess.Read, FileShare.None);

        viewModel.UseSharedModStore = false;
        await viewModel.WhenSharedModStoreSavedAsync();

        Assert.StartsWith(harness.Localization.FormatSharedModStoreBreakOutFailed("|").Split('|')[0], viewModel.SharedModStoreError);
        Assert.False(viewModel.UseSharedModStore);
        Assert.False(harness.Services.Settings.SharedModStore);
        Assert.False((await harness.Services.SettingsRepository.GetAsync())!.SharedModStore);
    }

    private static Task RecordStarMapAsync(BoreaServices services)
    {
        var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }

    private static async Task<Guid> InstallLinkedAsync(ViewModelHarness harness)
    {
        var services = harness.Services;
        var instanceId = (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance.InstanceId;
        await services.Instances.SetActiveInstanceAsync(instanceId);
        await services.Installer.InstallAsync(instanceId, Release(), InstallReason.Manual, enable: true);
        Assert.NotNull(new DirectoryInfo(ModFolder(harness, instanceId)).LinkTarget);
        return instanceId;
    }

    private static string ModFolder(ViewModelHarness harness, Guid instanceId)
        => Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instanceId), ModId);

    private static ModVersionMetadata Release() => new(
        specVersion: 1,
        modId: ModId,
        version: ModVersion.Parse("1.0.0"),
        releaseStatus: ReleaseStatus.Stable,
        releaseDate: DateTimeOffset.UnixEpoch,
        gameMin: "2026.1.1.1",
        gameMinRevision: 1,
        download: new DownloadInfo($"https://{ArchiveHost}/{ModId}/1.0.0.zip", sha256: null, sizeBytes: null, contentType: "application/zip"),
        installSizeBytes: null,
        dependencies: []);

    private static HttpResponseMessage? ServeArchive(HttpRequestMessage request)
    {
        if (request.RequestUri?.Host != ArchiveHost)
            return null;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mod.toml").Open(), Encoding.UTF8);
            writer.Write($"name = \"{ModId}\"");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) };
    }

    /// <summary>Hands out a loader whose game writes its log while the start is watched, and closes right after.</summary>
    private sealed class ClosingGameStarter : IProcessStarter
    {
        public IStartedProcess Start(LaunchPlan plan) => new ClosingGame(plan.Arguments[1]);

        private sealed class ClosingGame(string instanceRoot) : IStartedProcess
        {
            private bool _closed;

            public int Id => 4242;

            public bool HasExited => _closed;

            public int? ExitCode => _closed ? 0 : null;

            public IReadOnlyList<string> RecentOutput => [];

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                var log = Path.Combine(instanceRoot, "logs", "KittenSpaceAgency.260915-112433.4242.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, "11:24:36.689  INFO loaded settings from settings.toml\n");
                _closed = true;
                return Task.FromResult(false);
            }

            public void Dispose()
            {
            }
        }
    }
}
