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
    LibraryFolderChange,
    BackupRestore,
    BackupDelete,
}
