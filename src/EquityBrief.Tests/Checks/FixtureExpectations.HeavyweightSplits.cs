using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11's ruling of the evening of 2026-10-20, clause 3: a company's value reads its newest filed
// share count adjusted by every split between the day the count was asked and the session, the splits an S&P 400 or 600
// member's own bars carry, so a split after its newest count no longer misranks it; worked by hand over a two for one.
// see: A company's value on a session is the newest share count filed before it times the session's close, the two on one basis by every plain split between the count's day and the session
public partial class FixtureExpectations
{
    // The day the counts below were asked on, whose split basis each is stated on, and the two for one after it.
    static readonly DateOnly SplitCountBasis = new(2026, 6, 30);

    static readonly DateOnly TwoForOne = new(2026, 9, 1);

    [Fact]
    public void ASplitIsReadOffTheStepABarsAdjustmentTakesAndACountIsBroughtAcrossIt()
    {
        // S closes at 50 as the store adjusts it on the two sessions before the split, as traded at 100, and at 50 as
        // traded from the split on: its close over its close as traded steps from a half to one, a two for one on the
        // split's session. A dividend of one in a hundred stepping from 0.99 to one, and a spin-off's adjustment stepping
        // from 1 over 1.272 to one, are no split.
        SessionClose[] bars =
        [
            new(TwoForOne.AddDays(-2), 50m, 100m),
            new(TwoForOne.AddDays(-1), 50m, 100m),
            new(TwoForOne, 50m, 50m),
            new(TwoForOne.AddDays(1), 51m, 51m),
        ];

        Assert.Equal([new FiledSplit(TwoForOne, 2m, 1m)], CompanyValue.SplitsIn(bars));
        Assert.Empty(CompanyValue.SplitsIn([new(TwoForOne.AddDays(-1), 99m, 100m), new(TwoForOne, 100m, 100m)]));
        Assert.Empty(CompanyValue.SplitsIn([new(TwoForOne.AddDays(-1), 100m, 127.2m), new(TwoForOne, 100m, 100m)]));

        // A count of 150 million shares asked on 2026-06-30, before the split: the session before the split reads it at
        // its close as traded of 100, $15 billion; the split's session reads it doubled, 300 million, at 50, $15 billion
        // again, where the count at 50 alone would read $7.5 billion. The same company at the same price either side of the
        // split is worth the same.
        FiledCount[] counts = [new(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 30), 150_000_000m, SplitCountBasis)];
        var splits = CompanyValue.SplitsIn(bars);

        Assert.Equal(15_000_000_000m, CompanyValue.On(bars[1], counts, splits));
        Assert.Equal(15_000_000_000m, CompanyValue.On(bars[2], counts, splits));
        Assert.Equal(7_500_000_000m, CompanyValue.On(bars[2], counts, []));
        Assert.Equal(300_000_000m, CompanyValue.SharesOn(counts[0], TwoForOne, splits));
        Assert.Equal(150_000_000m, CompanyValue.SharesOn(counts[0], TwoForOne.AddDays(-1), splits));
    }

    // Six S&P 400 members of one sector over 261 calendar days to 2026-10-01 beside IJH, as the index heavyweights' store
    // is built: the fund rising 1 per cent and falling 0.8 on alternate days, S moving 1.5 times the fund and 0.1 per cent
    // a day more, M1 1.1 times and 0.1 more, and M2 to M5 half the fund and 0.3 less, each close rounded to four places, a
    // million shares a day and four profitable quarters each. Their counts were asked on 2026-06-30: M1's 300 million, M2's
    // 900, M3's 800, M4's 700, M5's 600, and S's 150 million, S splitting two for one on 2026-09-01, its closes as traded
    // twice the store's before the split and the same from it.
    static (TemporaryStore Store, Dictionary<string, decimal[]> Closes, DateOnly[] Days) SplitSectorStore()
    {
        var store = new TemporaryStore().Migrated();
        var night = new DateOnly(2026, 10, 1);
        var days = Enumerable.Range(0, 261).Select(at => night.AddDays(at - 260)).ToArray();
        var moves = new Dictionary<string, (decimal Beta, decimal Drift, long Shares)>(StringComparer.Ordinal)
        {
            ["S"] = (1.5m, 0.001m, 150_000_000),
            ["M1"] = (1.1m, 0.001m, 300_000_000),
            ["M2"] = (0.5m, -0.003m, 900_000_000),
            ["M3"] = (0.5m, -0.003m, 800_000_000),
            ["M4"] = (0.5m, -0.003m, 700_000_000),
            ["M5"] = (0.5m, -0.003m, 600_000_000),
        };
        var fund = new decimal[261];
        var closes = moves.Keys.ToDictionary(ticker => ticker, _ => new decimal[261], StringComparer.Ordinal);

        fund[0] = 100m;

        foreach (var ticker in closes.Keys)
        {
            closes[ticker][0] = 100m;
        }

        for (var at = 1; at < 261; at++)
        {
            var move = at % 2 == 0 ? 0.01m : -0.008m;

            fund[at] = Math.Round(fund[at - 1] * (1 + move), 4);

            foreach (var (ticker, (beta, drift, _)) in moves)
            {
                closes[ticker][at] = Math.Round(closes[ticker][at - 1] * (1 + (beta * move) + drift), 4);
            }
        }

        foreach (var (ticker, (_, _, shares)) in moves)
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                string.Join(", ", Enumerable.Range(0, 261).Select(at => FormattableString.Invariant(
                    $"('{ticker}', '{Day(days[at])}', '{closes[ticker][at]}', '{Math.Round(closes[ticker][at] * 1.005m, 4)}', '{Math.Round(closes[ticker][at] * 0.995m, 4)}', '{closes[ticker][at]}', 1000000, 'test', '{Day(days[at])}T21:00:00Z', '{(ticker == "S" && days[at] < TwoForOne ? closes[ticker][at] * 2 : closes[ticker][at])}')"))) + ";");
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{ticker}', '2026-09-01T00:00:00Z', 'CIK{ticker}', 'Industrials', NULL, NULL, NULL);");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
            store.Execute(FormattableString.Invariant($"UPDATE reported_quarter SET shares = '{shares}', basis_session = '{Day(SplitCountBasis)}', basis_close = '100' WHERE ticker = '{ticker}';"));
        }

        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            string.Join(", ", Enumerable.Range(0, 261).Select(at => FormattableString.Invariant($"('IJH', '{Day(days[at])}', '{fund[at]}', '{fund[at]}', '{fund[at]}', '{fund[at]}', 'test')"))) + ";");

        return (store, closes, days);
    }

    // A frozen S&P 400 heavyweights rule reading each sector's five largest members, two leaders a sector with a beta of
    // at least 1, over the sector above on its first night, 2026-10-01. On the night every close as traded is the store's,
    // S's 2026-09-01 split behind it: S is worth 150 million shares doubled at its close, M1 300 million, M2 900, M3 800,
    // M4 700 and M5 600 million at theirs, so the five largest are S, M1, M2, M3 and M4, and the two leaders over the 251
    // sessions with a beta of 1 or more are S and M1, which the rule buys. Read at the count alone, S would be worth half
    // that, under M5, and the rule would buy M1 alone.
    [Fact]
    public async Task AHeavyweightsBookValuesACompanyThatSplitTwoForOneAfterItsCountOnItsCountBroughtAcrossTheSplit()
    {
        var (store, closes, days) = SplitSectorStore();

        using (store)
        {
            var at = Array.IndexOf(days, new DateOnly(2026, 10, 1));
            var shares = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["S"] = 300_000_000m, ["M1"] = 300_000_000m, ["M2"] = 900_000_000m, ["M3"] = 800_000_000m, ["M4"] = 700_000_000m, ["M5"] = 600_000_000m };
            var values = shares.ToDictionary(pair => pair.Key, pair => pair.Value * closes[pair.Key][at], StringComparer.Ordinal);
            var largest = values.OrderByDescending(pair => pair.Value).Take(5).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();

            // The five largest worked by hand off the closes: S and M1 among them, M5 the sixth, and S at half its value the
            // sixth in M5's place.
            Assert.Equal(["M1", "M2", "M3", "M4", "S"], largest);
            Assert.True(values["S"] / 2 < values["M5"], $"S at half {values["S"] / 2} against M5's {values["M5"]}");

            var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");
            var frozenAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
            var parameters = new Dictionary<string, double>(IndexRules.Provisional(heavyweights), StringComparer.Ordinal) { [IndexHeavyweightCandidate.LargestParameter] = 5 };

            Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", Typed(parameters))).Code);

            var register = await new CandidateRegistrar(FixedClock.At(frozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

            await new IndexFamilies(FixedClock.At(new DateTimeOffset(2026, 10, 1, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync("night-split", default, register, frozenAt.AddHours(1));

            Assert.Equal(
                ["M1|2026-10-01|Industrials", "S|2026-10-01|Industrials"],
                TextRows(store, $"SELECT ticker || '|' || entered_on || '|' || sector FROM index_heavyweight_rule_holding WHERE candidate LIKE '{FamilyRecords.LivePrefix}%' ORDER BY ticker;"));
            Assert.Equal(
                closes["S"][at],
                decimal.Parse(TextRows(store, $"SELECT entry_close FROM index_heavyweight_rule_holding WHERE candidate LIKE '{FamilyRecords.LivePrefix}%' AND ticker = 'S';").Single(), CultureInfo.InvariantCulture));
        }
    }
}
