using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Ledger;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction of 2026-11-25: a code's long prices held against the newest pull's and read as the
// class the two fall in, BB&T read as TFC and IR started at its entry by hand, the builds' short interest from the two newest
// settlements, and every build reading the long history; each worked by hand.
// see: A code whose long prices part from the newest pull's is read at the pull's adjustment where both are one instrument, and from the pull where the long side is another and thin
// see: BB&T is read as TFC throughout, BBT's S&P 500 span to Truist's rename dropped by hand and IR's span started by hand at its entry
// see: A build reads a stock's short interest only from the two newest settlements FINRA published before the session
// see: The standing ledger reads the long history's members and prices from 2019, the list the every-session table reads
public partial class FixtureExpectations
{
    static SweepBar[] Days(int from, int count, Func<int, decimal> close, Func<int, long> volume) =>
        [.. Enumerable.Range(from, count).Select(day => Dated(day, close(day), volume(day)))];

    // 120 sessions both hold. The same closes and volumes agree. One instrument whose long side keeps a 3-for-2 step the pull
    // adjusted, 60 to 40 on day 60, steps once: its sessions before the step, and 10 before the pull's first, read at 40, its
    // own from the step on; a store's bar from day 100 is never moved. Three days 5% apart on one side move nothing. Another
    // instrument trading 50 to 349 shares against the pull's 50,000 reads the pull's closes and none of its own before the pull's
    // last; another trading as much as the pull is kept as it is, listed.
    [Fact]
    public void ACodesLongPricesAreReadAsTheClassTheyFallInAgainstTheNewestPull()
    {
        var pulled = Days(10, 120, _ => 40m, _ => 50_000);

        var same = LongPulled.Compare(Days(10, 120, _ => 40m, _ => 50_000), pulled);
        Assert.Equal(LongPulledKind.Agrees, same.Kind);
        Assert.False(same.Listed);

        var stepped = Days(0, 130, day => day < 60 ? 60m : 40m, _ => 50_000);
        var spun = LongPulled.Compare(stepped, pulled);
        Assert.Equal((LongPulledKind.OneInstrument, 120, 50, 1), (spun.Kind, spun.Both, spun.Apart, spun.Steps));

        var read = LongPulled.Read(stepped, pulled, null);
        Assert.All(read, bar => Assert.Equal(40m, bar.Close));
        Assert.Equal(stepped.Select(bar => bar.Volume), read.Select(bar => bar.Volume));

        var withStore = LongPulled.Read([.. stepped[..100], .. Days(100, 30, day => day < 110 ? 60m : 61m, _ => 50_000)], pulled, Dated(100, 60m).Session);
        Assert.All(withStore[..60], bar => Assert.Equal(40m, bar.Close));
        Assert.All(withStore[100..], bar => Assert.True(bar.Close >= 60m));

        var blip = Days(10, 120, day => day is 50 or 51 or 52 ? 42m : 40m, _ => 50_000);
        Assert.Equal(LongPulledKind.Agrees, LongPulled.Compare(blip, pulled).Kind);
        Assert.Equal(blip, LongPulled.Read(blip, pulled, null));

        var thin = Days(0, 140, day => 5m + (day % 3), day => 50 + (day * 7 % 300));
        var other = LongPulled.Compare(thin, pulled);
        Assert.Equal((LongPulledKind.AnotherThin, 50_000L), (other.Kind, other.PulledVolume));
        Assert.True(other.LongVolume < LongPulled.ThinVolume);

        var fromPull = LongPulled.Read(thin, pulled, null);
        Assert.Equal(Enumerable.Range(10, 120).Select(day => Dated(day, 40m).Session).Concat(Enumerable.Range(130, 10).Select(day => Dated(day, 0m).Session)), fromPull.Select(bar => bar.Session));
        Assert.All(fromPull[..120], bar => Assert.Equal(40m, bar.Close));

        var kept = Days(10, 120, day => 7m + (day % 5), day => 30_000 + (day * 997 % 20_000));
        Assert.Equal(LongPulledKind.AnotherKept, LongPulled.Compare(kept, pulled).Kind);
        Assert.Equal(kept, LongPulled.Read(kept, pulled, null));
    }

    // Over a constructed store of 120 sessions, both codes members throughout: SPN's long prices keep a step from 60 to 40 on
    // session 60 that its newest pull adjusted, and the reader reads it at 40 throughout; THN's long side trades 50 to 349 shares at
    // 5 from 10 sessions before its pull's first, and the reader reads the pull's 40 and none of the 10. An older pull of THN
    // is never read.
    [Fact]
    public async Task TheLongReaderReadsACodesLongPricesAgainstItsNewestPull()
    {
        using var store = new TemporaryStore().Migrated();
        var stamp = (DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var days = Enumerable.Range(0, 200).Select(at => new DateOnly(2019, 1, 7).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(130).ToArray();
        string Row(string ticker, DateOnly day, decimal close, long volume, string last) =>
            FormattableString.Invariant($"('{ticker}', '{stamp(day)}', '{close}', '{close}', '{close}', '{close}', '{close}', {volume}, '{last}')");

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{stamp(day)}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES "
            + string.Join(", ", new[] { "SPN", "THN" }.Select(ticker => $"('GSPC', '{ticker}', '{stamp(days[0])}', NULL, 'provider', 'r')")) + ";");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => Row("SPN", day, at < 60 ? 60m : 40m, 50_000, "r")).Concat(days.Select((day, at) => Row("THN", day, 5m, 50 + (at * 7 % 300), "r")))) + ";");
        store.Execute("INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES "
            + string.Join(", ", days[10..].Select(day => Row("SPN", day, 40m, 50_000, "history-pull-2"))
                .Concat(days[10..].Select(day => Row("THN", day, 40m, 50_000, "history-pull-2")))
                .Concat(days[10..].Select(day => Row("THN", day, 9m, 50_000, "history-pull-1")))) + ";");

        var read = await new LongHistoryReader(store.DatabaseFile).ReadAsync("GSPC", days[^1]);
        var bars = (string ticker) => read.Names.Single(one => one.Ticker == ticker).Bars;

        Assert.All(bars("SPN"), bar => Assert.Equal(40m, bar.Close));
        Assert.Equal(days[10..], bars("THN").Select(bar => bar.Session));
        Assert.All(bars("THN"), bar => Assert.Equal(40m, bar.Close));
    }

    // The provider's S&P 500 spans: BBT from 2018-10-01 to Truist's rename is dropped, TFC holding BB&T throughout, and IR's
    // span from 2018-10-01 starts at 2020-03-03, the code before it Ingersoll-Rand plc's under TT; PARA's from 2025-08-07 is
    // still dropped and a span starting after a hand start keeps its own.
    [Fact]
    public void BbtsSpanIsDroppedAndIrsStartedByHand()
    {
        var spans = LongSpans.Of(
            new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(),
            [
                new ProviderSpan("BBT", new DateOnly(2018, 10, 1), new DateOnly(2019, 12, 9)),
                new ProviderSpan("TFC", new DateOnly(2018, 10, 1), null),
                new ProviderSpan("IR", new DateOnly(2018, 10, 1), null),
                new ProviderSpan("TT", new DateOnly(2018, 10, 1), null),
                new ProviderSpan("PARA", new DateOnly(2025, 8, 7), null),
            ]);

        Assert.DoesNotContain(spans, span => span.Ticker is "BBT" or "PARA");
        Assert.Equal(new DateOnly(2020, 3, 3), spans.Single(span => span.Ticker == "IR").Joined);
        Assert.Equal(new DateOnly(2018, 10, 1), spans.Single(span => span.Ticker == "TFC").Joined);
        Assert.Equal(new DateOnly(2018, 10, 1), spans.Single(span => span.Ticker == "TT").Joined);
    }

    // Settlements published on 10 Jan, 25 Jan, 9 Feb and 24 Feb: on 26 Feb a report of 9 Feb or 24 Feb is read and one of 25 Jan
    // is not; on 24 Feb itself, the newest two before it are 25 Jan and 9 Feb; before the first nothing is read. From the pin move
    // before the run of 2027-02-06 the shared code reads by the same rule, so the check lists neither the short interest nor the
    // seven as expected.
    [Fact]
    public void ABuildReadsShortInterestFromTheTwoNewestSettlementsAlone()
    {
        DateOnly[] settlements = [new(2026, 1, 10), new(2026, 1, 25), new(2026, 2, 9), new(2026, 2, 24)];

        Assert.True(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 26)));
        Assert.True(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 2, 24), new DateOnly(2026, 2, 26)));
        Assert.False(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 1, 25), new DateOnly(2026, 2, 26)));
        Assert.True(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 1, 25), new DateOnly(2026, 2, 24)));
        Assert.False(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 1, 10), new DateOnly(2026, 2, 24)));
        Assert.False(SetupLedger.ShortInterestCurrent(settlements, new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 5)));
        Assert.Equal(2, SetupLedger.ShortInterestSettlementsRead);

        Assert.Equal(SetupLedger.ShortInterestSettlementsRead, LedgerContext.ShortInterestSettlementsRead);
        Assert.False(TableOwnReadings.Expected("short_interest_share"));
        Assert.False(TableOwnReadings.Expected("liquidity"));
        Assert.False(TableOwnReadings.Expected("rsi"));

        // A build's reading worked to 0.05 is left none where its report of 25 Jan is past the two newest before 26 Feb, kept where
        // the report is of 9 Feb, and left as it is with no settlements given, as a night reads it.
        var at = Core.Ledger.LedgerReadings.IndexOf("short_interest_share");
        var reports = new Dictionary<string, IReadOnlyList<LedgerShortInterest>>(StringComparer.Ordinal)
        {
            ["OLD"] = [new LedgerShortInterest(new DateOnly(2026, 1, 25), 100m)],
            ["NEW"] = [new LedgerShortInterest(new DateOnly(2026, 1, 25), 100m), new LedgerShortInterest(new DateOnly(2026, 2, 9), 100m)],
        };
        double?[] Worked() { var readings = new double?[Core.Ledger.LedgerReadings.Count]; readings[at] = 0.05; return readings; }
        var (old, fresh, night) = (Worked(), Worked(), Worked());

        SetupLedger.CapShortInterest(old, reports, settlements, "OLD", new DateOnly(2026, 2, 26));
        SetupLedger.CapShortInterest(fresh, reports, settlements, "NEW", new DateOnly(2026, 2, 26));
        SetupLedger.CapShortInterest(night, reports, null, "OLD", new DateOnly(2026, 2, 26));
        Assert.Equal<double?>([null, 0.05, 0.05], [old[at], fresh[at], night[at]]);

        // The builds' compute and the table's rows cap it after the catalogue's readings; the night's two paths never do, and the
        // readings' version's sources hold no cap.
        var ledger = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.cs"));
        var table = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.SessionReadings.cs"));
        var tonight = ledger[ledger.IndexOf("async Task<(IndexNightInputs Inputs, LedgerMarket Market, LedgerContext Context)?> TonightAsync", StringComparison.Ordinal)..ledger.IndexOf("public static async Task<(LedgerMarket Market, LedgerContext Context)> HistoryReadingsAsync", StringComparison.Ordinal)];
        var indexNight = ledger[ledger.IndexOf("async Task<LedgerIndex> IndexNightAsync", StringComparison.Ordinal)..];

        Assert.Contains("CapShortInterest(row.Readings, context.ShortInterest, Settlements, row.Ticker, calendar[at]);", ledger, StringComparison.Ordinal);
        Assert.Contains("CapShortInterest(readings, context.ShortInterest, Settlements, one.Name.Ticker, calendar[at]);", table, StringComparison.Ordinal);
        Assert.DoesNotContain("CapShortInterest", tonight, StringComparison.Ordinal);
        Assert.DoesNotContain("CapShortInterest", indexNight[..indexNight.IndexOf("BeginTransactionAsync", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.All(LedgerSources, source => Assert.DoesNotContain("CapShortInterest", File.ReadAllText(source), StringComparison.Ordinal));
    }

    // Every build and the standing check read the long history's members and prices, its market series and its filers wherever
    // the store holds the index's long spans, and the pulled history only where it holds none; the heavyweights read from 2005
    // on the long ledger alone.
    [Fact]
    public void EveryBuildAndTheStandingCheckReadTheLongHistoryWhereTheStoreHoldsItsSpans()
    {
        var ledger = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.cs"));
        var prepare = ledger[ledger.IndexOf("async Task<PreparedHistory> PrepareAsync", StringComparison.Ordinal)..ledger.IndexOf("async Task<(LiveRulesRead Rules, DateTimeOffset At)> LiveRulesAsync", StringComparison.Ordinal)];
        var check = ledger[ledger.IndexOf("public async Task<int> CheckAsync", StringComparison.Ordinal)..ledger.IndexOf("static string Shown(double? reading)", StringComparison.Ordinal)];

        foreach (var body in new[] { prepare, check })
        {
            Assert.Contains("longHistory ??= await LongSpansHeldAsync(connection, index, cancellation) ? new History.LongHistoryReader(databaseFile) : null;", body, StringComparison.Ordinal);
            Assert.Contains("MarketAsync(connection, longHistory is null ? PulledMarketBars : LongMarketBars, through, cancellation)", body, StringComparison.Ordinal);
            Assert.Contains("longHistory is null ? [PulledFilers, Filers] : [LongFilers, PulledFilers, Filers]", body, StringComparison.Ordinal);
        }

        Assert.Contains("longLedger ? Core.History.LongLedger.FirstScored : null", prepare, StringComparison.Ordinal);
        Assert.Contains("const string LongSpansOnIndex = \"SELECT 1 FROM long_span WHERE index_code = $index LIMIT 1;\";", ledger, StringComparison.Ordinal);

        // The long ledger written again from a session writes every session from it whatever its rows' pin, and the verb takes
        // the session only with '--again'.
        var longBuild = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Ledger", "SetupLedger.Long.cs"));
        var verb = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Program.cs"));

        Assert.Contains("var stored = again && againFrom is { } writeFrom\n            ? prepared.Calendar.Where(session => session < writeFrom).ToHashSet()\n            : await LongSessionsPassedAsync(connection, index, again, cancellation);", longBuild.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("(from is not null && (!longAgain || longFrom is null))", verb, StringComparison.Ordinal);
        Assert.Contains(".LongBuildAsync(store.DataRoot, index, Console.Out, again: longAgain, againFrom: longFrom)", verb, StringComparison.Ordinal);
    }
}
