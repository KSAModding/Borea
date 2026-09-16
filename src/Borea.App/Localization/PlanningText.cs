using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Borea.Core.Dependencies;
using Borea.Core.Game;
using Borea.Core.Mods;
using Borea.Core.Planning;

namespace Borea.App.Localization;

/// <summary>
/// The planner's messages and choices in the display language.
/// </summary>
internal static class PlanningText
{
    public static string Message(PlanningMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var dependency = Dependency(message.Dependency);
        return message.Kind switch
        {
            PlanningMessageKind.ForeignOwned => Resources.InstallMessageForeignOwned,
            PlanningMessageKind.ExactPinConflict => Fill(Resources.InstallMessageExactPinConflictFormat, message.Version, message.OtherVersion),
            PlanningMessageKind.OutsideChannel => Resources.InstallMessageOutsideChannel,
            PlanningMessageKind.MissingRequest => Resources.InstallMessageMissingRequest,
            PlanningMessageKind.ExactPin => Fill(Resources.InstallMessageExactPinFormat, message.Version),
            PlanningMessageKind.ProposedConflict => Fill(Resources.InstallMessageProposedConflictFormat, dependency),
            PlanningMessageKind.Yanked => WithReason(Resources.InstallMessageYanked, message.Value),
            PlanningMessageKind.YankedPin => WithReason(Resources.InstallMessageYankedPin, message.Value),
            PlanningMessageKind.ReleaseChannel => Fill(Resources.InstallReleaseChannelFormat, message.Version, StatusText(message.Status)),
            PlanningMessageKind.Incompatible => Fill(Resources.InstallMessageIncompatibleFormat, message.Value),
            PlanningMessageKind.Compatibility => message.Compatibility == GameCompatibility.Untested ? Resources.InstallMessageUntested : Resources.InstallMessageCompatibilityUnknown,
            PlanningMessageKind.Platform => Fill(Resources.InstallMessagePlatformFormat, PlatformName(message.Platform)),
            PlanningMessageKind.PlatformUnknown => Fill(Resources.InstallMessagePlatformUnknownFormat, message.Value),
            PlanningMessageKind.UnknownDependency => Fill(Resources.InstallMessageUnknownDependencyFormat, dependency),
            PlanningMessageKind.DependencyConflict => Fill(Resources.InstallMessageDependencyConflictFormat, dependency),
            PlanningMessageKind.ForeignConflictUnknown => Fill(Resources.InstallMessageForeignConflictUnknownFormat, dependency),
            PlanningMessageKind.ForeignConflict => Fill(Resources.InstallMessageForeignConflictFormat, dependency),
            PlanningMessageKind.ForeignVersionUnknown => Fill(Resources.InstallMessageForeignVersionUnknownFormat, dependency),
            PlanningMessageKind.UnsatisfiedDependency => Fill(Resources.InstallMessageUnsatisfiedDependencyFormat, dependency),
            PlanningMessageKind.AlternativeChoice => Fill(Resources.InstallMessageAlternativeChoiceFormat, string.Join(", ", Alternatives(message.Dependency))),
            PlanningMessageKind.InvalidAlternative => Fill(Resources.InstallMessageInvalidAlternativeFormat, message.Value),
            PlanningMessageKind.ForeignAlternativeUnknown => Fill(Resources.InstallMessageForeignVersionUnknownFormat, Alternative(message)),
            PlanningMessageKind.UnsatisfiedAlternative => Fill(Resources.InstallMessageUnsatisfiedDependencyFormat, Alternative(message)),
            PlanningMessageKind.RetainedAlternative => Fill(Resources.InstallMessageRetainedAlternativeFormat, dependency),
            PlanningMessageKind.RetainedDependent => Fill(Resources.InstallMessageRetainedDependentFormat, dependency),
            PlanningMessageKind.RetainedConflict => Fill(Resources.InstallMessageRetainedConflictFormat, dependency),
            PlanningMessageKind.RetainedUnsatisfied => Fill(Resources.InstallMessageRetainedUnsatisfiedFormat, dependency),
            PlanningMessageKind.SearchLimit => Fill(Resources.InstallMessageSearchLimitFormat, message.Limit ?? 0),
            PlanningMessageKind.RetractedPack => WithReason(Resources.InstallMessageRetractedPack, message.Value),
            PlanningMessageKind.UnlistedPin => Resources.InstallMessageUnlistedPin,
            _ => message.Message,
        };
    }

    /// <summary>"library >= 1.0.0", or "first or second" for an any_of entry.</summary>
    public static string Dependency(ModDependency? dependency)
    {
        if (dependency is null)
            return string.Empty;

        return dependency.IsAnyOf
            ? OneOf(Alternatives(dependency))
            : Bounds(dependency.ModId, dependency.MinVersion, dependency.MaxVersion);
    }

    public static string OneOf(IReadOnlyList<string> values)
        => values.Count < 2
            ? string.Concat(values)
            : Fill(Resources.InstallDependencyAlternativesFormat, string.Join(", ", values.Take(values.Count - 1)), values[^1]);

    public static string RecommendedFor(string dependency, string owner)
        => Fill(Resources.InstallChoiceForFormat, dependency, owner);

    public static string AlternativesFor(string owner)
        => Fill(Resources.InstallChoiceAlternativesFormat, owner);

    private static List<string> Alternatives(ModDependency? dependency)
        => dependency?.AnyOf?.Select(value => Bounds(value.ModId, value.MinVersion, value.MaxVersion)).ToList() ?? [];

    private static string Alternative(PlanningMessage message)
    {
        if (message.Value is null)
            return Dependency(message.Dependency);

        var alternative = message.Dependency?.AnyOf?.FirstOrDefault(value => ModIds.Equals(value.ModId, message.Value));
        return alternative is null ? message.Value : Bounds(alternative.ModId, alternative.MinVersion, alternative.MaxVersion);
    }

    private static string Bounds(string modId, ModVersion? min, ModVersion? max) => (min, max) switch
    {
        (null, null) => modId,
        ({ } low, null) => $"{modId} >= {low}",
        (null, { } high) => $"{modId} <= {high}",
        ({ } low, { } high) => $"{modId} {low} - {high}",
    };

    private static string StatusText(ReleaseStatus? status) => status switch
    {
        ReleaseStatus.Stable => Resources.ReleaseStable,
        ReleaseStatus.Testing => Resources.ReleaseTesting,
        ReleaseStatus.Dev => Resources.ReleaseDev,
        _ => Resources.ReleaseUnknown,
    };

    private static string PlatformName(OsPlatform? platform) => platform switch
    {
        OsPlatform.Windows => "Windows",
        OsPlatform.Linux => "Linux",
        OsPlatform.MacOs => "macOS",
        _ => string.Empty,
    };

    private static string WithReason(string text, string? reason)
        => string.IsNullOrWhiteSpace(reason) ? text : $"{text} {reason}";

    private static string Fill(string format, params object?[] values)
        => string.Format(CultureInfo.CurrentCulture, format, values);
}
