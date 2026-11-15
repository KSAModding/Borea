namespace Borea.App.ViewModels;

/// <summary>The main window as the choice of what Borea does when the game starts uses it.</summary>
internal interface IGameWindow
{
    /// <summary>Whether a modal shows, which may wait for an answer of the player.</summary>
    bool IsModalOpen { get; }

    bool IsMinimized { get; }

    /// <summary>Minimizes the window and gives memory back that the window can load again.</summary>
    void Minimize();

    /// <summary>Restores the window to the state it had before it was minimized, and brings it to the front.</summary>
    void Restore();
}
