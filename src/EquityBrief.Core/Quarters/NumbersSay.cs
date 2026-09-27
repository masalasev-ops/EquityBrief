using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Quarters;

// "What the numbers say": one sentence per reading, written by the code from fixed patterns with the
// stored figures filled in, and a heading carrying the state.
//
// The patterns are section 4's table, held to it word for word in both directions, and a sentence the
// page draws matches one of them. Each describes the business and none advises buying or selling. The
// heading says the state is read from sales and operating margin alone, so a reader seeing improving
// beside earnings that ran ahead of cash sees why both can be true.
// see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
// see: Code owns every number
public static partial class NumbersSay
{
    // The patterns, each under the name the table's row gives it. A slot in braces is filled from the
    // stored reading and nothing else.
    public static IReadOnlyList<(string Name, string Pattern)> Patterns { get; } =
    [
        ("heading", "What the numbers say: {state}, read from sales and operating margin alone"),
        ("awaited", "The quarter to {quarter} is awaited, so these read the quarter to {quarter}."),
        ("sales", "Sales {change} on a year earlier in the quarter to {quarter}, and {change} in the quarter to {quarter}."),
        ("sales rose", "rose {percent}"),
        ("sales fell", "fell {percent}"),
        ("sales held", "held level"),
        ("pace faster", "The newer quarter's growth was faster than the {percent} the same quarter showed a year before."),
        ("pace slower", "The newer quarter's growth was slower than the {percent} the same quarter showed a year before."),
        ("pace same", "The newer quarter's growth matched the {percent} the same quarter showed a year before."),
        ("margin wider", "The operating margin was {percent} against {percent} a year earlier, wider for {count} quarter(s) in a row."),
        ("margin narrower", "The operating margin was {percent} against {percent} a year earlier, narrower for {count} quarter(s) in a row."),
        ("margin unchanged", "The operating margin was {percent}, where it stood a year earlier."),
        ("trajectory too few", "Sales and the operating margin cannot be read against a year earlier over the two newest quarters."),
        ("record", "Against the analysts' estimates over its last {count} quarters it beat {count}, met {count} and missed {count}, met meaning within a cent or 1% of the estimate."),
        ("record too few", "{count} of the {count} quarters a record needs carry both an actual and an estimate, so no record is stated."),
        ("quality ahead", "Operating cash flow over the last four quarters was {ratio} times net income: earnings ran ahead of cash."),
        ("quality in line", "Operating cash flow over the last four quarters was {ratio} times net income: in line."),
        ("quality backed", "Operating cash flow over the last four quarters was {ratio} times net income: more than backed by cash."),
        ("quality loss", "Net income over the last four quarters was a loss, so cash is not compared against it."),
        ("quality missing", "The last four quarters do not all carry operating cash flow and net income, so earnings quality is not read."),
        ("valuation cheap", "At tonight's close it trades at {multiple} times its last four quarters' earnings, the cheap end of the {multiple} to {multiple} range of its last {count} quarters."),
        ("valuation middle", "At tonight's close it trades at {multiple} times its last four quarters' earnings, the middle of the {multiple} to {multiple} range of its last {count} quarters."),
        ("valuation expensive", "At tonight's close it trades at {multiple} times its last four quarters' earnings, the expensive end of the {multiple} to {multiple} range of its last {count} quarters."),
        ("valuation too few", "{count} of the {count} quarters a range needs carry a multiple, so the valuation position is not read."),
        ("valuation no earnings", "Its last four quarters' earnings are not above nought, so no multiple is read."),
        ("valuation no basis", "Tonight's close cannot be set on the price basis the quarters were fetched on, so no multiple is read."),
        ("none", "No reported quarter is stored for it yet, so nothing is read."),
    ];

    // The heading a state is drawn under.
    public static string Heading(string state) => Fill("heading", state);

    // The sentences one night's readings give, in the order the block draws them: the quarter awaited
    // where one is, then the trajectory, the record, the quality and the valuation.
    public static IReadOnlyList<string> Sentences(Readings readings)
    {
        if (readings.State == FundamentalState.NoFundamentalsYet)
        {
            return [Fill("none")];
        }

        var said = new List<string>();

        if (readings.Awaited is { } awaited && readings.ReadFrom is { } from)
        {
            said.Add(Fill("awaited", Date(awaited), Date(from)));
        }

        said.AddRange(Trajectory(readings.Trajectory));
        said.Add(Record(readings.Record));
        said.Add(Quality(readings.Quality));
        said.Add(Valuation(readings.Valuation));

        return said;
    }

    static IReadOnlyList<string> Trajectory(TrajectoryReading trajectory)
    {
        if (trajectory.Absent is not null || trajectory.Quarters.Count < QuarterReadings.TrajectoryQuarters)
        {
            return [Fill("trajectory too few")];
        }

        var (newer, older) = (trajectory.Quarters[0], trajectory.Quarters[1]);
        var said = new List<string>
        {
            Fill("sales", Change(newer.SalesGrowth), Date(newer.Quarter), Change(older.SalesGrowth), Date(older.Quarter)),
        };

        if (newer.SalesGrowthBefore is { } before)
        {
            said.Add(Fill(
                newer.SalesGrowth > before ? "pace faster" : newer.SalesGrowth < before ? "pace slower" : "pace same",
                Signed(before)));
        }

        said.Add(newer.Margin == newer.MarginYearEarlier
            ? Fill("margin unchanged", Percent(newer.Margin))
            : Fill(
                newer.Margin > newer.MarginYearEarlier ? "margin wider" : "margin narrower",
                Percent(newer.Margin),
                Percent(newer.MarginYearEarlier),
                Count(trajectory.MarginRun)));

        return said;
    }

    static string Record(RecordReading record) =>
        record.Absent is not null
            ? Fill("record too few", Count(record.Quarters.Count), Count(QuarterReadings.RecordMinimum))
            : Fill("record", Count(record.Quarters.Count), Count(record.Beat), Count(record.Met), Count(record.Missed));

    static string Quality(QualityReading quality) =>
        quality.Absent switch
        {
            QuarterReadings.Loss => Fill("quality loss"),
            not null => Fill("quality missing"),
            _ => Fill(
                quality.Band switch
                {
                    QuarterReadings.AheadOfCash => "quality ahead",
                    QuarterReadings.BackedByCash => "quality backed",
                    _ => "quality in line",
                },
                Ratio(quality.Ratio!.Value)),
        };

    static string Valuation(ValuationReading valuation) =>
        valuation.Absent switch
        {
            QuarterReadings.TooFew => Fill("valuation too few", Count(valuation.Quarters.Count), Count(QuarterReadings.ValuationMinimum)),
            QuarterReadings.NoEarnings => Fill("valuation no earnings"),
            QuarterReadings.NoBasis => Fill("valuation no basis"),
            not null => Fill("valuation too few", Count(valuation.Quarters.Count), Count(QuarterReadings.ValuationMinimum)),
            _ => Fill(
                valuation.Position switch
                {
                    QuarterReadings.CheapEnd => "valuation cheap",
                    QuarterReadings.ExpensiveEnd => "valuation expensive",
                    _ => "valuation middle",
                },
                Multiple(valuation.Multiple!.Value),
                Multiple(valuation.Low!.Value),
                Multiple(valuation.High!.Value),
                Count(valuation.Quarters.Count)),
        };

    // A pattern with its slots filled in the order they stand, each slot taking the next value.
    static string Fill(string name, params string[] values)
    {
        var pattern = Patterns.Single(entry => entry.Name == name).Pattern;
        var at = 0;

        return Slot().Replace(pattern, _ => at < values.Length
            ? values[at++]
            : throw new InvalidOperationException($"The pattern '{name}' has more slots than the {values.Length} value(s) it was given."));
    }

    // A sentence the page drew, matched against every pattern with each slot read as any value, so a
    // check can say which pattern a sentence came from and that it came from one.
    public static string? PatternOf(string sentence) =>
        Patterns
            .Where(entry => entry.Name is not ("sales rose" or "sales fell" or "sales held"))
            .FirstOrDefault(entry => Regex.IsMatch(sentence, "^" + Slot().Replace(Regex.Escape(entry.Pattern), ".+?") + "$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Name;

    [GeneratedRegex(@"\\?\{[a-z]+\\?\}|\{[a-z]+\}")]
    private static partial Regex Slot();

    static string Change(decimal growth) =>
        growth > 0m ? Fill("sales rose", Percent(growth)) : growth < 0m ? Fill("sales fell", Percent(-growth)) : Fill("sales held");

    static string Percent(decimal share) => (share * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    static string Signed(decimal share) => (share >= 0m ? "+" : string.Empty) + Percent(share);

    static string Ratio(decimal ratio) => ratio.ToString("0.00", CultureInfo.InvariantCulture);

    static string Multiple(decimal multiple) => multiple.ToString("0.0", CultureInfo.InvariantCulture);

    static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
