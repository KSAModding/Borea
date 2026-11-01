using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.ViewModels;

/// <summary>A picture that a share adds to its listing, in the order in which the player chose it.</summary>
public sealed partial class SharePictureRow : ObservableObject
{
    private readonly ListingEditor _owner;

    internal SharePictureRow(ListingEditor owner, string name, byte[] bytes)
    {
        _owner = owner;
        Name = name;
        Bytes = bytes;
    }

    public string Name { get; }

    public byte[] Bytes { get; private set; }

    /// <summary>The first picture of a new listing, which the host makes the cover. A listing that exists keeps its cover.</summary>
    [ObservableProperty]
    private bool _isCover;

    [ObservableProperty]
    private bool _isSent;

    /// <summary>Why the host has not taken this picture, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>The host has its copy, so a picture of up to 10 MB leaves the memory.</summary>
    internal void MarkSent()
    {
        Error = null;
        IsSent = true;
        Bytes = [];

        // The thumbnail holds the array through its binding, and the Describe step stays attached behind the Done step.
        OnPropertyChanged(nameof(Bytes));
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveSharePicture(this);
}
