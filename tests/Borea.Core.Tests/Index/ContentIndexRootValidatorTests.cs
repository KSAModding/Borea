using System.Text;
using Borea.Core.Index;

namespace Borea.Core.Tests.Index;

public sealed class ContentIndexRootValidatorTests
{
    private const string ValidIndexJson =
        """{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {} }""";

    private static HttpResponseMessage JsonResponse(string json, string mediaType = "application/json")
        => new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType),
        };

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    #region byte[] + HttpResponseMessage overload

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public void ValidateIndexRoot_BytesOverload_AcceptedContentType_ReturnsTrue(string mediaType)
    {
        var result = ContentIndexRootValidator.ValidateIndexRoot(Bytes(ValidIndexJson), JsonResponse(ValidIndexJson, mediaType));

        Assert.True(result);
    }

    [Fact]
    public void ValidateIndexRoot_BytesOverload_RejectedContentType_ThrowsHttpRequestException()
    {
        var response = JsonResponse(ValidIndexJson, "text/html");

        Assert.Throws<HttpRequestException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(Bytes(ValidIndexJson), response));
    }

    [Fact]
    public void ValidateIndexRoot_BytesOverload_MalformedJson_ThrowsHttpRequestException()
    {
        var badJson = "{ not valid json";

        Assert.Throws<HttpRequestException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(Bytes(badJson), JsonResponse(badJson)));
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [] }""", "game_versions")] // missing game_versions
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": [] }""", "sources")]
    public void ValidateIndexRoot_BytesOverload_InvalidShape_WrapsInHttpRequestException(string badShape, string named)
    {
        // A shape failure raises InvalidOperationException internally; the
        // bytes overload must convert it to HttpRequestException, not let it escape.
        var exception = Assert.Throws<HttpRequestException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(Bytes(badShape), JsonResponse(badShape)));

        Assert.Contains(named, exception.Message);
    }

    [Fact]
    public void ValidateIndexRoot_BytesOverload_InvalidShape_KeepsTheReasonAsMessageAndTheCauseAsInner()
    {
        var badShape = """{ "snapshot_version": 1, "listings": [], "packs": [] }""";

        var exception = Assert.Throws<HttpRequestException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(Bytes(badShape), JsonResponse(badShape)));

        var cause = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(cause.Message, exception.Message);
    }

    #endregion

    #region string + path overload

    [Fact]
    public void ValidateIndexRoot_StringOverload_MalformedJson_ThrowsInvalidOperationException_NamingThePath()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot("{ not valid json", "C:/some/index.json"));

        Assert.Contains("C:/some/index.json", exception.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    public void ValidateIndexRoot_StringOverload_RootIsNotAnObject_ThrowsInvalidOperationException(string json)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    [Theory]
    [InlineData("""{ "listings": [], "packs": [], "game_versions": {} }""")] // missing snapshot_version
    [InlineData("""{ "snapshot_version": "one", "listings": [], "packs": [], "game_versions": {} }""")] // non-numeric
    [InlineData("""{ "snapshot_version": 1.5, "listings": [], "packs": [], "game_versions": {} }""")] // non-integer
    [InlineData("""{ "snapshot_version": 0, "listings": [], "packs": [], "game_versions": {} }""")] // zero
    [InlineData("""{ "snapshot_version": -1, "listings": [], "packs": [], "game_versions": {} }""")] // negative
    [InlineData("""{ "snapshot_version": 2, "listings": [], "packs": [], "game_versions": {} }""")] // above highest
    public void ValidateIndexRoot_StringOverload_BadSnapshotVersion_ThrowsInvalidOperationException(string json)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "packs": [], "game_versions": {} }""")] // missing listings
    [InlineData("""{ "snapshot_version": 1, "listings": {}, "packs": [], "game_versions": {} }""")] // listings not an array
    [InlineData("""{ "snapshot_version": 1, "listings": [], "game_versions": {} }""")] // missing packs
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": {}, "game_versions": {} }""")] // packs not an array
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [] }""")] // missing game_versions
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": [] }""")] // game_versions not an object
    public void ValidateIndexRoot_StringOverload_BadShape_ThrowsInvalidOperationException(string json)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    [Fact]
    public void ValidateIndexRoot_StringOverload_SourcesFieldAbsent_StillValidates()
    {
        // Regression guard: 'sources' is documented as optional and must
        // never be required.
        var result = ContentIndexRootValidator.ValidateIndexRoot(ValidIndexJson, "index.json");

        Assert.True(result);
        Assert.DoesNotContain("sources", ValidIndexJson);
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": {} }""")]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": { "spacedock": {} } }""")]
    public void ValidateIndexRoot_StringOverload_SourcesIsAnObject_IsAccepted(string json)
    {
        Assert.True(ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    [Theory]
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": [] }""")] // array
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": "spacedock" }""")] // string
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": 1 }""")] // number
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": true }""")] // boolean
    [InlineData("""{ "snapshot_version": 1, "listings": [], "packs": [], "game_versions": {}, "sources": null }""")] // null
    public void ValidateIndexRoot_StringOverload_SourcesWrongShape_ThrowsInvalidOperationException(string json)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));

        Assert.Contains("sources", exception.Message);
    }

    [Fact]
    public void ValidateIndexRoot_StringOverload_SnapshotVersionAtHighest_IsAccepted()
    {
        var json = $$"""{ "snapshot_version": {{SnapshotVersions.Highest}}, "listings": [], "packs": [], "game_versions": {} }""";

        Assert.True(ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    [Fact]
    public void ValidateIndexRoot_StringOverload_ListingsAndPacksWithEntries_IsAccepted()
    {
        var json = """{ "snapshot_version": 1, "listings": [{"id":"a"}], "packs": [{"id":"b"}], "game_versions": {} }""";

        Assert.True(ContentIndexRootValidator.ValidateIndexRoot(json, "index.json"));
    }

    #endregion
}
