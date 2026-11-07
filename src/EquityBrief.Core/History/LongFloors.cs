namespace EquityBrief.Core.History;

// One quarter end of one fund against the floors: its holdings and their value, and those matched and priced.
public sealed record LongCoverage(string Index, DateOnly Period, int Holdings, int Matched, decimal Value, decimal MatchedValue)
{
    public double HoldingsShare => Holdings == 0 ? 0 : 1.0 * Matched / Holdings;

    public decimal ValueShare => Value == 0m ? 0m : MatchedValue / Value;

    public bool Stands => HoldingsShare >= LongFloors.HoldingsFloor && ValueShare >= LongFloors.ValueFloor;
}

// A holding matched to none whose value a share fell by half or more over its last two quarter ends: a company that may have
// failed, listed by name and checked by hand against the SEC's filings.
public sealed record LongFall(string Index, string Name, DateOnly Before, decimal BeforePrice, DateOnly Last, decimal LastPrice);

// The floors the long history's membership is held to before anything is written, and the stops: each quarter end matched and
// priced at 97 in every 100 of its holdings and 98.5 in every 100 of their value; a failed company left out, but the one the
// operator ruled out; each named by what was found.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
// see: Countrywide is left out of the long history on the operator's word and named in every figure that leaves it out
public static class LongFloors
{
    public const double HoldingsFloor = 0.97;

    public const decimal ValueFloor = 0.985m;

    // A value a share at or under this share of the one a quarter end before is a fall by half.
    public const decimal FellBy = 0.5m;

    // The one failed company ruled out of the long history, named in every figure that leaves it out.
    public const string RuledOut = "Countrywide Financial Corp.";

    // Each schedule's figures against the floors.
    public static IReadOnlyList<LongCoverage> Coverage(IReadOnlyList<LongSchedule> schedules, Func<LongSchedule, int, bool> matched) =>
    [
        .. schedules.OrderBy(schedule => schedule.Index, StringComparer.Ordinal).ThenBy(schedule => schedule.Period).Select(schedule =>
        {
            var (count, value) = (0, 0m);

            for (var place = 0; place < schedule.Holdings.Count; place++)
            {
                if (matched(schedule, place))
                {
                    count++;
                    value += schedule.Holdings[place].Value ?? 0m;
                }
            }

            return new LongCoverage(schedule.Index, schedule.Period, schedule.Holdings.Count, count, schedule.Holdings.Sum(holding => holding.Value ?? 0m), value);
        }),
    ];

    // Each name a fund held whose last two quarter ends both match it to none and whose value a share fell by half between them.
    public static IReadOnlyList<LongFall> Falls(IReadOnlyList<LongSchedule> schedules, Func<LongSchedule, int, bool> matched) =>
    [
        .. schedules
            .SelectMany(schedule => schedule.Holdings.Select((holding, place) => (Schedule: schedule, Place: place, Holding: holding)))
            .GroupBy(one => (one.Schedule.Index, one.Holding.Name))
            .Select(group => group.OrderBy(one => one.Schedule.Period).ToArray())
            .Where(held => held.Length >= 2)
            .Select(held => (Before: held[^2], Last: held[^1]))
            .Where(pair => !matched(pair.Before.Schedule, pair.Before.Place) && !matched(pair.Last.Schedule, pair.Last.Place)
                && pair.Before.Holding.ValuePerShare is { } before && pair.Last.Holding.ValuePerShare is { } last && before > 0m && last <= FellBy * before)
            .Select(pair => new LongFall(pair.Last.Schedule.Index, pair.Last.Holding.Name, pair.Before.Schedule.Period, pair.Before.Holding.ValuePerShare!.Value, pair.Last.Schedule.Period, pair.Last.Holding.ValuePerShare!.Value))
            .OrderBy(fall => fall.Index, StringComparer.Ordinal)
            .ThenBy(fall => fall.Name, StringComparer.Ordinal),
    ];

    // Why the run stops, none where nothing stops it: each quarter end under a floor, and each fall by half left out but the one
    // ruled out, until a person's check of it against the filings stands.
    public static IReadOnlyList<string> Stops(IReadOnlyList<LongCoverage> coverage, IReadOnlyList<LongFall> falls, Func<LongFall, bool> checkedByHand) =>
    [
        .. coverage.Where(quarter => !quarter.Stands).Select(quarter => FormattableString.Invariant(
            $"{quarter.Index} {quarter.Period:yyyy-MM-dd}: {quarter.Matched} of {quarter.Holdings} holdings matched and priced, {quarter.HoldingsShare:P1} against {HoldingsFloor:P1}, and {quarter.ValueShare:P1} of their value against {ValueFloor:P1}")),
        .. falls.Where(fall => fall.Name != RuledOut && !checkedByHand(fall)).Select(fall => FormattableString.Invariant(
            $"{fall.Index} {fall.Name}: a value a share of {fall.BeforePrice:0.00} at {fall.Before:yyyy-MM-dd} and {fall.LastPrice:0.00} at {fall.Last:yyyy-MM-dd}, priced under no code, not yet checked against the SEC's filings")),
    ];
}
