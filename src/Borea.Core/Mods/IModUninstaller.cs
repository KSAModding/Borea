namespace Borea.Core.Mods;

public interface IModUninstaller
{
    Task UninstallAsync(
        string modId,
        ModVersion version,
        string installDirectory,
        CancellationToken cancellationToken = default);
}