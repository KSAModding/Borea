namespace Borea.Core.Updates;

/// <summary>Why a build cannot replace itself.</summary>
public enum SelfUpdateBlock
{
    /// <summary>Nothing stops the update.</summary>
    None = 0,

    /// <summary>A package manager installed this build, so that manager updates it.</summary>
    PackageManaged = 1,

    /// <summary>The folder Borea runs in cannot be changed by this user.</summary>
    ReadOnlyLocation = 2,

    /// <summary>This build did not come from a release archive, so there is no archive to replace it with.</summary>
    NotAReleaseBuild = 3,

    /// <summary>No release is published for this operating system and processor.</summary>
    UnsupportedPlatform = 4,
}
