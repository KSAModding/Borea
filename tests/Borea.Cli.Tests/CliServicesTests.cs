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
        Assert.Same(graph.GameDirectoryChanger, services.GameDirectoryChanger);
        Assert.Same(graph.Instances, services.Instances);
        Assert.Same(graph.ModState, services.ModState);
        Assert.Same(graph.LatestVersion, services.LatestVersion);
        Assert.Same(graph.InstalledVersion, services.InstalledVersion);
        Assert.Same(graph.IndexFetcher, services.IndexFetcher);
        Assert.Same(graph.IndexReader, services.IndexReader);
        Assert.Same(graph.Paths, services.Paths);
        Assert.Same(graph, services.Graph);
    }

    [Fact]
    public async Task From_WithReplacements_UsesThemInsteadOfTheGraphs()
    {
        using var graph = await BoreaServices.BuildAsync(_tempRoot);
        var ping = new FakeLatestVersionPing();
        var installed = new FakeInstalledGameVersionProvider();
        var indexFetcher = new FakeContentIndexFetcher();
        var indexReader = new FakeContentIndexReader();

        var services = CliServices.From(graph, ping, installed, indexFetcher, indexReader);

        Assert.Same(ping, services.LatestVersion);
        Assert.Same(installed, services.InstalledVersion);
        Assert.Same(indexFetcher, services.IndexFetcher);
        Assert.Same(indexReader, services.IndexReader);
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
            GameDirectoryChanger = graph.GameDirectoryChanger,
            Instances = graph.Instances,
            ModState = graph.ModState,
            LatestVersion = new FakeLatestVersionPing(),
            InstalledVersion = new FakeInstalledGameVersionProvider(),
            IndexFetcher = new FakeContentIndexFetcher(),
            IndexReader = new FakeContentIndexReader(),
            Paths = graph.Paths,
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
