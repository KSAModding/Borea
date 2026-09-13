using System.Collections.ObjectModel;
using Borea.Core.Mods;

namespace Borea.Core.Index;

/// <summary>The download counts the index reports for one listing. A missing count is unknown, never zero.</summary>
public sealed class ListingDownloadCounts
{
    /// <summary>The sum of <see cref="Hosts"/>.</summary>
    public long Total { get; }

    /// <summary>The count of each host, in ordinal key order. An unknown host is kept, because it is part of <see cref="Total"/>.</summary>
    public IReadOnlyDictionary<string, long> Hosts { get; }

    /// <summary>The counts per version. A version without an entry has an unknown count.</summary>
    public IReadOnlyList<ReleaseDownloadCounts> Releases { get; }

    public ListingDownloadCounts(
        long total,
        IReadOnlyDictionary<string, long> hosts,
        IReadOnlyList<ReleaseDownloadCounts> releases)
    {
        Hosts = DownloadCountRules.CopyHosts(total, hosts, nameof(hosts));
        ArgumentNullException.ThrowIfNull(releases);

        var releaseCopy = releases.ToArray();
        if (releaseCopy.Any(release => release is null))
            throw new ArgumentException("A release download count cannot be null.", nameof(releases));

        if (releaseCopy.Select(release => release.Version).Distinct().Count() != releaseCopy.Length)
            throw new ArgumentException("Each version can have only one download count.", nameof(releases));

        Total = total;
        Releases = new ReadOnlyCollection<ReleaseDownloadCounts>(releaseCopy);
    }

    /// <summary>The counts of <paramref name="version"/>, or null when they are unknown.</summary>
    public ReleaseDownloadCounts? FindRelease(ModVersion version) =>
        Releases.FirstOrDefault(release => release.Version.Equals(version));
}

/// <summary>The download counts the index reports for one version of a listing.</summary>
public sealed class ReleaseDownloadCounts
{
    public ModVersion Version { get; }

    /// <summary>The sum of <see cref="Hosts"/>.</summary>
    public long Total { get; }

    public IReadOnlyDictionary<string, long> Hosts { get; }

    public ReleaseDownloadCounts(ModVersion version, long total, IReadOnlyDictionary<string, long> hosts)
    {
        Hosts = DownloadCountRules.CopyHosts(total, hosts, nameof(hosts));
        Version = version;
        Total = total;
    }
}

internal static class DownloadCountRules
{
    public static IReadOnlyDictionary<string, long> CopyHosts(
        long total,
        IReadOnlyDictionary<string, long> hosts,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(hosts, parameterName);
        if (total < 0)
            throw new ArgumentOutOfRangeException(nameof(total), total, "A download total cannot be negative.");

        if (hosts.Count == 0)
            throw new ArgumentException("A download count needs at least one host value.", parameterName);

        var copy = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long sum = 0;
        foreach (var (host, count) in hosts)
        {
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("A download host key cannot be empty.", parameterName);

            if (count < 0)
                throw new ArgumentException($"The download count of host '{host}' cannot be negative.", parameterName);

            copy.Add(host, count);
            try
            {
                sum = checked(sum + count);
            }
            catch (OverflowException ex)
            {
                throw new ArgumentException("The download host values are too large to add.", parameterName, ex);
            }
        }

        if (sum != total)
            throw new ArgumentException($"The download total {total} is not the sum {sum} of its host values.", parameterName);

        return new ReadOnlyDictionary<string, long>(copy);
    }
}
