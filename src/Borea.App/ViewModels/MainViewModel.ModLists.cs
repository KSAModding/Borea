using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.History;
using Borea.Core.Launch;
using Borea.Core.Instances;
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

    /// <summary>Plans the modlist into a new instance that exists only after the click in the modal.</summary>
    /// <param name="link">The rows of a modlist link and its checked releases, or null for a file import. A link with a problem in a row gets no plan.</param>
    /// <returns>False when another modal opened while the plan was made, which then stays open.</returns>
    private async Task<bool> ShowNewModListAsync(BoreaServices services, ModList modList, string wantedName, ModListLink? link)
    {
        var installer = InstallerFor(services);
        var name = string.IsNullOrWhiteSpace(wantedName) ? string.Empty : await installer.FreeNameAsync(wantedName);
        if (link is not null && link.Rows.Any(row => row.HasProblem))
            return await ShowModListPlanAsync(new ModListImportItem(this, plan: null, name, isDuplicate: false, notCopied: []) { LinkRows = link.Rows });

        var plan = await installer.PlanAsync(RequestFor(services, modList, InstanceSource.Custom.Value, reasons: null) with { Releases = link?.Releases });
        return await ShowModListPlanAsync(new ModListImportItem(this, plan, name, isDuplicate: false, notCopied: []) { LinkRows = link?.Rows ?? [] });
    }

    private void ShowImportError(string reason) => ShowErrorToast(() => Localization.ToastImportModListFailed, reason);

    internal async Task ConfirmModListAsync(ModListImportItem item)
    {
        if (_services is not { } services || !item.CanConfirm || item.Plan is not { } plan)
            return;

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
        if (item.Plan is { } plan)
            await ShowConflictsAsync(plan.Plan, again: null);
        return true;
    }

    /// <summary>A yanked release is a note already, so the planner's warning about it is left out.</summary>
    private void RefreshModListText()
    {
        if (ModListImport is not { } item)
            return;

        foreach (var row in item.LinkRows)
            row.RefreshText();

        if (item.Plan is not { } modListPlan)
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
        => new(services.Instances, services.InstallPlanner, services.PlanExecutor, services.ModState);

    private static ModListRequest RequestFor(BoreaServices services, ModList modList, InstanceSource source, IReadOnlyDictionary<string, InstallReason>? reasons)
        => new(modList, source, services.Mods, services.InstalledVersion.GetInstalledVersion()?.Version, services.GamePlatform.Current, reasons);
}

/// <summary>
/// The modal of a duplicate, an import or a modlist link: the plan, and the name the new instance gets.
/// </summary>
public sealed partial class ModListImportItem : ObservableObject, IInstallProgressRow
{
    private readonly MainViewModel _owner;

    /// <summary>The plan, or null for a modlist link that names a mod Borea cannot install.</summary>
    internal ModListPlan? Plan { get; }

    internal IReadOnlyList<string> NotCopied { get; }

    public bool IsDuplicate { get; }

    /// <summary>The mods a modlist link names, one row each, or empty for a duplicate or a file import.</summary>
    public IReadOnlyList<ModListLinkRow> LinkRows { get; init; } = [];

    public bool IsLink => LinkRows.Count > 0;

    public string Title => IsDuplicate ? _owner.Localization.ModListDuplicateTitle
        : IsLink ? _owner.Localization.LinkModListTitle
        : _owner.Localization.LibraryImportModList;

    /// <summary>A link counts its rows, because the dependencies and the loader that its plan adds have lines of their own.</summary>
    public string SummaryText => IsLink
        ? _owner.Localization.FormatLinkModListCount(LinkRows.Count)
        : _owner.Localization.FormatModListInstallCount(Plan?.Plan.Operations.Count ?? 0);

    /// <summary>A link that Borea cannot create leaves out that nothing installs until Create, because Create stays disabled.</summary>
    public string? LinkNote => !IsLink ? null
        : Plan is null ? _owner.Localization.LinkModListNoteBlocked
        : _owner.Localization.LinkModListNote;

    /// <summary>The mods that the plan of a link installs beside the ones the link names, with their names from the content index.</summary>
    public string? DependenciesText
    {
        get
        {
            var added = (Plan?.Plan.Operations ?? [])
                .Select(operation => operation.Release)
                .Where(release => IsLink && !LinkRows.Any(row => ModIds.Equals(row.ContentId, release.ModId)))
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
            if (!IsLink || Plan is not { } plan)
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

    public bool CanConfirm => Plan is { Plan.IsReady: true } && !IsInstalling && !string.IsNullOrWhiteSpace(Name);

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
    }

    [RelayCommand]
    private Task ConfirmAsync() => _owner.ConfirmModListAsync(this);

    [RelayCommand]
    private void Cancel() => _owner.CancelModList(this);
}

/// <summary>The rows of a modlist link, and the checked releases of the rows without a problem, which are the only releases its plan takes.</summary>
internal sealed record ModListLink(IReadOnlyList<ModListLinkRow> Rows, IReadOnlyList<ModVersionMetadata> Releases);

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
