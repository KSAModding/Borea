namespace Borea.Storage.Index.Dtos;

/// <summary>One image record inside the images value of an authored document.</summary>
public sealed class ImageDto
{
    public required string Url { get; set; }

    public required string Sha256 { get; set; }

    public required int Width { get; set; }

    public required int Height { get; set; }

    public required long Size { get; set; }

    public string? License { get; set; }

    public string? Attribution { get; set; }

    public string? Source { get; set; }
}
