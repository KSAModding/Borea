namespace Borea.Core.Announcements;

/// <summary>One post of the KSAModding team. <paramref name="Body"/> is Markdown, and <paramref name="Link"/> is an HTTPS URL or null.</summary>
public sealed record Announcement(string Id, string Title, DateTimeOffset Date, string Body, string? Link = null);

/// <summary>The posts of one announcements file, and why each post that was left out was skipped.</summary>
public sealed record AnnouncementFile(IReadOnlyList<Announcement> Posts, IReadOnlyList<string> SkippedPosts);
