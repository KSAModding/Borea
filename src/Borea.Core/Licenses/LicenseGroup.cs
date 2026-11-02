namespace Borea.Core.Licenses;

/// <summary>
/// What a license lets a player do, in the words of the License filter of Discover. The order goes from the group
/// that allows the most to the group that allows the least, because an AND takes the last group of its parts and an OR the first.
/// </summary>
public enum LicenseGroup
{
    /// <summary>Anyone may use, change and share it, also commercially: an OSI open source license or a free Creative Commons one.</summary>
    Free,

    /// <summary>Anyone may use, change and share it, but not commercially.</summary>
    NonCommercial,

    /// <summary>
    /// The own terms of the author, a license that forbids changes such as CC-BY-ND, a license outside the table,
    /// or a text that does not parse. Borea does not tell the player what these allow.
    /// </summary>
    Other,
}
