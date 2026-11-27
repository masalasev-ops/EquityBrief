using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Loop;

// One condition of an idea: a ledger reading held to a band of its tenths, read over the early years alone. The tenths run
// 1 to 10, lowest first; "above tenth n" keeps tenths n + 1 to 10, "below tenth n" keeps 1 to n - 1, and "between tenth a
// and b" keeps a to b, so each condition is one band of tenths on one reading.
public sealed record IdeaCondition(string Reading, int From, int To)
{
    public string Line => From == 1 ? FormattableString.Invariant($"if {Reading} below tenth {To + 1}")
        : To == IdeaGrammar.Tenths ? FormattableString.Invariant($"if {Reading} above tenth {From - 1}")
        : FormattableString.Invariant($"if {Reading} between tenth {From} and {To}");

    public string Words => From == 1 ? FormattableString.Invariant($"{Reading} in its lowest {To} tenths")
        : To == IdeaGrammar.Tenths ? FormattableString.Invariant($"{Reading} in its highest {IdeaGrammar.Tenths - From + 1} tenths")
        : FormattableString.Invariant($"{Reading} between its {Ordinal(From)} and {Ordinal(To)} tenths");

    static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => n.ToString(CultureInfo.InvariantCulture) + "th",
    };
}

// An idea in the grammar's closed parts: the entry event on an index, every setup the family's loose gates pass or only
// those its live setting passes; at most three conditions; the buy; the stop in typical days under the buy; the take-profit
// shape at a multiple of the risk; the time limit in sessions; and where it buys at most a number a night, the reading it
// orders them by.
public sealed record IdeaLine(
    string Family,
    string Index,
    bool Loose,
    IReadOnlyList<IdeaCondition> Conditions,
    string Entry,
    int Window,
    double StopDays,
    string Shape,
    double? Multiple,
    int Sessions,
    int? Cap,
    string? OrderBy,
    bool Highest)
{
    // From 24.35 the swing filter's dials a pullback's line keeps its setups by, none where it names none.
    public IdeaFilter? Filter { get; init; }

    // The one spelling of the idea, its parts in the grammar's order and its conditions by reading, so two spellings of
    // one idea read as the same line.
    public string Canonical => string.Join(IdeaGrammar.Separator, Parts());

    IEnumerable<string> Parts()
    {
        yield return FormattableString.Invariant($"event {Family}({IdeaGrammar.WordOf(Index)}){(Loose ? " loose" : string.Empty)}");

        if (Filter is { } filter)
        {
            yield return filter.Part;
        }

        foreach (var condition in Conditions.OrderBy(one => one.Reading, StringComparer.Ordinal))
        {
            yield return condition.Line;
        }

        yield return Entry == PlanPoints.NextOpen ? "buy next open"
            : PlanPoints.DistanceOf(Entry) is { } distance ? FormattableString.Invariant($"buy {IdeaGrammar.Number(distance)} typical days under the close within {Window} sessions")
            : FormattableString.Invariant($"buy at {Entry} within {Window} sessions");
        yield return "stop " + IdeaGrammar.Number(StopDays) + " typical days";
        yield return Shape switch
        {
            IdeaGrammar.TimeOnly => "take profit none",
            IdeaGrammar.FirstBand => Multiple is { } fallBack ? FormattableString.Invariant($"take profit first band else {IdeaGrammar.Number(fallBack)} risks") : "take profit first band",
            IdeaGrammar.HalfThenBand => FormattableString.Invariant($"take profit half at {IdeaGrammar.Number(Multiple!.Value)} risks then first band"),
            IdeaGrammar.HalfThenRule => FormattableString.Invariant($"take profit half at {IdeaGrammar.Number(Multiple!.Value)} risks then rule sale"),
            _ => FormattableString.Invariant($"take profit {Shape} at {IdeaGrammar.Number(Multiple!.Value)} risks"),
        };
        yield return FormattableString.Invariant($"sell after {Sessions} sessions");

        if (Cap is { } cap)
        {
            yield return FormattableString.Invariant($"cap {cap} a night by {OrderBy} {(Highest ? "highest" : "lowest")}");
        }
    }

    // The idea in plain words.
    public string Words
    {
        get
        {
            var family = IdeaGrammar.FamilyWords(Family);
            var only = Conditions.Count == 0 ? string.Empty : ", only where " + string.Join(" and ", Conditions.OrderBy(one => one.Reading, StringComparer.Ordinal).Select(one => one.Words));
            var buy = Entry == PlanPoints.NextOpen ? "bought at the next open"
                : PlanPoints.DistanceOf(Entry) is { } distance ? FormattableString.Invariant($"bought {IdeaGrammar.Number(distance)} typical daily moves under the close, waiting up to {Window} sessions")
                : FormattableString.Invariant($"bought at {Entry} within {Window} sessions");
            var stop = FormattableString.Invariant($"a stop {IdeaGrammar.Number(StopDays)} typical {(StopDays == 1 ? "day" : "days")} under the buy");
            var profit = Shape switch
            {
                IdeaGrammar.Whole => FormattableString.Invariant($"the whole sold at {IdeaGrammar.Number(Multiple!.Value)} times the risk"),
                IdeaGrammar.Half => FormattableString.Invariant($"half sold at {IdeaGrammar.Number(Multiple!.Value)} times the risk and the rest held to the time limit"),
                IdeaGrammar.FirstBand => "the whole sold at the first price band above the buy where it turned down before"
                    + (Multiple is { } fallBack ? FormattableString.Invariant($", or at {IdeaGrammar.Number(fallBack)} times the risk where no band stands above") : string.Empty),
                IdeaGrammar.HalfThenBand => FormattableString.Invariant($"half sold at {IdeaGrammar.Number(Multiple!.Value)} times the risk and the rest at the first price band above the buy where it turned down before"),
                IdeaGrammar.HalfThenRule => FormattableString.Invariant($"half sold at {IdeaGrammar.Number(Multiple!.Value)} times the risk and the rest by the rule's own sale"),
                _ => "no take-profit",
            };
            var cap = Cap is { } most ? FormattableString.Invariant($", at most {most} a night, {(Highest ? "highest" : "lowest")} {OrderBy} first") : string.Empty;

            var filtered = Filter is { } filter ? ", where its swing filter reads " + filter.Words : string.Empty;

            return FormattableString.Invariant($"{family}, {IdeaGrammar.IndexWords(Index)}{(Loose ? ", every setup its loose gates pass" : string.Empty)}{filtered}{only}. {char.ToUpperInvariant(buy[0])}{buy[1..]}, {stop}, {profit}, sold after {Sessions} sessions{cap}.");
        }
    }
}

// The grammar every idea is written in, closed so code builds and tests any line it parses with no build session: each part
// from a list the code already walks, a reading from the ledger's catalogue, a level a band of the reading's tenths. A line
// outside it is refused with the part it could not read.
// see: An idea is one line of a closed grammar that code parses into one canonical line and tests with no build session
public static class IdeaGrammar
{
    public const string Separator = " · ";

    public const int Tenths = 10;

    public const int MostConditions = 3;

    public const string Whole = "whole";

    public const string Half = "half";

    public const string TimeOnly = "none";

    // The live pullback rule's sale: the whole at the first price band above the buy where the stock turned down before, read
    // on the night it was picked, and where it names a multiple, at that multiple of the risk where no band stands above.
    public const string FirstBand = "first band";

    // From the operator's answers of 2026-11-23 the S&P 400's and 600's live pullback rules' sales: half at a multiple of the
    // risk, the stop on the rest then moved up to the buy and the rest sold at the first band above, the S&P 400's, or by
    // the rule's own sale, the S&P 600's.
    // see: Each S&P 400 and 600 pullback setup stores whether its live rule's other gates pass it, as the night read them
    public const string HalfThenBand = "half then first band";

    public const string HalfThenRule = "half then rule sale";

    // Whether a line sells by a band or the rule's own sale, which a queued pick is stored without.
    public static bool SellsByPlace(string shape) => shape is FirstBand or HalfThenBand or HalfThenRule;

    // The distances a buy under the close may name, in typical daily moves: every hundredth from a quarter to three, so the
    // distance each live rule registered can be named.
    public static IReadOnlyList<double> Distances { get; } = [.. Enumerable.Range(25, 276).Select(hundredths => hundredths / 100.0)];

    // The families an idea's event names, each a family whose setups the long history holds.
    public static IReadOnlyList<string> Families { get; } = PlanPoints.Families;

    public static IReadOnlyList<string> Indices { get; } = ["500", "400", "600"];

    public static IReadOnlyList<double> StopDays { get; } = [.. PlanPoints.TypicalDays.Concat(PlanPoints.WiderDays)];

    public static IReadOnlyList<double> Multiples { get; } = [1.5, 2, 3];

    // The time limits a line may name: the grammar's own and each family's own, so a line can always name the limit its
    // family runs today.
    public static IReadOnlyList<int> SessionLimits { get; } = [.. new[] { 5, 10, 20, 40, 63 }.Concat(Families.Select(FamilyTimeLimit)).Distinct().Order()];

    // The time limit each family's live rule runs today, which a survivor's variant holds its trades at where its line
    // states no other.
    public static int FamilyTimeLimit(string family) => family switch
    {
        "pullback" => EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap,
        "breakout" => EquityBrief.Core.Families.BreakoutRule.CapSessions,
        "drift" => EquityBrief.Core.Families.DriftRule.CapSessions,
        _ => EquityBrief.Core.Families.DipRule.Provisional.TimeLimit,
    };

    public static IReadOnlyList<int> Caps { get; } = [1, 3, 5, 10];

    public static IReadOnlyList<string> Entries { get; } = [.. PlanPoints.Zones.Append(PlanPoints.Average)];

    public static IReadOnlyList<int> Windows => PlanPoints.Windows;

    public static IReadOnlyList<string> Readings { get; } = [.. LedgerReadings.All.Select(reading => reading.Column)];

    public static string CodeOf(string word) => word switch
    {
        "500" => "GSPC",
        "400" => "MID",
        _ => "SML",
    };

    public static string WordOf(string code) => code switch
    {
        "GSPC" => "500",
        "MID" => "400",
        _ => "600",
    };

    public static string IndexWords(string code) => "S&P " + WordOf(code);

    public static string FamilyWords(string family) => family switch
    {
        "pullback" => RuleWording.SetupFamilies.PullbacksHeading,
        "breakout" => RuleWording.SetupFamilies.BreakoutsHeading,
        "drift" => "Earnings drift",
        _ => RuleWording.SetupFamilies.DipsHeading,
    };

    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // A line read into the grammar's parts, or the reason it is refused. Its parts may come in any order, separated by the
    // middle dot or a semicolon, in any case and spacing, a number written as 1.5 or 1.50, a day or days, a session or sessions.
    public static (IdeaLine? Line, string? Refused) Parse(string text)
    {
        var parts = text.Replace(';', '·').Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => string.Join(' ', part.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(part => part.Length > 0)
            .ToArray();

        string? family = null, index = null, entry = null, shape = null, orderBy = null;
        bool loose = false, highest = false;
        int window = 0, sessions = 0;
        int? cap = null;
        double? stop = null, multiple = null;
        IdeaFilter? filter = null;
        var conditions = new List<IdeaCondition>();

        foreach (var part in parts)
        {
            var words = part.Split(' ');

            switch (words[0])
            {
                case "filter" when filter is null:
                    if (IdeaFilter.Parse(part) is not { } read)
                    {
                        return (null, $"the filter names the swing filter's eight dials in order, each at a level the sweep's grid holds: \"{part}\"");
                    }

                    filter = read;
                    break;

                case "event" when family is null:
                {
                    var named = words.Length > 1 ? words[1] : string.Empty;
                    var open = named.IndexOf('(', StringComparison.Ordinal);

                    if (open < 0 || !named.EndsWith(')') || !Families.Contains(named[..open]) || !Indices.Contains(named[(open + 1)..^1])
                        || words.Length > 3 || (words.Length == 3 && words[2] != "loose"))
                    {
                        return (null, $"the event is not one of {string.Join(", ", Families)} on 500, 400 or 600: \"{part}\"");
                    }

                    (family, index, loose) = (named[..open], CodeOf(named[(open + 1)..^1]), words.Length == 3);
                    break;
                }

                case "if":
                {
                    if (ConditionOf(words) is not { } condition)
                    {
                        return (null, $"a condition is a catalogue reading above, below or between tenths 1 to {Tenths}: \"{part}\"");
                    }

                    if (conditions.Any(one => one.Reading == condition.Reading))
                    {
                        return (null, $"two conditions read {condition.Reading}; a band of its tenths is one condition: \"{part}\"");
                    }

                    conditions.Add(condition);
                    break;
                }

                case "buy" when entry is null:
                    if (part == "buy next open")
                    {
                        entry = PlanPoints.NextOpen;
                    }
                    else if (words.Length > 4 && words[1] == "at" && words[^1] is "session" or "sessions" && words[^3] == "within"
                        && Entries.Contains(string.Join(' ', words[2..^3])) && int.TryParse(words[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var within) && Windows.Contains(within))
                    {
                        (entry, window) = (string.Join(' ', words[2..^3]), within);
                    }
                    else if (words.Length == 10 && words[2] == "typical" && words[3] is "day" or "days" && words[4] == "under" && words[5] == "the" && words[6] == "close"
                        && words[7] == "within" && words[9] is "session" or "sessions" && DistanceIn(words[1]) is { } distance
                        && int.TryParse(words[8], NumberStyles.None, CultureInfo.InvariantCulture, out var waiting) && Windows.Contains(waiting))
                    {
                        (entry, window) = (PlanPoints.DistanceEntry(distance), waiting);
                    }
                    else
                    {
                        return (null, $"the buy is the next open, a zone or the average within 5, 10 or 20 sessions, or a quarter to three typical days under the close within 5, 10 or 20 sessions: \"{part}\"");
                    }

                    break;

                case "stop" when stop is null:
                    if (words.Length == 4 && words[2] == "typical" && words[3] is "day" or "days" && NumberOf(words[1]) is { } days && StopDays.Contains(days))
                    {
                        stop = days;
                    }
                    else
                    {
                        return (null, $"the stop is {string.Join(", ", StopDays.Select(Number))} typical days: \"{part}\"");
                    }

                    break;

                case "take" when shape is null:
                    if (part == "take profit none")
                    {
                        shape = TimeOnly;
                    }
                    else if (words.Length == 6 && part.StartsWith("take profit ", StringComparison.Ordinal) && words[2] is Whole or Half && words[3] == "at" && words[5] is "risk" or "risks"
                        && NumberOf(words[4]) is { } times && Multiples.Contains(times))
                    {
                        (shape, multiple) = (words[2], times);
                    }
                    else if (part == "take profit first band")
                    {
                        shape = FirstBand;
                    }
                    else if (words.Length == 7 && part.StartsWith("take profit first band else ", StringComparison.Ordinal) && words[6] is "risk" or "risks"
                        && NumberOf(words[5]) is { } fallBack && Multiples.Contains(fallBack))
                    {
                        (shape, multiple) = (FirstBand, fallBack);
                    }
                    else if (words.Length is 9 or 10 && part.StartsWith("take profit half at ", StringComparison.Ordinal) && words[5] is "risk" or "risks" && words[6] == "then"
                        && NumberOf(words[4]) is { } first && Multiples.Contains(first)
                        && string.Join(' ', words[7..]) is ("first band" or "rule sale") and var rest)
                    {
                        (shape, multiple) = (rest == "first band" ? HalfThenBand : HalfThenRule, first);
                    }
                    else
                    {
                        return (null, $"the take-profit is none, the whole or half at {string.Join(", ", Multiples.Select(Number))} risks, the first band, else one of those risks, or half at one of those risks then the first band or the rule's sale: \"{part}\"");
                    }

                    break;

                case "sell" when sessions == 0:
                    if (words.Length == 4 && words[1] == "after" && words[3] is "session" or "sessions"
                        && int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && SessionLimits.Contains(limit))
                    {
                        sessions = limit;
                    }
                    else
                    {
                        return (null, $"the time limit is {string.Join(", ", SessionLimits)} sessions: \"{part}\"");
                    }

                    break;

                case "cap" when cap is null:
                    if (words.Length == 7 && words[2] == "a" && words[3] == "night" && words[4] == "by" && Readings.Contains(words[5]) && words[6] is "highest" or "lowest"
                        && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var most) && Caps.Contains(most))
                    {
                        (cap, orderBy, highest) = (most, words[5], words[6] == "highest");
                    }
                    else
                    {
                        return (null, $"the cap is {string.Join(", ", Caps)} a night by a catalogue reading, highest or lowest: \"{part}\"");
                    }

                    break;

                default:
                    return (null, $"no part of the grammar reads \"{part}\", or it is named twice");
            }
        }

        if (conditions.Count > MostConditions)
        {
            return (null, $"{conditions.Count} conditions; an idea holds at most {MostConditions}");
        }

        string? missing = family is null ? "the event" : entry is null ? "the buy" : stop is null ? "the stop" : shape is null ? "the take-profit" : sessions == 0 ? "the time limit" : null;

        if (missing is null && filter is not null && (family != "pullback" || !loose))
        {
            return (null, "a filter reads the swing filter's dials over every pullback its loose gates pass, so it names the event pullback and loose");
        }

        return missing is not null
            ? (null, $"the line names no {missing[4..]}")
            : (new IdeaLine(family!, index!, loose, [.. conditions.OrderBy(one => one.Reading, StringComparer.Ordinal)], entry!, window, stop!.Value, shape!, multiple, sessions, cap, orderBy, highest) { Filter = filter }, null);
    }

    static IdeaCondition? ConditionOf(string[] words)
    {
        if (words.Length < 5 || !Readings.Contains(words[1]) || words[3] != "tenth")
        {
            return null;
        }

        int Tenth(string word) => int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var tenth) ? tenth : 0;

        return words[2] switch
        {
            "above" when words.Length == 5 && Tenth(words[4]) is >= 1 and < Tenths and var n => new IdeaCondition(words[1], n + 1, Tenths),
            "below" when words.Length == 5 && Tenth(words[4]) is > 1 and <= Tenths and var n => new IdeaCondition(words[1], 1, n - 1),
            "between" when words.Length == 7 && words[5] == "and" && Tenth(words[4]) is >= 1 and var a && Tenth(words[6]) is <= Tenths and var b && a < b && !(a == 1 && b == Tenths)
                => new IdeaCondition(words[1], a, b),
            _ => null,
        };
    }

    static double? NumberOf(string word) =>
        double.TryParse(word, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

    // A distance the grammar's list holds, read to the hundredth it names.
    static double? DistanceIn(string word) =>
        NumberOf(word) is { } value && Distances.FirstOrDefault(held => Math.Abs(held - value) < 1e-9) is var distance && distance > 0 ? distance : null;

    // The cut points between a reading's tenths over the early years' values: the nine values below which each of the
    // first nine tenths' share of them lies; none where fewer than ten values are read.
    public static double[]? CutsOf(IEnumerable<double> earlyValues)
    {
        var sorted = earlyValues.Where(double.IsFinite).Order().ToArray();

        return sorted.Length < Tenths
            ? null
            : [.. Enumerable.Range(1, Tenths - 1).Select(k => sorted[(int)Math.Ceiling(k * sorted.Length * 1.0 / Tenths) - 1])];
    }

    // The tenth a value stands in against the early years' cut points: 1 at or under the first, 10 above the ninth.
    public static int TenthOf(double value, IReadOnlyList<double> cuts)
    {
        var tenth = 1;

        while (tenth < Tenths && value > cuts[tenth - 1])
        {
            tenth++;
        }

        return tenth;
    }

    // Whether a setup's reading holds the condition's band; a reading it does not hold holds none.
    public static bool Holds(IdeaCondition condition, double? value, IReadOnlyList<double> cuts) =>
        value is { } held && double.IsFinite(held) && TenthOf(held, cuts) is var tenth && tenth >= condition.From && tenth <= condition.To;
}
