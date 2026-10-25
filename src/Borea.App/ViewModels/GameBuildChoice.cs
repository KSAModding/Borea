using Borea.Core.Game;

namespace Borea.App.ViewModels;

/// <summary>One row of the game build picker of the new instance modal.</summary>
/// <param name="Build">The kept build the instance starts on, or null for the game folder of the settings.</param>
public sealed record GameBuildChoice(GameVersion? Build, string Text);
