namespace Borea.Core.Mods;

/// <summary>
/// Watches the mod releases that instances share through links, and turns the
/// sharing on and off. A release that changed after it was stored is broken
/// out: every instance that links to it gets its own copy of it as it is now,
/// and its mod is copied into each instance from then on.
/// </summary>
public interface ISharedModStore
{
    /// <summary>
    /// Breaks out every stored release the instance links to that changed.
    /// Does nothing while the game runs.
    /// </summary>
    /// <returns>The broken out mods that were not private before, so the player hears of each mod once.</returns>
    Task<IReadOnlyList<InstalledMod>> CheckAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>Completes once the game of the instance, and any other game process, is closed.</summary>
    Task WaitForGameExitAsync(Guid instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the setting. Turning it off also gives every instance its own copy
    /// of each linked mod, so nothing is left pointing at the store.
    /// </summary>
    /// <returns>Turning it off is refused while the game or another Borea runs, and then nothing changes.</returns>
    Task<SharedModStoreChange> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
