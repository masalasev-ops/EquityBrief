using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.1: each line of a pick's card worked by hand on both sides of its value and with the value
// moved, the earnings line inside the held time, inside the cap alone and past both, the card's cover held to the
// coverage's own at its floor, a night's cards on the S&P 400 read from that index's own breadth and sectors with each
// failure a line names, and a rule's record the sweep's own reading of its one setting over a constructed history.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: The card warns of a report inside the time three in four of the rule's trades had ended by, and notes one inside the cap alone
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
public partial class FixtureExpectations
{
    // The rows the card adds that this check reaches: section 17's four values and section 18's four failures.
    internal static readonly string[] CardClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Card sector place"),
        CheckReach.Key(Scope.LimitsTable, "Card cover floor"),
        CheckReach.Key(Scope.LimitsTable, "Card held share"),
        CheckReach.Key(Scope.LimitsTable, "Card round trip"),
        CheckReach.Key(Scope.FailureTable, "A rule a pick's card names that has no record"),
        CheckReach.Key(Scope.FailureTable, "No report date on file for a pick"),
        CheckReach.Key(Scope.FailureTable, "A pick the night read nothing for"),
        CheckReach.Key(Scope.FailureTable, "A rule that sets no stop"),
    ];

    static readonly DateOnly CardNight = new(2026, 10, 6);

    // Four quarters filed before the night, each with the net income, operating income and interest expense given.
    static IReadOnlyList<FiledIncome> CardQuarters(decimal net, decimal operating, decimal? interest, int count = 4) =>
    [
        .. new[]
        {
            new FiledIncome(new DateOnly(2025, 12, 31), new DateOnly(2026, 2, 10), net, operating, interest),
            new FiledIncome(new DateOnly(2026, 3, 31), new DateOnly(2026, 5, 8), net, operating, interest),
            new FiledIncome(new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 7), net, operating, interest),
            new FiledIncome(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2), net, operating, interest),
        }.TakeLast(count),
    ];

    // Eleven sectors ranked from the highest mean return, the one named at the place given and the rest around it.
    static IReadOnlyList<SectorPlace> ElevenSectors() =>
        CardLines.Rank([.. Enumerable.Range(1, 11).Select(place => ($"Sector {place:00}", (12 - place) / 100.0))]);

    // The session the given count of sessions after the night, on the exchange's calendar.
    static DateOnly SessionsAfter(DateOnly night, int count)
    {
        var day = night;

        for (var found = 0; found < count;)
        {
            day = day.AddDays(1);
            found += ExchangeClosures.IsSession(day) ? 1 : 0;
        }

        return day;
    }

    [Fact]
    public void EachLineOfAPicksCardIsWorkedByHandOnBothSidesOfItsValue()
    {
        var card = CardSettings.Defaults;

        // Trend and strength: a tick on every card, naming the gates.
        var trend = CardLines.Trend("pullback", "S&P 400", ["floors, a close of at least $5"]);

        Assert.Equal((CardVerdict.Tick, "Passed every gate of the pullback on the S&P 400: floors, a close of at least $5."), (trend.Verdict, trend.Words));

        // The business: 20 of operating income against 10 of interest a quarter covers twice, the floor itself, and passes;
        // against 10.05 a quarter it covers 1.99 and warns. Net income summing to nothing warns, a cent over it passes.
        var twice = CardLines.Business(CardQuarters(10m, 20m, 10m), CardNight, "Industrials");
        var under = CardLines.Business(CardQuarters(10m, 20m, 10.05m), CardNight, "Industrials");

        Assert.Equal(2m, twice.Cover);
        Assert.Equal(CardVerdict.Tick, CardLines.Business(twice, "steady", card).Verdict);
        Assert.Equal(
            (CardVerdict.Warn, "Its operating income is 1.99 times its interest, under the card's floor of 2; its four newest quarters sum their net income above nothing; its reported quarters read steady."),
            (CardLines.Business(under, "steady", card).Verdict, CardLines.Business(under, "steady", card).Words));
        Assert.Equal(CardVerdict.Warn, CardLines.Business(CardLines.Business(CardQuarters(0m, 20m, 10m), CardNight, "Industrials"), "steady", card).Verdict);
        Assert.Equal(CardVerdict.Tick, CardLines.Business(CardLines.Business(CardQuarters(0.0025m, 20m, 10m), CardNight, "Industrials"), "steady", card).Verdict);

        // A financial company's cover and a company filing no interest are not read; a deteriorating state warns, and three
        // quarters or no state is a reading not held, which warns and says what is missing.
        Assert.Contains("its cover is not read", CardLines.Business(CardLines.Business(CardQuarters(10m, 5m, 10m), CardNight, MemberReadings.Financials), "steady", card).Words, StringComparison.Ordinal);
        Assert.Contains("it files no interest expense on them", CardLines.Business(CardLines.Business(CardQuarters(10m, 5m, null), CardNight, "Industrials"), "steady", card).Words, StringComparison.Ordinal);
        Assert.Equal(CardVerdict.Warn, CardLines.Business(twice, "deteriorating", card).Verdict);

        var missing = CardLines.Business(CardLines.Business(CardQuarters(10m, 20m, 10m, count: 3), CardNight, "Industrials"), null, card);

        Assert.Equal(CardVerdict.Warn, missing.Verdict);
        Assert.Contains("Not held: 3 of the four quarters the profit reading needs are filed", missing.Words, StringComparison.Ordinal);
        Assert.Contains("not held: the night read no state from its reported quarters", missing.Words, StringComparison.Ordinal);

        // The market and the sector: of eleven sectors the ninth is among the bottom three and warns, the eighth is not; a
        // sector not ranked warns.
        var sectors = ElevenSectors();
        var market = new MarketReading(0.6, 0.45, true);
        var ninth = CardLines.Market(market, sectors[8], "S&P 400", card);
        var eighth = CardLines.Market(market, sectors[7], "S&P 400", card);

        Assert.Equal(CardVerdict.Warn, ninth.Verdict);
        Assert.Contains("stands 9th of 11 on the S&P 400", ninth.Words, StringComparison.Ordinal);
        Assert.Contains("among the bottom 3", ninth.Words, StringComparison.Ordinal);
        Assert.Contains("the S&P 400's breadth is 60.0% against its floor of 45%", ninth.Words, StringComparison.Ordinal);
        Assert.Equal(CardVerdict.Tick, eighth.Verdict);
        Assert.Equal(CardVerdict.Warn, CardLines.Market(market, null, "S&P 400", card).Verdict);

        // Liquidity and cost: a round trip of 0.10 of the risk warns, a hair under it passes, a rule with no stop is read in
        // per cent and not warned on, and a dollar volume not held warns.
        Assert.Equal(CardVerdict.Warn, CardLines.Cost(20_000_000m, 0.10, false, false, card).Verdict);
        Assert.Equal(CardVerdict.Tick, CardLines.Cost(20_000_000m, 0.0999, false, false, card).Verdict);
        Assert.Equal(
            (CardVerdict.Tick, "It traded 20.0 million dollars a session over the 50 sessions to the night; its round trip at the published table is 0.50% of the buy, read in per cent since the rule sets no stop."),
            (CardLines.Cost(20_000_000m, 0.5, true, false, card).Verdict, CardLines.Cost(20_000_000m, 0.5, true, false, card).Words));
        Assert.Equal(CardVerdict.Warn, CardLines.Cost(null, 0.05, false, false, card).Verdict);
        Assert.Contains("reads high for a company this large", CardLines.Cost(20_000_000m, 0.05, false, true, card).Words, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportInsideTheHeldTimeWarnsOneInsideTheCapAloneNotesAndOnePastBothTicks()
    {
        var card = CardSettings.Defaults;

        // Twenty trades, one ended at each session from the first to the twentieth: three in four had ended by the
        // fifteenth, half by the tenth.
        int[] ended = [.. Enumerable.Repeat(1, 20)];

        Assert.Equal(15, RuleRecordFigures.HeldBy(ended, card.HeldShare));
        Assert.Equal(10, RuleRecordFigures.HeldBy(ended, 0.5));

        ReportAhead Moving(int sessions) => new(SessionsAfter(CardNight, sessions), EventTiming.Before, CardLines.SessionsTo(CardNight, SessionsAfter(CardNight, sessions), EventTiming.Before));

        // A report before the open moves its own session, one after the close the session after.
        Assert.Equal(15, CardLines.SessionsTo(CardNight, SessionsAfter(CardNight, 15), EventTiming.Before));
        Assert.Equal(15, CardLines.SessionsTo(CardNight, SessionsAfter(CardNight, 14), EventTiming.After));
        Assert.Equal(1, CardLines.SessionsTo(CardNight, CardNight, EventTiming.After));
        Assert.Null(CardLines.SessionsTo(CardNight, CardNight, EventTiming.Before));

        var inside = CardLines.Earnings(Moving(15), 15, 63, card);
        var past = CardLines.Earnings(Moving(16), 15, 63, card);
        var beyond = CardLines.Earnings(Moving(64), 15, 63, card);

        Assert.Equal(CardVerdict.Warn, inside.Verdict);
        Assert.Contains("moves the session 15 sessions after the night, inside the 15 sessions by which three in four of the rule's trades had ended", inside.Words, StringComparison.Ordinal);
        Assert.Equal(CardVerdict.Note, past.Verdict);
        Assert.Contains("inside the rule's cap of 63 sessions but after the 15 by which three in four of its trades had ended", past.Words, StringComparison.Ordinal);
        Assert.Equal(CardVerdict.Tick, beyond.Verdict);
        Assert.Contains("after the rule's cap of 63 sessions", beyond.Words, StringComparison.Ordinal);

        // No date on file warns; a rule with no record reads its cap alone and warns inside it.
        Assert.Equal(CardVerdict.Warn, CardLines.Earnings(null, 15, 63, card).Verdict);
        Assert.Equal(CardVerdict.Warn, CardLines.Earnings(Moving(30), null, 63, card).Verdict);
    }

    [Fact]
    public void EachValueOfTheCardsSettingsBlockMovesItsLinesVerdictAndWords()
    {
        // The defaults as the block reads them where it names none, and each value moved alone.
        Assert.Equal(CardSettings.Defaults, CardSettings.From(_ => null));

        var moved = CardSettings.From(key => key switch
        {
            "EquityBrief:Card:SectorBottom" => "4",
            "EquityBrief:Card:CoverFloor" => "1.5",
            "EquityBrief:Card:HeldShare" => "0.5",
            "EquityBrief:Card:RoundTripRisks" => "0.2",
            _ => null,
        });

        Assert.Equal(new CardSettings(4, 1.5m, 0.5, 0.2), moved);
        Assert.Throws<InvalidOperationException>(() => CardSettings.From(key => key == "EquityBrief:Card:HeldShare" ? "three in four" : null));
        Assert.Throws<InvalidOperationException>(() => CardSettings.From(key => key == "EquityBrief:Card:HeldShare" ? "1.5" : null));

        // The eighth sector of eleven warns with four bottom places.
        var market = new MarketReading(0.6, 0.45, true);
        var eighth = ElevenSectors()[7];

        Assert.Equal(CardVerdict.Tick, CardLines.Market(market, eighth, "S&P 400", CardSettings.Defaults).Verdict);
        Assert.Equal(CardVerdict.Warn, CardLines.Market(market, eighth, "S&P 400", moved).Verdict);
        Assert.Contains("among the bottom 4", CardLines.Market(market, eighth, "S&P 400", moved).Words, StringComparison.Ordinal);

        // A cover of 1.99 passes a floor of 1.5.
        var under = CardLines.Business(CardQuarters(10m, 20m, 10.05m), CardNight, "Industrials");

        Assert.Equal(CardVerdict.Warn, CardLines.Business(under, "steady", CardSettings.Defaults).Verdict);
        Assert.Equal(CardVerdict.Tick, CardLines.Business(under, "steady", moved).Verdict);

        // Half the trades ended by the tenth session, so a report at the fifteenth notes rather than warns.
        int[] ended = [.. Enumerable.Repeat(1, 20)];
        var report = new ReportAhead(SessionsAfter(CardNight, 15), EventTiming.Before, 15);

        Assert.Equal(CardVerdict.Warn, CardLines.Earnings(report, RuleRecordFigures.HeldBy(ended, CardSettings.Defaults.HeldShare), 63, CardSettings.Defaults).Verdict);
        Assert.Equal(CardVerdict.Note, CardLines.Earnings(report, RuleRecordFigures.HeldBy(ended, moved.HeldShare), 63, moved).Verdict);
        Assert.Contains("after the 10 by which half of its trades had ended", CardLines.Earnings(report, RuleRecordFigures.HeldBy(ended, moved.HeldShare), 63, moved).Words, StringComparison.Ordinal);

        // A round trip of 0.15 of the risk passes a value of 0.2.
        Assert.Equal(CardVerdict.Warn, CardLines.Cost(20_000_000m, 0.15, false, false, CardSettings.Defaults).Verdict);
        Assert.Equal(CardVerdict.Tick, CardLines.Cost(20_000_000m, 0.15, false, false, moved).Verdict);
    }

    [Fact]
    public void TheCardsCoverIsTheCoveragesOwnAtItsFloor()
    {
        // At the coverage's own floor the card passes the cover exactly where the S&P 400's and 600's quality does, on both
        // sides of it, at it, and for a financial company and one filing no interest.
        foreach (var (operating, interest, sector) in new (decimal, decimal?, string)[]
        {
            (20m, 10m, "Industrials"),
            (19.99m, 10m, "Industrials"),
            (20.01m, 10m, "Industrials"),
            (5m, 10m, MemberReadings.Financials),
            (5m, null, "Industrials"),
        })
        {
            var quarters = CardQuarters(10m, operating, interest);
            var reading = CardLines.Business(quarters, CardNight, sector);
            var card = CardLines.Business(reading, "steady", CardSettings.Defaults);

            Assert.Equal(MemberReadings.Coverage(quarters, CardNight, sector), card.Verdict == CardVerdict.Tick);
        }
    }

    [Fact]
    public async Task TheNightWritesACardForEachPickOnTheSAndP400FromItsOwnBreadthAndSectorsNamingWhatItCouldNotRead()
    {
        using var store = new TemporaryStore().Migrated();

        // Seventy weekdays to the night. Eleven sectors on each index, one member a sector, each rising by its sector's
        // place on the S&P 400 and the other way round on the S&P 500, so a card reading the S&P 500's sectors would place
        // Utilities second where the S&P 400's own place it tenth. PICK is in Utilities beside its member.
        var days = Weekdays(new DateOnly(2026, 7, 1), 70);
        string[] named = ["Energy", "Materials", "Industrials", "Consumer Discretionary", "Consumer Staples", "Health Care", "Financials", "Information Technology", "Communication Services", "Utilities", "Real Estate"];

        Assert.Equal(CardNight, days[^1]);
        Assert.True(days.Length > CardLines.SectorSessions, $"Read {days.Length} sessions, expected more than {CardLines.SectorSessions}.");

        using (var connection = store.Open())
        {
            using var transaction = connection.BeginTransaction();

            void Run(string sql)
            {
                using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            void Member(string index, string ticker, string sector, decimal rise)
            {
                Run($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2025-01-02', NULL, '2026-10-06T21:00:00Z');");
                Run($"INSERT INTO company (ticker, fetched_at, cik, sector) VALUES ('{ticker}', '2026-09-01T00:00:00Z', NULL, '{sector}');");

                foreach (var (day, at) in days.Select((day, at) => (day, at)))
                {
                    var close = (50m + (rise * at)).ToString(CultureInfo.InvariantCulture);
                    var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    Run($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{stamp}', '{close}', '{close}', '{close}', '{close}', 1000000, 'constructed', '2026-10-06T21:00:00Z', '{close}');");
                }
            }

            foreach (var (sector, place) in named.Select((sector, place) => (sector, place)))
            {
                Member("MID", "M" + place.ToString("00", CultureInfo.InvariantCulture), sector, (11 - place) / 100m);
                Member("GSPC", "L" + place.ToString("00", CultureInfo.InvariantCulture), sector, (place + 1) / 100m);
            }

            Member("MID", "PICK", "Utilities", 2 / 100m);
            Member("MID", "HW", "Energy", 11 / 100m);
            Run("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'NONE', '2025-01-02', NULL, '2026-10-06T21:00:00Z');");

            // The S&P 400's night at a breadth of 60 per cent over the floor its settings state, PICK and NONE listed by the
            // pullback and HW bought by its sector heavyweights.
            Run($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('MID', '2026-10-06', 14, 0.6, 1, '{IndexFamilies.Settings("MID")}', 1);");
            Run("INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
                "('MID', '2026-10-06', 'PICK', 'pullback', 1, 1, '50', '47.3', '55', NULL, 63, 2.0, NULL), ('MID', '2026-10-06', 'NONE', 'pullback', 1, 2, '20', '19', '23', NULL, 63, 1.0, NULL);");
            Run("INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
                "('MID', '2026-10-06', 'PICK', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL), ('MID', '2026-10-06', 'NONE', 'pullback', 'listed', 2, '[]', NULL, NULL, NULL);");
            Run("INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES ('MID', 'HW', '2026-10-06', 'Energy', '57.59', 1.0, '[]', '2026-10-06');");

            // PICK's readings and its quarters as the fetch before the night stored them, covering three times their
            // interest; its next report before the open on the fifth session after the night. HW's readings alone.
            Run("INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, company_value, state) VALUES ('MID', '2026-10-06', 'PICK', '50', '20000000', '800000000', 'steady'), ('MID', '2026-10-06', 'HW', '57.59', '30000000', '3000000000', 'improving');");

            // A reading of PICK's stored for the session after the night, which no card of the night reads, and the S&P
            // 600's night at a breadth under its floor, which no S&P 400 card reads.
            var after = SessionsAfter(CardNight, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            Run($"INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, company_value, state) VALUES ('MID', '{after}', 'PICK', '50', '1000000', '800000000', 'deteriorating');");
            Run($"INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES ('SML', '2026-10-06', 11, 0.3, 0, '{IndexFamilies.Settings("SML")}', 0);");

            foreach (var (end, filed) in new[] { ("2025-12-31", "2026-02-10"), ("2026-03-31", "2026-05-08"), ("2026-06-30", "2026-08-07"), ("2026-09-30", "2026-10-02") })
            {
                Run($"INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income, interest_expense) VALUES ('PICK', '2026-10-05T23:00:00Z', '2026-10-05', '{end}', '{filed}', '10', '30', '10');");
            }

            var reports = SessionsAfter(CardNight, 5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            Run($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('PICK', '{reports}', 'earnings', 'before', '{{}}', '2026-10-06T21:00:00Z');");

            // The pullback's record on the S&P 400: twenty trades, one ended at each session from the first to the twentieth.
            Run("INSERT INTO rule_record (index_code, family, rule, settings, recorded_at, first_session, last_session, membership, unit, trades, won, average, median_sessions, ended_by, worst_close) VALUES " +
                $"('MID', 'pullback', 'the pullback''s base', 'base', '2026-10-06T12:00:00Z', '2019-01-02', '2026-10-02', 'as it stood', 'risks', 20, 0.5, 0.12, 10, '{RuleRecordFigures.EndedByJson([.. Enumerable.Repeat(1, 20)])}', -0.6);");

            transaction.Commit();
        }

        var clock = new SweepClock(new DateTimeOffset(2026, 10, 6, 23, 50, 0, TimeSpan.Zero));

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("night-cards");
        await new DecisionCards(clock, store.DatabaseFile).RunAsync("night-cards-again");

        var cards = Cards(store);

        // A night run again replaces its own cards: three, PICK's, NONE's and HW's, and none on the S&P 500, which listed
        // nothing.
        Assert.Equal(["MID heavyweight HW", "MID pullback NONE", "MID pullback PICK"], cards.Keys.Order(StringComparer.Ordinal));

        var pick = cards["MID pullback PICK"];

        // PICK's market line reads the S&P 400's own breadth and its own sectors: Utilities tenth of eleven, PICK and its
        // member, among the bottom three, where the S&P 500's would place it first.
        Assert.Equal("warning", pick.Lines[2].Verdict);
        Assert.Contains("Its sector, Utilities, stands 10th of 11 on the S&P 400 by its 2 members' mean return over 63 sessions", pick.Lines[2].Words, StringComparison.Ordinal);
        Assert.Contains("the S&P 400's breadth is 60.0% against its floor of 45%", pick.Lines[2].Words, StringComparison.Ordinal);

        // Its business passes, its report on the fifth session falls inside the fifteen by which three in four of the
        // pullback's trades had ended, and its round trip at the table, a company of $800 million at $50, is 0.1255 a share
        // over a risk of 2.70.
        Assert.Equal(("tick", "Its four newest quarters sum their net income above nothing; its operating income is 3.00 times its interest; its reported quarters read steady."), (pick.Lines[1].Verdict, pick.Lines[1].Words));
        Assert.Equal("warning", pick.Lines[3].Verdict);
        Assert.Contains("moves the session 5 sessions after the night, inside the 15 sessions by which three in four of the rule's trades had ended", pick.Lines[3].Words, StringComparison.Ordinal);
        Assert.Equal("tick", pick.Lines[4].Verdict);
        Assert.Contains(FormattableString.Invariant($"its round trip at the published table is {0.1255 / 2.7:0.000} of the trade's risk"), pick.Lines[4].Words, StringComparison.Ordinal);
        Assert.Contains("It traded 20.0 million dollars a session", pick.Lines[4].Words, StringComparison.Ordinal);
        Assert.Equal(15, pick.Record!.Value.GetProperty("heldSessions").GetInt32());
        Assert.Equal(("50", "47.3", "55"), (pick.Entry, pick.Stop, pick.Target));

        // NONE, which the night read nothing for: each line reading what is not held warns and names it.
        var none = cards["MID pullback NONE"];

        Assert.Equal(["tick", "warning", "warning", "warning", "warning"], none.Lines.Select(line => line.Verdict));
        Assert.Contains("Not held: 0 of the four quarters the profit reading needs are filed", none.Lines[1].Words, StringComparison.Ordinal);
        Assert.Contains("not held: its sector is not filed or not ranked among the S&P 400's", none.Lines[2].Words, StringComparison.Ordinal);
        Assert.Equal("No report date is on file for it, so a report inside the hold cannot be ruled out.", none.Lines[3].Words);
        Assert.Contains("Not held: its dollar volume over 50 sessions", none.Lines[4].Words, StringComparison.Ordinal);

        // HW, which a rule with no stop bought: its round trip read in per cent and not warned on, and no record replayed,
        // so the card's record is none.
        var held = cards["MID heavyweight HW"];

        Assert.Contains("it led Energy at the rebalance of 2026-10-06", held.Lines[0].Words, StringComparison.Ordinal);
        Assert.Equal("tick", held.Lines[4].Verdict);
        Assert.Contains("of the buy, read in per cent since the rule sets no stop", held.Lines[4].Words, StringComparison.Ordinal);
        Assert.Null(held.Record);
        Assert.Null(held.Stop);

        // Every card stores the values it was read with.
        Assert.All(cards.Values, one => Assert.Equal(CardSettings.Defaults.Json(), one.Settings));
    }

    // A stored card's lines, record, plan and values by its index, family and stock.
    sealed record StoredCard(string? Entry, string? Stop, string? Target, string Settings, IReadOnlyList<(string Verdict, string Words)> Lines, JsonElement? Record);

    static Dictionary<string, StoredCard> Cards(TemporaryStore store)
    {
        var cards = new Dictionary<string, StoredCard>(StringComparer.Ordinal);

        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT index_code, family, ticker, entry, stop, target, settings, lines, record FROM decision_card;";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            using var lines = JsonDocument.Parse(reader.GetString(7));

            cards[$"{reader.GetString(0)} {reader.GetString(1)} {reader.GetString(2)}"] = new StoredCard(
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                [.. lines.RootElement.EnumerateArray().Select(line => (line.GetProperty("verdict").GetString()!, line.GetProperty("words").GetString()!))],
                reader.IsDBNull(8) ? null : JsonDocument.Parse(reader.GetString(8)).RootElement.Clone());
        }

        return cards;
    }

    [Fact]
    public async Task ARulesRecordIsTheSweepsOwnReadingOfItsOneSettingAfterEachTradesCost()
    {
        // The index sweep's constructed history of a breakout, three members over the weekdays from 2018-01-01 past 2019,
        // A breaking out 5 above the session before on 2019-02-01 on three times its volume, rising a point a session for
        // three and falling 6 on the fourth. Here its ranges narrow to 0.3 either side of the close over the ten sessions
        // before the breakout, so the breakout as frozen, its ranges at 0.85 of those before them, lists it.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var breakout = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < breakout ? 100m + (0.01m * day)
                : day == breakout ? a[day - 1] + 5
                : day <= breakout + 3 ? a[day - 1] + 1
                : day == breakout + 4 ? a[day - 1] - 6
                : a[day - 1];
        }

        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();

        using (var connection = store.Open())
        {
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ($ticker, $day, $close, $high, $low, $close, $volume, 'constructed', '2026-10-06T00:00:00Z', $close);";

            var ticker = insert.Parameters.Add("$ticker", SqliteType.Text);
            var session = insert.Parameters.Add("$day", SqliteType.Text);
            var close = insert.Parameters.Add("$close", SqliteType.Text);
            var high = insert.Parameters.Add("$high", SqliteType.Text);
            var low = insert.Parameters.Add("$low", SqliteType.Text);
            var volume = insert.Parameters.Add("$volume", SqliteType.Integer);

            foreach (var name in new[] { "A", "B", "C" })
            {
                for (var day = 0; day < calendar.Length; day++)
                {
                    var price = name == "A" ? a[day] : 100m + (0.01m * day);
                    var spread = name == "A" && day >= breakout - 10 && day < breakout ? 0.3m : 1m;

                    ticker.Value = name;
                    session.Value = calendar[day].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    close.Value = price.ToString(CultureInfo.InvariantCulture);
                    high.Value = (price + spread).ToString(CultureInfo.InvariantCulture);
                    low.Value = (price - spread).ToString(CultureInfo.InvariantCulture);
                    volume.Value = name == "A" && day == breakout ? 210_000 : 70_000;
                    insert.ExecuteNonQuery();
                }
            }

            using var rest = connection.CreateCommand();

            rest.Transaction = transaction;
            rest.CommandText = string.Concat(
                from name in new[] { "A", "B", "C" }
                select $"INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('SML', '{name}', 'US', NULL, NULL, NULL, 'constructed');")
                + string.Concat(
                    from name in new[] { "A", "B", "C" }
                    from quarter in new[] { ("2017-12-31", "2018-02-15"), ("2018-03-31", "2018-05-10"), ("2018-06-30", "2018-08-09"), ("2018-09-30", "2018-11-08") }
                    select $"INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) VALUES ('{name}', '{quarter.Item1}', '{quarter.Item2}', '1.00', '2.00', NULL, 'constructed');");
            rest.ExecuteNonQuery();
            transaction.Commit();
        }

        // The record's replay of the S&P 600's breakout and the sweep's own reading of the same setting.
        var replayed = Assert.Single(await RuleReplay.IndexAsync(store.DatabaseFile, "SML", _ => { }, default, [BreakoutRule.Name]));
        var record = RuleRecordFigures.Of(replayed.Trades);
        var clock = new SweepClock(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, await new IndexSweepRunner(clock, store.DatabaseFile, store.Root, sweeps.Path, TextWriter.Null).RunAsync("SML", BreakoutRule.Name, survivorsOnly: true));
        SweepRelease(store);

        var key = BreakoutSweep.Grid.Key([.. IndexNightRead.BreakoutAsFrozen]);

        using var figures = JsonDocument.Parse(File.ReadAllText(Path.Combine(sweeps.Path, "20261006T120000Z", IndexSweepRunner.FiguresFile)));

        JsonElement At(string read) => figures.RootElement.GetProperty(read).EnumerateArray().Single(one => one.GetProperty("Key").GetString() == key);

        // A's breakout alone trades: sold at the fourth session's close, 3 under its buy and its lowest close since, so its
        // worst close before costs is its result before costs, and the trailing rule sets no target and has no win.
        Assert.Equal((1, 1), (record.Trades, At("afterCosts").GetProperty("Trades").GetInt32()));
        Assert.Equal(At("afterCosts").GetProperty("Result").GetDouble(), record.Average!.Value, 12);
        Assert.Equal(At("beforeCosts").GetProperty("Result").GetDouble(), record.WorstClose!.Value, 12);
        Assert.True(record.Average < record.WorstClose, "the trade's cost comes off its result.");
        Assert.Equal((4, (double?)null), (record.MedianSessions, record.Won));
        Assert.Equal([0, 0, 0, 1], record.EndedBy);
        Assert.Equal((RuleReplay.SurvivorsOnly, RuleReplay.Risks, key), (replayed.Membership, replayed.Unit, replayed.Settings));

        // Named in the register's words from the frozen dials, 126, 1.5, 0.85 and 1.5, and never by the sweep's key.
        Assert.Equal("the breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath", replayed.Rule);
    }

    [Fact]
    public void ARulesRecordNamesItsRuleInTheRegistersWordsReadOffItsOwnSettingAndNeverByTheSweepsKey()
    {
        // Worked by hand from each setting's dials: the drift frozen at 3 sessions, 0.5 typical moves, 2 times the volume
        // and a target at 2.5 times the risk; the S&P 500's heavyweights at the 10 largest over 251 sessions against the
        // sector's fund with 2 leaders and a beta of at least 1, monthly, sold on no longer leading; and the S&P 400's and
        // 600's the same against the sector's members.
        Assert.Equal(
            "the drift rule within 3 sessions, up 0.5 typical moves on 2 times the volume, the target at 2.5 times the risk",
            RuleReplay.SwingWords(DriftRule.Name, IndexNightRead.DriftAsFrozen));
        Assert.Equal(
            "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            RuleReplay.HeavyweightWords(HeavyweightSweep.Frozen));
        Assert.Equal(
            "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's members, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            RuleReplay.HeavyweightWords(IndexHeavyweights.Provisional));

        // Each family's sweep key, whose dials these words state, carries an equals sign and a bar the words never do.
        foreach (var words in new[] { RuleReplay.SwingWords(BreakoutRule.Name, IndexNightRead.BreakoutAsFrozen), RuleReplay.SwingWords(DriftRule.Name, IndexNightRead.DriftAsFrozen), RuleReplay.HeavyweightWords(HeavyweightSweep.Frozen) })
        {
            Assert.DoesNotContain("=", words, StringComparison.Ordinal);
            Assert.DoesNotContain("|", words, StringComparison.Ordinal);
        }
    }
}
