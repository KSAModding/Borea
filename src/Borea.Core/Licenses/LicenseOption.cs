namespace Borea.Core.Licenses;

/// <summary>One exact license of the License filter, with the number of expressions that it puts in their group.</summary>
public sealed record LicenseOption(string License, int Count);
