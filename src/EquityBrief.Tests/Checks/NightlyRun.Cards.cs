using EquityBrief.Core.Time;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Nights;

namespace EquityBrief.Tests.Checks;

// nightly-run, 16.1: the swing filter's step writes each index's cards after the index families, and an index whose cards
// fail is undone and named on the stage's own row with why while every other index's cards are written and the night goes
// on to its close.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
public partial class NightlyRun
{
    // A card the store refuses to remove when the night clears its own: the cards' step stops on the refusal, names it
    // on its own row after the index families' with every index not computed, and the night closes.
    [Fact]
    public async Task ACardsStepThatFailsIsNamedOnItsOwnRowAfterTheIndexFamiliesAndTheNightCloses()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record) VALUES " +
            "('MID', '2026-09-08', 'breakout', 'KEPT', 1, NULL, NULL, NULL, 'The breakout on the S&P 400', '{}', '[]', NULL);");
        store.Execute("CREATE TRIGGER keep_the_card BEFORE DELETE ON decision_card BEGIN SELECT RAISE(ABORT, 'the card is kept'); END;");

        var (code, _, error) = await NightAsync(store, launcher: new NightLauncherForTheSuite());

        Assert.True(code == 0, error);

        const string Night = "run_id LIKE 'night-%' AND instr(run_id, '-queue-') = 0";
        var stages = Texts(store, $"SELECT stage FROM run_log WHERE {Night} ORDER BY rowid;").ToList();

        Assert.Equal(1, stages.Count(stage => stage == DecisionCards.Stage));
        Assert.True(stages.IndexOf(IndexFamilies.Stage) < stages.IndexOf(DecisionCards.Stage));
        Assert.True(stages.IndexOf(DecisionCards.Stage) < stages.IndexOf(NightClose.Stage));
        Assert.Equal([DecisionCards.NotComputed], Texts(store, $"SELECT outcome FROM run_log WHERE {Night} AND stage = '{DecisionCards.Stage}';"));

        var detail = Texts(store, $"SELECT detail FROM run_log WHERE {Night} AND stage = '{DecisionCards.Stage}';").Single();

        Assert.StartsWith("the S&P 500's cards not computed tonight: SqliteException: ", detail, StringComparison.Ordinal);
        Assert.Contains("; the S&P 400's cards not computed tonight: SqliteException: ", detail, StringComparison.Ordinal);
        Assert.Contains("; the S&P 600's cards not computed tonight: SqliteException: ", detail, StringComparison.Ordinal);
        Assert.Contains("; the step stopped on SqliteException: ", detail, StringComparison.Ordinal);
        Assert.Contains("the card is kept", detail, StringComparison.Ordinal);
        Assert.Equal(["ok"], Texts(store, $"SELECT outcome FROM run_log WHERE {Night} AND stage = '{NightClose.Stage}';"));
        Assert.Equal(["KEPT"], Texts(store, "SELECT ticker FROM decision_card;"));
    }

    // Two picks on the S&P 400 and one on the S&P 600, the S&P 400's second holding a reading that is not a number: the
    // S&P 400's first card written and then undone with the second's failure and named, the S&P 600's written, and the
    // step handing the night back rather than stopping it.
    [Fact]
    public async Task AnIndexWhoseCardsFailIsUndoneAndNamedWhileEveryOtherIndexsCardsAreWritten()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
            "('MID', 'MP', '2025-01-02', NULL, '2026-10-06T21:00:00Z'), ('MID', 'MQ', '2025-01-02', NULL, '2026-10-06T21:00:00Z'), " +
            "('SML', 'SP', '2025-01-02', NULL, '2026-10-06T21:00:00Z');");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "SELECT ticker, '2026-10-06', '20', '20', '20', '20', 1000000, 'constructed', '2026-10-06T21:00:00Z', '20' FROM membership;");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('MID', '2026-10-06', 2, 0.6, 1, '{IndexFamilies.Settings("MID")}', 0), " +
            $"('SML', '2026-10-06', 1, 0.6, 1, '{IndexFamilies.Settings("SML")}', 0);");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            "('MID', '2026-10-06', 'MP', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL), ('MID', '2026-10-06', 'MQ', 'pullback', 'listed', 2, '[]', NULL, NULL, NULL), " +
            "('SML', '2026-10-06', 'SP', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, company_value, state) VALUES " +
            "('MID', '2026-10-06', 'MQ', '20', 'not a number', '800000000', 'steady');");

        var outcome = await new DecisionCards(FixedClock.At(new DateTimeOffset(2026, 10, 6, 23, 50, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-cards");

        Assert.Equal(
            [("GSPC", 0, false), ("MID", 0, true), ("SML", 1, false)],
            outcome.Indices.Select(index => (index.Index, index.Cards, index.Fault is not null)));
        Assert.Equal(["SML|pullback|SP"], Texts(store, "SELECT index_code || '|' || family || '|' || ticker FROM decision_card;"));
        Assert.Equal([DecisionCards.NotComputed], Texts(store, $"SELECT outcome FROM run_log WHERE stage = '{DecisionCards.Stage}';"));
        Assert.Equal(1, Scalar(store, $"SELECT rows_written FROM run_log WHERE stage = '{DecisionCards.Stage}';"));

        var detail = Texts(store, $"SELECT detail FROM run_log WHERE stage = '{DecisionCards.Stage}';").Single();

        Assert.StartsWith("0 card(s) on the S&P 500; the S&P 400's cards not computed tonight: FormatException: ", detail, StringComparison.Ordinal);
        Assert.EndsWith("; 1 card(s) on the S&P 600", detail, StringComparison.Ordinal);
    }
}
