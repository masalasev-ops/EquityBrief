using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-run, 19.9: the ledger's step appends the fundamentals-first family's setups over a constructed store. Three S&P
// 500 members are pullback setups on the night, each having filed its quarters and its facts before it; the step writes
// the family's setups for the two whose revenue and cash clear its gates at their pullback rows' own plan and readings,
// the provisional rule's pass and the night's pick read beside each, and the family's row over the members read; the
// third, whose cash falls a hundredth short, is a pullback setup alone. Run again, the night writes what it wrote.
// see: The fundamentals-first family's setups are the pullback's whose revenue and cash clear a grid step past the widest level its search tried
public partial class NightlyRun
{
    [Fact]
    public async Task TheLedgerStepAppendsTheFundamentalsFirstFamilysSetupsWithTheProvisionalRulesPassAndThePick()
    {
        using var store = new TemporaryStore().Migrated();
        var night = BusinessFirstHistory.Seed(store);

        static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // The index families' step listed AAA under the family on the S&P 500 that night.
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            $"('GSPC', '{Stamp(night)}', 'AAA', '{FundamentalsRule.Name}', 'listed', 1, '[]', NULL, NULL, NULL);");

        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 1, 28, 3, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var outcome = await ledger.NightAsync("night-ledger-business");

        Assert.Equal(night, outcome.Session);
        Assert.All(outcome.Indices, index => Assert.Null(index.Fault));

        // A row a family over the seven members: the three strong members under the pullback, two of them under the
        // business-first family, AAA the one its provisional rule passes.
        Assert.Contains("GSPC|fundamentals|7|2|1", Texts(store, "SELECT index_code || '|' || family || '|' || members || '|' || setups || '|' || live_passes FROM setup_night ORDER BY family;"));
        Assert.Equal(["AAA", "BBB", "CCC"], Texts(store, "SELECT ticker FROM setup WHERE family = 'pullback' ORDER BY ticker;"));
        Assert.Equal(["AAA|1|1", "BBB|0|0"], Texts(store, "SELECT ticker || '|' || live_pass || '|' || picked FROM setup WHERE family = 'fundamentals' ORDER BY ticker;"));

        // Each the pullback row's own plan and readings, read beside it on the same stock and session, its rule the
        // family's provisional setting at the pullback's base, its path open on the night.
        const string Plan = "entry || '|' || stop || '|' || IFNULL(target, 'none') || '|' || cap || '|' || risk_moves || '|' || revenue_growth || '|' || cash_over_income || '|' || close_over_long || '|' || IFNULL(strength, 'none')";

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            Assert.Equal(
                Texts(store, $"SELECT {Plan} FROM setup WHERE family = 'pullback' AND ticker = '{ticker}';"),
                Texts(store, $"SELECT {Plan} FROM setup WHERE family = 'fundamentals' AND ticker = '{ticker}';"));
        }

        Assert.Equal([LedgerSetups.FundamentalsWords], Texts(store, "SELECT DISTINCT rule FROM setup WHERE family = 'fundamentals';"));
        Assert.Equal(["0.25|1.25", "0.25|0.6"], Texts(store, "SELECT revenue_growth || '|' || cash_over_income FROM setup WHERE family = 'fundamentals' ORDER BY ticker;"));
        Assert.Equal([$"{SetupEnds.Open}|0|{SetupLedger.NightSource}"], Texts(store, "SELECT DISTINCT end || '|' || settled || '|' || source FROM setup WHERE family = 'fundamentals';"));

        // Run again, the night writes the same rows once.
        await ledger.NightAsync("night-ledger-business-again");

        Assert.Equal(["2"], Texts(store, "SELECT COUNT(*) FROM setup WHERE family = 'fundamentals';"));
        Assert.Equal([$"{LedgerSetups.Families.Count}"], Texts(store, "SELECT COUNT(*) FROM setup_night WHERE index_code = 'GSPC';"));
    }

    // The tester's walk of the family over the same store: the pullback's base listings over the history, kept where every
    // part of the family's rule passes at the setting it stands at, AAA alone on the night at the provisional setting, and
    // walked under the family's own name; at the setting asking cash of 1.2 still AAA alone, its cash 1.25, and BBB, whose
    // cash is 0.6, kept at no setting since none asks less than 0.8.
    [Fact]
    public async Task TheTestersWalkOfTheFamilyKeepsThePullbacksBaseListingsItsRulePassesAtTheSettingItStandsAt()
    {
        using var store = new TemporaryStore().Migrated();
        var night = BusinessFirstHistory.Seed(store);

        // The history reads each member's quarters from the pulled income, the same four quarters the night reads.
        foreach (var ticker in BusinessFirstHistory.Strong)
        {
            foreach (var quarter in BusinessFirstHistory.Quarters())
            {
                store.Execute(
                    "INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull) " +
                    $"VALUES ('{ticker}', '{Stamp(quarter.PeriodEnd)}', '{Stamp(quarter.FilingDate)}', '10', '10', NULL, 'constructed');");
            }
        }

        var history = new EquityBrief.Worker.Sweep.SweepHistory(store.DatabaseFile);
        var inputs = await history.ReadAsync(night);
        var read = await EquityBrief.Worker.Loop.WalkForwardTester.ReadAsync("GSPC", history, night, inputs, default);
        var readings = await EquityBrief.Worker.Loop.LoopReadings.ReadAsync(read, store.DatabaseFile, night);
        var replay = EquityBrief.Worker.Sweep.SweepIdeasRunner.Read(read.Inputs, await history.MarketAsync(night)).Replay;
        var listings = EquityBrief.Worker.Loop.RuleWalk.BaseListings(read, replay);

        string[] Kept(FundamentalsSetting setting) =>
        [
            .. EquityBrief.Worker.Loop.RuleWalk.Fundamentals(read, replay.Series, listings, readings, setting, _ => { }).Listings
                .Select(listing => read.Ticker(listing.Name) + "|" + Stamp(read.Calendar[listing.Session])),
        ];

        // The base lists each strong member on the night; the family keeps AAA alone there.
        Assert.Contains(listings, listing => read.Ticker(listing.Name) == "BBB" && read.Calendar[listing.Session] == night);
        Assert.Equal([$"AAA|{Stamp(night)}"], Kept(FundamentalsRule.Provisional).Where(one => one.EndsWith(Stamp(night), StringComparison.Ordinal)));
        Assert.DoesNotContain(Kept(FundamentalsRule.Provisional), one => one.StartsWith("BBB|", StringComparison.Ordinal) || one.StartsWith("CCC|", StringComparison.Ordinal));
        Assert.DoesNotContain(Kept(FundamentalsRule.At([0, 0, 0])), one => one.StartsWith("BBB|", StringComparison.Ordinal));
        Assert.Equal([$"AAA|{Stamp(night)}"], Kept(FundamentalsRule.At([0, 0, 2])).Where(one => one.EndsWith(Stamp(night), StringComparison.Ordinal)));
        Assert.Equal(FundamentalsRule.Name, EquityBrief.Worker.Loop.RuleWalk.Fundamentals(read, replay.Series, listings, readings, FundamentalsRule.Provisional, _ => { }).Family);

        static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
