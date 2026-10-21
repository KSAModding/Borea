namespace Borea.Core.Preview;

/// <summary>
/// The render model component of a part template that a draw comes from.
/// </summary>
public enum PreviewModelKind
{
    /// <summary>A PartModel element.</summary>
    Model,

    /// <summary>A PartModelDynamic element. The preview shows its template pose, because it does not evaluate animations.</summary>
    Dynamic,

    /// <summary>A PartModelGlass element.</summary>
    Glass,
}
