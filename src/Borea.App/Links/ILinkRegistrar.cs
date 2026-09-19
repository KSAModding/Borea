namespace Borea.App.Links;

/// <summary>Registers a program as the handler of borea:// links for the current user.</summary>
internal interface ILinkRegistrar
{
    /// <returns>True when something was written. Nothing is written when the registration is already current.</returns>
    bool Register(string executablePath);

    /// <returns>True when the registration pointed at <paramref name="executablePath"/> and was removed.</returns>
    bool Unregister(string executablePath);
}
