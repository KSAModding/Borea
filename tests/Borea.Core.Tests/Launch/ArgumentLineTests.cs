using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class ArgumentLineTests
{
    // Each expected split was checked against CommandLineToArgvW on Windows.
    public static TheoryData<string, string[]> Splits => new()
    {
        { "", [] },
        { "  -a\t b  ", ["-a", "b"] },
        { "\"a b\" c", ["a b", "c"] },
        { "\"\"", [""] },
        { "\"\" \"\"", ["", ""] },
        { "\"a b", ["a b"] },
        { "a\"b c\"d e", ["ab cd", "e"] },
        { @"a\b a\\b", [@"a\b", @"a\\b"] },
        { @"a\""b", ["a\"b"] },
        { @"a\\\""b", [@"a\""b"] },
        { @"""a\\"" b", [@"a\", "b"] },
        { @"""C:\My Dir\""", ["C:\\My Dir\""] },
        { @"\\\\""a b""", [@"\\a b"] },
        { @"a\\\\b""c d""", [@"a\\\\bc d"] },
        { "\"a\"\"b c\"", ["a\"b", "c"] },
        { "\"a\"\"\"b c\"", ["a\"b c"] },
        { "a\"\"b c", ["ab", "c"] },
        { "\"\"\"\"", ["\""] },
        { "\"\"\"\"\"\"", ["\"\""] },
        { "\"\"\" x", ["\"", "x"] },
        { "\"\"\"\" x", ["\" x"] },
        { "\"a b\"\"c d\" e", ["a b\"c", "d e"] },
    };

    [Theory]
    [MemberData(nameof(Splits))]
    public void Split_FollowsCommandLineToArgvW(string text, string[] expected)
    {
        Assert.Equal(expected, ArgumentLine.Split(text));
    }

    [Theory]
    [InlineData("-windowed", "-windowed")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\My Dir\", @"""C:\My Dir\\""")]
    [InlineData(@"C:\Dir\", @"C:\Dir\")]
    [InlineData(@"a\""b", @"""a\\\""b""")]
    public void Join_QuotesOnlyWhatNeedsIt(string argument, string expected)
    {
        Assert.Equal(expected, ArgumentLine.Join([argument]));
    }

    [Fact]
    public void Join_ThenSplit_GivesTheSameArguments()
    {
        string[] arguments = ["-name", "a b", "", "\"", "\\", "\\\"", "tab\there", "line\nbreak", @"C:\Program Files\KSA\", "x\\\\\"y", "\u00e4 \u6f22"];

        Assert.Equal(arguments, ArgumentLine.Split(ArgumentLine.Join(arguments)));
    }

    [Fact]
    public void Join_NoArguments_IsEmpty()
    {
        Assert.Equal(string.Empty, ArgumentLine.Join([]));
    }
}
