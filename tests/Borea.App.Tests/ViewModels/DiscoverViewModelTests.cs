using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Preferences;

namespace Borea.App.Tests.ViewModels;

public sealed class DiscoverViewModelTests
{
    [Fact]
    public async Task Load_ListsOnlyTheContentIndex()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SetMainWindowDiscoverCommand.Execute(null);
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal("index", item.Source));
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(ContentType.Mod, item.Type));
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.OwnId);
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == ViewModelHarness.FakeSpaceDock.MirroredId);
        Assert.Equal(["MIT"], viewModel.LicenseOptions.Select(license => license.Value));
        Assert.Null(viewModel.DiscoverError);
    }

    [Fact]
    public async Task Load_TakesTheDownloadsFromTheSnapshotAndTheAgeFromTheChannelRelease()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithDownloadsAndDates(json, "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var armory = viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory");
        var release = await harness.Services.ContentIndex.GetLatestReleaseInChannelAsync(afc.ModId, harness.Services.Settings.ReleaseChannel);

        Assert.Equal(1234L, afc.Downloads);
        Assert.Equal(MainViewModel.CompactCount(1234), afc.DownloadsText);
        Assert.Equal(harness.Localization.FormatContentDownloadsExact(1234L.ToString("N0", CultureInfo.CurrentCulture)), afc.DownloadsExactText);
        Assert.StartsWith("Published ", afc.PublishedText);
        Assert.NotNull(afc.PublishedDateText);

        // the row installs the newest release of the channel, so its age comes from that release and not from updated_at
        Assert.Equal<DateTimeOffset?>(release!.ReleaseDate, afc.UpdatedAt);
        Assert.NotEqual<DateTimeOffset?>(SnapshotUpdatedAt, afc.UpdatedAt);
        Assert.Equal(harness.Localization.FormatTimeAgoShort(DateTimeOffset.UtcNow - afc.UpdatedAt!.Value), afc.UpdatedText);
        Assert.Equal($"Updated on {MainViewModel.DateText(afc.UpdatedAt.Value)}", afc.UpdatedDateText);

        Assert.Null(armory.DownloadsText);
        Assert.Null(armory.PublishedText);
        Assert.NotNull(armory.UpdatedText);
    }

    [Fact]
    public async Task LanguageChange_TranslatesTheAgesOfRowsAndCards()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithDownloadsAndDates(json, "AdvancedFlightComputer"));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var card = viewModel.RecentItems.Single(item => item.ModId == "AdvancedFlightComputer");
        var changed = new List<string?>();
        afc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        card.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var rowAge = afc.UpdatedText;
        var published = afc.PublishedText;
        var cardAge = card.UpdatedText;

        harness.Localization.TrySetCulture("de");

        Assert.Contains(nameof(DiscoverItem.UpdatedText), changed);
        Assert.Contains(nameof(DiscoverItem.PublishedText), changed);
        Assert.Contains(nameof(RecentItem.UpdatedText), changed);
        Assert.NotEqual(rowAge, afc.UpdatedText);
        Assert.NotEqual(published, afc.PublishedText);
        Assert.NotEqual(cardAge, card.UpdatedText);
    }

    private static readonly DateTimeOffset SnapshotUpdatedAt = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Adds a download count and both dates to one listing of the snapshot, with <see cref="SnapshotUpdatedAt"/> as its updated date.</summary>
    private static string WithDownloadsAndDates(string json, string listingId)
    {
        var marker = $"\"id\": \"{listingId}\",";
        var at = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return json.Insert(at, """ "downloads": { "total": 1234, "hosts": { "github": 1234 } }, "published_at": "2026-08-01T00:00:00Z", "updated_at": "2026-09-14T00:00:00Z",""");
    }

    [Fact]
    public async Task LoadersTab_ShowsOnlyLoaders()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.ShowDiscoverLoadersCommand.Execute(null);

        Assert.True(viewModel.IsLoadersTab);
        var starMap = Assert.Single(viewModel.DiscoverItems);
        Assert.Equal("StarMap", starMap.ModId);
        Assert.False(starMap.CanInstall);
        Assert.Equal(harness.Localization.ContentTypeModLoader, starMap.TypeText);

        viewModel.ShowDiscoverModsCommand.Execute(null);
        Assert.True(viewModel.IsModsTab);
    }

    [Fact]
    public async Task Search_MatchesNameAbstractOrAuthor()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "advanced flight";
        Assert.Equal(["AdvancedFlightComputer"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.SearchText = "laurens";
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "KSArmory");

        viewModel.SearchText = "no such mod anywhere";
        Assert.False(viewModel.HasDiscoverItems);
    }

    [Fact]
    public async Task Search_MatchesATag()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "weapons";

        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task Search_MatchesACuratedTagByItsName()
    {
        var tags = ViewModelHarness.CuratedTags(("weapons", "Hardware"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "hardware";

        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task Categories_WithoutCuratedTags_ShowNoRows()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Empty(viewModel.CategoryOptions);
    }

    [Fact]
    public async Task Categories_ListTheCuratedTagsInUseAndOther()
    {
        var tags = ViewModelHarness.CuratedTags(("gameplay", "Gameplay"), ("parts", "Parts"), ("library", "Library"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Equal(["Parts", harness.Localization.DiscoverCategoryOther], viewModel.CategoryOptions.Select(category => category.Name));
        Assert.Equal("parts", viewModel.CategoryOptions[0].Tag);
        Assert.Equal("Parts content.", viewModel.CategoryOptions[0].Meaning);
        Assert.True(viewModel.CategoryOptions[1].IsOther);

        var other = viewModel.CategoryOptions[1].Name;
        harness.Localization.TrySetCulture("de");
        Assert.NotEqual(other, viewModel.CategoryOptions[1].Name);
    }

    [Fact]
    public async Task Categories_TranslateAKnownTagAndKeepTheIndexNameOfAnUnknownOne()
    {
        var tags = ViewModelHarness.CuratedTags(("parts", "Parts"), ("weapons", "Hardware"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        harness.Localization.TrySetCulture("de");

        Assert.Equal(harness.Localization.DiscoverCategoryParts, viewModel.CategoryOptions.Single(category => category.Tag == "parts").Name);
        Assert.Equal("Hardware", viewModel.CategoryOptions.Single(category => category.Tag == "weapons").Name);
        var armory = viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory");
        Assert.Equal([harness.Localization.DiscoverCategoryParts, "Hardware", "physics"], armory.AllTags);
    }

    [Fact]
    public async Task Search_InAnotherLanguage_MatchesTheTranslatedNameAndTheTagButNotTheIndexName()
    {
        var tags = ViewModelHarness.CuratedTags(("parts", "Hardware"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        harness.Localization.TrySetCulture("de");

        viewModel.SearchText = harness.Localization.DiscoverCategoryParts;
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.SearchText = "parts";
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));

        // the App translates this tag, so its English name in the index is no longer shown or searched
        viewModel.SearchText = "hardware";
        Assert.False(viewModel.HasDiscoverItems);
    }

    [Fact]
    public async Task Search_WhenTheLanguageChanges_SelectsTheRowsAgain()
    {
        var tags = ViewModelHarness.CuratedTags(("parts", "Parts"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        harness.Localization.TrySetCulture("de");

        viewModel.SearchText = harness.Localization.DiscoverCategoryParts;
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));

        harness.Localization.TrySetCulture("en");

        Assert.False(viewModel.HasDiscoverItems);
    }

    [Fact]
    public async Task CategoryFilter_SelectsSeveralAndClearsOnASecondClick()
    {
        var tags = ViewModelHarness.CuratedTags(("parts", "Parts"), ("library", "Library"));
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: tags);
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var parts = viewModel.CategoryOptions.Single(category => category.Tag == "parts");
        var other = viewModel.CategoryOptions.Single(category => category.IsOther);

        viewModel.ToggleCategoryCommand.Execute(parts);
        Assert.True(parts.IsSelected);
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Equal([parts], viewModel.SelectedCategories);
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.ToggleCategoryCommand.Execute(other);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.ToggleCategoryCommand.Execute(parts);
        Assert.False(parts.IsSelected);
        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));

        viewModel.ShowDiscoverLoadersCommand.Execute(null);
        Assert.Equal("StarMap", Assert.Single(viewModel.DiscoverItems).ModId);

        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(other.IsSelected);
        Assert.Empty(viewModel.SelectedCategories);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal("StarMap", Assert.Single(viewModel.DiscoverItems).ModId);
    }

    [Fact]
    public async Task LicenseFilter_NarrowsTheListUntilCleared()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var all = viewModel.DiscoverItems.Count;

        viewModel.SelectLicenseCommand.Execute("GPL-3.0");
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.False(viewModel.HasDiscoverItems);

        viewModel.SelectOsCommand.Execute("windows");
        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal(all, viewModel.DiscoverItems.Count);
    }

    [Fact]
    public async Task LicenseFilter_MarksTheChosenLicenseUntilItIsChosenAgainOrCleared()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var mit = viewModel.LicenseOptions.Single();

        viewModel.SelectLicenseCommand.Execute("MIT");
        Assert.True(mit.IsSelected);

        viewModel.SelectLicenseCommand.Execute("MIT");
        Assert.False(mit.IsSelected);
        Assert.Null(viewModel.SelectedLicense);

        viewModel.SelectLicenseCommand.Execute("MIT");
        viewModel.SelectLicenseCommand.Execute(null);
        Assert.False(mit.IsSelected);

        viewModel.SelectLicenseCommand.Execute("MIT");
        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(mit.IsSelected);
    }

    [Fact]
    public async Task Count_WithoutAFilter_NamesTheEntriesOfTheTab()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Equal("3 mods", viewModel.DiscoverCountText);

        viewModel.ShowDiscoverLoadersCommand.Execute(null);
        Assert.Equal("1 mod loader", viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Count_WithAFilterOrASearch_NamesTheShownAndTheTotal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.SearchText = "advanced flight";
        Assert.Equal("1 of 3 mods", viewModel.DiscoverCountText);

        viewModel.SearchText = string.Empty;
        viewModel.SelectLicenseCommand.Execute("GPL-3.0");
        Assert.Equal("0 of 3 mods", viewModel.DiscoverCountText);

        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.Equal("3 mods", viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Count_InGerman_UsesTheGermanWords()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Contains(nameof(MainViewModel.DiscoverCountText), changed);
        Assert.Equal("3 Mods", viewModel.DiscoverCountText);
        viewModel.SearchText = "advanced flight";
        Assert.Equal("1 von 3 Mods", viewModel.DiscoverCountText);
    }

    [Fact]
    public async Task Count_WhenTheIndexIsUnreachable_IsHidden()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);
        await harness.ViewModel.EnsureDiscoverLoadedAsync();

        Assert.Null(harness.ViewModel.DiscoverCountText);
    }

    [Fact]
    public async Task HideInstalled_DropsModsTheActiveInstanceHolds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.True(afc.IsInstalled);
        Assert.False(afc.CanInstall);

        viewModel.HideInstalled = true;
        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
    }

    [Fact]
    public async Task InstalledInOtherInstances_ShowsOnlyModsAnotherInstanceHolds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var main = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: true, into: main);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: false, into: other);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false, into: other);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();

        viewModel.InstalledInOtherInstances = true;

        Assert.Equal(["KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task InstalledInOtherInstances_FollowsTheActiveInstanceAndARemoval()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var main = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        var other = (await harness.Services.Instances.CreateAsync("Other", InstanceSource.Custom.Value)).Instance;
        await InstalledContent.AddAsync(harness, "KSArmory", activate: false, ownership: ModInstallOwnership.Borea, into: other);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.InstalledInOtherInstances = true;
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));

        await viewModel.ActivateInstanceAsync(other.InstanceId);
        Assert.Equal(["AdvancedFlightComputer"], viewModel.DiscoverItems.Select(item => item.ModId));

        await viewModel.ActivateInstanceAsync(main.InstanceId);
        await viewModel.RemoveContentAsync(other.InstanceId, "KSArmory");
        Assert.Empty(viewModel.DiscoverItems);
    }

    [Fact]
    public async Task Install_DownloadFails_ShowsTheErrorOnTheRow()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        await item.InstallCommand.ExecuteAsync(null);

        // without a game the compatibility is unknown, so the plan waits for a confirmation
        Assert.NotNull(item.InstallWarning);
        Assert.Null(item.InstallError);
        await item.ConfirmInstallCommand.ExecuteAsync(null);

        Assert.Null(item.InstallWarning);
        Assert.Null(item.PendingPlan);
        Assert.NotNull(item.InstallError);
        Assert.False(item.IsInstalling);
        Assert.Equal(0, item.Progress);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Install_WarningCancelled_InstallsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        await harness.Services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "AdvancedFlightComputer");

        await item.InstallCommand.ExecuteAsync(null);
        Assert.Contains("AdvancedFlightComputer", item.InstallWarning);
        Assert.NotNull(item.PendingPlan);

        item.CancelInstallCommand.Execute(null);

        Assert.Null(item.InstallWarning);
        Assert.Null(item.PendingPlan);
        Assert.Null(item.InstallError);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
    }

    [Fact]
    public async Task Remove_AfterConfirmation_TakesTheModOutOfTheActiveInstance()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");
        Assert.Equal(harness.Localization.FormatDiscoverInstalledIn("Main"), viewModel.InstalledInText);
        Assert.Contains(afc.Name, afc.InstalledAutomationName);
        Assert.True(afc.BeginRemoveCommand.CanExecute(null));

        afc.BeginRemoveCommand.Execute(null);
        Assert.True(afc.IsConfirmingRemove);
        afc.CancelRemoveCommand.Execute(null);
        Assert.False(afc.IsConfirmingRemove);
        Assert.True(afc.IsInstalled);

        afc.BeginRemoveCommand.Execute(null);
        var canRemoveChanged = 0;
        afc.BeginRemoveCommand.CanExecuteChanged += (_, _) => canRemoveChanged++;
        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.True(canRemoveChanged > 0);
        Assert.False(afc.BeginRemoveCommand.CanExecute(null));
        Assert.False(afc.IsConfirmingRemove);
        Assert.False(afc.IsRemoving);
        Assert.Null(afc.InstallError);
        Assert.False(afc.IsInstalled);
        Assert.True(afc.CanInstall);
        Assert.Empty((await harness.Services.Instances.GetByIdAsync(instance.InstanceId))!.Mods);
        Assert.Equal(0, viewModel.ActiveInstance?.ModCount);
    }

    [Fact]
    public async Task Remove_FromDiscover_StaysOnDiscover()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await viewModel.LoadAsync();
        // the user visited the instance page earlier, then came to Discover
        await viewModel.ActiveInstance!.OpenCommand.ExecuteAsync(null);
        viewModel.SetMainWindowDiscover();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        afc.BeginRemoveCommand.Execute(null);
        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowInstance);
        Assert.False(afc.IsInstalled);
    }

    [Fact]
    public async Task Remove_ForeignFiles_StaysInstalledAndSaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true);
        await viewModel.LoadAsync();
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        afc.BeginRemoveCommand.Execute(null);
        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatContentRemoveNotOwned("AdvancedFlightComputer"), afc.InstallError);
        Assert.True(afc.IsInstalled);
        Assert.False(afc.IsConfirmingRemove);
    }

    [Fact]
    public async Task Remove_WithoutActiveInstance_DoesNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        await afc.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Null(afc.InstallError);
        Assert.Null(viewModel.InstalledInText);
    }

    [Fact]
    public async Task LanguageChange_RetranslatesRows()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var item = viewModel.DiscoverItems.Single(row => row.ModId == "KSArmory");
        var english = item.AuthorsText;

        harness.Localization.TrySetCulture("de");

        Assert.NotEqual(english, item.AuthorsText);
        Assert.Contains("Laurens", item.AuthorsText);
    }

    [Fact]
    public async Task Load_WithoutAGame_MarksCompatibilityUnknown()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        var afc = viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer");

        Assert.Equal(GameCompatibility.Unknown, afc.Compatibility);
        Assert.Equal(harness.Localization.CompatibilityUnknown, afc.CompatibilityText);
    }

    [Fact]
    public async Task HideIncompatible_DropsListingsWhoseNewestReleaseNeedsANewerGame()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));

        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.True(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer").IsIncompatible);
        Assert.True(viewModel.DiscoverItems.Single(item => item.ModId == "KSArmory").IsCompatible);

        viewModel.HideIncompatible = true;

        Assert.DoesNotContain(viewModel.DiscoverItems, item => item.IsIncompatible);
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "KSArmory");
    }

    [Fact]
    public async Task ShowFilters_CountAsFiltersAndClearAllTurnsThemOff()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(viewModel.HasDiscoverFilters);

        viewModel.HideIncompatible = true;
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Contains(nameof(MainViewModel.HasDiscoverFilters), changed);

        viewModel.ClearHideIncompatibleCommand.Execute(null);
        Assert.False(viewModel.HideIncompatible);
        Assert.False(viewModel.HasDiscoverFilters);

        changed.Clear();
        viewModel.HideInstalled = true;
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Contains(nameof(MainViewModel.HasDiscoverFilters), changed);

        viewModel.ClearHideInstalledCommand.Execute(null);
        Assert.False(viewModel.HideInstalled);
        Assert.False(viewModel.HasDiscoverFilters);

        viewModel.HideInstalled = true;
        viewModel.HideIncompatible = true;
        viewModel.SelectOsCommand.Execute("windows");

        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(viewModel.HideInstalled);
        Assert.False(viewModel.HideIncompatible);
        Assert.Null(viewModel.SelectedOs);
        Assert.False(viewModel.HasDiscoverFilters);
    }

    [Fact]
    public async Task InstalledInOtherInstances_CountsAsAFilterAndClearAllTurnsItOff()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.InstalledInOtherInstances = true;
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Contains(nameof(MainViewModel.HasDiscoverFilters), changed);

        viewModel.ClearInstalledInOtherInstancesCommand.Execute(null);
        Assert.False(viewModel.InstalledInOtherInstances);
        Assert.False(viewModel.HasDiscoverFilters);

        viewModel.InstalledInOtherInstances = true;
        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.False(viewModel.InstalledInOtherInstances);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task CompatibilityChip_NinetyPercentCompatible_HidesOnlyTheirChips()
    {
        // with this game, AdvancedFlightComputer and MeasureTools are incompatible and the eighteen KSArmory rows are compatible
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithCopies(json, "KSArmory", 17));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));

        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.Equal(20, viewModel.DiscoverItems.Count);
        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(!item.IsCompatible, item.ShowsCompatibility));
        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], viewModel.DiscoverItems.Where(item => item.ShowsCompatibility).Select(item => item.ModId).Order());

        harness.Localization.TrySetCulture("de");

        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(!item.IsCompatible, item.ShowsCompatibility));
    }

    [Fact]
    public async Task CompatibilityChip_BelowTheShare_ShowsEveryChip()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithCopies(json, "KSArmory", 16));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));

        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.Equal(19, viewModel.DiscoverItems.Count);
        Assert.Equal(17, viewModel.DiscoverItems.Count(item => item.IsCompatible));
        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.ShowsCompatibility));
    }

    [Fact]
    public async Task CompatibilityChip_FewerThanFiveRows_ShowsEveryChip()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.9.7.5402", out var installed));

        await viewModel.RefreshCompatibilityAsync(installed);

        Assert.Equal(3, viewModel.DiscoverItems.Count);
        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.IsCompatible));
        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.ShowsCompatibility));
    }

    [Fact]
    public async Task CompatibilityChip_FollowsTheFiltersAndTheGame()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithCopies(json, "KSArmory", 6));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.True(GameVersion.TryParse("2026.8.22.5348", out var installed));
        await viewModel.RefreshCompatibilityAsync(installed);
        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.ShowsCompatibility));

        viewModel.HideIncompatible = true;

        Assert.Equal(7, viewModel.DiscoverItems.Count);
        Assert.All(viewModel.DiscoverItems, item => Assert.False(item.ShowsCompatibility));

        viewModel.HideIncompatible = false;

        Assert.All(viewModel.DiscoverItems, item => Assert.True(item.ShowsCompatibility));

        await viewModel.RefreshCompatibilityAsync(null);

        Assert.All(viewModel.DiscoverItems, item => Assert.Equal(GameCompatibility.Unknown, item.Compatibility));
        Assert.All(viewModel.DiscoverItems, item => Assert.False(item.ShowsCompatibility));
    }

    [Fact]
    public async Task GameVersionRange_KeepsListingsWhoseChannelReleaseSupportsABuildInIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var builds = viewModel.GameVersionOptions;
        Assert.Equal("2026.9.7.5402", builds[0].Text);
        Assert.Equal(builds.Select(build => build.Revision).OrderDescending(), builds.Select(build => build.Revision));

        // the newest releases of AdvancedFlightComputer and MeasureTools need 5400, and the one of KSArmory needs 5261 with no upper bound
        viewModel.DiscoverGameMax = builds.Single(build => build.Revision == 5261);
        Assert.Equal(["KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.True(viewModel.HasDiscoverFilters);
        Assert.Equal("<= 2026.8.19.5261", viewModel.DiscoverGameVersionRangeText);

        viewModel.DiscoverGameMax = null;
        viewModel.DiscoverGameMin = builds.Single(build => build.Revision == 5402);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.Equal(">= 2026.9.7.5402", viewModel.DiscoverGameVersionRangeText);

        viewModel.DiscoverGameMax = builds.Single(build => build.Revision == 5402);
        Assert.Equal(3, viewModel.DiscoverItems.Count);
        Assert.Equal("2026.9.7.5402", viewModel.DiscoverGameVersionRangeText);

        viewModel.ClearDiscoverFiltersCommand.Execute(null);
        Assert.Null(viewModel.DiscoverGameMin);
        Assert.Null(viewModel.DiscoverGameMax);
        Assert.False(viewModel.HasDiscoverFilters);
        Assert.Equal(3, viewModel.DiscoverItems.Count);
    }

    [Fact]
    public async Task GameVersionRange_ClearingOneBound_KeepsTheOther()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var older = viewModel.GameVersionOptions.Single(build => build.Revision == 5261);
        var newer = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);
        viewModel.DiscoverGameMin = older;
        viewModel.DiscoverGameMax = newer;

        viewModel.ClearDiscoverGameMaxCommand.Execute(null);
        Assert.Same(older, viewModel.DiscoverGameMin);
        Assert.Equal(">= 2026.8.19.5261", viewModel.DiscoverGameVersionRangeText);

        viewModel.DiscoverGameMax = newer;
        viewModel.ClearDiscoverGameMinCommand.Execute(null);
        Assert.Same(newer, viewModel.DiscoverGameMax);
        Assert.Equal("<= 2026.9.7.5402", viewModel.DiscoverGameVersionRangeText);

        viewModel.ClearDiscoverGameVersionRangeCommand.Execute(null);
        Assert.Null(viewModel.DiscoverGameVersionRangeText);
        Assert.False(viewModel.HasDiscoverFilters);
    }

    [Fact]
    public async Task GameVersionRange_BoundPastTheOtherOne_MovesTheOtherBound()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var older = viewModel.GameVersionOptions.Single(build => build.Revision == 5261);
        var newer = viewModel.GameVersionOptions.Single(build => build.Revision == 5402);

        viewModel.DiscoverGameMax = older;
        viewModel.DiscoverGameMin = newer;
        Assert.Same(newer, viewModel.DiscoverGameMax);

        viewModel.DiscoverGameMax = older;
        Assert.Same(older, viewModel.DiscoverGameMin);
    }

    [Theory]
    [InlineData(DiscoverSortOrder.Popularity, new[] { "AdvancedFlightComputer", "MeasureTools", "KSArmory" })]
    [InlineData(DiscoverSortOrder.RecentlyUpdated, new[] { "MeasureTools", "AdvancedFlightComputer", "KSArmory" })]
    [InlineData(DiscoverSortOrder.Name, new[] { "AdvancedFlightComputer", "KSArmory", "MeasureTools" })]
    public async Task Sort_OrdersTheModsTab(DiscoverSortOrder order, string[] expected)
    {
        // KSArmory has no download count, and its newest release is the oldest of the three
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithDownloads(WithDownloads(json, "AdvancedFlightComputer", 1234), "MeasureTools", 56));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Equal(DiscoverSortOrder.Popularity, viewModel.DiscoverSort);

        viewModel.SelectDiscoverSortCommand.Execute(order);

        Assert.Equal(expected, viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task Sort_Change_MovesTheSameRows()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => WithDownloads(json, "MeasureTools", 56));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        var rows = viewModel.DiscoverItems.ToDictionary(item => item.ModId);
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.DiscoverItems.CollectionChanged += (_, e) => changes.Add(e.Action);

        viewModel.SelectDiscoverSortCommand.Execute(DiscoverSortOrder.Name);

        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools"], viewModel.DiscoverItems.Select(item => item.ModId));
        Assert.All(viewModel.DiscoverItems, item => Assert.Same(rows[item.ModId], item));
        Assert.All(changes, change => Assert.Equal(NotifyCollectionChangedAction.Move, change));
    }

    [Fact]
    public void Arrange_KeepsTheRowsThatStayAndFollowsTheNewOrder()
    {
        var rows = new ObservableCollection<string> { "a", "b", "c", "d", "e" };
        var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => changes.Add(e.Action);

        MainViewModel.Arrange(rows, ["e", "c", "x", "a"]);

        Assert.Equal(["e", "c", "x", "a"], rows);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.Equal(2, changes.Count(change => change == NotifyCollectionChangedAction.Remove));
        Assert.Equal(1, changes.Count(change => change == NotifyCollectionChangedAction.Add));
    }

    [Fact]
    public async Task Sort_LoadersTab_KeepsTheNameOrder()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json =>
        {
            var root = JsonNode.Parse(json)!;
            var listings = root["listings"]!.AsArray();
            var copy = listings.Single(node => (string?)node!["id"] == "StarMap")!.DeepClone();
            copy["id"] = "TestLoader";
            copy["authored"]!["id"] = "TestLoader";
            copy["authored"]!["name"] = "Test Loader";
            foreach (var release in copy["releases"]!.AsArray())
            {
                release!["id"] = "TestLoader";
                release["listing"]!["name"] = "Test Loader";
            }

            copy["downloads"] = JsonNode.Parse("""{ "total": 1234, "hosts": { "github": 1234 } }""");
            listings.Add(copy);
            return root.ToJsonString();
        });
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        Assert.Equal(DiscoverSortOrder.Popularity, viewModel.DiscoverSort);

        viewModel.ShowDiscoverLoadersCommand.Execute(null);

        Assert.Equal(["StarMap", "TestLoader"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    [Fact]
    public async Task SelectSort_SavesTheChoice()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        viewModel.SelectDiscoverSortCommand.Execute(DiscoverSortOrder.RecentlyUpdated);
        await viewModel.WhenPreferencesSavedAsync();

        var saved = await harness.Services.AppPreferences.GetAsync(MainViewModel.BundledThemeNames);
        Assert.Equal(DiscoverSortOrder.RecentlyUpdated, saved.Preferences.DiscoverSortOrder);
        Assert.Equal(harness.Localization.DiscoverSortRecentlyUpdated, viewModel.DiscoverSortText);
        Assert.Null(viewModel.PreferenceSaveError);
    }

    [Fact]
    public async Task Load_SavedSort_OrdersTheRowsByIt()
    {
        using var harness = await ViewModelHarness.CreateAsync(
            seed: services => services.AppPreferences.SaveAsync(AppPreferences.Empty.WithDiscoverSortOrder(DiscoverSortOrder.RecentlyUpdated), MainViewModel.BundledThemeNames));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();

        Assert.Equal(DiscoverSortOrder.RecentlyUpdated, viewModel.DiscoverSort);
        Assert.Equal(["MeasureTools", "AdvancedFlightComputer", "KSArmory"], viewModel.DiscoverItems.Select(item => item.ModId));
    }

    private static string WithCopies(string json, string listingId, int count)
    {
        var root = JsonNode.Parse(json)!;
        var listings = root["listings"]!.AsArray();
        var original = listings.Single(node => (string?)node!["id"] == listingId)!;
        for (var number = 1; number <= count; number++)
        {
            var id = $"{listingId}{number}";
            var copy = original.DeepClone();
            copy["id"] = id;
            copy["authored"]!["id"] = id;
            copy["authored"]!["name"] = id;
            foreach (var release in copy["releases"]!.AsArray())
            {
                release!["id"] = id;
                release["listing"]!["name"] = id;
            }

            listings.Add(copy);
        }

        return root.ToJsonString();
    }

    private static string WithDownloads(string json, string listingId, long total)
    {
        var marker = $"\"id\": \"{listingId}\",";
        var at = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return json.Insert(at, $$""" "downloads": { "total": {{total}}, "hosts": { "github": {{total}} } },""");
    }
}

/// <summary>
/// Puts a mod into an instance without downloading it: the release comes from
/// the index fixture, the newest one unless the caller names a version. The
/// files are marked as not Borea's unless the caller says Borea installed them,
/// and then the folder carries the ownership file an install writes.
/// </summary>
internal static class InstalledContent
{
    /// <param name="into">The instance to add to. Without one, the first instance, created as Main when there is none.</param>
    public static async Task<Instance> AddAsync(ViewModelHarness harness, string modId, bool activate, InstallReason reason = InstallReason.Manual, ModInstallOwnership ownership = ModInstallOwnership.Foreign, string? version = null, Instance? into = null)
    {
        var services = harness.Services;
        var instance = into
            ?? (await services.Instances.GetAllAsync()).FirstOrDefault()
            ?? (await services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var release = (version is null ? await services.Mods.GetLatestReleaseAsync(modId) : await services.Mods.GetReleaseAsync(modId, ModVersion.Parse(version)))
            ?? throw new InvalidOperationException($"The fixture has no release of {modId}.");

        // the manifest only lists a mod whose folder holds a mod.toml
        var folder = Directory.CreateDirectory(Path.Combine(services.Paths.GetInstanceModsFolder(instance.InstanceId), modId));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{modId}\"");
        var token = ownership == ModInstallOwnership.Borea ? Guid.NewGuid().ToString("N") : null;
        if (token is not null)
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, ".borea-owner"), token);
        instance.AddMod(new InstalledMod(modId, release.Version, reason, DateTimeOffset.UtcNow, release, ownership: ownership, ownershipToken: token));
        await services.Instances.SaveAsync(instance);
        await services.ModState.AddEntryAsync(instance.InstanceId, modId, enabled: true);
        if (activate)
            await services.Instances.SetActiveInstanceAsync(instance.InstanceId);
        return instance;
    }
}
