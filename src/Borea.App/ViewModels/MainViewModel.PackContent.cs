using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The pinned vehicles and saves of a pack (RFC 0085): their rows on the pack page, and the questions a pack install
/// or update asks before it runs, because it runs as one task. A taken name opens the name modal, and a changed item is
/// named in the confirmation, whose go replaces it.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The names of the vehicle and save listings a pack plan named, by listing id.</summary>
    private readonly Dictionary<string, string> _userContentNames = new(ModIds.Comparer);

    /// <summary>The vehicles the open pack pins.</summary>
    public ObservableCollection<PackContentItem> PackVehicles { get; } = [];

    /// <summary>The saves the open pack pins.</summary>
    public ObservableCollection<PackContentItem> PackSaves { get; } = [];

    /// <summary>The pinned vehicle or save the name modal asks a new name for. Null while the modal is for something else or closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameModalOpen), nameof(NameModalTitle), nameof(NameModalConfirmText), nameof(NameModalPlaceholder), nameof(ShowsGameSaveNameRules), nameof(NameModalExplanation), nameof(NameModalSkipText))]
    private PackContentNaming? _namingPackContent;

    /// <summary>The modal names a vehicle or a save, so it shows the rule of the game for those names.</summary>
    public bool ShowsGameSaveNameRules => RenamingGameSave is not null || NamingPackContent is not null;

    /// <summary>Why the modal asks for a name of a pinned vehicle or save.</summary>
    public string? NameModalExplanation => NamingPackContent?.Explanation;

    /// <summary>The button that closes the modal and lets the pack go on without the item.</summary>
    public string? NameModalSkipText => NamingPackContent is { IsPackMember: true } naming ? Localization.FormatPackContentSkip(naming.Name) : null;

    /// <summary>Fills the vehicle and save rows of the open pack.</summary>
    private async Task LoadPackContentAsync(PackItem pack)
    {
        if (_services is not { } services)
            return;

        var rows = new List<PackContentItem>();
        foreach (var (type, pin) in pack.Metadata.Vehicles.Select(pin => (ContentType.Vehicle, pin)).Concat(pack.Metadata.Saves.Select(pin => (ContentType.Save, pin))))
            rows.Add(new PackContentItem(this, type, pin, (await services.UserContent.FindAsync(pin.ContentId))?.Name));

        if (!ReferenceEquals(SelectedPack, pack))
            return;

        foreach (var row in rows)
            (row.Type == ContentType.Save ? PackSaves : PackVehicles).Add(row);
        ShowNewerContent();
        await ShowPackContentStateAsync();
    }

    private void ShowNewerContent()
    {
        foreach (var row in PackVehicles.Concat(PackSaves))
            row.NewerRelease = SelectedPack?.NewerMembers.FirstOrDefault(newer => newer.Type == row.Type && ModIds.Equals(newer.ModId, row.ContentId))?.Newer;
    }

    /// <summary>Marks each row that the active instance holds at the pinned version, by the records of what Borea installed.</summary>
    private async Task ShowPackContentStateAsync()
    {
        if (_services is not { } services || PackVehicles.Count + PackSaves.Count == 0)
            return;

        IReadOnlyList<InstalledContentRecord> records = [];
        if (ActiveInstance is { } active)
        {
            try
            {
                records = await services.InstalledContent.ListAsync(active.InstanceId);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // an unreadable record file shows every row as not installed, which is what Borea can say about it
            }
        }

        foreach (var row in PackVehicles.Concat(PackSaves))
            row.IsInstalled = records.Any(record => record.Type == row.Type && ModIds.Equals(record.ListingId, row.ContentId) && record.Version == row.Pin.Version);
    }

    /// <summary>Learns the names of the items a pack plan names, so the questions and results do not show bare ids.</summary>
    private async Task LearnContentNamesAsync(BoreaServices services, IReadOnlyList<ModPackContentResult> content)
    {
        foreach (var item in content.Where(item => !_userContentNames.ContainsKey(item.ContentId)))
        {
            if ((await services.UserContent.FindAsync(item.ContentId))?.Name is { } name)
                _userContentNames[item.ContentId] = name;
        }
    }

    /// <summary>
    /// What the confirmation of a pack says about its pinned items: a name in the way, an item the player changed, the
    /// mods it enables for an item, and each required mod that an item misses.
    /// </summary>
    private List<string> ContentQuestions(IReadOnlyList<ModPackContentResult> content, IReadOnlyList<PlanningMessage> warnings)
    {
        var questions = new List<string>();
        foreach (var item in content)
        {
            if (item.Status == ModPackContentStatus.NameTaken)
                questions.Add(NameTakenText(item));
            else if (item.Status == ModPackContentStatus.Changed && item.Changes is { } changes)
                questions.Add(Localization.FormatPackContentChanged(UserContentName(item.ContentId), string.Join(", ", changes.Added.Concat(changes.Removed).Concat(changes.Changed))));
            if (item.Enables is { Count: > 0 } enables)
                questions.Add(Localization.FormatPackContentEnables(string.Join(", ", enables.Select(ContentName)), UserContentName(item.ContentId)));
        }

        questions.AddRange(warnings.Where(warning => warning.Kind is PlanningMessageKind.MissingForVehicle or PlanningMessageKind.MissingForSave).Select(PlanningText.Message));
        return questions;
    }

    private string NameTakenText(ModPackContentResult item) =>
        Localization.FormatPackContentNameTaken(UserContentName(item.ContentId), item.FolderName ?? item.ContentId, item.Type == ContentType.Save ? Localization.InstanceGroupSaves : Localization.InstanceGroupVehicles);

    private static HashSet<string>? ChangedContent(IReadOnlyList<ModPackContentResult> content)
    {
        var changed = content.Where(item => item.Status == ModPackContentStatus.Changed).Select(item => item.ContentId).ToHashSet(ModIds.Comparer);
        return changed.Count == 0 ? null : changed;
    }

    /// <summary>
    /// Opens the name modal for the first pinned item whose name is taken. Its new name goes into <paramref name="names"/>,
    /// and <paramref name="replan"/> checks again.
    /// </summary>
    /// <returns>What opens the modal again after the player closed it, or null when no name is taken.</returns>
    private Action? AskContentName(IReadOnlyList<ModPackContentResult> content, Dictionary<string, string> names, Func<Task> replan)
    {
        if (content.FirstOrDefault(item => item.Status == ModPackContentStatus.NameTaken) is not { } taken)
            return null;

        void Open()
        {
            InstanceError = null;
            IsCreatingInstance = false;
            RenamingInstance = null;
            RenamingGameSave = null;
            ModalInstanceName = FreeNameSuggestion(names.GetValueOrDefault(taken.ContentId) ?? taken.FolderName ?? taken.ContentId);
            NamingPackContent = new PackContentNaming(taken.ContentId, UserContentName(taken.ContentId), NameTakenText(taken), names, replan);
        }

        Open();
        return Open;
    }

    /// <summary>The taken name with a number after it, or the next number when it has one, so the field starts with a name that may be free.</summary>
    internal static string FreeNameSuggestion(string taken)
    {
        var numbered = Regex.Match(taken, @"^(.*) (\d{1,6})$");
        var suggestion = numbered.Success ? $"{numbered.Groups[1].Value} {int.Parse(numbered.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + 1}" : $"{taken} 2";
        return GameSaveName.IsValid(suggestion) ? suggestion : taken;
    }

    /// <summary>Keeps the modal open while the game would change the name, and otherwise checks the pack again with it.</summary>
    private async Task NamePackContentFromModalAsync(PackContentNaming naming)
    {
        var name = ModalInstanceName.Trim();
        if (!GameSaveName.IsValid(name))
        {
            InstanceError = GameSaveName.Sanitize(name) is { Length: > 0 } sanitized
                ? Localization.FormatGameSaveNameInvalid(sanitized)
                : Localization.GameSaveNameNeedsLetter;
            return;
        }

        naming.Names[naming.ContentId] = name;
        NamingPackContent = null;
        ModalInstanceName = string.Empty;
        await naming.Replan();
    }

    /// <summary>A pinned item as a row of the results. An item the player went on without is skipped, not a failure.</summary>
    private PackResultItem ContentResult(ModPackContentResult item) => new(
        this,
        new ModPackMemberResult(
            item.ContentId,
            item.Version,
            InstallReason.ModPack,
            item.Status switch
            {
                ModPackContentStatus.Installed => ModPackMemberStatus.Installed,
                ModPackContentStatus.Updated => ModPackMemberStatus.Replaced,
                ModPackContentStatus.AlreadyInstalled => ModPackMemberStatus.AlreadyInstalled,
                ModPackContentStatus.Failed => ModPackMemberStatus.Failed,
                ModPackContentStatus.Skipped => ModPackMemberStatus.Unresolved,
                _ => ModPackMemberStatus.NotAttempted,
            },
            ContentMessage(item)),
        isSkipped: IsSkippedByChoice(item));

    /// <summary>The player went on without the item, because its name is taken or the player changed it.</summary>
    private static bool IsSkippedByChoice(ModPackContentResult item) => item.Status is ModPackContentStatus.NameTaken or ModPackContentStatus.Changed;

    private static bool IsInPlace(ModPackContentResult item) => item.Status is ModPackContentStatus.Installed or ModPackContentStatus.Updated or ModPackContentStatus.AlreadyInstalled;

    /// <summary>Why an item is not in place, in the language of the App. The text of a failure goes to the log only.</summary>
    private string? ContentMessage(ModPackContentResult item) => item.Status switch
    {
        ModPackContentStatus.Installed or ModPackContentStatus.Updated or ModPackContentStatus.AlreadyInstalled => null,
        ModPackContentStatus.NameTaken => Localization.FormatPackContentNameTakenShort(item.FolderName ?? item.ContentId),
        ModPackContentStatus.Changed => Localization.PackContentChangedShort,
        ModPackContentStatus.Skipped when item.Warning is { } warning => PlanningText.Message(warning),
        ModPackContentStatus.Failed => Localization.PackContentFailed,
        _ => Localization.PackContentNotReached,
    };

    /// <summary>Names each pinned item that did not install for another reason than the choice of the player, or null when there is none.</summary>
    private string? ContentProblems(IReadOnlyList<ModPackContentResult> content)
    {
        foreach (var item in content.Where(item => item.Status == ModPackContentStatus.Failed))
            _services?.Log.Write($"Pack item {item.ContentId} {item.Version} did not install. {item.Message}");

        var missed = content
            .Where(item => !IsInPlace(item) && !IsSkippedByChoice(item))
            .Select(item => Localization.FormatPackContentNotInstalled(UserContentName(item.ContentId), item.Version.ToString(), ContentMessage(item)))
            .ToList();
        return missed.Count == 0 ? null : string.Join(" ", missed);
    }

    /// <summary>Names each pinned item the player went on without, or null when there is none.</summary>
    private string? SkippedContent(IReadOnlyList<ModPackContentResult> content)
    {
        var skipped = content
            .Where(IsSkippedByChoice)
            .Select(item => Localization.FormatPackContentSkipped(UserContentName(item.ContentId), item.Version.ToString(), ContentMessage(item)!))
            .ToList();
        return skipped.Count == 0 ? null : string.Join(" ", skipped);
    }

    /// <summary>The name of a pinned vehicle or save as the index lists it, else its id.</summary>
    internal string UserContentName(string id) =>
        _userContentNames.GetValueOrDefault(id)
        ?? PackVehicles.Concat(PackSaves).FirstOrDefault(row => ModIds.Equals(row.ContentId, id))?.Name
        ?? id;
}

/// <summary>A vehicle or save whose name is taken, pinned by a pack or installed alone, which the name modal asks a new name for.</summary>
/// <param name="Explanation">Which item is in the way, and that the pack can go on without this one.</param>
/// <param name="Names">Where the new name goes, by listing id.</param>
/// <param name="Replan">Checks the pack again with the new name.</param>
/// <param name="IsPackMember">False for one vehicle or save installed alone, which has nothing to go on without.</param>
public sealed record PackContentNaming(string ContentId, string Name, string Explanation, Dictionary<string, string> Names, Func<Task> Replan, bool IsPackMember = true);

/// <summary>One vehicle or save a pack version pins.</summary>
public sealed partial class PackContentItem : ObservableObject
{
    private readonly MainViewModel _owner;

    internal ContentType Type { get; }

    internal ModPackEntry Pin { get; }

    public string ContentId => Pin.ContentId;

    /// <summary>The name of the listing, or the id when the index does not list it.</summary>
    public string Name { get; }

    public string Version => Pin.Version.ToString();

    /// <summary>Whether the active instance holds this item at the pinned version.</summary>
    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewerText))]
    private ModVersionMetadata? _newerRelease;

    public string? NewerText => NewerRelease is { } newer ? _owner.Localization.FormatPackMemberNewer(newer.Version.ToString()) : null;

    public PackContentItem(MainViewModel owner, ContentType type, ModPackEntry pin, string? name)
    {
        _owner = owner;
        Type = type;
        Pin = pin;
        Name = name ?? pin.ContentId;
    }

    internal void RefreshText() => OnPropertyChanged(nameof(NewerText));
}
