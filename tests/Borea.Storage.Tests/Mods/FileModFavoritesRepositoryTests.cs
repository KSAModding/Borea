using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileModFavoritesRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileModFavoritesRepository _repository;

    public FileModFavoritesRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileModFavoritesRepository(_pathProvider);
    }

    [Fact]
    public async Task NoFavoritesFile_ReturnsEmpty()
    {
        Assert.Empty(await _repository.GetFavoriteModIdsAsync());
        Assert.False(await _repository.IsFavoriteAsync("anything"));
    }

    [Fact]
    public async Task AddFavoriteAsync_PersistsAcrossFreshRepository()
    {
        await _repository.AddFavoriteAsync("mod-a");

        var fresh = new FileModFavoritesRepository(_pathProvider);
        Assert.True(await fresh.IsFavoriteAsync("mod-a"));
    }

    [Fact]
    public async Task AddFavoriteAsync_Twice_DoesNotDuplicate()
    {
        await _repository.AddFavoriteAsync("mod-a");
        await _repository.AddFavoriteAsync("mod-a");

        var all = await _repository.GetFavoriteModIdsAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task RemoveFavoriteAsync_RemovesOnlyTheGivenMod()
    {
        await _repository.AddFavoriteAsync("mod-a");
        await _repository.AddFavoriteAsync("mod-b");

        await _repository.RemoveFavoriteAsync("mod-a");

        Assert.False(await _repository.IsFavoriteAsync("mod-a"));
        Assert.True(await _repository.IsFavoriteAsync("mod-b"));
    }

    [Fact]
    public async Task RemoveFavoriteAsync_OnUnfavorited_IsNoOp()
    {
        // Should not throw, and should not create a file for a no-op.
        await _repository.RemoveFavoriteAsync("never-favorited");

        Assert.False(File.Exists(_pathProvider.GetModFavoritesPath()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}