namespace Borea.Cli.Tests;

public sealed class LaunchCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Launch_StarMapStartsWithItsExistingHandover()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(0, run.ExitCode);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
        Assert.True(Path.IsPathFullyQualified(plan.Arguments[1]));
        Assert.Equal(plan.Arguments[1], plan.EnvironmentVariables["STARMAP_INSTANCE_PATH"]);
        Assert.Contains("Process id: 42", run.Output);
    }

    [Fact]
    public async Task Launch_LoaderWithoutAnExistingHandover_FailsWithoutStarting()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "OtherLoader");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("does not know how OtherLoader takes an instance", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_UnknownInstance_Fails()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        var run = await _host.RunAsync("launch", "Missing", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No instance is named", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_CancelledAfterLoaderLookup_DoesNotStartAProcess()
    {
        var listing = LoaderFixtures.Listing();
        _host.Mods.Listings.Add(listing);
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        using var cancellation = new CancellationTokenSource();
        _host.Mods.AvailableMods = _ =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<Borea.Core.Mods.ModMetadata>>([listing]);
        };

        var run = await _host.RunAsync(cancellation.Token, "launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("command was cancelled", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    public void Dispose() => _host.Dispose();
}
