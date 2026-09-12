using System.Net;
using System.Text;
using System.Text.Json;
using Borea.Composition;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.Cli.Tests;

public sealed class LoaderIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly FakeProcessStarter _processStarter = new();

    [Fact]
    public async Task Commands_UseTheIndexLoaderContractToAdoptUpdateAndLaunch()
    {
        var oldGame = Directory.CreateDirectory(Path.Combine(_root, "OldGame")).FullName;
        var newGame = Directory.CreateDirectory(Path.Combine(_root, "NewGame")).FullName;
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _root, oldGame);

        Assert.Equal(0, (await RunAsync("settings", "set", "game", oldGame)).ExitCode);
        var adopt = await RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        var change = await RunAsync("settings", "set", "game", newGame);
        Assert.Equal(0, (await RunAsync("instance", "create", "Flight Test")).ExitCode);
        var launch = await RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, adopt.ExitCode);
        Assert.Contains("Adopted StarMap", adopt.Output);
        Assert.Equal(0, change.ExitCode);
        using var configuration = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(loaderDirectory, "StarMapConfig.json")));
        Assert.Equal(newGame, configuration.RootElement.GetProperty("GameLocation").GetString());
        Assert.Equal(0, launch.ExitCode);
        var plan = Assert.Single(_processStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
        Assert.Equal(plan.Arguments[1], plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
    }

    private async Task<CliRun> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var snapshot = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory,
            "Index",
            "Fixtures",
            "current-snapshot.json"));

        async Task<CliServices> BuildAsync(CancellationToken cancellationToken)
        {
            var graph = await BoreaServices.BuildAsync(
                _root,
                new ControlledHttpMessageHandler(snapshot),
                new EmptyModRepository(),
                cancellationToken);
            return CliServices.From(
                graph,
                launcher: new LoaderLauncher(graph.Paths, _processStarter));
        }

        var exitCode = await BoreaCli.RunAsync(args, BuildAsync, output, error, CancellationToken.None);
        return new CliRun(exitCode, output.ToString(), error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ControlledHttpMessageHandler(string snapshot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri != "https://ksamodding.github.io/content-index-releases/v1/index.json")
                throw new InvalidOperationException($"Unexpected request to {request.RequestUri}.");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class EmptyModRepository : IModRepository
    {
        public Task<IReadOnlyList<ModMetadata>> GetAvailableModsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);

        public Task<ModVersionMetadata?> GetLatestReleaseAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<ModVersionMetadata?> GetReleaseAsync(
            string modId,
            ModVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ModVersionMetadata?>(null);

        public Task<IReadOnlyList<ModVersion>> GetAvailableVersionsAsync(
            string modId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModVersion>>([]);

        public Task<IReadOnlyList<ModMetadata>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModMetadata>>([]);
    }
}
