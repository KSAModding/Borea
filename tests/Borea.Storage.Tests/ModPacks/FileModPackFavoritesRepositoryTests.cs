using Borea.Storage.ModPacks;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.ModPacks;

public sealed class FileModPackFavoritesRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly TestGamePathProvider _pathProvider;
    private readonly FileModPackFavoritesRepository _repository;

    public FileModPackFavoritesRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _repository = new FileModPackFavoritesRepository(_pathProvider);
    }

    [Fact]
    public async Task NoFavoritesFile_ReturnsEmpty()
    {
        Assert.Empty(await _repository.GetFavoriteModPackIdsAsync());
        Assert.False(await _repository.IsFavoriteAsync("anything"));
    }

    [Fact]
    public async Task AddFavoriteAsync_PersistsAcrossFreshRepository()
    {
        await _repository.AddFavoriteAsync("pack-a");

        var fresh = new FileModPackFavoritesRepository(_pathProvider);
        Assert.True(await fresh.IsFavoriteAsync("pack-a"));
    }

    [Fact]
    public async Task AddFavoriteAsync_Twice_DoesNotDuplicate()
    {
        await _repository.AddFavoriteAsync("pack-a");
        await _repository.AddFavoriteAsync("pack-a");

        Assert.Single(await _repository.GetFavoriteModPackIdsAsync());
    }

    [Fact]
    public async Task RemoveFavoriteAsync_OnUnfavorited_IsNoOp()
    {
        await _repository.RemoveFavoriteAsync("never-favorited");

        Assert.False(File.Exists(_pathProvider.GetModPackFavoritesPath()));
    }

    [Fact]
    public async Task ModAndModPackFavorites_DoNotCollide()
    {
        // Regression guard: confirms the two repositories don't accidentally
        // share a file path, which their near-identical implementations
        // made a real risk.
        Assert.NotEqual(_pathProvider.GetModFavoritesPath(), _pathProvider.GetModPackFavoritesPath());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}