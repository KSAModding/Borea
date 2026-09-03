using Borea.Composition;

namespace Borea.Cli.Tests;

public sealed class CliServicesTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    [Fact]
    public async Task From_CarriesTheGraphsServices_AndKeepsTheGraph()
    {
        using var graph = await BoreaServices.BuildAsync(_tempRoot);

        var services = CliServices.From(graph);

        Assert.Same(graph.Settings, services.Settings);
        Assert.Same(graph.SettingsRepository, services.SettingsRepository);
        Assert.Same(graph.Instances, services.Instances);
        Assert.Same(graph.ModState, services.ModState);
        Assert.Same(graph.LatestVersion, services.LatestVersion);
        Assert.Same(graph, services.Graph);
    }

    [Fact]
    public async Task From_WithAPing_UsesItInsteadOfTheGraphs()
    {
        using var graph = await BoreaServices.BuildAsync(_tempRoot);
        var ping = new FakeLatestVersionPing();

        var services = CliServices.From(graph, ping);

        Assert.Same(ping, services.LatestVersion);
        Assert.Same(graph.Instances, services.Instances);
    }

    [Fact]
    public async Task Dispose_DisposesTheGraph()
    {
        using var graph = await BoreaServices.BuildAsync(_tempRoot);
        var owner = new DisposalProbe();
        var services = new CliServices
        {
            Settings = graph.Settings,
            SettingsRepository = graph.SettingsRepository,
            Instances = graph.Instances,
            ModState = graph.ModState,
            LatestVersion = new FakeLatestVersionPing(),
            Graph = owner,
        };

        services.Dispose();

        Assert.True(owner.Disposed);
    }

    [Fact]
    public void From_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CliServices.From(null!));
    }

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
