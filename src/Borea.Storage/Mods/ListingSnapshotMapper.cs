using Borea.Core.Mods;

namespace Borea.Storage.Mods;

public static class ListingSnapshotMapper
{
    public static ListingSnapshotDto ToDto(ListingSnapshot listing) => new()
    {
        Name = listing.Name,
        Authors = listing.Authors.ToList(),
        Abstract = listing.Abstract,
        Description = listing.Description,
        License = listing.License,
        Tags = listing.Tags.ToList(),
        Links = listing.Links.ToDictionary(p => p.Key, p => p.Value),
    };

    public static ListingSnapshot FromDto(ListingSnapshotDto dto) => new(
        dto.Name,
        dto.Authors,
        dto.Abstract,
        dto.License,
        dto.Tags,
        dto.Links,
        dto.Description);
}
