namespace Borea.App.ViewModels;

/// <summary>One line of the warnings of a plan of "Update all".</summary>
/// <param name="IsBreaking">The update can break a recorded vehicle or save.</param>
public sealed record UpdateWarningLine(string Text, bool IsBreaking);
