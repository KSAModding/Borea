using System;
using Borea.Core.Updates;

namespace Borea.App;

/// <summary>
/// What the App starts after its window closed and its single-instance lock is free. A new build
/// that a self-update unpacked goes here, so it does not meet the lock of the build it replaces.
/// </summary>
internal sealed class PendingHandover
{
    public Action? Run { get; set; }

    /// <summary>
    /// Says what a failed handover left behind, in the language the player reads. The window is gone
    /// by then, so this is the only sentence they get.
    /// </summary>
    public Func<SelfUpdateFailedException, string>? Describe { get; set; }
}
