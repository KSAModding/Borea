namespace Borea.Core.History;

public enum TaskKind
{
    IndexRefresh,
    ModInstall,
    PackInstall,
    Update,
    UpdateAll,
    LoaderInstall,
    ModRemoval,
    ModListImport,
    ManualReplace,
    ModHandover,
    LibraryFolderChange,
    BackupRestore,
    BackupDelete,
    PackUpdate,

    /// <summary>Borea replaces itself with a newer release.</summary>
    BoreaUpdate,
}
