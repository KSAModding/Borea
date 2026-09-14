using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;

namespace Borea.App.Tests.ViewModels;

public sealed class LaunchFailureTests
{
    private const int UnhandledException = -532462766;

    private static readonly string[] KsArmoryCrash =
    [
        "Unhandled exception. System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        "Method 'DrawAxes' in type 'KSArmory.RoundFollowable' from assembly 'KSArmory, Version=0.8.44.0, Culture=neutral, PublicKeyToken=null' does not have an implementation.",
    ];

    /// <summary>A harness with StarMap recorded and a loader process that has already crashed with <see cref="KsArmoryCrash"/>.</summary>
    private static Task<ViewModelHarness> CreateAsync(int exitCode = UnhandledException) =>
        ViewModelHarness.CreateAsync(
            services =>
            {
                var loader = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
                // both launch targets, so the plan is found on Windows and through dotnet elsewhere
                File.WriteAllBytes(Path.Combine(loader, "StarMap.exe"), []);
                File.WriteAllBytes(Path.Combine(loader, "StarMap.dll"), []);
                return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
                    "StarMap",
                    new LoaderInstallation(loader, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
            },
            processStarter: new CrashingStarter(exitCode, KsArmoryCrash));

    [Fact]
    public async Task Play_LoaderCrashesOnAMod_NamesItAndOffersToDisableIt()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchModBroke("KSArmory", "0.8.44", "StarMap"), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.Contains("DrawAxes", viewModel.LaunchOutputText);
        Assert.True(viewModel.CanDisableBlamedMod);
        Assert.Equal(harness.Localization.FormatLaunchDisableMod("KSArmory"), viewModel.DisableBlamedModText);
        Assert.False(viewModel.IsLaunching);

        viewModel.ToggleLaunchOutputCommand.Execute(null);
        Assert.True(viewModel.IsLaunchOutputShown);

        await viewModel.DisableBlamedModCommand.ExecuteAsync(null);

        Assert.False(await harness.Services.ModState.IsActiveAsync(instance.InstanceId, "KSArmory"));
        Assert.Equal(harness.Localization.FormatLaunchModDisabled("KSArmory"), viewModel.LaunchMessage);
        Assert.False(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
        Assert.False(viewModel.ContentGroups.SelectMany(group => group.Items).Single().IsEnabled);
    }

    [Fact]
    public async Task Play_LoaderStopsWithoutNamingAMod_ShowsTheDetailsWithoutADisableButton()
    {
        using var harness = await CreateAsync(exitCode: 3);
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);

        await viewModel.PlayCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatLaunchExitedEarly("StarMap", 3), viewModel.LaunchMessage);
        Assert.True(viewModel.HasLaunchOutput);
        Assert.False(viewModel.CanDisableBlamedMod);
    }

    /// <summary>Hands out a loader process that has already exited with the given code and output.</summary>
    private sealed class CrashingStarter(int exitCode, IReadOnlyList<string> output) : IProcessStarter
    {
        public IStartedProcess Start(LaunchPlan plan) => new CrashedProcess(exitCode, output);

        private sealed class CrashedProcess(int exitCode, IReadOnlyList<string> output) : IStartedProcess
        {
            public int Id => 4242;

            public bool HasExited => true;

            public int? ExitCode => exitCode;

            public IReadOnlyList<string> RecentOutput => output;

            public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult(true);

            public void Dispose()
            {
            }
        }
    }
}
