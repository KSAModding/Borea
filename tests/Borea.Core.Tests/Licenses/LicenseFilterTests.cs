using Borea.Core.Licenses;

namespace Borea.Core.Tests.Licenses;

public sealed class LicenseFilterTests
{
    [Fact]
    public void Options_AreTheLicensesThatDecideTheGroupsEachOnce_TheMostUsedFirst()
    {
        var options = LicenseFilter.Options(["MIT", "MIT AND CC-BY-SA-4.0", "GPL-3.0-or-later", "mit OR Apache-2.0", "gpl-3.0-or-later", "MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0"]);

        Assert.Equal(
            [new("MIT", 3), new("GPL-3.0-or-later", 2), new("Apache-2.0", 1), new("CC-BY-NC-SA-3.0", 1), new("CC-BY-SA-4.0", 1)],
            options);
    }

    [Fact]
    public void Options_ALicenseWithAnExceptionOrALicenseRef_IsOneOption_AndAFreeLicenseNextToALicenseRefIsNone()
    {
        var options = LicenseFilter.Options(["GPL-2.0-only WITH Classpath-exception-2.0", "LicenseRef-MyModLicense AND MIT", "GPL-2.0-only"]);

        Assert.Equal(["GPL-2.0-only", "GPL-2.0-only WITH Classpath-exception-2.0", "LicenseRef-MyModLicense"], options.Select(option => option.License));
    }

    [Fact]
    public void Options_TheExpressionsOfTheIndex_AreTheLicensesOnTheLinesOfTheGroups()
    {
        var options = LicenseFilter.Options(IndexLicenses);

        Assert.Equal(LicenseFilter.GroupOptions(IndexLicenses).SelectMany(group => group.Licenses).Order(StringComparer.OrdinalIgnoreCase), options.Select(option => option.License).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(new LicenseOption("MIT", 28), options[0]);
        Assert.DoesNotContain(options, option => option.License == "CC-BY-3.0");
    }

    [Fact]
    public void Options_ALicenseWrittenTwiceInOneExpression_CountsOnce()
    {
        Assert.Equal([new("Zlib", 2), new("MIT", 1)], LicenseFilter.Options(["MIT AND mit", "Zlib", "Zlib"]));
    }

    [Fact]
    public void Options_ParenthesesDeeperThanTheLimit_AreOneOptionWithoutACrash()
    {
        var text = new string('(', 10000) + "MIT" + new string(')', 10000);

        Assert.Equal([new LicenseOption(text, 1)], LicenseFilter.Options([text]));
    }

    [Fact]
    public void Options_TextThatDoesNotParse_IsOneOption()
    {
        Assert.Equal([new("MIT", 2), new("Public domain, ask me", 1)], LicenseFilter.Options(["Public domain, ask me", "MIT", "MIT", "  "]));
    }

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("MIT OR Apache-2.0", true)]
    [InlineData("MIT AND CC-BY-SA-4.0", true)]
    [InlineData("MIT OR CC-BY-NC-4.0", true)]
    [InlineData("MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0", false)]
    [InlineData("MIT AND LicenseRef-KSP-Assets", false)]
    [InlineData("GPL-3.0-only", false)]
    public void Matches_OnlyMitChosen_MatchesTheExpressionsThatMitPutsInTheirGroup(string expression, bool matches)
    {
        Assert.Equal(matches, LicenseFilter.Matches(expression, ["MIT"]));
    }

    [Fact]
    public void Matches_ChosenIds_IgnoreCase()
    {
        Assert.True(LicenseFilter.Matches("MIT AND CC-BY-SA-4.0", ["cc-by-sa-4.0"]));
    }

    [Fact]
    public void Matches_ALicenseWithAnException_OnlyWhenThatOptionIsChosen()
    {
        const string expression = "GPL-2.0-only WITH Classpath-exception-2.0";

        Assert.False(LicenseFilter.Matches(expression, ["GPL-2.0-only"]));
        Assert.True(LicenseFilter.Matches(expression, ["GPL-2.0-only WITH Classpath-exception-2.0"]));
        Assert.True(LicenseFilter.Matches("gpl-2.0-only with Classpath-exception-2.0", ["GPL-2.0-only WITH Classpath-exception-2.0"]));
        Assert.False(LicenseFilter.Matches("GPL-2.0-only", ["GPL-2.0-only WITH Classpath-exception-2.0"]));
    }

    [Fact]
    public void Matches_TwoChosen_MatchesWhatNamesEitherOne()
    {
        string[] chosen = ["MIT", "GPL-3.0-or-later"];

        Assert.True(LicenseFilter.Matches("MIT", chosen));
        Assert.True(LicenseFilter.Matches("GPL-3.0-or-later", chosen));
        Assert.False(LicenseFilter.Matches("Apache-2.0", chosen));
    }

    [Theory]
    [InlineData("MIT AND CC-BY-SA-4.0")]
    [InlineData("Public domain, ask me")]
    [InlineData("")]
    public void Matches_NoneChosen_MatchesEveryExpression(string expression)
    {
        Assert.True(LicenseFilter.Matches(expression, []));
    }

    [Fact]
    public void Matches_TextThatDoesNotParse_WhenThatTextIsChosen()
    {
        Assert.True(LicenseFilter.Matches(" Public domain, ask me ", ["Public domain, ask me"]));
        Assert.False(LicenseFilter.Matches("Public domain, ask me", ["MIT"]));
    }

    /// <summary>The 39 license expressions that the authored documents of the content index named on 2026-11-02.</summary>
    private static readonly string[] IndexLicenses =
    [
        .. Enumerable.Repeat("MIT", 25),
        .. Enumerable.Repeat("CC-BY-4.0", 3),
        .. Enumerable.Repeat("MIT AND OFL-1.1", 2),
        .. Enumerable.Repeat("GPL-3.0-or-later", 2),
        "MIT AND LicenseRef-KSP-Assets AND LicenseRef-KSA-Assets",
        "MIT AND CC-BY-SA-4.0",
        "MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0",
        "LGPL-3.0-or-later",
        "GPL-3.0",
        "GPL-2.0-only AND CC-BY-SA-4.0",
        "CC-BY-NC-SA-4.0",
    ];

    [Theory]
    [InlineData("MIT", LicenseGroup.Free)]
    [InlineData("CC-BY-4.0", LicenseGroup.Free)]
    [InlineData("MIT AND OFL-1.1", LicenseGroup.Free)]
    [InlineData("GPL-3.0-or-later", LicenseGroup.Free)]
    [InlineData("MIT AND LicenseRef-KSP-Assets AND LicenseRef-KSA-Assets", LicenseGroup.Other)]
    [InlineData("MIT AND CC-BY-SA-4.0", LicenseGroup.Free)]
    [InlineData("MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0", LicenseGroup.NonCommercial)]
    [InlineData("LGPL-3.0-or-later", LicenseGroup.Free)]
    [InlineData("GPL-3.0", LicenseGroup.Free)]
    [InlineData("GPL-2.0-only AND CC-BY-SA-4.0", LicenseGroup.Free)]
    [InlineData("CC-BY-NC-SA-4.0", LicenseGroup.NonCommercial)]
    public void GroupOf_EachExpressionOfTheIndex_IsTheGroupOfItsTable(string expression, LicenseGroup group)
    {
        Assert.Contains(expression, IndexLicenses);
        Assert.Equal(group, LicenseFilter.GroupOf(expression));
    }

    [Fact]
    public void GroupOptions_TheExpressionsOfTheIndex_AreThreeGroupsWithTheLicensesThatPutThemThere()
    {
        var options = LicenseFilter.GroupOptions(IndexLicenses);

        Assert.Equal(39, IndexLicenses.Length);
        Assert.Equal([LicenseGroup.Free, LicenseGroup.NonCommercial, LicenseGroup.Other], options.Select(option => option.Group));
        Assert.Equal([36, 2, 1], options.Select(option => option.Count));
        Assert.Equal(["MIT", "CC-BY-4.0", "CC-BY-SA-4.0", "GPL-3.0-or-later", "OFL-1.1", "GPL-2.0-only", "GPL-3.0", "LGPL-3.0-or-later"], options[0].Licenses);
        Assert.Equal(["CC-BY-NC-SA-3.0", "CC-BY-NC-SA-4.0"], options[1].Licenses);
        Assert.Equal(["LicenseRef-KSA-Assets", "LicenseRef-KSP-Assets"], options[2].Licenses);
    }

    [Theory]
    [InlineData("MIT", LicenseGroup.Free)]
    [InlineData("gpl-3.0-or-later", LicenseGroup.Free)]
    [InlineData("GPL-2.0+", LicenseGroup.Free)]
    [InlineData("OFL-1.1", LicenseGroup.Free)]
    [InlineData("CC0-1.0", LicenseGroup.Free)]
    [InlineData("CC-BY-SA-3.0-IGO", LicenseGroup.Free)]
    [InlineData("WTFPL", LicenseGroup.Free)]
    [InlineData("LGPL-2.0-only", LicenseGroup.Free)]
    [InlineData("MPL-1.1", LicenseGroup.Free)]
    [InlineData("MPL-2.0-no-copyleft-exception", LicenseGroup.Free)]
    [InlineData("OSL-3.0", LicenseGroup.Free)]
    [InlineData("Apache-1.1", LicenseGroup.Free)]
    [InlineData("cc-by-nc-3.0", LicenseGroup.NonCommercial)]
    [InlineData("CC-BY-ND-4.0", LicenseGroup.Other)]
    [InlineData("CC-BY-NC-ND-4.0", LicenseGroup.Other)]
    [InlineData("LicenseRef-KSA-Assets", LicenseGroup.Other)]
    [InlineData("Some-Unknown-1.0", LicenseGroup.Other)]
    public void GroupOf_OneLicense_IsTheGroupOfItsTable(string expression, LicenseGroup group)
    {
        Assert.Equal(group, LicenseFilter.GroupOf(expression));
    }

    [Theory]
    [InlineData("CC-BY-NC-4.0 AND LicenseRef-MyModLicense", LicenseGroup.Other)]
    [InlineData("MIT AND CC-BY-NC-4.0", LicenseGroup.NonCommercial)]
    [InlineData("MIT OR LicenseRef-MyModLicense", LicenseGroup.Free)]
    [InlineData("CC-BY-NC-4.0 OR LicenseRef-MyModLicense", LicenseGroup.NonCommercial)]
    [InlineData("(MIT AND CC-BY-NC-4.0) OR CC-BY-4.0", LicenseGroup.Free)]
    [InlineData("MIT AND (CC-BY-NC-4.0 OR Apache-2.0)", LicenseGroup.Free)]
    public void GroupOf_AndTakesThePartThatAllowsTheLeast_OrThePartThatAllowsTheMost(string expression, LicenseGroup group)
    {
        Assert.Equal(group, LicenseFilter.GroupOf(expression));
    }

    [Theory]
    [InlineData("GPL-2.0-only WITH Classpath-exception-2.0", LicenseGroup.Free)]
    [InlineData("LicenseRef-MyModLicense WITH Classpath-exception-2.0", LicenseGroup.Other)]
    public void GroupOf_ALicenseWithAnException_IsTheGroupOfTheLicense(string expression, LicenseGroup group)
    {
        Assert.Equal(group, LicenseFilter.GroupOf(expression));
    }

    [Fact]
    public void GroupOf_TextThatDoesNotParse_IsOtherWithoutACrash()
    {
        Assert.Equal(LicenseGroup.Other, LicenseFilter.GroupOf("Public domain, ask me"));
        Assert.Equal(LicenseGroup.Other, LicenseFilter.GroupOf(new string('(', 10000) + "MIT" + new string(')', 10000)));
    }

    [Fact]
    public void GroupOptions_TextThatDoesNotParse_IsOtherWithTheTextAsItsLicense()
    {
        var other = Assert.Single(LicenseFilter.GroupOptions(["Public domain, ask me", "  "]));

        Assert.Equal((LicenseGroup.Other, 2), (other.Group, other.Count));
        Assert.Equal(["Public domain, ask me"], other.Licenses);
    }

    [Fact]
    public void GroupOptions_AnOr_NamesOnlyThePartsOfItsGroup()
    {
        var options = LicenseFilter.GroupOptions(["CC-BY-NC-4.0 OR LicenseRef-MyModLicense", "MIT OR Apache-2.0"]);

        Assert.Equal(["Apache-2.0", "MIT"], options[0].Licenses);
        Assert.Equal(["CC-BY-NC-4.0"], options[1].Licenses);
    }

    [Fact]
    public void InGroups_TwoChosen_MatchesWhatFallsInEitherOne_NoneChosenMatchesEveryExpression()
    {
        LicenseGroup[] chosen = [LicenseGroup.NonCommercial, LicenseGroup.Other];

        Assert.True(LicenseFilter.InGroups("MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0", chosen));
        Assert.True(LicenseFilter.InGroups("Public domain, ask me", chosen));
        Assert.False(LicenseFilter.InGroups("MIT", chosen));
        Assert.True(LicenseFilter.InGroups("MIT", []));
    }
}
