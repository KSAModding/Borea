namespace Borea.Storage.Preview.Ksa;

/// <summary>What <see cref="PartFormatProbe"/> found in the part files of a game.</summary>
internal enum PartLayout
{
    /// <summary>Every listed file was read and none has a mark of the newer layout.</summary>
    Today,

    /// <summary>A listed file has a mark of the newer layout.</summary>
    Newer,

    /// <summary>No file has a mark, but a listed file is there and could not be opened or read.</summary>
    Uncertain,
}
