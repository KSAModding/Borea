using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

/// <summary>
/// A steward who amends on the author's request (RFC 0079): the rows of its table that tools/amend.py has no option for, the dependencies that stay,
/// and the request line of the pull request. The expected answers are those of tools/check_amendment.py for the same files.
/// </summary>
public sealed partial class ReleaseAmendmentOnRequestTests
{
    private const string Link = "https://github.com/KSAModding/content-index/issues/42#issuecomment-7";

    private static readonly JsonObject File = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "amendment-vectors.json")))!.AsObject();

    private static readonly IReadOnlyList<string> GameVersions = [.. File["game_versions"]!.AsArray().Select(version => (string)version!)];

    /// <summary>ExampleMod 1.2.0 with the derived optional KittenExtensions and the authored required ExampleLibrary 2.0.0 to 2.9.0.</summary>
    private static readonly string Base = (string)File["vectors"]!.AsArray().Single(vector => (string)vector!["name"]! == "the owner raises a game_max")!["base"]!;

    private static readonly string Path120 = ReleaseAmendment.PathOf("ExampleMod", "1.2.0");

    [Fact]
    public void NewKind_OfADerivedDependency_OnTheAuthorsRequest_MakesItAuthoredAndKeepsIt()
    {
        var amendment = ReleaseAmendment.Create(new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind("kittenextensions", "required")] }, GameVersions, DateTimeOffset.UtcNow);

        var amended = amendment.Apply(Path120, Base, ReleaseAmender.Owner);

        Assert.NotNull(amended);
        Assert.True(amended.Widens);
        Assert.Equal(
            Base.Replace("\"id\": \"KittenExtensions\",\n      \"kind\": \"optional\",\n      \"source\": \"derived\"", "\"id\": \"KittenExtensions\",\n      \"kind\": \"required\",\n      \"source\": \"authored\"", StringComparison.Ordinal),
            amended.Text);
    }

    [Fact]
    public void NewKind_ByAStewardAlone_IsRefusedAsAWidening()
    {
        var amendment = ReleaseAmendment.Create(new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind("ExampleLibrary", "recommends")] }, GameVersions, DateTimeOffset.UtcNow);

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => amendment.Apply(Path120, Base, ReleaseAmender.Steward));
        var amended = amendment.Apply(Path120, Base, ReleaseAmender.Owner);

        Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
        Assert.Contains("the dependency 'examplelibrary' changes kind from 'required' to 'recommends'", refused.Details);
        Assert.Contains("\"kind\": \"recommends\",\n      \"min\": \"2.0.0\"", amended?.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Nothing", "required", ReleaseAmendmentRefusal.NoDependency)]
    [InlineData("KittenExtensions", "wants", ReleaseAmendmentRefusal.InvalidChange)]
    [InlineData(" ", "required", ReleaseAmendmentRefusal.InvalidChange)]
    public void NewKind_OfNoStatedDependencyOrNoKind_IsRefused(string id, string kind, ReleaseAmendmentRefusal refusal)
    {
        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() =>
            ReleaseAmendment.Create(new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind(id, kind)] }, GameVersions, DateTimeOffset.UtcNow)
                .Apply(Path120, Base, ReleaseAmender.Owner));

        Assert.Equal(refusal, refused.Refusal);
    }

    [Fact]
    public void NewKind_ThatTheDependencyHasAlready_ChangesNothing()
    {
        var amendment = ReleaseAmendment.Create(new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind("KittenExtensions", "optional")] }, GameVersions, DateTimeOffset.UtcNow);

        Assert.Null(amendment.Apply(Path120, Base, ReleaseAmender.Owner));
    }

    /// <summary>
    /// What the index answers when a dependency is removed or changes kind, as tools/check_amendment.py gives it without the archive.
    /// No change of the form removes an entry, and the check refuses a file that lost one, so a derived entry always stays.
    /// </summary>
    [Theory]
    [InlineData("KittenExtensions", null, null, "the dependency 'kittenextensions' is removed, and a derived entry stays, because the loader acts on it", null)]
    [InlineData("ExampleLibrary", null, null,
        "the dependency 'examplelibrary' is removed, and the archive was not read, so nothing shows that its mod.toml does not declare it",
        "the dependency 'examplelibrary' is removed, which widens the release")]
    [InlineData("KittenExtensions", "required", "derived",
        "the dependency 'kittenextensions' changes kind and stays derived, and a kind the archive's mod.toml does not declare is authored",
        "the dependency 'kittenextensions' changes kind from 'optional' to 'required'")]
    [InlineData("KittenExtensions", "required", "authored", null, "the dependency 'kittenextensions' changes kind from 'optional' to 'required'")]
    public void Check_GivesTheAnswerOfTheIndex(string id, string? kind, string? source, string? error, string? ownerOnly)
    {
        var published = JsonNode.Parse(Base)!.AsObject();
        var amended = (JsonObject)published.DeepClone();
        var dependencies = amended["dependencies"]!.AsArray();
        var entry = dependencies.OfType<JsonObject>().Single(candidate => (string)candidate["id"]! == id);
        if (kind is null)
        {
            dependencies.Remove(entry);
        }
        else
        {
            entry["kind"] = kind;
            entry["source"] = source;
        }

        var errors = new List<string>();
        var widened = new List<string>();
        ReleaseAmendmentCheck.Check(Path120, published, amended, errors, widened);

        string[] expectedErrors = error is null ? [] : [error];
        string[] expectedWidened = ownerOnly is null ? [] : [ownerOnly];
        Assert.Equal(expectedErrors, errors);
        Assert.Equal(expectedWidened, widened);
    }

    [Fact]
    public void Body_OnTheAuthorsRequest_NamesTheRequestInTheLineThatTheChecksRead_AndTheCommandRunsAsTheOwner()
    {
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), new ReleaseChange { GameMax = "2026.8.22.5348" }, "The author tested the new build.", Link);
        var preview = new ReleaseAmendmentPreview(request, [new ReleaseFilePreview("1.2.0", Path120, "{}\n", "{ }\n")], ["alice"]);

        Assert.Equal(
            "Amends release 1.2.0 of `ExampleMod`.\n\nReason: The author tested the new build.\n\nRequested by the author: <" + Link + ">\n\n"
            + "The same amendment with the tools of this repository:\n\n```text\npython3 tools/amend.py --listing ExampleMod --version 1.2.0 --owner --game-max 2026.8.22.5348\n```\n\n@alice owns `ExampleMod`.",
            preview.Body);
        Assert.Equal(Link, Assert.Single(RequestLine().Matches(preview.Body)).Groups[1].Value);
        var alone = preview with { Request = request with { AuthorRequest = null } };
        Assert.Empty(RequestLine().Matches(alone.Body));
        Assert.DoesNotContain("--owner", alone.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_OfANewKind_SaysThatToolsAmendHasNoOptionForIt()
    {
        var kinds = new ReleaseChange { DependencyKinds = [new ReleaseDependencyKind("KittenExtensions", "required")] };
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), kinds, "The author asks for it.", Link);
        var withBound = request with { Change = kinds with { DependencyBounds = [new ReleaseDependencyBounds("ExampleLibrary", null, "2.5.0")] } };
        var files = new[] { new ReleaseFilePreview("1.2.0", Path120, "{}\n", "{ }\n") };

        var alone = new ReleaseAmendmentPreview(request, files, []).Body;
        var together = new ReleaseAmendmentPreview(withBound, files, []).Body;

        Assert.False(request.HasToolOptions);
        Assert.DoesNotContain("```", alone, StringComparison.Ordinal);
        Assert.Contains("tools/amend.py has no option for these changes, so this amendment has no command: `KittenExtensions` becomes `required`.", alone, StringComparison.Ordinal);
        Assert.Contains("--owner --dependency-max ExampleLibrary=2.5.0\n```", together, StringComparison.Ordinal);
        Assert.Contains("tools/amend.py has no option for some of these changes, so the command leaves out: `KittenExtensions` becomes `required`.", together, StringComparison.Ordinal);
        Assert.True(request.ChangesListingFields);
    }

    [Fact]
    public void Body_NamesEveryChangeThatToolsAmendHasNoOptionFor()
    {
        var change = new ReleaseChange
        {
            GameMin = "2026.8.3.5117",
            RemoveGameMax = true,
            Os = ["windows", "linux"],
            Unyank = true,
            RemoveLoaderMin = true,
            RemoveLoaderMax = true,
            RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("ExampleLibrary", true, true), new ReleaseDependencyBoundRemoval("Other", false, true)],
            DependencyKinds = [new ReleaseDependencyKind("KittenExtensions", "required")],
        };
        var request = new ReleaseAmendmentRequest("ExampleMod", ReleaseSelection.Of("1.2.0"), change, "The author asks for it.", Link);
        var body = new ReleaseAmendmentPreview(request, [new ReleaseFilePreview("1.2.0", Path120, "{}\n", "{ }\n")], []).Body;

        Assert.Contains("python3 tools/amend.py --listing ExampleMod --version 1.2.0 --owner --game-min 2026.8.3.5117\n```", body, StringComparison.Ordinal);
        Assert.Contains(
            "tools/amend.py has no option for some of these changes, so the command leaves out: `game_max` is removed; `os` becomes `windows, linux`; "
            + "the yank is taken back; the loader min is removed; the loader max is removed; the min and the max of `ExampleLibrary` is removed; "
            + "the max of `Other` is removed; `KittenExtensions` becomes `required`.",
            body, StringComparison.Ordinal);
        Assert.Equal(new[] { "`os` is removed" }, (request with { Change = new ReleaseChange { Os = [] } }).ChangesWithoutToolOption);
        Assert.True((request with { Change = new ReleaseChange { Os = [] } }).ChangesListingFields);
        Assert.False((request with { Change = new ReleaseChange { RemoveGameMax = true, Unyank = true } }).ChangesListingFields);
    }

    /// <summary>
    /// A row of the table of RFC 0079 that tools/amend.py has no option for. The base, the change, the written file, and the owner-only
    /// messages that tools/check_amendment.py gives for the same two files.
    /// </summary>
    private static (string Published, ReleaseChange Change, string Written, string[] OwnerOnly) OwnerRow(string name)
    {
        const string GameMax = "  \"game_max\": \"2026.8.19.5261\",\n  \"game_max_revision\": 5261,\n";
        const string AfterGameMax = "  \"game_max_revision\": 5261,\n";
        const string LoaderMin = "    \"min\": \"0.4.5\",\n";
        var linux = Base.Replace(AfterGameMax, AfterGameMax + "  \"os\": [\n    \"linux\"\n  ],\n", StringComparison.Ordinal);
        var both = Base.Replace(AfterGameMax, AfterGameMax + "  \"os\": [\n    \"windows\",\n    \"linux\"\n  ],\n", StringComparison.Ordinal);
        var yanked = Base.TrimEnd()[..^1].TrimEnd() + ",\n  \"yanked\": true,\n  \"yanked_reason\": \"It deletes saves.\"\n}\n";
        var derived = "\"id\": \"KittenExtensions\",\n      \"kind\": \"optional\",\n      \"source\": \"derived\"";
        var derivedWithMin = Base.Replace(derived, "\"id\": \"KittenExtensions\",\n      \"kind\": \"optional\",\n      \"min\": \"1.0.0\",\n      \"source\": \"derived\"", StringComparison.Ordinal);

        return name switch
        {
            "remove game_max" => (Base, new ReleaseChange { RemoveGameMax = true }, Base.Replace(GameMax, string.Empty, StringComparison.Ordinal),
                ["game_max_revision 5261 is removed, which widens the release"]),
            "add os" => (Base, new ReleaseChange { Os = ["windows", "linux"] }, both, ["os changes from no restriction to windows, linux"]),
            "change os" => (linux, new ReleaseChange { Os = ["windows", "linux"] }, both, ["os changes from linux to windows, linux"]),
            "remove os" => (linux, new ReleaseChange { Os = [] }, Base, ["os changes from linux to no restriction"]),
            "take back a yank" => (yanked, new ReleaseChange { Unyank = true }, Base, ["the release is un-yanked, which widens the release"]),
            "remove the loader min" => (Base, new ReleaseChange { RemoveLoaderMin = true }, Base.Replace(LoaderMin, string.Empty, StringComparison.Ordinal),
                ["the loader removes its min '0.4.5', which widens the release"]),
            "remove the loader max" => (Base.Replace(LoaderMin, LoaderMin + "    \"max\": \"0.5.0\",\n", StringComparison.Ordinal), new ReleaseChange { RemoveLoaderMax = true }, Base,
                ["the loader removes its max '0.5.0', which widens the release"]),
            "remove a dependency max" => (Base, new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("ExampleLibrary", false, true)] },
                Base.Replace("      \"max\": \"2.9.0\",\n", string.Empty, StringComparison.Ordinal),
                ["the dependency 'examplelibrary' removes its max '2.9.0', which widens the release"]),
            "remove the min of a derived dependency" => (derivedWithMin, new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("kittenextensions", true, false)] },
                Base.Replace(derived, "\"id\": \"KittenExtensions\",\n      \"kind\": \"optional\",\n      \"source\": \"authored\"", StringComparison.Ordinal),
                ["the dependency 'kittenextensions' removes its min '1.0.0', which widens the release"]),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    public static TheoryData<string> OwnerRows =>
    [
        "remove game_max", "add os", "change os", "remove os", "take back a yank", "remove the loader min", "remove the loader max",
        "remove a dependency max", "remove the min of a derived dependency",
    ];

    [Theory]
    [MemberData(nameof(OwnerRows))]
    public void OwnerRow_OnTheAuthorsRequest_IsWrittenAsTheIndexChecksIt_AndByAStewardAloneIsRefused(string name)
    {
        var (published, change, written, ownerOnly) = OwnerRow(name);
        var amendment = ReleaseAmendment.Create(change, GameVersions, DateTimeOffset.UtcNow);

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => amendment.Apply(Path120, published, ReleaseAmender.Steward));
        var amended = amendment.Apply(Path120, published, ReleaseAmender.Owner);

        Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
        string[] details = [.. ownerOnly, "only the verified owner of the listing widens a release, or a steward who names the author's request"];
        Assert.Equal(details, refused.Details);
        Assert.NotNull(amended);
        Assert.True(amended.Widens);
        Assert.Equal(Encoding.UTF8.GetBytes(written), Encoding.UTF8.GetBytes(amended.Text));
        Assert.Null(amendment.Apply(Path120, written, ReleaseAmender.Owner));
    }

    public static TheoryData<string> ContradictoryChanges =>
    [
        "yank and un-yank", "set and remove game_max", "set and remove the loader min", "set and remove the loader max", "an unknown platform",
        "a platform twice", "a bound set and removed", "a removal of no bound", "a removal of no dependency",
    ];

    [Theory]
    [MemberData(nameof(ContradictoryChanges))]
    public void OwnerRow_ThatContradictsItselfOrNamesNothing_IsRefused(string name)
    {
        var change = name switch
        {
            "yank and un-yank" => new ReleaseChange { Yank = true, Unyank = true },
            "set and remove game_max" => new ReleaseChange { GameMax = "2026.8.19.5261", RemoveGameMax = true },
            "set and remove the loader min" => new ReleaseChange { LoaderMin = "0.5.0", RemoveLoaderMin = true },
            "set and remove the loader max" => new ReleaseChange { LoaderMax = "0.5.0", RemoveLoaderMax = true },
            "an unknown platform" => new ReleaseChange { Os = ["freebsd"] },
            "a platform twice" => new ReleaseChange { Os = ["linux", "linux"] },
            "a bound set and removed" => new ReleaseChange
            {
                DependencyBounds = [new ReleaseDependencyBounds("ExampleLibrary", null, "2.5.0")],
                RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("examplelibrary", false, true)],
            },
            "a removal of no bound" => new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("ExampleLibrary", false, false)] },
            "a removal of no dependency" => new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval(" ", true, false)] },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Create(change, GameVersions, DateTimeOffset.UtcNow));

        Assert.Equal(ReleaseAmendmentRefusal.InvalidChange, refused.Refusal);
    }

    [Fact]
    public void OwnerRow_OfABoundTheReleaseDoesNotStateOrADependencyItDoesNotName_ChangesNothingOrIsRefused()
    {
        var withoutMax = ReleaseAmendment.Create(new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("KittenExtensions", false, true)] }, GameVersions, DateTimeOffset.UtcNow);
        var unknown = ReleaseAmendment.Create(new ReleaseChange { RemovedDependencyBounds = [new ReleaseDependencyBoundRemoval("Nothing", true, false)] }, GameVersions, DateTimeOffset.UtcNow);

        Assert.Null(withoutMax.Apply(Path120, Base, ReleaseAmender.Owner));
        Assert.Equal(ReleaseAmendmentRefusal.NoDependency, Assert.Throws<ReleaseAmendmentRefusedException>(() => unknown.Apply(Path120, Base, ReleaseAmender.Owner)).Refusal);
    }

    [Theory]
    [InlineData(Link, true)]
    [InlineData("https://forums.ahwoo.com/threads/example-mod.123/post-9", true)]
    [InlineData("HTTPS://discord.com/channels/1/2/3", true)]
    [InlineData("http://github.com/KSAModding/content-index/issues/42", false)]
    [InlineData("github.com/KSAModding/content-index/issues/42", false)]
    [InlineData("https://github.com/a b", false)]
    [InlineData("https://github.com/<a>", false)]
    [InlineData("https://", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidAuthorRequest_TakesOneHttpsLinkThatTheRequestLineCarries(string? link, bool valid)
    {
        Assert.Equal(valid, ReleaseAmendmentRequest.IsValidAuthorRequest(link));
        if (valid)
            Assert.Equal(link, RequestLine().Match($"{ReleaseAmendmentRequest.RequestLine} <{link}>").Groups[1].Value);
    }

    /// <summary>REQUEST_LINE of tools/decide.py, which finds the author's request in the pull request description.</summary>
    [GeneratedRegex(@"^[ \t]*Requested by the author:[ \t]*(?:<(https://[^\s<>]+)>|(https://\S+))[ \t\r]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex RequestLine();
}
