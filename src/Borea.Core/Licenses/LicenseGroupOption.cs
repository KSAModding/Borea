namespace Borea.Core.Licenses;

/// <summary>One group of the License filter, with the number of expressions in it.</summary>
/// <param name="Licenses">
/// The licenses that put the expressions in this group, the most named first. For "MIT AND CC-BY-NC-SA-4.0" that is
/// CC-BY-NC-SA-4.0 alone, because MIT does not make the expression non-commercial.
/// </param>
public sealed record LicenseGroupOption(LicenseGroup Group, int Count, IReadOnlyList<string> Licenses);
