using System.Text.Json.Nodes;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class ReleaseAmendmentPreviewTests
{
    private static readonly JsonArray Vectors = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "amendment-vectors.json")))!["vectors"]!.AsArray();

    private static readonly ReleaseAmendmentRequest Yank = new("MyMod", ReleaseSelection.Of("1.2.0"), new ReleaseChange { Yank = true }, " The archive carries malware. ");

    [Fact]
    public void Patch_OfAnAddedGameMax_IsOneHunkWithThreeLinesAroundIt()
    {
        var vector = Vector("a game_max is added after game_min_revision");
        var before = (string)vector["base"]!;
        var lines = before.Split('\n');

        var patch = new ReleaseFilePreview("1.2.0", "releases/ExampleMod/1.2.0.json", before, (string)vector["written"]!).Patch;

        Assert.Equal(
            string.Join('\n', [
                "@@ -8,6 +8,8 @@",
                .. lines[7..10].Select(line => " " + line),
                "+  \"game_max\": \"2026.8.19.5261\",",
                "+  \"game_max_revision\": 5261,",
                .. lines[10..13].Select(line => " " + line),
            ]),
            patch);
    }

    [Fact]
    public void Patch_OfAYank_ChangesTheLastLinesOnly()
    {
        var vector = Vector("a steward yanks a release on a report");
        var before = (string)vector["base"]!;
        var count = before.Split('\n').Length - 1;

        var patch = new ReleaseFilePreview("1.2.0", "releases/ExampleMod/1.2.0.json", before, (string)vector["written"]!).Patch!;

        Assert.StartsWith($"@@ -{count - 4},5 +{count - 4},7 @@\n", patch, StringComparison.Ordinal);
        Assert.EndsWith("\n-  }\n+  },\n+  \"yanked\": true,\n+  \"yanked_reason\": \"The archive carries malware.\"\n }", patch, StringComparison.Ordinal);
    }

    [Fact]
    public void Unified_KeepsChangesFarApartInHunksOfTheirOwn_AndGivesNothingForTheSameText()
    {
        var before = string.Concat(Enumerable.Range(1, 20).Select(line => $"line {line}\n"));
        var after = before.Replace("line 2\n", "line two\n", StringComparison.Ordinal).Replace("line 18\n", "line eighteen\n", StringComparison.Ordinal) + "line 21\n";

        var patch = LineDiff.Unified(before, after);

        Assert.Equal(
            "@@ -1,5 +1,5 @@\n line 1\n-line 2\n+line two\n line 3\n line 4\n line 5\n"
            + "@@ -15,6 +15,7 @@\n line 15\n line 16\n line 17\n-line 18\n+line eighteen\n line 19\n line 20\n+line 21",
            patch);
        Assert.Equal(string.Empty, LineDiff.Unified(before, before));
        Assert.Null(new ReleaseFilePreview("1.0.0", "releases/MyMod/1.0.0.json", before, null).Patch);
    }

    [Fact]
    public void Title_NamesTheChangedVersionsOldestFirst_AndARangeBeyondThree()
    {
        var three = Preview(Yank, [Release("1.10.0"), Release("1.9.0", changed: false), Release("1.2.0"), Release("1.2.0-beta.1")]);
        var four = Preview(Yank, [Release("2.0.0"), Release("1.10.0"), Release("1.2.0"), Release("1.0.0")]);

        Assert.Equal("Amend MyMod 1.2.0-beta.1, 1.2.0, 1.10.0", three.Title);
        Assert.Equal(["1.10.0", "1.2.0", "1.2.0-beta.1"], three.Changed.Select(file => file.Version));
        Assert.Equal("Amend MyMod 1.0.0 to 2.0.0 (4 releases)", four.Title);
        Assert.Equal("Amend MyMod 1.2.0", Preview(Yank, [Release("1.2.0")]).Title);
    }

    [Fact]
    public void Body_OfAYank_NamesTheReasonAndTheCommandOfToolsAmend_AndMentionsTheOwner()
    {
        var body = Preview(Yank, [Release("1.2.0")], ["alice"]).Body;

        Assert.Equal(
            "Amends release 1.2.0 of `MyMod`.\n\nReason: The archive carries malware.\n\nThe same amendment with the tools of this repository:\n\n"
            + "```text\npython3 tools/amend.py --listing MyMod --version 1.2.0 --yank --reason 'The archive carries malware.'\n```\n\n@alice owns `MyMod`.",
            body);
    }

    [Fact]
    public void Body_OfBoundsUpToAVersion_SaysThatTheListingStatesThemSeparately_AndMentionsEveryOwner()
    {
        var change = new ReleaseChange
        {
            GameMax = "2026.8.19.5261",
            LoaderMax = "0.5.0",
            AddedDependencies = [new ReleaseDependencyAddition("BadMod", "conflict")],
            DependencyBounds = [new ReleaseDependencyBounds("BadMod", null, "1.2.0"), new ReleaseDependencyBounds("Lib", "2.1.0", null)],
        };
        var request = new ReleaseAmendmentRequest("MyMod", ReleaseSelection.UpTo(" 1.2.0 "), change, "It breaks on the new build.");

        var body = Preview(request, [Release("1.2.0"), Release("1.1.0"), Release("1.0.0")], ["alice", "bob"]).Body;

        Assert.Equal(
            "Amends 3 releases of `MyMod`: 1.0.0, 1.1.0, 1.2.0.\n\nReason: It breaks on the new build.\n\nThe same amendment with the tools of this repository:\n\n"
            + "```text\npython3 tools/amend.py --listing MyMod --up-to 1.2.0 --game-max 2026.8.19.5261 --loader-max 0.5.0 --dependency-min Lib=2.1.0 --dependency-max BadMod=1.2.0 --add-dependency BadMod:conflict\n```\n\n"
            + "The listing in content-index states its bounds, os and dependencies separately, so the next release is stamped without this change until the listing has it too.\n\n@alice @bob own `MyMod`.",
            body);
    }

    [Fact]
    public void Request_GivesAYankItsReason_QuotesForTheShell_AndNamesItsBranch()
    {
        var request = new ReleaseAmendmentRequest("My.Mod", ReleaseSelection.All, new ReleaseChange { Yank = true }, "It's broken; don't use $HOME.");
        var own = request with { Change = new ReleaseChange { Yank = true, YankReason = "Own." } };
        var bound = request with { Selection = ReleaseSelection.Of("1.0.0", "1.1.0"), Change = new ReleaseChange { GameMin = "2026.8" } };

        Assert.Equal("It's broken; don't use $HOME.", request.Amendment.YankReason);
        Assert.Equal("Own.", own.Amendment.YankReason);
        Assert.Null(bound.Amendment.YankReason);
        Assert.Equal(@"python3 tools/amend.py --listing My.Mod --all --yank --reason 'It'\''s broken; don'\''t use $HOME.'", request.Command);
        Assert.Equal("python3 tools/amend.py --listing My.Mod --version 1.0.0 --version 1.1.0 --game-min 2026.8", bound.Command);
        Assert.Equal("steward/amend-my.mod", request.Branch);
        Assert.False(request.ChangesListingFields);
    }

    [Fact]
    public void HasSameFiles_ComparesEveryTextBeforeAndAfter()
    {
        var preview = Preview(Yank, [Release("1.2.0"), Release("1.1.0", changed: false)]);

        Assert.True(preview.HasSameFiles(Preview(Yank, [Release("1.2.0"), Release("1.1.0", changed: false)], ["alice"])));
        Assert.False(preview.HasSameFiles(Preview(Yank, [Release("1.2.0"), Release("1.1.0")])));
        Assert.False(preview.HasSameFiles(Preview(Yank, [Release("1.2.0") with { Before = "{}\n" }, Release("1.1.0", changed: false)])));
        Assert.False(preview.HasSameFiles(Preview(Yank, [Release("1.2.0")])));
    }

    private static ReleaseAmendmentPreview Preview(ReleaseAmendmentRequest request, IReadOnlyList<ReleaseFilePreview> files, IReadOnlyList<string>? owners = null) =>
        new(request, files, owners ?? []);

    private static ReleaseFilePreview Release(string version, bool changed = true) =>
        new(version, $"releases/MyMod/{version}.json", $"{{\"version\": \"{version}\"}}\n", changed ? $"{{\"version\": \"{version}\", \"yanked\": true}}\n" : null);

    private static JsonObject Vector(string name) => Vectors.Single(vector => (string)vector!["name"]! == name)!.AsObject();
}
