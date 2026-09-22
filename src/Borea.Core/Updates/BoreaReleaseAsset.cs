namespace Borea.Core.Updates;

/// <summary>One file published with a Borea release, such as a platform archive or the checksums.</summary>
public sealed record BoreaReleaseAsset(string Name, string Url, long SizeBytes = 0);
