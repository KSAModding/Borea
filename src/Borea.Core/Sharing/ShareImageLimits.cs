namespace Borea.Core.Sharing;

/// <summary>What a share host that takes pictures takes for one listing. The optional picture request of a host names these, and hosts.toml does not.</summary>
public static class ShareImageLimits
{
    public const int MaxCount = 6;

    public const long MaxBytes = 10 * 1024 * 1024;
}
