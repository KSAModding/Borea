using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Borea.App.Localization;
using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Borea.App.ViewModels;

/// <summary>What a plan asks before it installs.</summary>
public sealed partial class InstallChoices : ViewModelBase
{
    private readonly Func<string, string> _name;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _namedModIds = new(ModIds.Comparer);

    /// <param name="name">The display name of a mod id.</param>
    internal InstallChoices(Guid instanceId, IReadOnlyList<RequestedMod> requested, Func<string, string> name)
    {
        InstanceId = instanceId;
        Requested = requested;
        _name = name;
    }

    internal Guid InstanceId { get; }

    internal IReadOnlyList<RequestedMod> Requested { get; }

    public ObservableCollection<RecommendedChoice> Recommended { get; } = [];

    public ObservableCollection<string> Suggested { get; } = [];

    public ObservableCollection<AlternativeChoice> Alternatives { get; } = [];

    public bool HasRecommended => Recommended.Count > 0;

    public bool HasSuggested => Suggested.Count > 0;

    /// <summary>Every group that applies has one mod selected.</summary>
    public bool IsComplete => Alternatives.All(group => !group.IsRequired || group.Selected is not null);

    /// <summary>Why the plan with these choices cannot run, or null.</summary>
    [ObservableProperty]
    private string? _blockedText;

    internal IReadOnlySet<string> SelectedRecommendations => Recommended.Where(choice => choice.IsSelected).Select(choice => choice.Key).ToHashSet(StringComparer.Ordinal);

    internal IReadOnlySet<string> DeselectedRecommendations => Recommended.Where(choice => !choice.IsSelected).Select(choice => choice.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The mods that the recommendations and the alternatives name.</summary>
    internal IReadOnlySet<string> NamedModIds => _namedModIds;

    internal IReadOnlyDictionary<string, string> SelectedAlternatives => Alternatives
        .Where(group => group.IsRequired && group.Selected is not null)
        .ToDictionary(group => group.Key, group => group.Selected!.ModId, StringComparer.Ordinal);

    /// <summary>Whether the plan asks something, and for a plan that cannot run, whether a choice can change that.</summary>
    internal static bool AreNeeded(InstallPlan plan)
        => plan.Choices.Any(choice => choice.Kind switch
        {
            PlanningChoiceKind.Alternative => choice.Selected is null,
            PlanningChoiceKind.Suggestion => plan.IsReady,
            _ => true,
        });

    /// <summary>Shows the choices of a new plan, keeps what the user selected before, and returns whether the plan asks something new.</summary>
    internal bool Apply(InstallPlan plan)
    {
        var shown = plan.Choices
            .Where(choice => _keys.Contains(choice.Key) || choice.Kind != PlanningChoiceKind.Alternative || choice.Selected is null)
            .ToList();
        var added = shown.Any(choice => !_keys.Contains(choice.Key));

        var recommended = shown
            .Where(choice => choice.Kind == PlanningChoiceKind.Recommendation)
            .Select(choice => Recommended.FirstOrDefault(item => item.Key == choice.Key)
                ?? new RecommendedChoice(this, choice.Key, Describe(choice), choice.Selected == "include"))
            .ToList();
        var alternatives = shown
            .Where(choice => choice.Kind == PlanningChoiceKind.Alternative)
            .Select(choice =>
            {
                var group = Alternatives.FirstOrDefault(item => item.Key == choice.Key) ?? new AlternativeChoice(this, choice, _name);
                var recommendation = shown.FirstOrDefault(other => other.Kind == PlanningChoiceKind.Recommendation && ReferenceEquals(other.Dependency, choice.Dependency));
                group.Recommendation = recommended.FirstOrDefault(item => item.Key == recommendation?.Key);
                return group;
            })
            .ToList();

        Replace(Recommended, recommended);
        Replace(Alternatives, alternatives);
        Replace(Suggested, shown.Where(choice => choice.Kind == PlanningChoiceKind.Suggestion).Select(Describe).ToList());
        _keys.Clear();
        _keys.UnionWith(shown.Select(choice => choice.Key));
        _namedModIds.Clear();
        _namedModIds.UnionWith(shown.Where(choice => choice.Kind != PlanningChoiceKind.Suggestion).SelectMany(choice => ModIdsOf(choice.Dependency)));
        OnPropertyChanged(nameof(HasRecommended));
        OnPropertyChanged(nameof(HasSuggested));
        Refresh();
        return added;
    }

    internal void Refresh()
    {
        foreach (var group in Alternatives)
            group.Refresh();
        OnPropertyChanged(nameof(IsComplete));
    }

    private string Describe(PlanningChoice choice)
    {
        var dependency = choice.Dependency;
        var names = dependency.IsAnyOf ? dependency.AnyOf.Select(value => _name(value.ModId)).ToList() : [_name(dependency.ModId)];
        return PlanningText.RecommendedFor(PlanningText.OneOf(names), _name(choice.OwnerModId));
    }

    private static IEnumerable<string> ModIdsOf(ModDependency dependency)
        => dependency.IsAnyOf ? dependency.AnyOf.Select(value => value.ModId) : [dependency.ModId!];

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        if (target.SequenceEqual(values))
            return;

        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}

public sealed partial class RecommendedChoice : ObservableObject
{
    private readonly InstallChoices _owner;

    internal RecommendedChoice(InstallChoices owner, string key, string text, bool isSelected)
    {
        _owner = owner;
        Key = key;
        Text = text;
        _isSelected = isSelected;
    }

    internal string Key { get; }

    public string Text { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _owner.Refresh();
}

/// <summary>
/// An any_of group of a required dependency, or of a selected recommendation.
/// </summary>
public sealed class AlternativeChoice : ObservableObject
{
    private readonly InstallChoices _owner;

    internal AlternativeChoice(InstallChoices owner, PlanningChoice choice, Func<string, string> name)
    {
        _owner = owner;
        Key = choice.Key;
        Text = PlanningText.AlternativesFor(name(choice.OwnerModId));
        Options = choice.Options.Select(modId => new AlternativeOption(this, modId, name(modId), ModIds.Equals(modId, choice.Selected))).ToList();
    }

    internal string Key { get; }

    public string Text { get; }

    public IReadOnlyList<AlternativeOption> Options { get; }

    /// <summary>The recommendation the group belongs to, or null for a required dependency.</summary>
    internal RecommendedChoice? Recommendation { get; set; }

    public bool IsRequired => Recommendation?.IsSelected ?? true;

    public AlternativeOption? Selected => Options.FirstOrDefault(option => option.IsSelected);

    internal void OnOptionChanged(AlternativeOption option)
    {
        if (option.IsSelected)
        {
            foreach (var other in Options.Where(other => !ReferenceEquals(other, option)))
                other.IsSelected = false;
        }

        OnPropertyChanged(nameof(Selected));
        _owner.Refresh();
    }

    internal void Refresh() => OnPropertyChanged(nameof(IsRequired));
}

public sealed partial class AlternativeOption : ObservableObject
{
    private readonly AlternativeChoice _group;

    internal AlternativeOption(AlternativeChoice group, string modId, string name, bool isSelected)
    {
        _group = group;
        ModId = modId;
        Name = name;
        _isSelected = isSelected;
    }

    internal string ModId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _group.OnOptionChanged(this);
}
