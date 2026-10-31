namespace Borea.Core.Game;

/// <summary>
/// A folder inside a Wine prefix, which runs Windows programs on Linux or
/// macOS.
/// </summary>
/// <param name="PrefixRoot">The prefix folder, which holds dosdevices and system.reg, with its links resolved.</param>
/// <param name="Wrapper">The macOS wrapper app the prefix is part of, or null when it is a plain prefix.</param>
public sealed record WineInstall(string PrefixRoot, WineDriveMap Drives, WineWrapper? Wrapper);

/// <summary>
/// A macOS app that carries a Wine prefix and starts its Windows program, such
/// as a Sikarugir or Wineskin wrapper.
/// </summary>
/// <param name="BundlePath">The .app folder.</param>
/// <param name="LauncherPath">The program in Contents/MacOS that CFBundleExecutable of its Info.plist names.</param>
public sealed record WineWrapper(string BundlePath, string LauncherPath);
