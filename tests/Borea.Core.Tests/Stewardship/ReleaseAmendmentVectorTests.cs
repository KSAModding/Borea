using System.Text;
using System.Text.Json.Nodes;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

/// <summary>Every case of tools/amendment-vectors.json of content-index-releases, as README.md of that repository says a client runs them.</summary>
public sealed class ReleaseAmendmentVectorTests
{
    private static readonly JsonObject File = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "amendment-vectors.json")))!.AsObject();

    private static readonly IReadOnlyList<string> GameVersions = [.. File["game_versions"]!.AsArray().Select(version => (string)version!)];

    /// <summary>The refusal that Borea gives for the reason of each rejected vector. A reason from the invariant must also be in the details, which use its words.</summary>
    private static readonly Dictionary<string, (ReleaseAmendmentRefusal Refusal, bool InDetails)> Refusals = new(StringComparer.Ordinal)
    {
        ["nothing to amend"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["says nothing without"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["game_max is empty"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["is neither a game version nor a month"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["not over"] = (ReleaseAmendmentRefusal.MonthNotOver, false),
        ["names a month with no build"] = (ReleaseAmendmentRefusal.UnknownMonth, false),
        ["states no loader"] = (ReleaseAmendmentRefusal.NoLoader, false),
        ["does not parse"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["states no dependency on 'Nothing'"] = (ReleaseAmendmentRefusal.NoDependency, false),
        ["is not ID=VERSION"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["is not ID:KIND"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["is not one of"] = (ReleaseAmendmentRefusal.InvalidChange, false),
        ["carries no game_min_revision"] = (ReleaseAmendmentRefusal.NotStamperFile, true),
        ["so the compatibility range is empty"] = (ReleaseAmendmentRefusal.OutsideClass, true),
        ["game_min_revision falls from 5117 to 5018"] = (ReleaseAmendmentRefusal.Widens, true),
        ["game_max_revision rises from 5261 to 5348"] = (ReleaseAmendmentRefusal.Widens, true),
        ["the loader lowers its min from '0.4.5' to '0.4.0'"] = (ReleaseAmendmentRefusal.Widens, true),
        ["the loader raises its max from '0.4.9' to '0.5.0'"] = (ReleaseAmendmentRefusal.Widens, true),
        ["the loader ends up with max '0.4.0' below min '0.4.5'"] = (ReleaseAmendmentRefusal.OutsideClass, true),
        ["lowers its min from '2.0.0' to '1.0.0'"] = (ReleaseAmendmentRefusal.Widens, true),
        ["raises its max from '2.9.0' to '3.0.0'"] = (ReleaseAmendmentRefusal.Widens, true),
        ["ends up with max '1.0.0' below min '2.0.0'"] = (ReleaseAmendmentRefusal.OutsideClass, true),
        ["carries 'downloads', which a release file does not have"] = (ReleaseAmendmentRefusal.OutsideClass, true),
    };

    public static TheoryData<string> Names => [.. Vectors().Select(vector => (string)vector["name"]!)];

    [Fact]
    public void TheFile_HasEveryVector()
    {
        Assert.Equal(55, Vectors().Select(vector => (string)vector["name"]!).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Vector_GivesTheVerdictAndTheBytesOfToolsAmend(string name)
    {
        var vector = Vectors().Single(candidate => (string)candidate["name"]! == name);
        var amender = (string)vector["actor"]! switch
        {
            "owner" => ReleaseAmender.Owner,
            "steward" => ReleaseAmender.Steward,
            var actor => throw new InvalidOperationException($"The vector names the actor '{actor}', which this test does not know."),
        };
        var options = vector["amendment"]!.AsObject();

        switch ((string)vector["verdict"]!)
        {
            case "accepted":
                var amended = Run(vector, options, amender);
                Assert.NotNull(amended);
                Assert.Equal(Encoding.UTF8.GetBytes((string)vector["written"]!), Encoding.UTF8.GetBytes(amended.Text));
                if (amender == ReleaseAmender.Owner)
                    AssertWidensOnlyWhereAStewardIsRefused(vector, options, amended);
                else
                    Assert.False(amended.Widens);
                break;
            case "unchanged":
                Assert.Null(Run(vector, options, amender));
                break;
            case "rejected":
                var reason = (string)vector["reason"]!;
                Assert.True(Refusals.TryGetValue(reason, out var expected), $"Map the reason '{reason}' of the vector '{name}' to a refusal.");
                var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => Run(vector, options, amender));
                Assert.Equal(expected.Refusal, refused.Refusal);
                if (expected.InDetails)
                    Assert.Contains(refused.Details, detail => detail.Contains(reason, StringComparison.Ordinal));
                break;
            case var verdict:
                Assert.Fail($"The vector '{name}' has the verdict '{verdict}', which this test does not know.");
                break;
        }
    }

    /// <summary>Each widening of RFC 0079 that the file gives once for a steward alone and once for the owner, by the steward and the owner vector.</summary>
    public static TheoryData<string, string> Widenings
    {
        get
        {
            var pairs = new TheoryData<string, string>();
            foreach (var (steward, owner) in WideningPairs().Where(pair => pair.Owner is not null))
                pairs.Add(steward, owner!);
            return pairs;
        }
    }

    [Fact]
    public void TheFile_HasAnOwnerVectorForEveryWideningThatAStewardAloneIsRefused()
    {
        var pairs = WideningPairs();

        Assert.All(pairs, pair => Assert.True(pair.Owner is not null, $"The widening '{pair.Steward}' has no owner vector with the same base and amendment."));
        Assert.True(pairs.Count >= 6, $"The file has {pairs.Count} widenings, and it had 6.");
    }

    /// <summary>
    /// Each steward refusal whose reason is a widening, with the one owner vector of the same base and amendment, or null when there is none.
    /// A refusal for any other reason has no owner twin and is left out.
    /// </summary>
    private static List<(string Steward, string? Owner)> WideningPairs()
    {
        string[] widens = ["widens", "rises", "falls", "lowers", "raises"];
        return
        [
            .. Vectors()
                .Where(vector => (string)vector["actor"]! == "steward" && (string)vector["verdict"]! == "rejected"
                    && widens.Any(word => ((string)vector["reason"]!).Contains(word, StringComparison.Ordinal)))
                .Select(steward => ((string)steward["name"]!, Vectors()
                    .Where(candidate => (string)candidate["actor"]! == "owner" && (string)candidate["verdict"]! == "accepted"
                        && (string)candidate["base"]! == (string)steward["base"]! && JsonNode.DeepEquals(candidate["amendment"], steward["amendment"]))
                    .Select(owner => (string?)owner["name"])
                    .ToList() is [var single] ? single : null)),
        ];
    }

    /// <summary>A steward who names the author's request widens as the owner does, and the same amendment without the request is refused.</summary>
    [Theory]
    [MemberData(nameof(Widenings))]
    public void Widening_OnTheAuthorsRequest_GivesTheBytesOfTheOwner_AndWithoutTheRequestIsRefused(string steward, string owner)
    {
        var refusedVector = Vectors().Single(vector => (string)vector["name"]! == steward);
        var acceptedVector = Vectors().Single(vector => (string)vector["name"]! == owner);
        var published = JsonNode.Parse((string)acceptedVector["base"]!)!;
        var change = ChangeOf(acceptedVector["amendment"]!.AsObject());
        var alone = new ReleaseAmendmentRequest((string)published["id"]!, ReleaseSelection.Of((string)published["version"]!), change, "The author asks for it.");
        var onRequest = alone with { AuthorRequest = "https://github.com/KSAModding/content-index/issues/42#issuecomment-7" };

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => Run(refusedVector, refusedVector["amendment"]!.AsObject(), alone.Amender));
        var amended = Run(acceptedVector, acceptedVector["amendment"]!.AsObject(), onRequest.Amender);

        Assert.Equal((ReleaseAmender.Steward, ReleaseAmender.Owner), (alone.Amender, onRequest.Amender));
        Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
        Assert.Contains(refused.Details, detail => detail.Contains((string)refusedVector["reason"]!, StringComparison.Ordinal));
        Assert.NotNull(amended);
        Assert.True(amended.Widens);
        Assert.Equal(Encoding.UTF8.GetBytes((string)acceptedVector["written"]!), Encoding.UTF8.GetBytes(amended.Text));
        Assert.DoesNotContain("--owner", alone.Command, StringComparison.Ordinal);
        Assert.Contains($"--version {published["version"]} --owner --", onRequest.Command, StringComparison.Ordinal);
    }

    /// <summary>An owner's amendment widens exactly when a steward alone may not make it.</summary>
    private static void AssertWidensOnlyWhereAStewardIsRefused(JsonObject vector, JsonObject options, AmendedRelease amended)
    {
        try
        {
            var byStewards = Run(vector, options, ReleaseAmender.Steward);
            Assert.False(amended.Widens);
            Assert.Equal(amended, byStewards);
        }
        catch (ReleaseAmendmentRefusedException refused)
        {
            Assert.Equal(ReleaseAmendmentRefusal.Widens, refused.Refusal);
            Assert.True(amended.Widens);
        }
    }

    private static AmendedRelease? Run(JsonObject vector, JsonObject options, ReleaseAmender amender)
    {
        var text = (string)vector["base"]!;
        var published = JsonNode.Parse(text)!;
        var id = (string)published["id"]!;
        var version = (string)published["version"]!;

        var amendment = ReleaseAmendment.Create(ChangeOf(options), GameVersions, DateTimeOffset.UtcNow);
        Assert.Equal([version], ReleaseAmendment.Select([version], ReleaseSelection.Of(version)));
        return amendment.Apply(ReleaseAmendment.PathOf(id, version), text, amender);
    }

    /// <summary>
    /// The options of tools/amend.py as a change. A pair is split at its first separator the way the tool splits it, and
    /// a pair without one gives an empty half, which Borea refuses as the tool refuses the pair.
    /// </summary>
    private static ReleaseChange ChangeOf(JsonObject options)
    {
        var known = new[] { "game-min", "game-max", "yank", "reason", "loader-min", "loader-max", "dependency-min", "dependency-max", "add-dependency" };
        Assert.All(options, option => Assert.Contains(option.Key, known));

        var minimums = Pairs(options["dependency-min"], '=');
        var maximums = Pairs(options["dependency-max"], '=');
        return new ReleaseChange
        {
            GameMin = (string?)options["game-min"],
            GameMax = (string?)options["game-max"],
            Yank = options["yank"] is not null && (bool)options["yank"]!,
            YankReason = (string?)options["reason"],
            LoaderMin = (string?)options["loader-min"],
            LoaderMax = (string?)options["loader-max"],
            AddedDependencies = [.. Split(options["add-dependency"], ':').Select(pair => new ReleaseDependencyAddition(pair.Key, pair.Value))],
            DependencyBounds =
            [
                .. minimums.Keys.Concat(maximums.Keys.Where(id => !minimums.ContainsKey(id)))
                    .Select(id => new ReleaseDependencyBounds(id, minimums.GetValueOrDefault(id), maximums.GetValueOrDefault(id))),
            ],
        };
    }

    /// <summary>The pairs by their trimmed first half, a later one replacing an earlier one in its place, as a Python dict does.</summary>
    private static OrderedDictionary<string, string> Pairs(JsonNode? values, char separator)
    {
        var pairs = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in Split(values, separator))
            pairs[key] = value;
        return pairs;
    }

    private static IEnumerable<KeyValuePair<string, string>> Split(JsonNode? values, char separator) =>
        (values?.AsArray() ?? []).Select(item => (string)item!).Select(value => value.IndexOf(separator, StringComparison.Ordinal) is var at && at < 0
            ? KeyValuePair.Create(value.Trim(), string.Empty)
            : KeyValuePair.Create(value[..at].Trim(), value[(at + 1)..]));

    private static IEnumerable<JsonObject> Vectors() => File["vectors"]!.AsArray().Select(vector => vector!.AsObject());
}
