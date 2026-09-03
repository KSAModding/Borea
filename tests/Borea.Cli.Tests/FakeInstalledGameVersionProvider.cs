using Borea.Core.Game;

namespace Borea.Cli.Tests;

/// <summary>
/// Answers in place of the version resource of KSA.dll.
/// </summary>
internal sealed class FakeInstalledGameVersionProvider : IInstalledGameVersionProvider
{
    public InstalledGameVersion? Installed { get; set; }

    public InstalledGameVersion? GetInstalledVersion() => Installed;
}
