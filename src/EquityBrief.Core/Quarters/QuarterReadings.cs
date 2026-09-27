using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityBrief.Core.Quarters;

// The state a member's reported quarters give it, one word a night.
//
// Read from the trajectory alone: sales and the operating margin, each against the same quarter a
// year earlier, in both of the two newest quarters. The record against the estimates, the earnings
// quality and the valuation position are shown beside it and never move it, so a business can read
// improving beside earnings that ran ahead of cash.
// see: Four readings of a member's reported quarters are worked out every night, and its state is read from sales and operating margin alone
public static class FundamentalState
{
    public const string Improving = "improving";

    public const string Steady = "steady";

    public const string Deteriorating = "deteriorating";

    public const string NotEnoughQuarters = "not enough quarters";

    public const string NoFundamentalsYet = "no fundamentals yet";

    public static IReadOnlyList<string> All { get; } = [Improving, Steady, Deteriorating, NotEnoughQuarters, NoFundamentalsYet];

    // Where a state stands in the order tonight's list is drawn in among the members that pass:
    // improving first, then steady, then the two that read nothing, then deteriorating. A state the
    // night read none of, which is every night before the readings existed, stands with the two that
    // read nothing, and the order within a state is the filter's own.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    public static int Place(string? state) => state switch
    {
        Improving => 0,
        Steady => 1,
        Deteriorating => 3,
        _ => 2,
    };

    // The same place over a column holding a state, for a reader that orders in its query, written from
    // the one rule above so the two cannot come to order differently.
    public static string PlaceIn(string column) =>
        $"CASE {column} WHEN '{Improving}' THEN {Place(Improving)} WHEN '{Steady}' THEN {Place(Steady)} WHEN '{Deteriorating}' THEN {Place(Deteriorating)} ELSE {Place(null)} END";
}

// The trajectory's two quarters, newest first: sales against the same quarter a year earlier and that
// quarter's own growth on the year before it, and the operating margin against a year earlier.
public sealed record TrajectoryQuarter(DateOnly Quarter, decimal SalesGrowth, decimal? SalesGrowthBefore, decimal Margin, decimal MarginYearEarlier);

// The trajectory, with how many quarters in a row, from the newest, the margin stood on the newest
// quarter's side of a year earlier, and what was absent where it could not be read.
public sealed record TrajectoryReading(IReadOnlyList<TrajectoryQuarter> Quarters, int MarginRun, string? Absent);

// The record against the analysts' estimates over the quarters counted, newest first.
public sealed record RecordReading(IReadOnlyList<DateOnly> Quarters, int Beat, int Met, int Missed, string? Absent);

// Earnings quality over the four newest quarters: operating cash flow and net income summed, their
// ratio and its band.
public sealed record QualityReading(IReadOnlyList<DateOnly> Quarters, decimal? OperatingCashFlow, decimal? NetIncome, decimal? Ratio, string? Band, string? Absent);

// The valuation position: tonight's multiple, the range of the quarters' own multiples it is read
// against, and where in that range it sits.
public sealed record ValuationReading(IReadOnlyList<DateOnly> Quarters, decimal? Multiple, decimal? Low, decimal? High, string? Position, string? Absent);

// One member's readings on one night: the state, the newest quarter they were read from, a newer
// quarter awaited where one is, and the four readings.
public sealed record Readings(
    string State,
    DateOnly? ReadFrom,
    DateOnly? Awaited,
    TrajectoryReading Trajectory,
    RecordReading Record,
    QualityReading Quality,
    ValuationReading Valuation)
{
    // The stored form: money and ratios as text, the storage form a money value takes, so nothing is
    // written in a form a machine with a comma for a decimal point could not read back.
    static readonly JsonSerializerOptions Stored = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Stored);

    public static Readings FromJson(string json) =>
        JsonSerializer.Deserialize<Readings>(json, Stored)
            ?? throw new FormatException("A stored reading holds no object, and a row the night wrote always holds one.");
}

// The four readings of one fetch's quarters, and the state, worked out every night by arithmetic over
// what the store holds and no model.
//
// Every figure is proposed and settled from the members' measured split once every member holds
// quarters or is marked absent.
// owes: The fundamental state rule settled from the members' measured split
// see: Four readings of a member's reported quarters are worked out every night, and its state is read from sales and operating margin alone
// see: Code owns every number
public static class QuarterReadings
{
    // The trajectory reads the two newest quarters, each against the same quarter a year earlier: two
    // so one quarter's one-off cannot move the word, and a year earlier so the seasons fall out.
    public const int TrajectoryQuarters = 2;

    // The record counts the eight newest quarters carrying both an actual and an estimate, and is
    // stated only where four or more do.
    public const int RecordQuarters = 8;

    public const int RecordMinimum = 4;

    // An actual within a cent, or within this share of the estimate where that is larger, met it.
    // A cent is the smallest gap a per-share figure filed to the cent can show, and the share scales
    // the tolerance for a company earning several dollars a share.
    public const decimal MetCents = 0.01m;

    public const decimal MetShare = 0.01m;

    // Earnings quality reads the four newest quarters, a year, and names a gap of a fifth or more
    // either way: below the first, earnings ran ahead of cash; above the second, they were more than
    // backed by it.
    public const int QualityQuarters = 4;

    public const decimal QualityLow = 0.8m;

    public const decimal QualityHigh = 1.2m;

    // The valuation position reads the kept quarters' own multiples and is stated where eight or more
    // carry one, tonight's multiple placed in the thirds of their range.
    public const int ValuationMinimum = 8;

    // Why a reading could not be read, as the stored reading names it.
    public const string TooFew = "too few quarters";

    public const string Incomplete = "a quarter lacks a figure";

    public const string Loss = "a loss";

    public const string NoEarnings = "earnings not above nought";

    public const string NoBasis = "no close on the fetch's basis";

    public const string NoQuarter = "no quarter stored";

    // The three bands of earnings quality and the three places in the range, as a reading names them.
    public const string AheadOfCash = "ran ahead of cash";

    public const string InLine = "in line";

    public const string BackedByCash = "more than backed by cash";

    public const string CheapEnd = "the cheap end";

    public const string Middle = "the middle";

    public const string ExpensiveEnd = "the expensive end";

    // The readings of one fetch's quarters, tonight's close already on that fetch's basis, and the
    // quarter awaited where a newer one is.
    public static Readings Of(IReadOnlyList<ReportedQuarter> quarters, decimal? closeOnBasis, DateOnly? awaited)
    {
        var ordered = quarters.OrderByDescending(quarter => quarter.PeriodEnd).ToArray();

        if (ordered.Length == 0)
        {
            return new Readings(
                FundamentalState.NoFundamentalsYet,
                null,
                awaited,
                new TrajectoryReading([], 0, NoQuarter),
                new RecordReading([], 0, 0, 0, NoQuarter),
                new QualityReading([], null, null, null, null, NoQuarter),
                new ValuationReading([], null, null, null, null, NoQuarter));
        }

        var trajectory = Trajectory(ordered);

        return new Readings(
            StateOf(trajectory),
            ordered[0].PeriodEnd,
            awaited,
            trajectory,
            Record(ordered),
            Quality(ordered),
            Valuation(ordered, closeOnBasis));
    }

    // The trajectory over the two newest quarters, which must each hold sales and an operating margin
    // against a year earlier.
    public static TrajectoryReading Trajectory(IReadOnlyList<ReportedQuarter> ordered)
    {
        var two = ordered.Take(TrajectoryQuarters).ToArray();

        if (two.Length < TrajectoryQuarters || two.Any(quarter => quarter.SalesGrowth is null || quarter.OperatingMargin is null || quarter.MarginYearEarlier is null))
        {
            return new TrajectoryReading([], 0, TooFew);
        }

        var side = Math.Sign(two[0].OperatingMargin!.Value - two[0].MarginYearEarlier!.Value);

        var run = side == 0
            ? 0
            : ordered
                .TakeWhile(quarter => quarter.OperatingMargin is { } margin && quarter.MarginYearEarlier is { } earlier && Math.Sign(margin - earlier) == side)
                .Count();

        return new TrajectoryReading(
            [
                .. two.Select(quarter => new TrajectoryQuarter(
                    quarter.PeriodEnd,
                    quarter.SalesGrowth!.Value,
                    quarter.SalesGrowthBefore,
                    quarter.OperatingMargin!.Value,
                    quarter.MarginYearEarlier!.Value)),
            ],
            run,
            null);
    }

    // Improving where sales stood above a year earlier and the margin wider in both quarters,
    // deteriorating where sales stood below and the margin narrower in both, steady otherwise, and not
    // enough quarters where the trajectory could not be read.
    public static string StateOf(TrajectoryReading trajectory)
    {
        if (trajectory.Absent is not null || trajectory.Quarters.Count < TrajectoryQuarters)
        {
            return FundamentalState.NotEnoughQuarters;
        }

        if (trajectory.Quarters.All(quarter => quarter.SalesGrowth > 0m && quarter.Margin > quarter.MarginYearEarlier))
        {
            return FundamentalState.Improving;
        }

        return trajectory.Quarters.All(quarter => quarter.SalesGrowth < 0m && quarter.Margin < quarter.MarginYearEarlier)
            ? FundamentalState.Deteriorating
            : FundamentalState.Steady;
    }

    // The record over the newest quarters carrying both an actual and an estimate. A quarter with no
    // actual has not reported and is never read as one that met its estimate.
    public static RecordReading Record(IReadOnlyList<ReportedQuarter> ordered)
    {
        var counted = ordered.Where(quarter => quarter.EpsActual is not null && quarter.EpsEstimate is not null).Take(RecordQuarters).ToArray();

        if (counted.Length < RecordMinimum)
        {
            return new RecordReading([.. counted.Select(quarter => quarter.PeriodEnd)], 0, 0, 0, TooFew);
        }

        var (beat, met, missed) = (0, 0, 0);

        foreach (var quarter in counted)
        {
            switch (Against(quarter.EpsActual!.Value, quarter.EpsEstimate!.Value))
            {
                case > 0:
                    beat++;
                    break;
                case < 0:
                    missed++;
                    break;
                default:
                    met++;
                    break;
            }
        }

        return new RecordReading([.. counted.Select(quarter => quarter.PeriodEnd)], beat, met, missed, null);
    }

    // One quarter against its estimate: nought where it met it, within the tolerance either way, and
    // the direction it cleared the tolerance in otherwise.
    public static int Against(decimal actual, decimal estimate)
    {
        var tolerance = Math.Max(MetCents, Math.Abs(estimate) * MetShare);
        var gap = actual - estimate;

        return Math.Abs(gap) <= tolerance ? 0 : Math.Sign(gap);
    }

    // Earnings quality over the four newest quarters, each carrying both figures. A loss over the year
    // is named and not compared, since cash against a loss says nothing a ratio could.
    public static QualityReading Quality(IReadOnlyList<ReportedQuarter> ordered)
    {
        var four = ordered.Take(QualityQuarters).ToArray();
        IReadOnlyList<DateOnly> read = [.. four.Select(quarter => quarter.PeriodEnd)];

        if (four.Length < QualityQuarters || four.Any(quarter => quarter.OperatingCashFlow is null || quarter.NetIncome is null))
        {
            return new QualityReading(read, null, null, null, null, four.Length < QualityQuarters ? TooFew : Incomplete);
        }

        var cash = four.Sum(quarter => quarter.OperatingCashFlow!.Value);
        var income = four.Sum(quarter => quarter.NetIncome!.Value);

        if (income <= 0m)
        {
            return new QualityReading(read, cash, income, null, null, Loss);
        }

        var ratio = decimal.Round(cash / income, 6, MidpointRounding.ToEven);

        return new QualityReading(read, cash, income, ratio, BandOf(ratio), null);
    }

    // The band a ratio falls in, the two cut points in the middle band.
    public static string BandOf(decimal ratio) =>
        ratio < QualityLow ? AheadOfCash : ratio > QualityHigh ? BackedByCash : InLine;

    // The valuation position: each kept quarter's multiple is the close after its report over its four
    // quarters' earnings, both from the one fetch, and tonight's multiple is tonight's close on that
    // fetch's basis over the newest quarter's four quarters' earnings.
    public static ValuationReading Valuation(IReadOnlyList<ReportedQuarter> ordered, decimal? closeOnBasis)
    {
        var multiples = ordered
            .Where(quarter => quarter.CloseAfter is not null && quarter.EpsTrailing is > 0m)
            .Select(quarter => (quarter.PeriodEnd, Multiple: decimal.Round(quarter.CloseAfter!.Value / quarter.EpsTrailing!.Value, 6, MidpointRounding.ToEven)))
            .ToArray();

        IReadOnlyList<DateOnly> read = [.. multiples.Select(pair => pair.PeriodEnd)];

        if (multiples.Length < ValuationMinimum)
        {
            return new ValuationReading(read, null, null, null, null, TooFew);
        }

        if (ordered[0].EpsTrailing is not > 0m)
        {
            return new ValuationReading(read, null, null, null, null, NoEarnings);
        }

        if (closeOnBasis is not { } close)
        {
            return new ValuationReading(read, null, null, null, null, NoBasis);
        }

        var tonight = decimal.Round(close / ordered[0].EpsTrailing!.Value, 6, MidpointRounding.ToEven);
        var low = multiples.Min(pair => pair.Multiple);
        var high = multiples.Max(pair => pair.Multiple);

        return new ValuationReading(read, tonight, low, high, PositionOf(tonight, low, high), null);
    }

    // The third of the range a multiple sits in, below the range reading as its cheap end and above it
    // as its expensive end, and a range of one value reading as its middle.
    public static string PositionOf(decimal multiple, decimal low, decimal high)
    {
        if (high <= low)
        {
            return Middle;
        }

        var place = (multiple - low) / (high - low);

        return place < 1m / 3m ? CheapEnd : place > 2m / 3m ? ExpensiveEnd : Middle;
    }
}
