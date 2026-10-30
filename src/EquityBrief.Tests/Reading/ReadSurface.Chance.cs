using System.Globalization;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.7: the setups' chance on a pick's card, matched by the night from the ledger over the constructed store
// the score part's test reads and read back off the rendered card. The breakout's pick holds no target, so its setups are
// read as a trailing setup's: forty setups at a buy of 50 and a stop of 48, winning and losing a risk in turn, twenty
// ending above their buy and their mean result after the published spread's round trip at 50, 52 and 48 a lost three
// cents a dollar risked; then the nearest 250 of three hundred, every one winning a risk, made 97 cents after it.
// see: Each pick's card says how often the setups like it won and what they made after costs, under the rule's heading
public partial class ReadSurface
{
    // The part of the card's section this check reaches from 21.7.
    internal static readonly string[] ChanceClaims =
    [
        CheckReach.Key(Scope.CardPage, Scope.CardChancePart),
    ];

    // A risk's round trip at a buy of 50 and a stop of 48 sold at a price, its company holding no count so read in the
    // table's band of $1 to 2 billion: half the band's spread at 40 dollars and over, 0.129 per cent, at the buy and half at
    // the sale, over the two dollars risked.
    static double RoundTripAt(double sale) => ((0.129 / 200 * 50) + (0.129 / 200 * sale)) / 2;

    static string Dollars(double value) => value < 0 ? FormattableString.Invariant($"-${-value:0.00}") : FormattableString.Invariant($"${value:0.00}");

    [Fact]
    public async Task APicksCardSaysHowOftenTheSetupsLikeItWonAndWhatTheyMadeAfterCostsUnderTheSetupsHeading()
    {
        using var store = ScoreStore();
        var clock = new WaitedClock(new DateTimeOffset(2026, 4, 13, 23, 50, 0, TimeSpan.Zero));

        ScoreRun(store, "loop-test-GSPC-1", passed: false);

        for (var at = 0; at < 40; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"S{at:00}"), at, 1 + (0.01 * at), at % 2 == 0 ? 1 : -1);
        }

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-1");

        // Worked by hand: twenty at 1 less the round trip sold at 52, twenty at -1 less the round trip sold at 48.
        var won = 1 - RoundTripAt(52);
        var lost = -1 - RoundTripAt(48);
        var mean = (won + lost) / 2;
        var error = Math.Sqrt(40 * ((won - mean) * (won - mean)) / 39) / Math.Sqrt(40);
        var stored = CardSimilar.Read(Strings(store, "SELECT similar FROM decision_card WHERE ticker = 'AAA';").Single());

        Assert.Equal(("trailing", 40, 20), (stored.Form, stored.WithResult, stored.Won));
        Assert.Equal(mean, stored.ResultMean!.Value, 6);
        Assert.Equal(mean - (1.6448536269514722 * error), stored.ResultLow!.Value, 6);

        var part = ScorePart((await ScorePagesAsync(store)).Named);

        Assert.Contains(
            FormattableString.Invariant($">Of the 40 past setups most like this one under this setup, 20 (50%) ended above their buy. On average they lost {Dollars(-mean)} per $1 risked after costs, and nine times in ten that average falls between {Dollars(mean - (1.6448536269514722 * error))} and {Dollars(mean + (1.6448536269514722 * error))}. This is those setups' record, not this stock's chance.</p>"),
            part,
            StringComparison.Ordinal);
        Assert.True(
            part.IndexOf("<p class=\"card-chance\"", StringComparison.Ordinal) < part.IndexOf("<p class=\"card-similar\"", StringComparison.Ordinal),
            "The chance is not drawn first in the part.");

        // The nearest 250 of three hundred, every one winning a risk: all of them above their buy, and one risk less the
        // round trip sold at 52 each, so no range around it.
        store.Execute("DELETE FROM setup;");

        for (var at = 0; at < 300; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"T{at:000}"), at, 0.5 + (0.002 * at), at >= 50 ? 1 : -1);
        }

        ScoreRun(store, "loop-test-GSPC-2", passed: true);
        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-2");

        Assert.Contains(
            FormattableString.Invariant($">Of the 250 past setups most like this one under this setup, 250 (100%) ended above their buy. On average they made {Dollars(won)} per $1 risked after costs, and nine times in ten that average falls between {Dollars(won)} and {Dollars(won)}. This is those setups' record, not this stock's chance.</p>"),
            ScorePart((await ScorePagesAsync(store)).Named),
            StringComparison.Ordinal);

        // A card written before 21.7 holds no chance and draws none.
        store.Execute("UPDATE decision_card SET similar = '{\"count\":40,\"distance\":0.1,\"mean\":0,\"low\":-0.1,\"high\":0.1,\"rule_mean\":0,\"rule_setups\":40,\"readings\":[\"close_over_twenty\"]}';");

        Assert.DoesNotContain("card-chance", ScorePart((await ScorePagesAsync(store)).Named), StringComparison.Ordinal);
    }

    // The ledger's readings with the close over its 20-session average alone held, the one reading the constructed score
    // weighs.
    static double?[] CloseOverTwenty(double value)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[LedgerReadings.All.Select((reading, at) => (reading.Column, at)).Single(pair => pair.Column == "close_over_twenty").at] = value;

        return readings;
    }

    // A fold's score of the same reading stored for a year of the run.
    static void ScoreFold(TemporaryStore store, string run, int year)
    {
        var model = ScoreOfTheCloseOverTwenty();

        store.Execute(
            "INSERT INTO loop_model (run_id, index_code, family, kind, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words) " +
            FormattableString.Invariant($"VALUES ('{run}', 'GSPC', 'breakout', 'ridge', {year}, '2019-01-02', '{year}-01-01', 400, 'close_over_twenty', '{model.Canonical()}', '{model.Hash}', '{RidgeScore.Version}', 'the score weighs most close_over_twenty higher');"));
    }

    [Fact]
    public async Task TheExpectedValueAnOrderReadsIsTheMeanOfTheSetupsLikeEachMemberAndTheTestersIsMatchedAmongWhatEndedBeforeItsYear()
    {
        using var store = ScoreStore();

        ScoreRun(store, "loop-test-GSPC-1", passed: false);

        for (var at = 0; at < 40; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"S{at:00}"), at, 1 + (0.01 * at), at % 2 == 0 ? 1 : -1);
        }

        // The night's figure for a member is the card's mean over the same forty, worked by hand above; none for a member
        // the night holds no readings of, and none for a family no score is stored for on the index.
        var mean = ((1 - RoundTripAt(52)) + (-1 - RoundTripAt(48))) / 2;

        await using (var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile)))
        {
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

            var expected = await LikeSetups.ExpectedAsync(connection, transaction, "GSPC", BreakoutRule.Name, ticker => ticker == "UNREAD" ? null : CloseOverTwenty(1.2), _ => false, CancellationToken.None);
            var none = await LikeSetups.ExpectedAsync(connection, transaction, "GSPC", DriftRule.Name, _ => CloseOverTwenty(1.2), _ => false, CancellationToken.None);

            Assert.Equal(mean, expected("AAA")!.Value, 9);
            Assert.Null(expected("UNREAD"));
            Assert.Null(none("AAA"));
        }

        // Thirty more ending in 2026, each winning three risks, sold at 56; and the run's fold scores for 2025 and 2026
        // beside its score on all finished data. A listing in 2026 is matched among the forty that ended in 2025 alone, one
        // in 2027 among all seventy, one in 2025 among none, and one in a year no fold score stands for reads none.
        for (var at = 400; at < 430; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"L{at}"), at, 1 + (0.01 * (at - 400)), 3);
        }

        ScoreFold(store, "loop-test-GSPC-1", 2025);
        ScoreFold(store, "loop-test-GSPC-1", 2026);

        await using var reading = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await reading.OpenAsync();

        var pool = await LikePool.ReadAsync(reading, "GSPC", CancellationToken.None);

        Assert.Equal(70, pool.Count);
        Assert.Equal(mean, pool.ExpectedAt(BreakoutRule.Name, 2026, "AAA", CloseOverTwenty(1.2))!.Value, 9);
        Assert.Equal(((40 * mean) + (30 * (3 - RoundTripAt(56)))) / 70, pool.ExpectedAt(BreakoutRule.Name, 2027, "AAA", CloseOverTwenty(1.2))!.Value, 9);
        Assert.Null(pool.ExpectedAt(BreakoutRule.Name, 2025, "AAA", CloseOverTwenty(1.2)));
        Assert.Null(pool.ExpectedAt(BreakoutRule.Name, 2024, "AAA", CloseOverTwenty(1.2)));
        Assert.Null(pool.ExpectedAt(BreakoutRule.Name, 2026, "AAA", new double?[LedgerReadings.Count]));
        Assert.Null(pool.ExpectedAt(DriftRule.Name, 2026, "AAA", CloseOverTwenty(1.2)));
    }
}
