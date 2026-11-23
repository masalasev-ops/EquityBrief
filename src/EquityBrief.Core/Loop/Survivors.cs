using System.Globalization;
using EquityBrief.Core.Families;

namespace EquityBrief.Core.Loop;

// A variant of a setup as the room for a survivor reads it: its registered name, whether it is the setup's running revision,
// the nights it has run and its result per $1 risked over the nights it shares with the survivor's setup, none before any
// trade of it has closed.
public sealed record SurvivorSlot(string Name, bool Running, int Nights, double? PerDollar);

// What a survivor's admission comes to: registered in a slot the setup has free, registered in the place of the variant it
// retires, or queued and why.
public sealed record SurvivorAdmission(string Outcome, string? Retires, string Why);

// The survivors' rules: an idea the one tester passed is walked from that night by itself. Where its setup's variants can run
// every part of its line it registers as one of them, in a slot the setup has free, or in the place of the variant with the
// lowest result per $1 risked over the same nights that has run its minimum of nights and is not the running revision;
// where none can go, or where its setup cannot yet run a part of its line, it queues, walked each night as a record whose
// picks are drawn among no buys, and carries that record when it registers. Nothing here registers, approves or retires a
// running revision.
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
// see: A passed idea registers by itself as its setup's variant where it has room or a variant past its minimum makes room, and queues where none can go
public static class Survivors
{
    public const string Registered = "registered";

    public const string Queued = "queued";

    public const string Retired = "retired";

    // The nights a variant runs before it can be retired to make room, a quarter of the market's year.
    public const int MinimumNights = 63;

    // A setup holds its running revision and at most this many rules in all.
    public static int Room => Candidates.CandidateFamily.Maximum;

    // Why a setup's variants cannot yet run a line, none where they can: a variant buys what the setup's live setting passes,
    // sells on the setup's own time limit and lists the setup's own number a night in an order, until the one pin move
    // before the rule freeze ends teaches each price family a time limit and a cap a night of their own.
    public static string? CannotRunYet(IdeaLine line)
    {
        var limit = IdeaGrammar.FamilyTimeLimit(line.Family);

        // From 24.35 a filter's line is the S&P 500 swing filter at other dials, which its variant registers as its settings,
        // and the filter takes every member it fires on; the band strength it states none of.
        if (line.Filter is { } filter)
        {
            return line.Index != "GSPC" ? "its index's rule reads other gates beside the swing filter's dials, which its variants hold at the live rule's"
                : filter.Band != 0 ? "the swing filter states no band strength, and the line reads one"
                : double.IsPositiveInfinity(filter.DryUp) ? "the swing filter states a dry-up ceiling, and the line reads none"
                : line.Sessions != limit ? string.Create(CultureInfo.InvariantCulture, $"its setup sells on its own time limit of {limit} sessions, and the line sells after {line.Sessions}")
                : line.Cap is not null ? "the swing filter takes every member it fires on, and the line caps a night"
                : null;
        }

        if (line.Loose)
        {
            return "its setup's variants buy only what its live setting passes, and the line reads every setup its loose gates pass";
        }

        if (line.Sessions != limit)
        {
            return string.Create(CultureInfo.InvariantCulture, $"its setup sells on its own time limit of {limit} sessions until it is taught a limit of its own, and the line sells after {line.Sessions}");
        }

        return line.Cap is SetupFamilies.ListedANight && line.OrderBy is not null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"its setup lists {SetupFamilies.ListedANight} a night in an order until it is taught a cap of its own, and the line {(line.Cap is { } cap ? $"caps {cap} a night" : "buys every setup it keeps")}");
    }

    // The variant's registered settings: the running revision's own, each condition as a level its readings must meet, the
    // line's plan, and its order as a score of its one reading, highest first or lowest first. A band's lower edge is read
    // as the least figure above the early years' cut point under it, so the variant keeps what the tester kept.
    public static IReadOnlyDictionary<string, double> Parameters(IdeaLine line, IReadOnlyDictionary<string, double> running, IReadOnlyDictionary<string, double[]> cuts)
    {
        var settings = new Dictionary<string, double>(running, StringComparer.Ordinal);

        foreach (var condition in line.Conditions)
        {
            var levels = cuts[condition.Reading];

            if (condition.From > 1)
            {
                settings[RuleHooks.AlsoPrefix + condition.Reading + RuleHooks.AboveSuffix] = Math.BitIncrement(levels[condition.From - 2]);
            }

            if (condition.To < IdeaGrammar.Tenths)
            {
                settings[RuleHooks.AlsoPrefix + condition.Reading + RuleHooks.BelowSuffix] = levels[condition.To - 1];
            }
        }

        foreach (var (name, value) in PlanHook.ParametersOf(PlanOf(line)))
        {
            settings[name] = value;
        }

        // From 24.35 a filter's dials as the swing filter's settings, the market gate off where the breadth is.
        if (line.Filter is { } filter)
        {
            settings["strengthFloor"] = filter.Strength;
            settings["depthLow"] = filter.DepthLow;
            settings["depthHigh"] = filter.DepthHigh;
            settings["dryUpCeiling"] = filter.DryUp;
            settings["arrivalSessions"] = filter.Arrival;
            settings["rewardToRiskFloor"] = filter.Reward;
            settings["stopLow"] = filter.StopLow;
            settings["stopHigh"] = filter.StopHigh;

            if (double.IsNegativeInfinity(filter.Breadth))
            {
                settings[Candidates.SwingFilterRule.MarketGateParameter] = 0;
            }
            else
            {
                settings["breadthFloor"] = filter.Breadth;
            }
        }

        if (line.OrderBy is { } order)
        {
            settings[RuleHooks.ScorePrefix + order] = line.Highest ? 1 : -1;
        }

        return settings;
    }

    // The plan a line buys and sells by: its buy, its stop in typical days under the buy, sold on its time limit, taking
    // profit on the whole or on half at its multiple of the risk; or from 24.35 sold at the first band above the buy, the live
    // pullback rule's sale, falling back to its multiple of the risk where no band stands above, as that rule registers it:
    // its sale the first band and its take-profit the multiple of the risk alone.
    public static PlanCombination PlanOf(IdeaLine line)
    {
        var stop = PlanPoints.TypicalStop(line.StopDays);

        if (line.Shape == IdeaGrammar.FirstBand)
        {
            var band = new PlanCombination(line.Entry, line.Window, stop, PlanPoints.FirstBand);

            return line.Multiple is { } fallBack ? band.TakingAtRisk(fallBack) : band;
        }

        // From the operator's answers of 2026-11-23 half at the multiple and the rest at the first band or by the rule's sale,
        // as the S&P 400's and 600's live pullback rules register theirs.
        if (line.Shape is IdeaGrammar.HalfThenBand or IdeaGrammar.HalfThenRule)
        {
            return new PlanCombination(line.Entry, line.Window, stop, line.Shape == IdeaGrammar.HalfThenBand ? PlanPoints.FirstBand : PlanPoints.RuleSale).Halving(line.Multiple!.Value);
        }

        var plan = new PlanCombination(line.Entry, line.Window, stop, PlanPoints.TimeLimit);

        return line.Shape switch
        {
            IdeaGrammar.Whole => plan.TakingAtRisk(line.Multiple!.Value),
            IdeaGrammar.Half => plan.Halving(line.Multiple!.Value),
            _ => plan,
        };
    }

    // Where a survivor goes: queued where its setup cannot yet run its line; registered where the setup holds fewer than its
    // room; registered in the place of the variant with the lowest result per $1 risked over the same nights among those
    // past the minimum, never the running revision and never one with no closed trade, the oldest first where two tie; and
    // queued where none can go.
    public static SurvivorAdmission Admit(IdeaLine line, IReadOnlyList<SurvivorSlot> standing)
    {
        if (CannotRunYet(line) is { } why)
        {
            return new SurvivorAdmission(Queued, null, char.ToUpperInvariant(why[0]) + why[1..] + ".");
        }

        if (standing.Count < Room)
        {
            return new SurvivorAdmission(Registered, null, string.Create(CultureInfo.InvariantCulture, $"Its setup held {standing.Count} of its {Room}."));
        }

        var retirable = standing
            .Select((slot, at) => (slot, at))
            .Where(one => !one.slot.Running && one.slot.Nights >= MinimumNights && one.slot.PerDollar is not null)
            .OrderBy(one => one.slot.PerDollar)
            .ThenBy(one => one.at)
            .Select(one => one.slot)
            .FirstOrDefault();

        if (retirable is not null)
        {
            return new SurvivorAdmission(Registered, retirable.Name, string.Create(CultureInfo.InvariantCulture, $"Its setup held all {Room}; {retirable.Name} made room, the lowest per $1 risked of those past {MinimumNights} nights."));
        }

        var nearest = standing.Where(slot => !slot.Running && slot.Nights < MinimumNights).Select(slot => slot.Nights).DefaultIfEmpty(0).Max();

        return new SurvivorAdmission(Queued, null, string.Create(CultureInfo.InvariantCulture, $"All {Room} slots held; none can make room yet, the nearest having run {nearest} of the {MinimumNights} nights."));
    }

    // A setup's slots as the case page's row reads them: its running revision, then each variant with the nights it ran and
    // its result per $1 risked over the nights it shares with the running revision.
    public static IReadOnlyList<SurvivorSlot> SlotsOf(PromotionRowView row) =>
    [
        new SurvivorSlot(string.Empty, true, int.MaxValue, row.Running?.Figure),
        .. row.Variants.Select(variant => new SurvivorSlot(variant.Variant, false, variant.Sessions, variant.Same.Closed > 0 ? variant.Same.Figure : null)),
    ];

    // Whether a name a survivor's admission would write or retire is the setup's running revision's, which it never touches.
    public static bool TouchesRunning(string candidate, string running) =>
        string.Equals(candidate, running, StringComparison.Ordinal) || Candidates.FamilyRecords.IsLive(candidate);

    // A queued pick walked under its line from the night it was stored: bought at the next open, stopped its typical days
    // under the price it paid, taking profit as its line says and sold on its time limit, its result in the risk it took
    // before costs; none while its walk is still open, and none for a line buying at a level or selling at the first band,
    // which a queued pick is stored without.
    public static double? PickResult(IdeaLine line, double typicalMove, IReadOnlyList<(double Open, double Close)> fromTheNight)
    {
        if (line.Entry != PlanPoints.NextOpen || IdeaGrammar.SellsByPlace(line.Shape) || fromTheNight.Count < 2 || !(typicalMove > 0))
        {
            return null;
        }

        var closes = fromTheNight.Select(bar => bar.Close).ToArray();
        var opens = fromTheNight.Select(bar => bar.Open).ToArray();
        var fill = opens[1];
        var stop = fill - (line.StopDays * typicalMove);
        var sale = new PlanSale(null, null, []);

        if (line.Multiple is { } multiple)
        {
            sale = TakeProfit.Shaped(sale, fill, stop, _ => null, multiple, TakeProfitPlacing.Risk, line.Shape == IdeaGrammar.Half);
        }

        return PlanPoints.Walk(closes, opens, 1, fill, stop, sale, line.Sessions) is { } outcome ? (outcome.Sold - fill) / (fill - stop) : null;
    }

    // A survivor's record carried across its registration: the picks it was walked to while it queued and the trades its
    // variant closed after, one record, its closed count and their mean per $1 risked, each closed result weighing alike.
    public static (int Closed, double? PerDollar) Record(IReadOnlyList<double?> queued, int registeredClosed, double? registeredPerDollar)
    {
        var walked = queued.OfType<double>().ToArray();
        var after = registeredPerDollar is not null ? registeredClosed : 0;
        var closed = walked.Length + after;

        return (closed, closed == 0 ? null : (walked.Sum() + (after * (registeredPerDollar ?? 0))) / closed);
    }
}
