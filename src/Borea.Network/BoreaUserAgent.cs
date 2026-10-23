using System.Net.Http.Headers;
using Borea.Core.Updates;

namespace Borea.Network;

/// <summary>
/// The User-Agent of every request Borea sends, for example "Borea/0.3.0 (+https://github.com/KSAModding/Borea)".
/// The Ahwoo forums let Borea read a thread only when the User-Agent names Borea and a contact link.
/// </summary>
public static class BoreaUserAgent
{
    public const string ContactUrl = "https://github.com/KSAModding/Borea";

    public static void Apply(HttpClient client) => Apply(client, BoreaBuild.Version);

    /// <summary>Adds the User-Agent with <paramref name="version"/> in place of the version of this build.</summary>
    internal static void Apply(HttpClient client, string version)
    {
        ArgumentNullException.ThrowIfNull(client);

        var userAgent = client.DefaultRequestHeaders.UserAgent;
        userAgent.Add(new ProductInfoHeaderValue(Product(version)));
        userAgent.Add(new ProductInfoHeaderValue($"(+{ContactUrl})"));
    }

    // A version that is not an HTTP token can only come from a hand-set build property.
    // It drops out instead of failing every client.
    private static ProductHeaderValue Product(string version)
        => ProductHeaderValue.TryParse($"Borea/{version}", out var product) ? product : new ProductHeaderValue("Borea");
}
