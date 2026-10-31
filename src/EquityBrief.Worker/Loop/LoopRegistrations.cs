using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Improving;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Quality;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// From 21.5 the rule an approved change registers as its family's live rule on an index: the live rule standing there
// with the change made on top of it, its rule, test and evaluator the live rule's, named as the family's freeze names its
// live rule and the words of the hooks it then stands at after them; or why none is. A frozen swing rule on the S&P 400
// or 600 takes a change of its hooks alone, its setting moving only by a freeze; a frozen heavyweights book there takes
// its menu's sale under the 200-day average, the one dial of its menu its rule reads; the short-term dip takes a change
// of its hooks or of its setting on its sweep's grid; and Quality at a fair price and the Fundamentals family a change of
// their book's hooks or of their setting on their history test's grid.
// see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
// see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
public static class LoopRegistrations
{
    // The heavyweights' menu's sale under the 200-day average as a change states it to a frozen book on the S&P 400 or 600.
    public static string AverageExitKey { get; } = IndexHeavyweightCandidate.AverageExitParameter + "=1";

    // Why a change is not registered: it names a setting the family's rule does not read.
    public const string SettingUnread = "the change names a setting the family's rule does not hold";

    // Why a frozen heavyweights book's change is not registered: its menu's other changes are dials its rule does not read.
    public const string BookDialUnread = "a frozen heavyweights book reads its menu's sale under the 200-day average alone, so an approval of any other change to it is not applied";

    public static (Registration? Registration, string? Refused) Of(string index, RegisterRow live, LoopChange change)
    {
        var parameters = CandidateEvaluator.Read(live.Parameters);
        var evaluator = CandidateEvaluators.Find(live.Evaluator);

        if (evaluator is null)
        {
            return (null, $"the live rule runs on '{live.Evaluator}', which the code no longer carries");
        }

        var standingHooks = HooksOf(evaluator, parameters);
        var hooks = LoopChange.OfHooks(change.Hooks).OnTopOf(new LoopChange(null, null, null, standingHooks)).Hooks;

        switch (evaluator)
        {
            case IndexHeavyweightCandidate heavyweights:
            {
                if (change.Hooks.Count > 0 || change.Places is not null || change.Key != AverageExitKey)
                {
                    return (null, BookDialUnread);
                }

                var moved = new Dictionary<string, double>(parameters, StringComparer.Ordinal) { [IndexHeavyweightCandidate.AverageExitParameter] = 1 };

                return (Named(live, IndexRuleCandidate.NameOf(index) + " " + IndexRules.Words(heavyweights, moved), moved, LoopChange.NoHooks), null);
            }

            case IndexRuleCandidate own:
            {
                if (change.Places is not null || change.Key is not null || change.StopFloor is not null || change.Shape is { Count: > 0 })
                {
                    return (null, LoopDecisions.FrozenSettingRefused);
                }

                var setting = Without(parameters, evaluator);

                return (Named(live, IndexRuleCandidate.NameOf(index) + " " + IndexRules.Words(own, setting), setting, hooks), null);
            }

            case DipCandidate dip:
            {
                var setting = change.Places is { } places ? DipParameters(index, places) : Without(DipCandidate.Renamed(parameters), evaluator);

                if (setting is null)
                {
                    return (null, SettingUnread);
                }

                return (Named(live, OwnStepCandidate.NameOf(index) + " dip rule at " + DipRules.Short(dip.SettingOf(setting), index), setting, hooks), null);
            }

            case QualityCandidate:
            {
                // From 21.16 a setting of the grid with the sale under the 200-day average on, which the grid states none of.
                var setting = change.Key is { } key
                    ? QualityRule.Grid.Select(one => one.Key == key ? one : (one with { SoldUnderAverage = true }).Key == key ? one with { SoldUnderAverage = true } : null).FirstOrDefault(one => one is not null)
                    : QualityCandidate.SettingsOf(parameters);

                if (setting is null)
                {
                    return (null, SettingUnread);
                }

                return (Named(live, OwnStepCandidate.NameOf(index) + " quality rule at " + QualityRules.Short(setting), QualityCandidate.ParametersOf(setting), hooks), null);
            }

            case ImprovingCandidate:
            {
                var setting = change.Key is { } key
                    ? ImprovingGrid.Settings.Select(one => one.Key == key ? one : (one with { SoldUnderAverage = true }).Key == key ? one with { SoldUnderAverage = true } : null).FirstOrDefault(one => one is not null)
                    : ImprovingCandidate.SettingsOf(parameters);

                if (setting is null)
                {
                    return (null, SettingUnread);
                }

                return (Named(live, OwnStepCandidate.NameOf(index) + " Fundamentals rule at " + ImprovingRules.Short(setting), ImprovingCandidate.ParametersOf(setting), hooks), null);
            }

            default:
                return (null, LoopDecisions.FamilyRefused);
        }
    }

    // The hooks a rule stands at, as its evaluator reads them beside its own parameters: every parameter named as a hook
    // that is not one of the rule's own, the dip's sell dial read under its name first, since a dip rule registered before
    // 21.4 names it exit.
    public static IReadOnlyDictionary<string, double> HooksOf(CandidateEvaluator evaluator, IReadOnlyDictionary<string, double> parameters) =>
        (evaluator is DipCandidate ? DipCandidate.Renamed(parameters) : parameters)
            .Where(pair => RuleHooks.IsHook(pair.Key) && !evaluator.Parameters.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // A rule's own parameters, the ones it reads and the optional ones it states, its hooks left out.
    static Dictionary<string, double> Without(IReadOnlyDictionary<string, double> parameters, CandidateEvaluator evaluator) =>
        parameters
            .Where(pair => evaluator.Parameters.Contains(pair.Key, StringComparer.Ordinal) || !RuleHooks.IsHook(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    // The dip's parameters at the places its sweep's grid states a setting by, each dial's level at its place, the market
    // its place among the index's levels and the drop's volume read where the last place is 1; none where a place is off
    // the grid.
    public static Dictionary<string, double>? DipParameters(string index, IReadOnlyList<int> places)
    {
        var dials = DipRules.Dials(index);

        if (places.Count != dials.Count || places.Select((place, at) => place >= 0 && place < dials[at].Levels.Count).Any(on => !on))
        {
            return null;
        }

        return dials.Select((dial, at) => (dial.Parameter, Level: dial.Levels[places[at]])).ToDictionary(pair => pair.Parameter, pair => pair.Level, StringComparer.Ordinal);
    }

    // The places on the dip's grid its parameters state, the inverse of the parameters at a setting's places.
    public static IReadOnlyList<int> DipPlaces(string index, IReadOnlyDictionary<string, double> parameters)
    {
        var renamed = DipCandidate.Renamed(parameters);

        return [.. DipRules.Dials(index).Select(dial => dial.Levels.ToList().IndexOf(renamed[dial.Parameter]))];
    }

    // The registration: the family's live rule's name with the hooks' words after it, its rule, test and evaluator the
    // live rule's, and its parameters its own setting's with the hooks beside them.
    static Registration Named(RegisterRow live, string words, IReadOnlyDictionary<string, double> setting, IReadOnlyDictionary<string, double> hooks)
    {
        var hooked = RuleHooks.Of(hooks).Words();
        var parameters = setting.Concat(hooks).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new Registration(FamilyRecords.LivePrefix + words + (hooked is null ? string.Empty : "; " + hooked), live.Rule, live.Test, live.Evaluator, parameters);
    }

    // The words a place's level reads in, for a change's key.
    public static string KeyOf(string index, IReadOnlyList<int> places) =>
        string.Join("|", DipRules.Dials(index).Select((dial, at) => dial.Parameter + "=" + dial.Levels[places[at]].ToString("0.##", CultureInfo.InvariantCulture)));
}
