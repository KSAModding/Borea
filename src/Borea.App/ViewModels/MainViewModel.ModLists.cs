using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.History;
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

    /// <summary>What the last export or copy of a modlist did.</summary>
    [ObservableProperty]
    private string? _instanceNotice;

    internal async Task BeginDuplicateAsync(Guid instanceId)
    {
        if (_services is not { } services || ModListImport is not null)
            return;

        ClearInstanceMessages();
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
            var plan = await installer.PlanAsync(RequestFor(services, ModList.FromInstance(instance, manifest), instance.Source, reasons));
            ShowModListPlan(new ModListImportItem(this, plan, name, isDuplicate: true, instance.ForeignMods.Select(mod => mod.FolderName).ToList()));
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            InstanceError = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ImportModListAsync()
    {
        if (WindowServices is not { } window || ModListImport is not null)
            return;

        ClearInstanceMessages();
        try
        {
            if (await window.OpenTextFileAsync(Localization.LibraryImportModList, Localization.ModListFileType) is { } file)
                await BeginImportAsync(file.Name, file.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            InstanceError = exception.Message;
        }
    }

    /// <summary>A file Borea cannot read sets <see cref="InstanceError"/>.</summary>
    internal async Task BeginImportAsync(string fileName, string text)
    {
        if (_services is not { } services || ModListImport is not null)
            return;

        ClearInstanceMessages();
        ModList modList;
        try
        {
            modList = services.ModListFormat.Read(text);
        }
        catch (UnsupportedModListFormatException exception)
        {
            InstanceError = Localization.FormatModListNewerFormat(fileName, exception.Format);
            return;
        }
        catch (FormatException exception)
        {
            InstanceError = Localization.FormatModListUnreadable(fileName, exception.Message);
            return;
        }

        try
        {
            var installer = InstallerFor(services);
            var wanted = modList.Name ?? Path.GetFileNameWithoutExtension(fileName);
            var name = string.IsNullOrWhiteSpace(wanted) ? string.Empty : await installer.FreeNameAsync(wanted);
            var plan = await installer.PlanAsync(RequestFor(services, modList, InstanceSource.Custom.Value, reasons: null));
            ShowModListPlan(new ModListImportItem(this, plan, name, isDuplicate: false, notCopied: []));
        }
        catch (Exception exception) when (IsInstallFailure(exception))
        {
            InstanceError = exception.Message;
        }
    }

    internal async Task ConfirmModListAsync(ModListImportItem item)
    {
        if (_services is not { } services || !item.CanConfirm)
            return;

        item.InstallError = null;
        item.IsInstalling = true;
        var run = item.Run = StartInstallRun(StartTask(TaskKind.ModListImport, item.Name.Trim()));
        var completed = false;
        var stopped = false;
        try
        {
            await InstallerFor(services).InstallAsync(item.Plan, item.Name.Trim(), ProgressOf(item), run.InstallStop);
            completed = true;
            if (ReferenceEquals(ModListImport, item))
                ModListImport = null;
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
            item.IsInstalling = false;
            item.Run = null;
            item.Progress = 0;
            item.ProgressStatus = null;
            item.ProgressDetail = null;
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

        ClearInstanceMessages();
        try
        {
            if (await ModListOfAsync(services, instanceId) is not { } export)
                return;

            var text = services.ModListFormat.Write(export.ModList);
            var fileName = await window.SaveTextFileAsync(Localization.LibraryExportModList, FileNameFor(export.Instance.Name), Localization.ModListFileType, text);
            if (fileName is not null)
                InstanceNotice = WithForeignFolders(Localization.FormatModListExported(export.Instance.Name, fileName), export.Instance);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            InstanceError = exception.Message;
        }
    }

    internal async Task CopyModListAsync(Guid instanceId)
    {
        if (_services is not { } services || WindowServices is not { } window)
            return;

        ClearInstanceMessages();
        try
        {
            if (await ModListOfAsync(services, instanceId) is not { } export)
                return;

            await window.CopyTextAsync(services.ModListFormat.Write(export.ModList));
            InstanceNotice = WithForeignFolders(Localization.FormatModListCopied(export.Instance.Name), export.Instance);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            InstanceError = exception.Message;
        }
    }

    private string WithForeignFolders(string notice, Instance instance)
        => instance.ForeignMods.Count == 0
            ? notice
            : $"{notice} {Localization.FormatModListNotExported(string.Join(", ", instance.ForeignMods.Select(mod => mod.FolderName)))}";

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

    private void ShowModListPlan(ModListImportItem item)
    {
        ModListImport = item;
        RefreshModListText();
    }

    /// <summary>A yanked release is a note already, so the planner's warning about it is left out.</summary>
    private void RefreshModListText()
    {
        if (ModListImport is not { } item)
            return;

        var plan = item.Plan.Plan;
        var notes = item.NotCopied.Select(Localization.FormatModListNotCopied)
            .Concat(item.Plan.Unknown.Select(unknown => Localization.FormatModListUnknown(unknown.Entry.ModId, unknown.Entry.Version.ToString())))
            .Concat(item.Plan.Yanked.Select(yanked => Localization.FormatPackMemberYanked(yanked.Entry.ModId, yanked.Entry.Version.ToString(), yanked.Release!.YankedReason)))
            .ToList();
        var warnings = plan.Warnings.Where(warning => warning.Code != "yanked").ToList();
        item.ShowText(
            notes,
            warnings.Count == 0 ? null : Describe(warnings),
            plan.IsReady ? null : Describe(plan.Conflicts.Concat(plan.UnresolvedChoices)));
    }

    private void ClearInstanceMessages()
    {
        InstanceError = null;
        InstanceNotice = null;
    }

    private static ModListInstaller InstallerFor(BoreaServices services)
        => new(services.Instances, services.InstallPlanner, services.PlanExecutor, services.ModState);

    private static ModListRequest RequestFor(BoreaServices services, ModList modList, InstanceSource source, IReadOnlyDictionary<string, InstallReason>? reasons)
        => new(modList, source, services.Mods, services.InstalledVersion.GetInstalledVersion()?.Version, CurrentPlatform(), reasons);
}

/// <summary>
/// The modal of a duplicate or an import: the plan, and the name the new instance gets.
/// </summary>
public sealed partial class ModListImportItem : ObservableObject, IInstallProgressRow
{
    private readonly MainViewModel _owner;

    internal ModListPlan Plan { get; }

    internal IReadOnlyList<string> NotCopied { get; }

    public bool IsDuplicate { get; }

    public string Title => IsDuplicate ? _owner.Localization.ModListDuplicateTitle : _owner.Localization.LibraryImportModList;

    public string SummaryText => _owner.Localization.FormatModListInstallCount(Plan.Plan.Operations.Count);

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

    public bool CanConfirm => Plan.Plan.IsReady && !IsInstalling && !string.IsNullOrWhiteSpace(Name);

    public ModListImportItem(MainViewModel owner, ModListPlan plan, string name, bool isDuplicate, IReadOnlyList<string> notCopied)
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
    }

    [RelayCommand]
    private Task ConfirmAsync() => _owner.ConfirmModListAsync(this);

    [RelayCommand]
    private void Cancel() => _owner.CancelModList(this);
}
