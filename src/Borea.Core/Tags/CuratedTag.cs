namespace Borea.Core.Tags;

public sealed record CuratedTag
{
    public string Tag { get; }
    public string Name { get; }
    public string Meaning { get; }
    public string? ForumPrefix { get; }

    public CuratedTag(string tag, string name, string meaning, string? forumPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(meaning);
        Tag = tag;
        Name = name;
        Meaning = meaning;
        ForumPrefix = forumPrefix;
    }
}
