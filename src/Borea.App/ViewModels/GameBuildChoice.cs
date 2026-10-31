using Borea.Core.Game;

namespace Borea.App.ViewModels;

/// <summary>One row of the game build picker of the new instance modal.</summary>
/// <param name="Build">The kept build the instance starts on, or null for the current game build.</param>
/// <param name="VersionText">The build that shows next to the text, or null when Borea cannot read the build of the game folder.</param>
public sealed record GameBuildChoice(GameVersion? Build, string Text, string? VersionText);
