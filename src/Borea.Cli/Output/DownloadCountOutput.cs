using System.Globalization;
using Borea.Core.Index;

namespace Borea.Cli.Output;

/// <summary>Shapes download counts from the index for text and JSON output.</summary>
internal static class DownloadCountOutput
{
    public static DownloadCountView? From(ListingDownloadCounts? counts) => counts is null
        ? null
        : new DownloadCountView(counts.Total, counts.Hosts);

    public static DownloadCountView? From(ReleaseDownloadCounts? counts) => counts is null
        ? null
        : new DownloadCountView(counts.Total, counts.Hosts);

    public static string Total(DownloadCountView counts) => counts.Total.ToString(CultureInfo.InvariantCulture);

    /// <summary>The total and each host value, for example "1200 (github 479, spacedock 721)".</summary>
    public static string Describe(DownloadCountView counts)
    {
        var hosts = counts.Hosts.Select(host => $"{host.Key} {host.Value.ToString(CultureInfo.InvariantCulture)}");
        return $"{Total(counts)} ({string.Join(", ", hosts)})";
    }
}

/// <summary>A download total and its host values, in the shape the snapshot uses.</summary>
internal sealed record DownloadCountView(long Total, IReadOnlyDictionary<string, long> Hosts);
