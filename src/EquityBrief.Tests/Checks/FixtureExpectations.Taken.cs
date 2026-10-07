using System.Globalization;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.3: a taken trade followed by hand over constructed sessions to its stop, its target, its cap,
// along a raised trail, through a split and to an exit the operator recorded, a close equal to its stop leaving it open;
// a provisional fill replaced by its session's stored open, an entered price never replaced, and an open under the stop
// read at the plan's distance; a sector heavyweight ended by its book's sale in per cent; and the operator's record per
// family and index under the twenty and at it.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
// see: The operator's own record states its average result once twenty of its trades in a family and index have ended
public partial class FixtureExpectations
{
    // The rows 16.3 adds that this check reaches: section 17's three values and section 18's three rows.
    internal static readonly string[] FollowerClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Dividend calendar sessions"),
        CheckReach.Key(Scope.LimitsTable, "Operator's record minimum"),
        CheckReach.Key(Scope.LimitsTable, "Card hold with no cap"),
        CheckReach.Key(Scope.FailureTable, "The dividend calendar refuses, or answers in a form that cannot be read"),
        CheckReach.Key(Scope.FailureTable, "A taken trade whose card's night or fill's session the store holds no bar for"),
        CheckReach.Key(Scope.FailureTable, "A hold running past the market events table's last date"),
    ];

    // The card's night, a Thursday, and the sessions after it.
    const string TakenNight = "2026-10-01";

    static readonly string[] TakenSessions = ["2026-10-02", "2026-10-05", "2026-10-06", "2026-10-07"];

    // One trade taken from a pullback card on the S&P 500 bought at 50.00 with its stop at 47.00 and its target at 56.00,
    // capped at 63 sessions, unless a case states otherwise.
    sealed record TakenCase(
        string Family = "pullback",
        string? Fill = null,
        string? Stop = "47",
        string? Target = "56",
        string? Trail = null,
        int? Cap = 63,
        string? ExitPrice = null,
        string? ExitDate = null);

    // The trade as the follower left it: its fill, whether still provisional, where it ended, at what price, why and its
    // result.
    static async Task<string> Followed(TakenCase trade, IReadOnlyList<(string Session, string Open, string Close, string Raw)> bars, string? left = null, string? sold = null)
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
            $"('GSPC', 'T', NULL, {(left is null ? "NULL" : $"'{left}'")}, '2026-10-01T21:00:00Z');");
        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record) VALUES " +
            $"('GSPC', '{TakenNight}', '{trade.Family}', 'T', 1, '50', {Text(trade.Stop)}, {Text(trade.Target)}, 'the rule', '{{}}', '[]', NULL);");
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through) VALUES " +
            $"('T', '2026-10-02T01:00:00Z', 'GSPC', '{trade.Family}', '{TakenNight}', 'Energy', '{trade.Fill ?? "50"}', '{TakenSessions[0]}', {(trade.Fill is null ? 1 : 0)}, {(trade.Fill is null ? 0 : 1)}, " +
            $"{Text(trade.Stop)}, {Text(trade.Target)}, {Text(trade.Trail)}, {(trade.Cap is { } cap ? cap.ToString(CultureInfo.InvariantCulture) : "NULL")}, {Text(trade.ExitPrice)}, {Text(trade.ExitDate)}, NULL);");

        foreach (var (session, open, close, raw) in bars)
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('T', '{session}', '{open}', '{Math.Max(decimal.Parse(open, CultureInfo.InvariantCulture), decimal.Parse(close, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)}', " +
                $"'{Math.Min(decimal.Parse(open, CultureInfo.InvariantCulture), decimal.Parse(close, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture)}', '{close}', 1000, 'test', '{session}T21:00:00Z', '{raw}');");
        }

        if (sold is not null)
        {
            store.Execute(
                "INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return) VALUES " +
                $"('T', '{TakenNight}', 'Energy', 'T Co', '60', 1.0, '[]', '{sold}', '{sold}', '63', 'no longer the leader', 0.05, 0.01);");
        }

        await new TakenFollower(new SweepClock(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("night-follow");

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT fill, provisional, IFNULL(ended_on, '-'), IFNULL(end_price, '-'), IFNULL(end_reason, '-'), result, followed_through FROM taken_trade;";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());

        var result = reader.IsDBNull(5) ? "-" : reader.GetDouble(5).ToString("0.0000", CultureInfo.InvariantCulture);

        return $"{Cents(reader.GetString(0))} {reader.GetInt64(1)} {reader.GetString(2)} {Cents(reader.GetString(3))} {reader.GetString(4)} {result} {reader.GetString(6)}";

        static string Text(string? value) => value is null ? "NULL" : $"'{value}'";

        static string Cents(string stored) => stored == "-" ? stored : decimal.Parse(stored, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture);
    }

    // The night's bar and the sessions after it, each traded at the price stored, with closes as given.
    static (string, string, string, string)[] Sessions(string fillOpen, params string[] closes) =>
    [
        (TakenNight, "49", "50", "50"),
        .. closes.Select((close, at) => (TakenSessions[at], at == 0 ? fillOpen : close, close, close)),
    ];

    [Fact]
    public async Task ATakenTradeIsFollowedToItsStopAndACloseEqualToTheStopLeavesItOpen()
    {
        // The provisional fill is replaced by the fill's session's open, 50.40, and the risk is 50.40 less 47.00, 3.40.
        // A close of 47.00 on 2026-10-05 equals the stop and leaves it open; 46.90 the session after is under it, a
        // result of (46.90 - 50.40) / 3.40, -1.0294.
        Assert.Equal("50.40 0 - - - - 2026-10-05", await Followed(new TakenCase(), Sessions("50.40", "49.00", "47.00")));
        Assert.Equal("50.40 0 2026-10-06 46.90 stop -1.0294 2026-10-06", await Followed(new TakenCase(), Sessions("50.40", "49.00", "47.00", "46.90")));
    }

    [Fact]
    public async Task ATakenTradeIsFollowedToItsTargetToItsCapAndAlongARaisedTrail()
    {
        // An entered fill of 50.00 is never replaced by the open of 50.40; a close of 56.10 is at or above the target of
        // 56.00: (56.10 - 50.00) / 3.00, 2.0333.
        Assert.Equal("50.00 0 2026-10-05 56.10 target 2.0333 2026-10-05", await Followed(new TakenCase(Fill: "50.00"), Sessions("50.40", "52.00", "56.10")));

        // A cap of three sessions, the fill's own the first: sold at the third close, 53.00, (53.00 - 50.00) / 3.00, 1.0000.
        Assert.Equal("50.00 0 2026-10-06 53.00 cap 1.0000 2026-10-06", await Followed(new TakenCase(Fill: "50.00", Cap: 3), Sessions("50.40", "51.00", "52.00", "53.00")));

        // The breakout's trail of 3.00 and no target: the stop raised to 49.00 after 52.00 and to 52.00 after 55.00, so
        // 51.90 is under it, (51.90 - 50.00) / 3.00, 0.6333, where the plan's own stop of 47.00 would have held.
        Assert.Equal(
            "50.00 0 2026-10-06 51.90 stop 0.6333 2026-10-06",
            await Followed(new TakenCase(Family: "breakout", Fill: "50.00", Target: null, Trail: "3"), Sessions("50.40", "52.00", "55.00", "51.90")));
    }

    [Fact]
    public async Task ATakenTradeIsFollowedThroughASplitAndToAnExitTheOperatorRecorded()
    {
        // A two-for-one split on 2026-10-05, the store holding the year as the provider adjusts it: the night's close
        // stored at 25.00 against 50.00 as it traded, and the fill's session at 24.50 against 49.00. The stop of 47.00 and
        // the fill of 50.00 are read at half, 23.50 and 25.00, so closes of 24.50, 24.00 and 23.60 end nothing.
        Assert.Equal(
            "50.00 0 - - - - 2026-10-06",
            await Followed(new TakenCase(Fill: "50.00"), [(TakenNight, "24.50", "25.00", "50.00"), ("2026-10-02", "25.20", "24.50", "49.00"), ("2026-10-05", "24.00", "24.00", "24.00"), ("2026-10-06", "23.60", "23.60", "23.60")]));

        // An exit the operator recorded at 53.00 on 2026-10-05 ends the trade there, though a close on 2026-10-06 is
        // under its stop: (53.00 - 50.00) / 3.00, 1.0000.
        Assert.Equal(
            "50.00 0 2026-10-05 53.00 exit 1.0000 2026-10-06",
            await Followed(new TakenCase(Fill: "50.00", ExitPrice: "53.00", ExitDate: "2026-10-05"), Sessions("50.40", "52.00", "53.20", "46.00")));
    }

    [Fact]
    public async Task AnOpenUnderTheStopIsReadAtThePlansDistanceAndAHoldingIsEndedByItsBooksSale()
    {
        // The fill's session opened at 46.50, under the stop of 47.00: the fill is that open, and the risk the plan's own,
        // 50.00 less 47.00. Its close of 46.80 is under the stop: (46.80 - 46.50) / 3.00, 0.1000.
        Assert.Equal("46.50 0 2026-10-02 46.80 stop 0.1000 2026-10-02", await Followed(new TakenCase(), Sessions("46.50", "46.80")));

        // A sector heavyweight, with no stop, no target and no cap, held until its book sold it on 2026-10-06 at 63.00:
        // (63.00 / 60.00 - 1) in per cent, 5.0000.
        Assert.Equal(
            "60.00 0 2026-10-06 63.00 sold 5.0000 2026-10-07",
            await Followed(new TakenCase(Family: HeavyweightRule.Name, Fill: "60.00", Stop: null, Target: null, Cap: null), Sessions("60.40", "61.00", "62.00", "63.00", "64.00"), sold: "2026-10-06"));

        // With no bar for the fill's session, the provisional fill stands as the plan's buy and nothing ends, though the
        // close after it is under the stop; with no bar for the card's night, the fill is replaced by its session's open
        // and still nothing ends.
        Assert.Equal("50.00 1 - - - - 2026-10-05", await Followed(new TakenCase(), [(TakenNight, "49", "50", "50"), ("2026-10-05", "46", "46", "46")]));
        Assert.Equal("50.40 0 - - - - 2026-10-05", await Followed(new TakenCase(), [("2026-10-02", "50.40", "49", "49"), ("2026-10-05", "46", "46", "46")]));

        // A stock that has left every index by the night ends at its last close the store holds.
        Assert.Equal(
            "50.00 0 2026-10-05 51.00 left 0.3333 2026-10-05",
            await Followed(new TakenCase(Fill: "50.00"), Sessions("50.40", "50.50", "51.00"), left: "2026-10-05"));
    }

    [Fact]
    public async Task TheOperatorsRecordDrawsItsAverageOnceTwentyOfAFamilysTradesHaveEnded()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('X', '2026-10-07', '1', '1', '1', '1', 1, 'test', '2026-10-07T21:00:00Z', '1');");

        // Nineteen pullback trades ended on the S&P 400, eleven at the target at +2 and eight at the stop at -1, one still
        // open, and two breakout trades ended on the S&P 500.
        void Ended(int at, string index, string family, string reason, double result) =>
            store.Execute(
                "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through, ended_on, end_price, end_reason, result) VALUES " +
                $"('E{at}', '2026-09-01T00:00:{at:00}Z', '{index}', '{family}', '2026-08-31', NULL, '50', '2026-09-01', 0, 1, '47', NULL, NULL, 63, NULL, NULL, '2026-10-06', '2026-09-20', '50', '{reason}', {result.ToString(CultureInfo.InvariantCulture)});");

        for (var at = 0; at < 19; at++)
        {
            Ended(at, "MID", "pullback", at < 11 ? TakenWalk.Targeted : TakenWalk.Stopped, at < 11 ? 2.0 : -1.0);
        }

        Ended(50, "GSPC", "breakout", TakenWalk.Stopped, 0.5);
        Ended(51, "GSPC", "breakout", TakenWalk.Capped, 1.5);
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through) VALUES " +
            "('OPEN', '2026-10-06T00:00:00Z', 'MID', 'pullback', '2026-10-05', NULL, '50', '2026-10-06', 0, 1, '47', '56', NULL, 63, NULL, NULL, NULL);");

        async Task<string[]> Records()
        {
            await new TakenFollower(new SweepClock(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("night-record");

            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT index_code || ' ' || family || ' ' || unit || ' ' || won || ' ' || lost || ' ' || ended || ' ' || open_trades || ' ' || CASE WHEN average IS NULL THEN '-' ELSE printf('%.4f', average) END FROM taken_record ORDER BY index_code, family;";

            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return [.. rows];
        }

        // Under the twenty no average; a trailing rule counts its trades ended and none won or lost.
        Assert.Equal(["GSPC breakout risks 0 0 2 0 -", "MID pullback risks 11 8 19 1 -"], await Records());

        // The twentieth ended at the target: (12 x 2 - 8) / 20, 0.8000.
        Ended(19, "MID", "pullback", TakenWalk.Targeted, 2.0);

        Assert.Equal(["GSPC breakout risks 0 0 2 0 -", "MID pullback risks 12 8 20 1 0.8000"], await Records());
        Assert.Equal(20, TakenWalk.RecordMinimum);
    }

    [Fact]
    public async Task TheRulesOwnPicksOnTheNightsTheOperatorTookOneAreFollowedFromThePlansBuyBesideTheRecord()
    {
        using var store = new TemporaryStore().Migrated();

        void Bar(string ticker, string session, string open, string close) =>
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '{session}', '{open}', '{(decimal.Parse(open, CultureInfo.InvariantCulture) > decimal.Parse(close, CultureInfo.InvariantCulture) ? open : close)}', " +
                $"'{(decimal.Parse(open, CultureInfo.InvariantCulture) < decimal.Parse(close, CultureInfo.InvariantCulture) ? open : close)}', '{close}', 1000, 'test', '{session}T21:00:00Z', '{close}');");

        void Card(string night, int place, string ticker)
        {
            store.Execute(
                "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap) VALUES " +
                $"('GSPC', '{night}', 'pullback', '{ticker}', {place}, '50', '47', '56', 'the rule', '{{}}', '[]', NULL, 63);");
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-10-01T21:00:00Z');");
        }

        // Twenty pullback picks on the S&P 500 on 2026-10-01, each planned at a buy of 50.00, a stop of 47.00 and a target
        // of 56.00, opening at 50.60 on the session after: P00 to P11 close at 56.10 on 2026-10-05, at the target, P12 to
        // P18 at 46.90, under the stop, and P19 at 52.00, still open. And one pick on 2026-10-02 the operator took nothing
        // from, which closes at the target too.
        for (var at = 0; at < 20; at++)
        {
            var ticker = FormattableString.Invariant($"P{at:00}");

            Card(TakenNight, at + 1, ticker);
            Bar(ticker, TakenNight, "49", "50");
            Bar(ticker, "2026-10-02", "50.60", "51");
            Bar(ticker, "2026-10-05", "51", at < 12 ? "56.10" : at < 19 ? "46.90" : "52");
        }

        Card("2026-10-02", 1, "Q");
        Bar("Q", "2026-10-02", "49", "50");
        Bar("Q", "2026-10-05", "50.60", "56.10");

        // The operator took P00 with no price entered, so its fill is the open of 50.60 and its risk 3.60.
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap, exit_price, exit_date, followed_through) VALUES " +
            $"('P00', '2026-10-02T01:00:00Z', 'GSPC', 'pullback', '{TakenNight}', 'Energy', '50', '2026-10-02', 1, 0, '47', '56', NULL, 63, NULL, NULL, NULL);");

        async Task<string> Record()
        {
            await new TakenFollower(new SweepClock(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("night-same");

            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText =
                "SELECT won || ' ' || lost || ' ' || ended || ' ' || open_trades || ' ' || CASE WHEN average IS NULL THEN '-' ELSE printf('%.4f', average) END || ' | ' || " +
                "same_nights || ' ' || rule_listed || ' ' || rule_won || ' ' || rule_lost || ' ' || rule_ended || ' ' || CASE WHEN rule_average IS NULL THEN '-' ELSE printf('%.4f', rule_average) END " +
                "FROM taken_record WHERE index_code = 'GSPC' AND family = 'pullback';";

            return (string)command.ExecuteScalar()!;
        }

        // The operator's one trade won at the target. On its one night the rule listed twenty, twelve won and seven lost,
        // nineteen ended, so no average; Q's night is not one the operator took a trade on.
        Assert.Equal("1 0 1 0 - | 1 20 12 7 19 -", await Record());

        // P19 closes at 56.10 on 2026-10-06 and the twentieth ends. From the plan's buy of 50.00 with a risk of 3.00, a
        // win is (56.10 - 50.00) / 3.00 and a loss (46.90 - 50.00) / 3.00: (13 x 6.10 - 7 x 3.10) / (20 x 3.00), 0.9600.
        // Bought at the next session's open of 50.60, as the operator's fill was, it would read 0.6333.
        Bar("P19", "2026-10-06", "52", "56.10");

        Assert.Equal("1 0 1 0 - | 1 20 13 7 20 0.9600", await Record());
    }
}
