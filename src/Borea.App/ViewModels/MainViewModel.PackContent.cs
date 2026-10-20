using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.App.Localization;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>
/// The pinned vehicles and saves of a pack (RFC 0085): their rows on the pack page, and the questions a pack install
/// or update asks before it runs, because it runs as one task. A taken name opens the name modal, and a changed item is
/// named in the confirmation, whose go replaces it.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The vehicles the open pack pins.</summary>
    public ObservableCollection<PackContentItem> PackVehicles { get; } = [];

    /// <summary>The saves the open pack pins.</summary>
    public ObservableCollection<PackContentItem> PackSaves { get; } = [];

    /// <summary>The pinned vehicle or save the name modal asks a new name for. Null while the modal is for something else or closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameModalOpen), nameof(NameModalTitle), nameof(NameModalConfirmText), nameof(NameModalPlaceholder), nameof(ShowsGameSaveNameRules))]
    private PackContentNaming? _namingPackContent;

    /// <summary>The modal names a vehicle or a save, so it shows the rule of the game for those names.</summary>
    public bool ShowsGameSaveNameRules => RenamingGameSave is not null || NamingPackContent is not null;

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

    /// <summary>What the confirmation of a pack says about a pinned item whose name is taken or that the player changed.</summary>
    private List<string> ContentQuestions(IReadOnlyList<ModPackContentResult> content)
    {
        var questions = new List<string>();
        foreach (var item in content)
        {
            if (item.Status == ModPackContentStatus.NameTaken)
                questions.Add(Localization.FormatPackContentNameTaken(UserContentName(item.ContentId), item.FolderName ?? item.ContentId));
            else if (item.Status == ModPackContentStatus.Changed && item.Changes is { } changes)
                questions.Add(Localization.FormatPackContentChanged(UserContentName(item.ContentId), string.Join(", ", changes.Added.Concat(changes.Removed).Concat(changes.Changed))));
        }

        return questions;
    }

    private static HashSet<string>? ChangedContent(IReadOnlyList<ModPackContentResult> content)
    {
        var changed = content.Where(item => item.Status == ModPackContentStatus.Changed).Select(item => item.ContentId).ToHashSet(ModIds.Comparer);
        return changed.Count == 0 ? null : changed;
    }

    /// <summary>Opens the name modal for the first pinned item whose name is taken. Its new name goes into <paramref name="names"/>, and <paramref name="replan"/> checks again.</summary>
    private void AskContentName(IReadOnlyList<ModPackContentResult> content, Dictionary<string, string> names, Func<Task> replan)
    {
        if (content.FirstOrDefault(item => item.Status == ModPackContentStatus.NameTaken) is not { } taken)
            return;

        InstanceError = null;
        IsCreatingInstance = false;
        RenamingInstance = null;
        RenamingGameSave = null;
        ModalInstanceName = names.GetValueOrDefault(taken.ContentId) ?? taken.FolderName ?? string.Empty;
        NamingPackContent = new PackContentNaming(taken.ContentId, UserContentName(taken.ContentId), names, replan);
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

    /// <summary>A pinned item as a member result, so the result list of a pack shows it next to the mods.</summary>
    private static ModPackMemberResult AsMember(ModPackContentResult item) => new(
        item.ContentId,
        item.Version,
        InstallReason.ModPack,
        item.Status switch
        {
            ModPackContentStatus.Installed => ModPackMemberStatus.Installed,
            ModPackContentStatus.Updated => ModPackMemberStatus.Replaced,
            ModPackContentStatus.AlreadyInstalled => ModPackMemberStatus.AlreadyInstalled,
            ModPackContentStatus.Failed => ModPackMemberStatus.Failed,
            ModPackContentStatus.NotAttempted => ModPackMemberStatus.NotAttempted,
            _ => ModPackMemberStatus.Unresolved,
        },
        item.Message);

    /// <summary>Names each pinned item that did not install and why, or null when every one did.</summary>
    private string? NotInstalledContent(IReadOnlyList<ModPackContentResult> content)
    {
        var missed = content
            .Where(item => item.Status is not (ModPackContentStatus.Installed or ModPackContentStatus.Updated or ModPackContentStatus.AlreadyInstalled))
            .Select(item => Localization.FormatPackContentNotInstalled(UserContentName(item.ContentId), item.Version.ToString(), item.Warning is { } warning ? PlanningText.Message(warning) : item.Message))
            .ToList();
        return missed.Count == 0 ? null : string.Join(" ", missed);
    }

    /// <summary>The name of a pinned vehicle or save as the open pack page shows it, else its id.</summary>
    private string UserContentName(string id) =>
        PackVehicles.Concat(PackSaves).FirstOrDefault(row => ModIds.Equals(row.ContentId, id))?.Name ?? id;
}

/// <summary>A pinned vehicle or save whose name is taken, which the name modal asks a new name for.</summary>
/// <param name="Names">Where the new name goes, by listing id.</param>
/// <param name="Replan">Checks the pack again with the new name.</param>
public sealed record PackContentNaming(string ContentId, string Name, Dictionary<string, string> Names, Func<Task> Replan);

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
