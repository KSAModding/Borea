namespace Borea.Core.Licenses;

/// <summary>
/// The License filter of Discover. A player chooses groups that say what the license allows, exact licenses, or both.
/// An expression falls in one group, and it matches an exact license when that license puts it in its group.
/// So "MIT AND CC-BY-NC-SA-4.0" matches CC-BY-NC-SA-4.0 and not MIT, because the player must keep the non-commercial terms,
/// and the exact licenses under a group are the same licenses that the line under the group names.
/// A text that does not parse as an expression counts as one license in the group Other.
/// </summary>
public static class LicenseFilter
{
    /// <summary>
    /// The licenses whose group Borea knows, each SPDX id by its name. Every license that is not here is Other, so a gap can
    /// only make a free license look unknown, never make a restricted one look free. The Free ids are the common licenses
    /// that the OSI approves, with the deprecated ids that listings still write, plus the free Creative Commons licenses,
    /// the font licenses and WTFPL. CC-BY-ND and CC-BY-NC-ND forbid changes, so they are not here and go to Other.
    /// </summary>
    public static IReadOnlyDictionary<string, LicenseGroup> Groups { get; } = Table(
        (LicenseGroup.Free,
        [
            "0BSD", "AFL-3.0", "AGPL-3.0", "AGPL-3.0-only", "AGPL-3.0-or-later", "Apache-1.1", "Apache-2.0", "Artistic-2.0",
            "BlueOak-1.0.0", "BSD-2-Clause", "BSD-3-Clause", "BSL-1.0", "CDDL-1.0", "EPL-1.0", "EPL-2.0", "EUPL-1.1", "EUPL-1.2",
            "GPL-2.0", "GPL-2.0-only", "GPL-2.0-or-later", "GPL-3.0", "GPL-3.0-only", "GPL-3.0-or-later", "ISC",
            "LGPL-2.0", "LGPL-2.0-only", "LGPL-2.0-or-later", "LGPL-2.1", "LGPL-2.1-only", "LGPL-2.1-or-later",
            "LGPL-3.0", "LGPL-3.0-only", "LGPL-3.0-or-later", "MIT", "MIT-0", "MPL-1.1", "MPL-2.0", "MPL-2.0-no-copyleft-exception",
            "MS-PL", "MS-RL", "NCSA", "OSL-3.0", "PostgreSQL", "Unlicense", "UPL-1.0", "WTFPL", "Zlib",
            "OFL-1.0", "OFL-1.0-RFN", "OFL-1.0-no-RFN", "OFL-1.1", "OFL-1.1-RFN", "OFL-1.1-no-RFN",
            "CC0-1.0", "CC-BY-1.0", "CC-BY-2.0", "CC-BY-2.5", "CC-BY-2.5-AU", "CC-BY-3.0", "CC-BY-3.0-AT", "CC-BY-3.0-AU",
            "CC-BY-3.0-DE", "CC-BY-3.0-IGO", "CC-BY-3.0-NL", "CC-BY-3.0-US", "CC-BY-4.0",
            "CC-BY-SA-1.0", "CC-BY-SA-2.0", "CC-BY-SA-2.0-UK", "CC-BY-SA-2.1-JP", "CC-BY-SA-2.5", "CC-BY-SA-3.0",
            "CC-BY-SA-3.0-AT", "CC-BY-SA-3.0-DE", "CC-BY-SA-3.0-IGO", "CC-BY-SA-4.0",
        ]),
        (LicenseGroup.NonCommercial,
        [
            "CC-BY-NC-1.0", "CC-BY-NC-2.0", "CC-BY-NC-2.5", "CC-BY-NC-3.0", "CC-BY-NC-3.0-DE", "CC-BY-NC-3.0-IGO", "CC-BY-NC-4.0",
            "CC-BY-NC-SA-1.0", "CC-BY-NC-SA-2.0", "CC-BY-NC-SA-2.0-DE", "CC-BY-NC-SA-2.0-FR", "CC-BY-NC-SA-2.0-UK",
            "CC-BY-NC-SA-2.5", "CC-BY-NC-SA-3.0", "CC-BY-NC-SA-3.0-DE", "CC-BY-NC-SA-3.0-IGO", "CC-BY-NC-SA-4.0",
        ]));

    /// <summary>
    /// Each license that puts an expression in its group, once with the number of those expressions, the license of the most expressions first.
    /// A license that an expression names without deciding its group, such as MIT in "MIT AND CC-BY-NC-SA-4.0", does not count for it.
    /// </summary>
    public static IReadOnlyList<LicenseOption> Options(IEnumerable<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        return Count(expressions.Select(DecidingLicenses));
    }

    /// <summary>The groups that the expressions fall in, each once with the licenses that put the expressions there, the group that allows the most first.</summary>
    public static IReadOnlyList<LicenseGroupOption> GroupOptions(IEnumerable<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        return expressions
            .Select(expression => (Group: GroupOf(expression), Licenses: DecidingLicenses(expression)))
            .GroupBy(entry => entry.Group)
            .OrderBy(group => group.Key)
            .Select(group => new LicenseGroupOption(group.Key, group.Count(), Count(group.Select(entry => entry.Licenses)).Select(option => option.License).ToList()))
            .ToList();
    }

    /// <summary>
    /// Whether one of the chosen licenses puts the expression in its group, the same rule as <see cref="Options"/>.
    /// With none chosen, every expression matches.
    /// </summary>
    public static bool Matches(string expression, IEnumerable<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(chosen);
        var licenses = chosen.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (licenses.Count == 0)
            return true;

        return DecidingLicenses(expression).Any(licenses.Contains);
    }

    /// <summary>Whether the group of the expression is one of the chosen groups. With none chosen, every expression matches.</summary>
    public static bool InGroups(string expression, IEnumerable<LicenseGroup> chosen)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(chosen);
        var groups = chosen.ToHashSet();
        return groups.Count == 0 || groups.Contains(GroupOf(expression));
    }

    /// <summary>
    /// The group of an expression. An AND takes the group of the part that allows the least, because the player must keep
    /// every part. An OR takes the group of the part that allows the most, because the player may choose that part.
    /// </summary>
    public static LicenseGroup GroupOf(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return SpdxExpression.Parse(expression) is { } parsed ? GroupOf(parsed) : LicenseGroup.Other;
    }

    private static LicenseGroup GroupOf(SpdxExpression expression) =>
        expression.Fold(GroupOfLicense, parts => parts.Max(), parts => parts.Min());

    // An exception after WITH only adds permissions, so the license alone decides the group,
    // and a + after the id means this version or a later one, so the id without it decides.
    private static LicenseGroup GroupOfLicense(SpdxLicense license) =>
        Groups.GetValueOrDefault(license.Id.TrimEnd('+'), LicenseGroup.Other);

    private static IReadOnlyDictionary<string, LicenseGroup> Table(params (LicenseGroup Group, string[] Ids)[] groups)
    {
        var table = new Dictionary<string, LicenseGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, ids) in groups)
        {
            foreach (var id in ids)
                table.Add(id, group);
        }

        return table;
    }

    private static IReadOnlyList<LicenseOption> Count(IEnumerable<IEnumerable<string>> licensesPerExpression)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var licenses in licensesPerExpression)
        {
            foreach (var license in licenses)
                counts[license] = counts.GetValueOrDefault(license) + 1;
        }

        return counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new LicenseOption(entry.Key, entry.Value))
            .ToList();
    }

    // the licenses of an expression whose own group is the group of the whole expression, or the text itself when it does not parse
    private static IEnumerable<string> DecidingLicenses(string expression)
    {
        if (SpdxExpression.Parse(expression) is not { } parsed)
        {
            var text = expression.Trim();
            return text.Length == 0 ? [] : [text];
        }

        var group = GroupOf(parsed);
        return parsed.Licenses.Where(license => GroupOfLicense(license) == group).Select(license => license.Name).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
