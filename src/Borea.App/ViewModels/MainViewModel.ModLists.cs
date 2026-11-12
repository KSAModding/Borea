using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Launch;
using Borea.Core.Instances;
using Borea.Core.ModPacks;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>
/// The new instance of a duplicate or an import exists only after the user confirms the plan in the modal.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Export, copy and import do nothing until the window sets it.</summary>
    internal IWindowServices? WindowServices { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewingModList))]
    private ModListImportItem? _modListImport;

    public bool IsReviewingModList => ModListImport is not null;

    internal async Task BeginDuplicateAsync(Guid instanceId)
    {
        if (_services is not { } services || ModListImport is not null)
            return;

        try
        {
            var instance = await services.Instances.GetByIdAsync(instanceId);
            if (instance is null)
            {
                await ReloadInstancesAsync();
                return;
            }

            var manifest = await services.ModState.GetEntriesAsync(instanceId);
            var installer = InstallerFor(services);
            var name = await installer.FreeNameAsync(Localization.FormatModListCopyName(instance.Name));
            var reasons = instance.Mods.ToDictionary(mod => mod.ModId, mod => mod.Reason, ModIds.Comparer);
            // the copy runs on the build of the source, so it is checked against that build and never against the settings' one
            var gameVersion = PlanningGameVersion(services, instance);
            var plan = await installer.PlanAsync(RequestFor(services, ModList.FromInstance(instance, manifest), instance.Source, reasons) with { GameVersion = gameVersion, Origin = InstanceOrigin.Duplicate, GameBuild = instance.GameBuild, Loader = instance.Loader });
            await ShowModListPlanAsync(new ModListImportItem(this, plan, name, isDuplicate: true, instance.ForeignMods.Select(mod => mod.FolderName).ToList()));
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            var instanceName = InstanceName(instanceId);
            ShowErrorToast(() => Localization.FormatToastDuplicateFailed(instanceName), exception.Message);
        }
    }

    [RelayCommand]
    private async Task ImportModListAsync()
    {
        if (WindowServices is not { } window || ModListImport is not null)
            return;

        try
        {
            if (await window.OpenTextFileAsync(Localization.LibraryImportModList, Localization.ModListFileType) is { } file)
                await BeginImportAsync(file.Name, file.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowImportError(exception.Message);
        }
    }

    /// <summary>A file Borea cannot read shows an error toast.</summary>
    internal async Task BeginImportAsync(string fileName, string text)
    {
        if (_services is not { } services || ModListImport is not null)
            return;

        ModList modList;
        try
        {
            modList = services.ModListFormat.Read(text);
        }
        catch (UnsupportedModListFormatException exception)
        {
            ShowImportError(Localization.FormatModListNewerFormat(fileName, exception.Format));
            return;
        }
        catch (FormatException exception)
        {
            ShowImportError(Localization.FormatModListUnreadable(fileName, exception.Message));
            return;
        }

        try
        {
            await ShowNewModListAsync(services, modList, modList.Name ?? Path.GetFileNameWithoutExtension(fileName), link: null);
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            ShowImportError(exception.Message);
        }
    }

    /// <summary>
    /// Plans the modlist into a new instance that exists only after the click in the modal. A link with an id also plans the
    /// update of each instance that an earlier link with the same id created, and the modal offers those updates first.
    /// </summary>
    /// <param name="link">The rows of a modlist link and its checked releases, or null for a file import. A link with a problem in a row gets no plan.</param>
    /// <returns>False when another modal opened while the plan was made, which then stays open.</returns>
    private async Task<bool> ShowNewModListAsync(BoreaServices services, ModList modList, string wantedName, ModListLink? link)
    {
        var installer = InstallerFor(services);
        var name = string.IsNullOrWhiteSpace(wantedName) ? string.Empty : await installer.FreeNameAsync(wantedName);
        if (link is not null && link.Rows.Any(row => row.HasProblem))
            return await ShowModListPlanAsync(new ModListImportItem(this, plan: null, name, isDuplicate: false, notCopied: []) { LinkRows = link.Rows });

        var request = RequestFor(services, modList, InstanceSource.Custom.Value, reasons: null) with { Releases = link?.Releases, ListId = link?.ListId };
        var plan = await installer.PlanAsync(request);
        var updates = link?.ListId is { } listId ? await PlanModListUpdatesAsync(services, installer, request, listId) : [];
        var item = new ModListImportItem(this, plan, name, isDuplicate: false, notCopied: []) { LinkRows = link?.Rows ?? [] };
        item.OfferUpdates(updates);
        return await ShowModListPlanAsync(item);
    }

    /// <summary>
    /// The updates of the instances that recorded <paramref name="listId"/>, the one played last first. Only a modlist link
    /// records an id, so an instance the player made, or one from a pack or a file, is never offered, whatever its name.
    /// </summary>
    private async Task<IReadOnlyList<ModListUpdatePlan>> PlanModListUpdatesAsync(BoreaServices services, ModListInstaller installer, ModListRequest request, string listId)
    {
        var candidates = (await services.Instances.GetAllAsync())
            .Where(instance => instance.LinkedModList is { } linked && ModIds.Equals(linked.ListId, listId))
            .OrderByDescending(instance => instance.LastPlayedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(instance => instance.CreatedAt)
            .ToList();
        var updates = new List<ModListUpdatePlan>();
        foreach (var candidate in candidates)
        {
            try
            {
                updates.Add(await installer.PlanUpdateAsync(request with { GameVersion = PlanningGameVersion(services, candidate) }, candidate.InstanceId));
            }
            catch (InvalidOperationException)
            {
                // a kept build that is not available, or an instance deleted meanwhile, leaves out only this update, so the link still opens with a new instance
            }
        }

        return updates;
    }

    private void ShowImportError(string reason) => ShowErrorToast(() => Localization.ToastImportModListFailed, reason);

    internal async Task ConfirmModListAsync(ModListImportItem item)
    {
        if (_services is not { } services || !item.CanConfirm || item.Plan is not { } plan)
            return;

        if (item.SelectedUpdate is { } update)
        {
            await ConfirmModListUpdateAsync(services, item, update);
            return;
        }

        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            item.InstallError = Localization.LibraryFolderBusy;
            return;
        }

        item.InstallError = null;
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.ModListImport, item.Name.Trim(), plan.InstanceId));
        var completed = false;
        var stopped = false;
        try
        {
            var created = await InstallerFor(services).InstallAsync(plan, item.Name.Trim(), ProgressOf(item), run.InstallStop);
            completed = true;
            if (ReferenceEquals(ModListImport, item))
                ModListImport = null;
            if (created.Activated)
                ShowSuccessToast(() => Localization.FormatLibraryNowActive(created.Instance.Name));
        }
        catch (InstallStoppedException)
        {
            // only a closing window stops an import, and the installer removed the new instance again
            stopped = true;
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            item.InstallError = exception.Message;
        }
        finally
        {
            EndInstallRun(run, completed, stopped, item.InstallError);
            EndRowInstall(item, null);
        }

        await ReloadInstancesAsync();
    }

    /// <summary>
    /// Runs the update like the other changes of an instance, one at a time per instance, so the instance page cannot change
    /// it meanwhile. The modal stays open after an error or a stop and names the mods that the update would remove and are still there.
    /// </summary>
    private async Task ConfirmModListUpdateAsync(BoreaServices services, ModListImportItem item, ModListUpdatePlan update)
    {
        var instanceId = update.Instance.InstanceId;
        var instanceName = update.Instance.Name;
        if (await services.Instances.GetByIdAsync(instanceId) is null)
        {
            item.InstallError = Localization.FormatLinkModListInstanceGone(instanceName);
            return;
        }

        if (_runningUpdates.ContainsKey(instanceId))
        {
            item.InstallError = Localization.FormatLinkModListInstanceBusy(instanceName);
            return;
        }

        await RunUpdateAsync(item, instanceId, () => RunModListUpdateAsync(services, item, update));
    }

    private async Task<bool> RunModListUpdateAsync(BoreaServices services, ModListImportItem item, ModListUpdatePlan update)
    {
        using var libraryUse = TryUseLibrary();
        if (libraryUse is null)
        {
            item.InstallError = Localization.LibraryFolderBusy;
            return false;
        }

        item.InstallError = null;
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.PackUpdate, update.Instance.Name, update.Instance.InstanceId));
        run.TaskItem.MarkRunning(update.Plan.Plan);
        var completed = false;
        var stopped = false;
        string? error = null;
        try
        {
            await InstallerFor(services).UpdateAsync(update, ProgressOf(item), run.InstallStop);
            completed = true;
            if (ReferenceEquals(ModListImport, item))
                ModListImport = null;
        }
        catch (InstallStoppedException)
        {
            stopped = true;
            item.InstallError = Sentences(Localization.UpdateStopped, await StillInstalledTextAsync(services, update));
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            error = item.InstallError = Sentences(exception.Message, await StillInstalledTextAsync(services, update));
        }
        finally
        {
            EndInstallRun(run, completed, stopped, error);
            EndRowInstall(item, null);
        }

        return true;
    }

    /// <summary>Names the mods that the update would remove and the instance still has, or null when it has none of them.</summary>
    private async Task<string?> StillInstalledTextAsync(BoreaServices services, ModListUpdatePlan update)
    {
        var instance = await services.Instances.GetByIdAsync(update.Instance.InstanceId);
        var names = update.Changes
            .Where(change => change.Kind == ModPackChangeKind.Remove && instance?.Mods.Any(mod => ModIds.Equals(mod.ModId, change.ModId)) == true)
            .Select(change => ContentName(change.ModId))
            .ToList();
        return names.Count == 0 ? null : Localization.FormatPackUpdateStillInstalled(string.Join(", ", names));
    }

    internal void CancelModList(ModListImportItem item)
    {
        if (!item.IsInstalling && ReferenceEquals(ModListImport, item))
            ModListImport = null;
    }

    internal async Task ExportModListAsync(Guid instanceId)
    {
        if (_services is not { } services || WindowServices is not { } window)
            return;

        try
        {
            if (await ModListOfAsync(services, instanceId) is not { } export)
                return;

            var text = services.ModListFormat.Write(export.ModList);
            var fileName = await window.SaveTextFileAsync(Localization.LibraryExportModList, FileNameFor(export.Instance.Name), Localization.ModListFileType, text);
            if (fileName is not null)
                ShowSuccessToast(() => Localization.FormatModListExported(export.Instance.Name, fileName), ForeignFoldersNote(export.Instance));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var instanceName = InstanceName(instanceId);
            ShowErrorToast(() => Localization.FormatToastExportModListFailed(instanceName), exception.Message);
        }
    }

    internal async Task CopyModListAsync(Guid instanceId)
    {
        if (_services is not { } services || WindowServices is not { } window)
            return;

        try
        {
            if (await ModListOfAsync(services, instanceId) is not { } export)
                return;

            await window.CopyTextAsync(services.ModListFormat.Write(export.ModList));
            ShowSuccessToast(() => Localization.FormatModListCopied(export.Instance.Name), ForeignFoldersNote(export.Instance));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var instanceName = InstanceName(instanceId);
            ShowErrorToast(() => Localization.FormatToastCopyModListFailed(instanceName), exception.Message);
        }
    }

    private string? ForeignFoldersNote(Instance instance)
        => instance.ForeignMods.Count == 0
            ? null
            : Localization.FormatModListNotExported(string.Join(", ", instance.ForeignMods.Select(mod => mod.FolderName)));

    private static async Task<(Instance Instance, ModList ModList)?> ModListOfAsync(BoreaServices services, Guid instanceId)
    {
        var instance = await services.Instances.GetByIdAsync(instanceId);
        return instance is null ? null : (instance, ModList.FromInstance(instance, await services.ModState.GetEntriesAsync(instanceId)));
    }

    private static string FileNameFor(string instanceName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(instanceName.Select(character => invalid.Contains(character) ? '_' : character).ToArray()) + ".toml";
    }

    /// <summary>
    /// The new instance does not exist yet, so the conflict modal has no mod to remove. Planning awaits the network,
    /// so a modal that opened in the meantime stays open and the new one is not shown.
    /// </summary>
    private async Task<bool> ShowModListPlanAsync(ModListImportItem item)
    {
        if (ModListImport is not null)
            return false;

        ModListImport = item;
        RefreshModListText();
        if (item.ShownPlan is { } plan)
            await ShowConflictsAsync(plan.Plan, again: null);
        return true;
    }

    /// <summary>
    /// A yanked release is a note already, so the planner's warning about it is left out. The text follows the choice of the
    /// modal, so the conflicts of the plan that is not chosen show only when the player chooses it.
    /// </summary>
    internal void RefreshModListText()
    {
        if (ModListImport is not { } item)
            return;

        foreach (var row in item.LinkRows)
            row.RefreshText();

        if (item.ShownPlan is not { } modListPlan)
        {
            item.ShowText([], warning: null, planError: null);
            return;
        }

        var plan = modListPlan.Plan;
        var notes = new List<string>();
        if (item.NotCopied.Count > 0)
            notes.Add(Localization.FormatModListNotCopied(item.NotCopied));

        notes.AddRange(modListPlan.Unknown.Select(unknown => Localization.FormatModListUnknown(unknown.Entry.ModId, unknown.Entry.Version.ToString())));
        notes.AddRange(modListPlan.Yanked.Select(yanked => Localization.FormatPackMemberYanked(yanked.Entry.ModId, yanked.Entry.Version.ToString(), yanked.Release!.YankedReason)));
        var warnings = plan.Warnings.Where(warning => warning.Code != "yanked").ToList();
        item.ShowText(
            notes,
            warnings.Count == 0 ? null : Describe(warnings),
            plan.IsReady ? null : Describe(plan.Conflicts.Concat(plan.UnresolvedChoices)));
    }

    private static ModListInstaller InstallerFor(BoreaServices services)
        => new(services.Instances, services.InstallPlanner, services.PlanExecutor, services.ModState, services.Uninstaller);

    private static ModListRequest RequestFor(BoreaServices services, ModList modList, InstanceSource source, IReadOnlyDictionary<string, InstallReason>? reasons)
        => new(modList, source, services.Mods, services.InstalledVersion.GetInstalledVersion()?.Version, services.GamePlatform.Current, reasons);
}

/// <summary>
/// The modal of a duplicate, an import or a modlist link: the plan, and the name the new instance gets. A link with an id
/// can instead update an instance that an earlier link with the same id created.
/// </summary>
public sealed partial class ModListImportItem : ObservableObject, IInstallRow
{
    private readonly MainViewModel _owner;
    private bool _choosing;

    /// <summary>The plan of a new instance, or null for a modlist link that names a mod Borea cannot install.</summary>
    internal ModListPlan? Plan { get; }

    /// <summary>The updates the modal offers, one per instance, and then the new instance. Empty when there is nothing to update.</summary>
    public IReadOnlyList<ModListTarget> Targets { get; private set; } = [];

    public bool HasTargets => Targets.Count > 0;

    /// <summary>The chosen update, or null when the modal creates a new instance.</summary>
    internal ModListUpdatePlan? SelectedUpdate => Targets.FirstOrDefault(target => target.IsSelected)?.Update;

    public bool IsUpdate => SelectedUpdate is not null;

    /// <summary>The plan that the click runs.</summary>
    internal ModListPlan? ShownPlan => SelectedUpdate?.Plan ?? Plan;

    /// <summary>What the chosen update adds, changes, removes and keeps, or empty when the modal creates a new instance.</summary>
    public IReadOnlyList<string> ChangeTexts => Targets.FirstOrDefault(target => target.IsSelected) is { Update: not null } chosen
        ? chosen.ChangeTexts
        : [];

    public string ConfirmText => IsUpdate ? _owner.Localization.ContentUpdate : _owner.Localization.LibraryCreate;

    InstallPlan? IPlanRow.PendingPlan
    {
        get => SelectedUpdate?.Plan.Plan;
        set { }
    }

    InstallChoices? IPlanRow.Choices
    {
        get => null;
        set { }
    }

    internal IReadOnlyList<string> NotCopied { get; }

    public bool IsDuplicate { get; }

    /// <summary>The mods a modlist link names, one row each, or empty for a duplicate or a file import.</summary>
    public IReadOnlyList<ModListLinkRow> LinkRows { get; init; } = [];

    public bool IsLink => LinkRows.Count > 0;

    public string Title => IsDuplicate ? _owner.Localization.ModListDuplicateTitle
        : IsUpdate ? _owner.Localization.LinkModListUpdateTitle
        : IsLink ? _owner.Localization.LinkModListTitle
        : _owner.Localization.LibraryImportModList;

    /// <summary>A link counts its rows, because the dependencies and the loader that its plan adds have lines of their own.</summary>
    public string SummaryText => IsLink
        ? _owner.Localization.FormatLinkModListCount(LinkRows.Count)
        : _owner.Localization.FormatModListInstallCount(Plan?.Plan.Operations.Count ?? 0);

    /// <summary>A link that Borea cannot create leaves out that nothing installs until Create, because Create stays disabled.</summary>
    public string? LinkNote => !IsLink ? null
        : SelectedUpdate is { } update ? _owner.Localization.FormatLinkModListUpdateNote(update.Instance.Name)
        : Plan is null ? _owner.Localization.LinkModListNoteBlocked
        : _owner.Localization.LinkModListNote;

    /// <summary>
    /// The mods that the plan of a new instance from a link installs beside the ones the link names, with their names from the
    /// content index. The changes of an update name them already.
    /// </summary>
    public string? DependenciesText
    {
        get
        {
            var added = (Plan?.Plan.Operations ?? [])
                .Select(operation => operation.Release)
                .Where(release => IsLink && !IsUpdate && !LinkRows.Any(row => ModIds.Equals(row.ContentId, release.ModId)))
                .Select(release => $"{_owner.FindIndexContent(release.ModId).Listing?.Name ?? release.ModId} {release.Version}")
                .ToList();
            return added.Count == 0 ? null : _owner.Localization.FormatLinkModListDependencies(string.Join(", ", added));
        }
    }

    /// <summary>
    /// The loader that the mods of a link need. Like a pack or a single mod, a link installs no loader, because Play and the
    /// instance page offer the version that fits.
    /// </summary>
    public string? LoaderText
    {
        get
        {
            if (!IsLink || ShownPlan is not { } plan)
                return null;

            var lines = LoaderNeed.With(null, plan.Plan.Operations.Select(operation => operation.Release).ToList()).Loaders
                .Select(loader => _owner.Localization.FormatLinkModListLoader(
                    _owner.FindIndexContent(loader.LoaderId).Listing?.Name ?? loader.LoaderId,
                    _owner.LoaderBoundsText(loader.MinVersion, loader.MaxVersion)))
                .ToList();
            return lines.Count == 0 ? null : string.Join(" ", lines);
        }
    }

    /// <summary>Why a link with a problem in a row creates no instance.</summary>
    public string? BlockedText => IsLink && Plan is null ? _owner.Localization.LinkModListBlocked : null;

    public IReadOnlyList<string> Notes { get; private set; } = [];

    public bool HasNotes => Notes.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _progressStatus;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private InstallRun? _run;

    [ObservableProperty]
    private string? _installError;

    /// <summary>The planner's warnings. They do not stop the instance from being created.</summary>
    [ObservableProperty]
    private string? _installWarning;

    /// <summary>The planner's conflicts and open choices. They stop the instance from being created.</summary>
    [ObservableProperty]
    private string? _planError;

    public bool CanConfirm => ShownPlan is { Plan.IsReady: true } && !IsInstalling && (IsUpdate || !string.IsNullOrWhiteSpace(Name));

    public ModListImportItem(MainViewModel owner, ModListPlan? plan, string name, bool isDuplicate, IReadOnlyList<string> notCopied)
    {
        _owner = owner;
        Plan = plan;
        _name = name;
        IsDuplicate = isDuplicate;
        NotCopied = notCopied;
    }

    internal void ShowText(IReadOnlyList<string> notes, string? warning, string? planError)
    {
        Notes = notes;
        InstallWarning = warning;
        PlanError = planError;
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(LinkNote));
        OnPropertyChanged(nameof(DependenciesText));
        OnPropertyChanged(nameof(LoaderText));
        OnPropertyChanged(nameof(BlockedText));
        OnPropertyChanged(nameof(IsUpdate));
        OnPropertyChanged(nameof(ChangeTexts));
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(CanConfirm));
        foreach (var target in Targets)
            target.RefreshText();
    }

    /// <summary>Offers each update with the first one chosen, and then the new instance. No update leaves the modal as it was.</summary>
    internal void OfferUpdates(IReadOnlyList<ModListUpdatePlan> updates)
    {
        if (updates.Count == 0)
            return;

        Targets = [.. updates.Select((update, index) => new ModListTarget(this, update, isSelected: index == 0)), new ModListTarget(this, update: null, isSelected: false)];
        OnPropertyChanged(nameof(Targets));
        OnPropertyChanged(nameof(HasTargets));
    }

    /// <summary>Keeps one choice selected, and shows the plan, the conflicts and the button of the chosen one.</summary>
    internal void OnTargetChanged(ModListTarget target)
    {
        if (_choosing)
            return;

        _choosing = true;
        if (target.IsSelected)
        {
            foreach (var other in Targets.Where(other => !ReferenceEquals(other, target)))
                other.IsSelected = false;
        }
        else if (!Targets.Any(other => other.IsSelected))
        {
            target.IsSelected = true;
        }

        _choosing = false;
        InstallError = null;
        _owner.RefreshModListText();
    }

    internal string TargetText(ModListUpdatePlan? update)
        => update is null ? _owner.Localization.LinkModListChoiceNew : _owner.Localization.FormatLinkModListChoiceUpdate(update.Instance.Name);

    /// <summary>The changes of an update in the words of a pack update, with a mod the player added named as kept for that reason.</summary>
    internal IReadOnlyList<string> ChangeTextsOf(ModListUpdatePlan update)
    {
        var linked = update.Instance.LinkedModList?.Mods;
        var texts = update.Changes
            .Select(change => change.Kind == ModPackChangeKind.Keep && change.To == change.From && linked?.Contains(change.ModId) != true
                ? _owner.Localization.FormatLinkModListKeepOwn(_owner.ContentName(change.ModId), change.From!.Value.ToString())
                : _owner.PackChangeText(change))
            .ToList();
        return texts.Count == 0 ? [_owner.Localization.PackUpdateNoModChanges] : texts;
    }

    [RelayCommand]
    private Task ConfirmAsync() => _owner.ConfirmModListAsync(this);

    [RelayCommand]
    private void Cancel() => _owner.CancelModList(this);
}

/// <summary>One choice of the modal of a link with an id: the update of an instance, or a new instance when <see cref="Update"/> is null.</summary>
public sealed partial class ModListTarget : ObservableObject
{
    private readonly ModListImportItem _item;

    internal ModListTarget(ModListImportItem item, ModListUpdatePlan? update, bool isSelected)
    {
        _item = item;
        Update = update;
        _isSelected = isSelected;
    }

    internal ModListUpdatePlan? Update { get; }

    public string Text => _item.TargetText(Update);

    internal IReadOnlyList<string> ChangeTexts => Update is null ? [] : _item.ChangeTextsOf(Update);

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _item.OnTargetChanged(this);

    internal void RefreshText() => OnPropertyChanged(nameof(Text));
}

/// <summary>
/// The rows of a modlist link, the checked releases of the rows without a problem, which are the only releases its plan takes,
/// and the id of the link, or null when it has none.
/// </summary>
internal sealed record ModListLink(IReadOnlyList<ModListLinkRow> Rows, IReadOnlyList<ModVersionMetadata> Releases, string? ListId = null);

/// <summary>
/// One mod of a modlist link. The name, the authors and the icon come from the content index and never from the link, and a
/// mod that is not in the index shows its content id.
/// </summary>
public sealed class ModListLinkRow : ObservableObject
{
    private readonly MainViewModel _owner;
    private readonly DiscoverItem? _listing;
    private readonly Func<string>? _problem;

    internal ModListLinkRow(MainViewModel owner, DiscoverItem? listing, ModListEntry entry, Func<string>? problem)
    {
        _owner = owner;
        _listing = listing;
        _problem = problem;
        ContentId = entry.ModId;
        Version = entry.Version.ToString();
    }

    /// <summary>The content id from the link, which tells two listings with the same name apart.</summary>
    public string ContentId { get; }

    public string Version { get; }

    public string Name => _listing?.Name ?? ContentId;

    public ListingImage? Icon => _listing?.Icon;

    public string? AuthorsText => _listing?.Listing.Authors is { Count: > 0 } authors
        ? _owner.Localization.FormatContentByAuthor(string.Join(", ", authors))
        : null;

    /// <summary>Why the link cannot install this mod, or null when it can.</summary>
    public string? Problem => _problem?.Invoke();

    public bool HasProblem => _problem is not null;

    internal void RefreshText() => OnPropertyChanged(string.Empty);
}
