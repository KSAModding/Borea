using Borea.App.ViewModels;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class InstanceSizeTests
{
    [Fact]
    public async Task OpenInstance_ShowsTheFolderSizesOnTheRowsAndTheTotalInTheHeader()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Foreign);
        var mods = harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId);
        Write(Path.Combine(mods, "AdvancedFlightComputer", "afc.dll"), 2_500_000);
        Write(Path.Combine(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "save.json"), 500_000);
        await viewModel.LoadAsync();

        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        await viewModel.WhenInstanceSizesLoadedAsync();

        var rows = viewModel.ContentGroups.SelectMany(group => group.Items).ToList();
        var afc = rows.Single(row => row.ModId == "AdvancedFlightComputer");
        var armory = rows.Single(row => row.ModId == "KSArmory");
        Assert.Equal(MainViewModel.SizeText(2_500_000 + Existing(mods, "AdvancedFlightComputer")), afc.SizeText);
        Assert.Equal(MainViewModel.SizeText(Existing(mods, "KSArmory")), armory.SizeText);
        Assert.NotNull(viewModel.InstanceSizeText);
        Assert.StartsWith("3.", viewModel.InstanceSizeText!.Replace(',', '.'), StringComparison.Ordinal);
        Assert.EndsWith(" MB", viewModel.InstanceSizeText, StringComparison.Ordinal);
    }

    /// <summary>What the install helper already put into the folder, so the test measures the same files.</summary>
    private static long Existing(string mods, string modId)
    {
        var folder = Path.Combine(mods, modId);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => !file.EndsWith("afc.dll", StringComparison.Ordinal)).Sum(file => new FileInfo(file).Length)
            : 0;
    }

    private static void Write(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
