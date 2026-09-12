using Borea.Storage.Index;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Index;

public sealed class IndexValidatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _pathProvider;
    private readonly IndexValidator _validator;

    private const string ValidIndexJson = """
        {
            "snapshot_version": 1,
            "listings": [],
            "packs": [],
            "game_versions": { "spec_version": 1, "source": "master-server", "versions": ["2026.9.7.5402"] }
        }
        """;

    public IndexValidatorTests()
    {
        _pathProvider = new TestGamePathProvider(_tempRoot);
        _validator = new IndexValidator(_pathProvider);
    }

    private string IndexPath => _pathProvider.GetIndexPath();

    private void WriteIndexFile(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        File.WriteAllText(IndexPath, content);
    }

    [Fact]
    public void Constructor_NullPathProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexValidator(null!));
    }

    [Fact]
    public void ValidateIndex_MissingFile_ThrowsNamingThePath()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => _validator.ValidateIndex());

        Assert.Contains(IndexPath, exception.Message);
    }

    [Fact]
    public void ValidateIndex_EmptyFile_ThrowsSayingItIsEmpty()
    {
        WriteIndexFile(string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(() => _validator.ValidateIndex());

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateIndex_MalformedJson_ThrowsNamingThePath()
    {
        WriteIndexFile("{ not valid json");

        var exception = Assert.Throws<InvalidOperationException>(() => _validator.ValidateIndex());

        Assert.Contains(IndexPath, exception.Message);
    }

    [Fact]
    public void ValidateIndex_EnvelopeInvalid_ThrowsBeforeParsingEverRuns()
    {
        // 'packs' is entirely missing: the root validator rejects this
        // before SnapshotParser gets a chance to run, so the message is the
        // root validator's friendlier one, not a generic deserialization
        // failure naming every missing required property at once.
        WriteIndexFile("""
            { "snapshot_version": 1, "listings": [], "game_versions": { "spec_version": 1, "source": "s", "versions": [] } }
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => _validator.ValidateIndex());

        Assert.Contains("packs", exception.Message);
    }

    [Fact]
    public void ValidateIndex_ValidEnvelope_ReturnsAParsedResult()
    {
        WriteIndexFile(ValidIndexJson);

        var result = _validator.ValidateIndex();

        Assert.Empty(result.ValidListings);
        Assert.Empty(result.ValidPacks);
        Assert.Equal("master-server", result.GameVersions.Source);
    }

    [Fact]
    public void ValidateIndex_EnvelopeValidButOneListingIsBroken_RunsTheFullPipeline()
    {
        // Proves ValidateIndex actually calls SnapshotParser and not just
        // the root validator: the envelope is structurally fine (listings
        // is an array), but the one entry inside it cannot be a listing.
        WriteIndexFile("""
            {
                "snapshot_version": 1,
                "listings": [ 42 ],
                "packs": [],
                "game_versions": { "spec_version": 1, "source": "master-server", "versions": [] }
            }
            """);

        var result = _validator.ValidateIndex();

        var rejected = Assert.Single(result.MalformedListings);
        Assert.Null(rejected.Id);
    }

    [Fact]
    public void WriteIndex_ThenReadIndex_RoundTrips()
    {
        Directory.CreateDirectory(_tempRoot);

        _validator.WriteIndex(IndexPath, ValidIndexJson);

        Assert.Equal(ValidIndexJson, _validator.ReadIndex(IndexPath));
    }

    [Fact]
    public void WriteIndex_EmptyPath_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _validator.WriteIndex("", "content"));
    }

    [Fact]
    public void ReadIndex_EmptyPath_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _validator.ReadIndex(""));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
