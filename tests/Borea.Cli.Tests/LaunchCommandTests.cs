using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class LaunchCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    [Fact]
    public async Task Launch_StarMapStartsWithTheHandoverItsListingNames()
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
    public async Task Launch_LoaderStopsRightAway_FailsWithWhatItWrote()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await _host.RunAsync("instance", "create", "Flight Test");
        _host.ProcessStarter.CrashExitCode = -532462766;
        _host.ProcessStarter.CrashOutput.Add("Unhandled exception. System.TypeLoadException: Method 'DrawAxes' from assembly 'KSArmory, Version=0.8.44.0' does not have an implementation.");

        var run = await _host.RunAsync("launch", "Flight Test", "StarMap");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("exit code -532462766", run.Error);
        Assert.Contains("DrawAxes", run.Error);
        Assert.DoesNotContain("Process id", run.Output);
    }

    [Fact]
    public async Task Launch_LoaderWhoseListingHasNoInstanceTable_FailsWithoutStarting()
    {
        _host.Mods.Listings.Add(LoaderFixtures.ListingWithoutInstance("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "OtherLoader");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The listing of OtherLoader does not say how it takes an instance", run.Error);
        Assert.Contains("[provides.instance]", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_UnknownInstance_WritesTheCommandAndTheFailureToTheCliLog()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());

        await _host.RunAsync("launch", "Missing", "StarMap");

        var logs = Directory.GetFiles(Path.Combine(_host.Root, "Logs"), "borea-*.log");
        var text = File.ReadAllText(Assert.Single(logs));
        Assert.Contains("[cli] Command: borea launch Missing StarMap", text);
        Assert.Contains("[cli] Command failed." + Environment.NewLine + "System.InvalidOperationException: No instance is named 'Missing'.", text);
    }

    [Fact]
    public async Task InstanceList_WritesTheExitCodeToTheCliLog()
    {
        var run = await _host.RunAsync("instance", "list");

        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_host.Root, "Logs"), "borea-*.log")));
        Assert.Contains($"[cli] Command finished with exit code {run.ExitCode}.", text);
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

    [Fact]
    public async Task Launch_WithoutLoaderId_UsesTheOneLoaderTheModsNeed()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        var loaderDirectory = LoaderCommandTests.CreateLoaderDirectory("StarMap", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "StarMap", loaderDirectory);
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "starmap"), ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("The mods in 'Flight Test' need StarMap.", run.Output);
        var plan = Assert.Single(_host.ProcessStarter.Plans);
        Assert.Equal("-InstancePath", plan.Arguments[0]);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_NeededLoaderNotAvailable_NamesTheNeededLoader()
    {
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("The mods in 'Flight Test' need StarMap.", run.Output);
        Assert.Contains("Mod loader 'StarMap' is not available", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_GivenLoaderId_IsUsedWhenTheModsNeedAnother()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));
        var directory = LoaderCommandTests.CreateLoaderDirectory("OtherLoader", "not a program", _host.Root);
        await _host.RunAsync("settings", "set", "loader", "OtherLoader", directory);
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"));

        var run = await _host.RunAsync("launch", "Flight Test", "OtherLoader");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("The mods in", run.Output);
        Assert.Contains("Started OtherLoader", run.Output);
        Assert.Single(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ModsNeedNoLoader_PointsToGameLaunch()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        await SaveInstanceAsync(ContentCommandFixtures.Release("parts-pack"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No mod in 'Flight Test' needs a mod loader", run.Error);
        Assert.Contains("'borea game launch'", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_WithoutLoaderId_ModsNeedTwoLoaders_AsksForTheId()
    {
        _host.Mods.Listings.Add(LoaderFixtures.Listing());
        _host.Mods.Listings.Add(LoaderFixtures.Listing("OtherLoader"));
        await SaveInstanceAsync(NeedsLoader("flight-tools", "StarMap"), NeedsLoader("orbit-tools", "OtherLoader"));

        var run = await _host.RunAsync("launch", "Flight Test");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("need different mod loaders: OtherLoader, StarMap", run.Error);
        Assert.Contains("<loader-id>", run.Error);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    [Fact]
    public async Task Launch_InvalidLoaderId_IsBadUsage()
    {
        await _host.RunAsync("instance", "create", "Flight Test");

        var run = await _host.RunAsync("launch", "Flight Test", "not a valid id");

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(_host.ProcessStarter.Plans);
    }

    private async Task SaveInstanceAsync(params ModVersionMetadata[] releases)
    {
        var mods = releases
            .Select(release => new InstalledMod(release.ModId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release))
            .ToList();
        var instance = Instance.FromExisting(Guid.NewGuid(), "Flight Test", InstanceSource.Custom.Value, DateTimeOffset.UtcNow, mods, isFavorite: false);
        await new FileInstanceRepository(_host.Paths).SaveAsync(instance);
    }

    private static ModVersionMetadata NeedsLoader(string id, string loaderId) =>
        ContentCommandFixtures.Release(id, loader: new LoaderRequirement(loaderId, ModVersion.Parse("0.4.0")));

    public void Dispose() => _host.Dispose();
}
