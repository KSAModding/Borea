using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class LoaderPromptTests
{
    private const string StarMapUrl = "https://github.com/StarMapLoader/StarMap/releases/download/0.4.6/StarMap-0.4.6.zip";

    private static readonly byte[] StarMapArchive = CreateStarMapArchive();

    /// <summary>A harness with a saved game directory, whose StarMap release is <see cref="StarMapArchive"/>.</summary>
    private static async Task<ViewModelHarness> CreateAsync(IProcessStarter starter, Func<HttpRequestMessage, HttpResponseMessage?> respond)
    {
        var harness = await ViewModelHarness.CreateAsync(
            respond: respond,
            editSnapshot: json => json
                .Replace("BC9510994DAF56FD826B734EF0F2704C8E4D1B91BCF010C76733E168CC23604A", Convert.ToHexString(SHA256.HashData(StarMapArchive)), StringComparison.Ordinal)
                .Replace("\"size\": 891020", $"\"size\": {StarMapArchive.Length}", StringComparison.Ordinal),
            processStarter: starter);
        harness.ViewModel.GameDirectoryInput = Directory.CreateDirectory(Path.Combine(harness.Root, "game")).FullName;
        await harness.ViewModel.SaveGameDirectoryCommand.ExecuteAsync(null);
        return harness;
    }

    [Fact]
    public async Task Play_NeededLoaderNotInstalled_AsksToInstallIt()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter, ServeStarMap);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchInstallLoader("StarMap"), viewModel.LoaderPromptText);
        Assert.Null(viewModel.LaunchMessage);
        Assert.False(viewModel.IsSettingsOpen);
        Assert.False(starter.Started);

        viewModel.CancelLoaderPromptCommand.Execute(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Empty(harness.Services.Settings.LoaderInstallations);
    }

    [Fact]
    public async Task Play_NeededLoaderNotListed_SaysItIsNotInstalled()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var root = JsonNode.Parse(json)!;
            var listings = root["listings"]!.AsArray();
            listings.Remove(listings.Single(node => (string?)node!["id"] == "StarMap"));
            return root.ToJsonString();
        });
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchNeededLoaderNotInstalled("StarMap"), viewModel.LaunchMessage);
    }

    [Fact]
    public async Task Install_Succeeds_StartsTheLaunch()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter, ServeStarMap);
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.Null(viewModel.SetupError);
        Assert.False(viewModel.IsSetupBusy);
        Assert.Equal(ModVersion.Parse("0.4.6"), harness.Services.Settings.LoaderInstallations["StarMap"].Version);
        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.False(viewModel.IsLaunching);
        Assert.True(viewModel.CurrentWindowInstance);
    }

    [Fact]
    public async Task Install_Fails_ShowsTheReasonAndDoesNotLaunch()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter, request => request.RequestUri?.AbsoluteUri == StarMapUrl ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.PlayCommand.ExecuteAsync(null);

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.SetupError));
        Assert.False(viewModel.IsSetupBusy);
        Assert.Empty(harness.Services.Settings.LoaderInstallations);
        Assert.False(starter.Started);
    }

    [Fact]
    public async Task PlayActiveInstance_NoLoaderInstalled_OffersALoaderThatTakesAnInstance()
    {
        var starter = new RunningStarter();
        using var harness = await CreateAsync(starter, ServeStarMap);
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        starter.GameLog = harness.Services.Paths.GetInstanceGameLogPath(instance.InstanceId);
        await viewModel.LoadAsync();

        await viewModel.PlayActiveInstanceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchInstallLoader("StarMap"), viewModel.LoaderPromptText);
        Assert.Contains(harness.Services.Log.ReadRecentLines(20), line => line.EndsWith("did not start, NoLoaderTakesInstance.", StringComparison.Ordinal));

        await viewModel.InstallPromptedLoaderCommand.ExecuteAsync(null);

        Assert.True(harness.Services.Launcher.IsRunning(instance.InstanceId));
        Assert.True(viewModel.CurrentWindowHome);
    }

    [Fact]
    public async Task Play_WithoutAGameFolder_LeadsToTheGameTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLoaderPromptOpen);
        Assert.Equal(harness.Localization.FormatLaunchSetUpGameFirst("StarMap"), viewModel.LoaderPromptText);
        Assert.False(viewModel.IsSettingsOpen);

        await viewModel.SetUpGameForLoaderCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLoaderPromptOpen);
        Assert.True(viewModel.IsSettingsOpen);
        Assert.True(viewModel.IsGameTab);
    }

    private static HttpResponseMessage? ServeStarMap(HttpRequestMessage request) =>
        request.RequestUri?.AbsoluteUri == StarMapUrl
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(StarMapArchive) }
            : null;

    /// <summary>The app host and the assembly of a StarMap release.</summary>
    private static byte[] CreateStarMapArchive()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("StarMap.exe");
            archive.CreateEntry("StarMap.dll");
        }

        return buffer.ToArray();
    }

    /// <summary>Hands out a loader process that keeps running and writes <see cref="GameLog"/> once the start is watched, like a game that started.</summary>
    private sealed class RunningStarter : IProcessStarter
    {
        public string? GameLog { get; set; }

        public bool Started { get; private set; }

        public IStartedProcess Start(LaunchPlan plan)
        {
            Started = true;
            return new RunningProcess(this);
        }

        private sealed class RunningProcess(RunningStarter owner) : IStartedProcess
        {
            public int Id => 4244;

            public bool HasExited => false;

            public int? ExitCode => null;

            public IReadOnlyList<string> RecentOutput => [];

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                if (owner.GameLog is { } log)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.WriteAllText(log, "KSA started.");
                }

                return Task.FromResult(false);
            }

            public void Dispose()
            {
            }
        }
    }
}
