namespace Borea.Core.Preferences;

public enum DiscoverSortOrder
{
    /// <summary>Most downloads first, and listings without a count last.</summary>
    Popularity = 0,

    /// <summary>The newest release in the saved release channel first.</summary>
    RecentlyUpdated = 1,

    Name = 2,
}
