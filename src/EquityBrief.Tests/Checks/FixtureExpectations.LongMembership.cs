using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.3: each schedule form of the trust's older filings read from a captured page and a summary refused;
// each key of the long history's matching worked by hand over constructed schedules and closes; a match by hand at the cent and
// a refusal; the stop on both sides on each measure and a fall by half named and named by no one; each kind a left-out company
// left as, worked from constructed filings; the spans worked by hand; the sampled S&P announcements read against the spans; the
// write over a constructed store moving no row of a standing table; and no source a rule's version pins among those 23.3 writes.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public partial class FixtureExpectations
{
    internal static string[] LongMembershipClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The long history's stop"),
        CheckReach.Key(Scope.LimitsTable, "The long history's hand check"),
        CheckReach.Key(Scope.LimitsTable, "The long history's keys"),
        CheckReach.Key(Scope.LimitsTable, "The long history's left-out kinds"),
        CheckReach.Key(Scope.FailureTable, "The long history's matching under its stop"),
    ];

    // Every row 23.3 adds to the document.
    internal static string[] LongMembershipRows => [.. ComponentAccess.LongMembershipRows, .. SchemaColumns.LongStoreRows, .. LongMembershipClaims, .. Reading.ReadSurface.LongMembershipPageRows];

    static string LongFixture => Path.Combine(Repository.Root, "fixtures", "long-membership-2026-11-07");

    static string LongDocument(string file) => File.ReadAllText(Path.Combine(LongFixture, "documents", file));

    // The four forms, each holding's figures typed from the page as a person reads it.
    [Fact]
    public void EachScheduleFormOfTheTrustsOlderFilingsIsReadFromACapturedPageAndASummaryIsRefused()
    {
        // The N-Q of 2004-12-31, the fund named on the page after its heading, a footnote mark set apart from the name.
        var ijh = LongSchedules.Parse(LongDocument("0001193125-05-039953-dnq.htm"), "MID", new DateOnly(2004, 12, 31), "0001193125-05-039953");

        Assert.Equal(12, ijh.Holdings.Count);
        Assert.Equal(new LongHolding("Catalina Marketing Corp.", "ADVERTISING", 112_416m, 3_330_886m), ijh.Holdings[0]);
        Assert.Equal(new LongHolding("Alliant Techsystems Inc.", "AEROSPACE & DEFENSE", 81_036m, 5_298_134m), ijh.Holdings[2]);
        Assert.Equal("Titan Corp. (The)", ijh.Holdings[4].Name);
        Assert.Equal(new LongHolding("Timberland Co. Class A", "APPAREL", 74_014m, 4_638_457m), ijh.Holdings[9]);

        // The N-Q of 2018-06-30, its heading's page carrying the fund's name, the industry heading with its share after a dash.
        var ivv = LongSchedules.Parse(LongDocument("0001193125-18-261300-d577177dnq.htm"), "GSPC", new DateOnly(2018, 6, 30), "0001193125-18-261300");

        Assert.Equal(12, ivv.Holdings.Count);
        Assert.Equal(new LongHolding("Arconic Inc.", "Aerospace & Defense", 2_754_108m, 46_847_377m), ivv.Holdings[0]);
        Assert.Equal(new LongHolding("TransDigm Group Inc.", "Aerospace & Defense", 312_106m, 107_720_265m), ivv.Holdings[11]);

        // The annual report of 2005-03-31, its summary first and its complete schedule further on, the complete one read.
        var ijr = LongSchedules.Parse(LongDocument("0001193125-05-122978-dncsr.htm"), "SML", new DateOnly(2005, 3, 31), "0001193125-05-122978");

        Assert.Equal(12, ijr.Holdings.Count);
        Assert.Equal(new LongHolding("ADVO Inc.", "ADVERTISING", 202_105m, 7_568_832m), ijr.Holdings[0]);
        Assert.Equal(new LongHolding("AAR Corp.", "AEROSPACE & DEFENSE", 209_042m, 2_842_971m), ijr.Holdings[1]);
        Assert.DoesNotContain(ijr.Holdings, holding => holding.Name.StartsWith("Other securities", StringComparison.Ordinal));
        Assert.DoesNotContain(ijr.Holdings, holding => holding.Name.StartsWith("Oshkosh", StringComparison.Ordinal));

        // The semi-annual report of 2013-09-30, carrying a summary alone, refused rather than read as holding its largest names.
        var refused = Assert.Throws<FormatException>(() => LongSchedules.Parse(LongDocument("0001193125-13-466531-d609194dncsrs.htm"), "MID", new DateOnly(2013, 9, 30), "0001193125-13-466531"));

        Assert.EndsWith("only a summary of it.", refused.Message, StringComparison.Ordinal);
    }

    static readonly DateOnly[] KeyQuarters = [new(2010, 3, 31), new(2010, 6, 30), new(2010, 9, 30), new(2010, 12, 31)];

    static ListedSymbol Stock(string code, string name) => new(code, name, "NYSE", ProviderSymbols.CommonStock, null, false);

    static LongHolding Held(string name, decimal price) => new(name, "Things", 100m, price * 100m);

    // A constructed fund's schedules, each holding at 100 shares and the value a share given, quarter end by quarter end.
    static IReadOnlyList<LongSchedule> KeySchedules(params (string Name, decimal?[] Prices)[] holdings) =>
    [
        .. KeyQuarters.Select((period, at) => new LongSchedule(
            "MID",
            period,
            "constructed",
            [.. holdings.Where(holding => at < holding.Prices.Length && holding.Prices[at] is not null).Select(holding => Held(holding.Name, holding.Prices[at]!.Value))])),
    ];

    static IReadOnlyDictionary<DateOnly, QuarterCloses> KeyCloses(params (string Code, decimal?[] Closes)[] codes) =>
        KeyQuarters.Select((period, at) => (period, Closes: (IReadOnlyDictionary<string, decimal>)codes
                .Where(code => at < code.Closes.Length && code.Closes[at] is not null)
                .ToDictionary(code => code.Code, code => code.Closes[at]!.Value, StringComparer.Ordinal)))
            .ToDictionary(pair => pair.period, pair => new QuarterCloses(pair.Closes));

    static LongMatch? KeyMatch(IReadOnlyDictionary<(DateOnly Period, int Place), LongMatch> matches, IReadOnlyList<LongSchedule> schedules, int quarter, string name) =>
        schedules[quarter].Holdings.Select((holding, place) => (holding, place)).Where(one => one.holding.Name == name).Select(one => matches.GetValueOrDefault((schedules[quarter].Period, one.place))).FirstOrDefault();

    // Each key worked by hand. Alpha's name reads ALPW at the level at both quarter ends; Beta's no name the lists carry, GAMH at
    // the cent at two adjacent quarter ends; Single Cent's code at the cent at one quarter end alone; Delta's own code at its second
    // quarter end by name where another code falls at the cent at both; Epsilon's code at the cent at its second and third quarter
    // ends and in step at its first; Kappa's code at the level at both but its ratio to the fund's values stepping too far;
    // Lambda's code the one its company's annual report names, at half the value a share at both.
    [Fact]
    public void EachKeyOfTheLongHistorysMatchingIsWorkedByHandOverConstructedSchedulesAndCloses()
    {
        ListedSymbol[] symbols =
        [
            Stock("ALPW", "Alpha Widgets Inc"),
            Stock("GAMH", "Gamma Holdings Inc"),
            Stock("SNGL", "Zeta Things Inc"),
            Stock("DELT", "Delta Corp"),
            Stock("ZZZZ", "Omega Unrelated Inc"),
            Stock("EPSN", "Unrelated Name Co"),
            Stock("KAPA", "Kappa Moves Inc"),
            Stock("LMBD", "Mu Corp"),
        ];
        var schedules = KeySchedules(
            ("Alpha Widgets Inc.", [10.00m, 11.00m]),
            ("Beta Renamed Corp.", [20.00m, 21.00m]),
            ("Single Cent Co.", [30.00m, 31.00m]),
            ("Delta Corp.", [40.00m, 41.00m]),
            ("Epsilon Rebrand Inc.", [50.00m, 51.00m, 52.00m]),
            ("Kappa Moves Inc.", [60.00m, 60.00m]),
            ("Lambda Systems Inc.", [70.00m, 71.00m]));
        var closes = KeyCloses(
            ("ALPW", [10.00m, 11.00m]),
            ("GAMH", [20.00m, 21.00m]),
            ("SNGL", [30.00m, 35.00m]),
            ("DELT", [null, 41.00m]),
            ("ZZZZ", [40.00m, 41.00m]),
            ("EPSN", [50.10m, 51.00m, 52.00m]),
            ("KAPA", [57.50m, 62.50m]),
            ("LMBD", [35.00m, 35.50m]));
        var matches = LongMatching.Match(
            schedules,
            closes,
            new HoldingMatcher(symbols),
            names: new LongNames(symbols),
            annualReport: name => name == "Lambda Systems Inc." ? ["LMBD"] : []);

        Assert.Equal(new[] { ("ALPW", LongMatching.ByName), ("ALPW", LongMatching.ByName) }, new[] { 0, 1 }.Select(at => (KeyMatch(matches, schedules, at, "Alpha Widgets Inc.")!.Ticker, KeyMatch(matches, schedules, at, "Alpha Widgets Inc.")!.By)));
        Assert.Equal(new[] { ("GAMH", LongMatching.ByPrice), ("GAMH", LongMatching.ByPrice) }, new[] { 0, 1 }.Select(at => (KeyMatch(matches, schedules, at, "Beta Renamed Corp.")!.Ticker, KeyMatch(matches, schedules, at, "Beta Renamed Corp.")!.By)));

        // One quarter end at the cent is refused: 35.00 against 31.00 at the second.
        Assert.Null(KeyMatch(matches, schedules, 0, "Single Cent Co."));
        Assert.Null(KeyMatch(matches, schedules, 1, "Single Cent Co."));

        // Delta's second quarter end is its own code by name, and the code at the cent at both gives way to it at the first.
        Assert.Equal(("DELT", LongMatching.ByName), (KeyMatch(matches, schedules, 1, "Delta Corp.")!.Ticker, KeyMatch(matches, schedules, 1, "Delta Corp.")!.By));
        Assert.NotEqual("ZZZZ", KeyMatch(matches, schedules, 0, "Delta Corp.")?.Ticker);

        // Epsilon at the cent at its second and third, 50.10 against 50.00 at its first, in step and carried back.
        Assert.Equal(("EPSN", LongMatching.ByPrice), (KeyMatch(matches, schedules, 1, "Epsilon Rebrand Inc.")!.Ticker, KeyMatch(matches, schedules, 1, "Epsilon Rebrand Inc.")!.By));
        Assert.Equal(("EPSN", LongMatching.ByCarried), (KeyMatch(matches, schedules, 0, "Epsilon Rebrand Inc.")!.Ticker, KeyMatch(matches, schedules, 0, "Epsilon Rebrand Inc.")!.By));

        // Kappa's code at 57.50 and 62.50 against 60.00 twice: inside the level at each, its ratio stepping from 1.043 to 0.960,
        // more than the twentieth a step holds to, and the last quarter end held, refused.
        Assert.Null(KeyMatch(matches, schedules, 0, "Kappa Moves Inc."));
        Assert.Null(KeyMatch(matches, schedules, 1, "Kappa Moves Inc."));

        // Lambda's code its own report names, at a ratio of 2 at both quarter ends.
        Assert.Equal(("LMBD", LongMatching.ByFiling), (KeyMatch(matches, schedules, 0, "Lambda Systems Inc.")!.Ticker, KeyMatch(matches, schedules, 0, "Lambda Systems Inc.")!.By));
        Assert.Equal(("LMBD", LongMatching.ByFiling), (KeyMatch(matches, schedules, 1, "Lambda Systems Inc.")!.Ticker, KeyMatch(matches, schedules, 1, "Lambda Systems Inc.")!.By));
    }

    // A match by hand stands at the cent and is refused a cent away; a refusal removes its name's code at every quarter end.
    [Fact]
    public void AMatchByHandStandsWithinHalfACentAndARefusalRemovesItsCodeAtEveryQuarterEnd()
    {
        ListedSymbol[] symbols = [Stock("ALPW", "Alpha Widgets Inc"), Stock("NUHC", "Some Other Name Inc")];
        var schedules = KeySchedules(("Alpha Widgets Inc.", [10.00m, 11.00m]), ("Nu Holdings Co.", [80.00m]));
        var hand = new Dictionary<(DateOnly Period, string Name), LongHandMatch>
        {
            [(KeyQuarters[0], "Nu Holdings Co.")] = new("MID", KeyQuarters[0], "Nu Holdings Co.", "NUHC", "constructed"),
        };

        IReadOnlyDictionary<(DateOnly, int), LongMatch> Run(decimal close, Func<string, string, bool>? refused = null) =>
            LongMatching.Match(schedules, KeyCloses(("ALPW", [10.00m, 11.00m]), ("NUHC", [close])), new HoldingMatcher(symbols), names: new LongNames(symbols), byHand: hand, refused: refused);

        var within = Run(80.004m);

        Assert.Equal(("NUHC", LongMatching.ByHand), (KeyMatch(within, schedules, 0, "Nu Holdings Co.")!.Ticker, KeyMatch(within, schedules, 0, "Nu Holdings Co.")!.By));
        Assert.Null(KeyMatch(Run(80.01m), schedules, 0, "Nu Holdings Co."));

        var refused = Run(80.004m, (name, code) => name == "Alpha Widgets Inc." && code == "ALPW");

        Assert.Null(KeyMatch(refused, schedules, 0, "Alpha Widgets Inc."));
        Assert.Null(KeyMatch(refused, schedules, 1, "Alpha Widgets Inc."));
        Assert.Equal("ALPW", KeyMatch(Run(80.004m), schedules, 1, "Alpha Widgets Inc.")!.Ticker);

        // Every hand match and every refusal in the long history's code names what was read.
        Assert.All(LongHandMatches.All, match => Assert.True(match.Read.Length > 40, match.Name));
        Assert.All(LongHandMatches.Refused, refusal => Assert.True(refusal.Read.Length > 40, refusal.Name));
    }

    static LongSchedule Floored(string index, DateOnly period, params decimal[] values) =>
        new(index, period, "constructed", [.. values.Select((value, at) => new LongHolding(FormattableString.Invariant($"Holding {at}"), null, 10m, value))]);

    // The stop on both sides on each measure, the lower coverage label's years, and a fall by half named and named by no one.
    [Fact]
    public void AQuarterEndUnderTheStopOnEitherMeasureAndAFallNamedByNoOneStopTheRunAndANamedFallDoesNot()
    {
        var period = new DateOnly(2008, 12, 31);
        LongSchedule[] schedules =
        [
            // Nine of ten matched at 100 each: 0.90 on both, standing.
            Floored("GSPC", period, 100, 100, 100, 100, 100, 100, 100, 100, 100, 100),
            // Eight of ten, the two left out worth 1 each: 0.80 of the holdings and 800 of 802 of the value, under the stop on
            // the holdings alone.
            Floored("MID", period, 100, 100, 100, 100, 100, 100, 100, 100, 1, 1),
            // Nine of ten, the one left out worth 200: 900 of 1,100, 0.818 of the value.
            Floored("SML", period, 100, 100, 100, 100, 100, 100, 100, 100, 100, 200),
        ];
        bool Matched(LongSchedule schedule, int place) => schedule.Index == "MID" ? place < 8 : place < 9;

        var coverage = LongFloors.Coverage(schedules, Matched);

        Assert.True(coverage.Single(one => one.Index == "GSPC").Stands);
        Assert.False(coverage.Single(one => one.Index == "MID").Stands);
        Assert.False(coverage.Single(one => one.Index == "SML").Stands);
        Assert.Equal(0.90, coverage.Single(one => one.Index == "GSPC").HoldingsShare, 6);
        Assert.Equal(900m / 1100m, coverage.Single(one => one.Index == "SML").ValueShare);
        Assert.True(coverage.Single(one => one.Index == "MID").ValueShare >= LongFloors.ValueStop);
        Assert.Equal(0.80, coverage.Single(one => one.Index == "MID").HoldingsShare, 6);
        Assert.Equal(2, LongFloors.Stops(coverage, []).Count);

        Assert.True(coverage.Single(one => one.Index == "MID").Lower);
        Assert.False(coverage.Single(one => one.Index == "GSPC").Lower);
        Assert.True(LongFloors.IsLower("SML", new DateOnly(2004, 12, 31)));
        Assert.False(LongFloors.IsLower("SML", new DateOnly(2009, 3, 31)));

        // Two quarter ends of three holdings matched to none: a named failure from 20 to 5, one named by no one from 20 to 10, and
        // one from 20 to 10.10, which is more than half.
        LongSchedule[] fell =
        [
            new("GSPC", new DateOnly(2007, 12, 31), "constructed", [new("Ambac Financial Group Inc.", null, 10m, 200m), new("Nameless Fallen Inc.", null, 10m, 200m), new("Half Less Inc.", null, 10m, 200m)]),
            new("GSPC", new DateOnly(2008, 3, 31), "constructed", [new("Ambac Financial Group Inc.", null, 10m, 50m), new("Nameless Fallen Inc.", null, 10m, 100m), new("Half Less Inc.", null, 10m, 101m)]),
        ];
        var falls = LongFloors.Falls(fell, (_, _) => false);

        Assert.Equal(["Ambac Financial Group Inc.", "Nameless Fallen Inc."], falls.Select(fall => fall.Name));

        var stops = LongFloors.Stops([], falls);

        Assert.Single(stops);
        Assert.Contains("Nameless Fallen Inc.", stops[0], StringComparison.Ordinal);
        Assert.Equal(LongFloors.Failed, LongFloors.LeftOutAs("Ambac Financial Group Inc.")!.NamedAs);
        Assert.Equal(LongFloors.Unpriced, LongFloors.LeftOutAs("Belo Corp. Class A")!.NamedAs);
        Assert.Equal(LongFloors.BoughtOut, LongFloors.LeftOutAs("Countrywide Financial Corp.")!.NamedAs);
    }

    static FilerFiling Filed(string form, DateOnly filed, params string[] items) => new("0000000000-00-000000", form, filed, null, items, "doc.htm");

    // Each kind a left-out company left as, worked from constructed filings around a last quarter end of 2010-06-30.
    [Fact]
    public void EachKindALeftOutCompanyLeftAsIsWorkedByHandFromConstructedFilings()
    {
        var last = new DateOnly(2010, 6, 30);

        Assert.Equal(LongLeaving.Failed, LongLeaving.Of("A Co.", [Filed("8-K", new DateOnly(2010, 9, 1), "1.03", "9.01")], last).As);
        Assert.Equal(LongLeaving.BoughtOut, LongLeaving.Of("B Co.", [Filed("DEFM14A", new DateOnly(2010, 3, 1))], last).As);
        Assert.Equal(LongLeaving.BoughtOut, LongLeaving.Of("C Co.", [Filed("8-K", new DateOnly(2011, 1, 15), "2.01", "5.01")], last).As);

        // An annual report 17 months after went on; one 8 months after says nothing alone.
        Assert.Equal(LongLeaving.Dropped, LongLeaving.Of("D Co.", [Filed("10-K", new DateOnly(2011, 12, 1))], last).As);
        Assert.Equal(LongLeaving.Unknown, LongLeaving.Of("E Co.", [Filed("10-K", new DateOnly(2011, 3, 1))], last).As);

        // A bankruptcy 30 months after is outside the window and the company went on before it.
        Assert.Equal(LongLeaving.Dropped, LongLeaving.Of("F Co.", [Filed("10-K", new DateOnly(2012, 3, 1)), Filed("8-K", new DateOnly(2013, 1, 1), "1.03")], last).As);

        // A failure before a buy-out is a failure; no filer found is unknown; the operator's naming stands, but unpriced.
        Assert.Equal(LongLeaving.Failed, LongLeaving.Of("G Co.", [Filed("DEFM14A", new DateOnly(2010, 8, 1)), Filed("8-K", new DateOnly(2010, 9, 1), "1.03")], last).As);
        Assert.Equal(LongLeaving.Unknown, LongLeaving.Of("H Co.", null, last).As);
        Assert.Equal(LongLeaving.Failed, LongLeaving.Of("Ambac Financial Group Inc.", null, last).As);
        Assert.StartsWith("named by the operator", LongLeaving.Of("Ambac Financial Group Inc.", null, last).Read, StringComparison.Ordinal);
        Assert.Equal(LongLeaving.BoughtOut, LongLeaving.Of("Belo Corp. Class A", [Filed("DEFM14A", new DateOnly(2010, 1, 1))], last).As);

        // Each year's left-out share by kind over the value the fund held at the year's quarter ends: 100 bought out and 50 failed of
        // 1,000 held over two quarter ends.
        LongSchedule[] year =
        [
            new("MID", new DateOnly(2010, 3, 31), "constructed", [new("Kept", null, 1m, 350m), new("Bought", null, 1m, 100m), new("Gone", null, 1m, 50m)]),
            new("MID", new DateOnly(2010, 6, 30), "constructed", [new("Kept", null, 1m, 500m)]),
        ];
        var shares = LongLeaving.ByYear(year, (schedule, place) => schedule.Holdings[place].Name == "Kept", (_, name) => new LongLeft(name == "Bought" ? LongLeaving.BoughtOut : LongLeaving.Failed, null, null, "constructed")).Single();

        Assert.Equal(0.10m, shares.Share(LongLeaving.BoughtOut));
        Assert.Equal(0.05m, shares.Share(LongLeaving.Failed));
        Assert.Equal(0m, shares.Share(LongLeaving.Dropped));
    }

    // The spans worked by hand: a leaver, a name away at one snapshot and one away at two, a name in two funds, IVV's spans
    // closing at the provider's dates, and the provider's spans with no start read by code and by name, two codes of one company once.
    [Fact]
    public void TheLongHistorysSpansAreWorkedByHandForALeaverANameAwayTwoSnapshotsAndANameInTwoFunds()
    {
        DateOnly[] periods = [new(2010, 3, 31), new(2010, 6, 30), new(2010, 9, 30), new(2010, 12, 31), new(2011, 3, 31), new(2011, 6, 30)];

        IReadOnlyDictionary<DateOnly, IReadOnlySet<string>> Held(params (string Code, int[] At)[] codes) =>
            periods.Select((period, at) => (period, Codes: (IReadOnlySet<string>)codes.Where(code => code.At.Contains(at)).Select(code => code.Code).ToHashSet(StringComparer.Ordinal)))
                .ToDictionary(pair => pair.period, pair => pair.Codes);

        var snapshots = new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(StringComparer.Ordinal)
        {
            ["MID"] = Held(("LEAV", [0, 1]), ("AWAY", [0, 3, 4, 5]), ("ONCE", [0, 2, 3, 4, 5]), ("TWIN", [0, 1, 2])),
            ["GSPC"] = Held(("TWIN", [2, 3, 4, 5]), ("NOJN", [1, 2])),
        };
        ProviderSpan[] provider =
        [
            new("PROV", new DateOnly(2013, 5, 1), null),
            new("NOJN", null, null),
            new("BYNM", null, new DateOnly(2014, 1, 2), "Byname Industries Inc"),
            new("TWO1", new DateOnly(2015, 1, 2), null, "Twofold Corp"),
            new("TWO2", new DateOnly(2015, 1, 2), null, "Twofold Corp"),
            new("GONE", new DateOnly(2001, 1, 2), new DateOnly(2010, 5, 3)),
        ];
        var byName = new Dictionary<string, DateOnly>(StringComparer.Ordinal) { [HoldingMatcher.NameKey("Byname Industries Inc.")] = periods[0] };
        var spans = LongSpans.Of(snapshots, provider, byName);

        string[] Of(string index, string ticker) =>
        [
            .. spans.Where(span => span.Index == index && span.Ticker == ticker).Select(span => FormattableString.Invariant($"{span.Joined:yyyy-MM-dd}..{span.Left:yyyy-MM-dd} {span.ReadFrom}")),
        ];

        Assert.Equal(["2010-03-31..2010-09-30 fund"], Of("MID", "LEAV"));
        Assert.Equal(["2010-03-31..2010-06-30 fund", "2010-12-31.. fund"], Of("MID", "AWAY"));
        Assert.Equal(["2010-03-31.. fund"], Of("MID", "ONCE"));

        // TWIN in both funds at 2010-09-30 reads in the S&P 500 there, so its S&P 400 span ends at that snapshot.
        Assert.Equal(["2010-03-31..2010-09-30 fund"], Of("MID", "TWIN"));
        Assert.Equal(["2010-09-30..2012-01-01 fund"], Of("GSPC", "TWIN"));

        // From 2012 the provider's dates, a span with no start taking the first snapshot holding its code or its company's name.
        Assert.Equal(["2013-05-01.. provider"], Of("GSPC", "PROV"));
        Assert.Equal(["2010-06-30..2010-12-31 fund", "2012-01-01.. provider"], Of("GSPC", "NOJN"));
        Assert.Equal(["2012-01-01..2014-01-02 provider"], Of("GSPC", "BYNM"));
        Assert.Equal(["2015-01-02.. provider"], Of("GSPC", "TWO1"));
        Assert.Empty(Of("GSPC", "TWO2"));
        Assert.Empty(Of("GSPC", "GONE"));

        Assert.True(LongSpans.IsMember(spans, "MID", "LEAV", new DateOnly(2010, 8, 1)));
        Assert.False(LongSpans.IsMember(spans, "MID", "LEAV", new DateOnly(2010, 9, 30)));
    }

    sealed record Announcement(string Index, string Ticker, string Change, string Effective, string Timing, string? Code, string Read);

    sealed record AnnouncementMiss(string Index, string Ticker, string Why);

    static readonly JsonSerializerOptions AnnouncementReading = new() { PropertyNameCaseInsensitive = true };

    // The weekdays from one day to another, a holiday read as a session, which is the stricter count.
    static int SessionsApart(DateOnly one, DateOnly other)
    {
        var (from, to) = one <= other ? (one, other) : (other, one);
        var count = 0;

        for (var day = from.AddDays(1); day <= to; day = day.AddDays(1))
        {
            count += day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0 : 1;
        }

        return count;
    }

    // The sampled announcements of S&P's own text and wire copies read against the spans computed from 23.3's run over the
    // capture: at least 95 in every 100 stand, and the misses are the three the fixture names with what left each.
    [Fact]
    public void AtLeast95InEvery100OfTheSampledAnnouncementsStandBetweenTheQuarterEndsOrWithinThreeSessionsAndEachMissIsNamed()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(LongFixture, "expectations", "announcements.json")));
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(LongFixture, "spans-input.json")));
        static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var snapshots = new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(StringComparer.Ordinal);

        foreach (var index in input.RootElement.GetProperty("snapshots").EnumerateObject())
        {
            var held = index.Value.EnumerateArray().Select(period => Day(period.GetString()!)).ToDictionary(period => period, _ => new HashSet<string>(StringComparer.Ordinal));

            foreach (var code in input.RootElement.GetProperty("held").EnumerateObject())
            {
                if (code.Value.TryGetProperty(index.Name, out var periods))
                {
                    foreach (var period in periods.EnumerateArray())
                    {
                        held[Day(period.GetString()!)].Add(code.Name);
                    }
                }
            }

            snapshots[index.Name] = held.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value);
        }

        ProviderSpan[] provider =
        [
            .. input.RootElement.GetProperty("provider").EnumerateArray().Select(span => new ProviderSpan(
                span.GetProperty("ticker").GetString()!,
                span.GetProperty("joined").GetString() is { } joined ? Day(joined) : null,
                span.GetProperty("left").GetString() is { } left ? Day(left) : null,
                span.GetProperty("name").GetString())),
        ];
        var byName = input.RootElement.GetProperty("ivvNames").EnumerateObject()
            .GroupBy(name => HoldingMatcher.NameKey(LongMatching.InOrder(name.Name)), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(name => Day(name.Value.GetString()!)), StringComparer.Ordinal);
        var spans = LongSpans.Of(snapshots, provider, byName);

        var announcements = expected.RootElement.GetProperty("announcements").Deserialize<Announcement[]>(AnnouncementReading)!;
        var within = expected.RootElement.GetProperty("sessionsWithin").GetInt32();
        var misses = new List<(string Index, string Ticker)>();

        foreach (var row in announcements)
        {
            // The first session the change stands on: the next weekday after a close, the day itself before an open.
            var first = Day(row.Effective);

            if (row.Timing == "after the close")
            {
                do
                {
                    first = first.AddDays(1);
                }
                while (first.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
            }

            var plain = row.Code ?? row.Ticker;
            string[] codes = [plain, plain + "_old", .. Enumerable.Range(1, LongNames.OlderForms - 1).Select(form => FormattableString.Invariant($"{plain}_old{form}"))];
            var own = spans.Where(span => span.Index == row.Index && codes.Contains(span.Ticker)).ToArray();
            DateOnly? Edge(LongSpan span) => row.Change == "join" ? span.Joined : span.Left;

            var stands = row.Index == "GSPC" && first >= LongSpans.ProviderFrom
                ? own.Any(span => span.ReadFrom == LongSpans.FromProvider && Edge(span) is { } day && SessionsApart(day, first) <= within)
                : snapshots[row.Index].Keys.Where(period => period >= first).Order().FirstOrDefault() is var snapshot && own.Any(span => Edge(span) == snapshot);

            if (!stands)
            {
                misses.Add((row.Index, row.Ticker));
            }
        }

        var named = expected.RootElement.GetProperty("misses").Deserialize<AnnouncementMiss[]>(AnnouncementReading)!;

        Assert.All(named, miss => Assert.False(string.IsNullOrWhiteSpace(miss.Why), miss.Ticker));
        var floor = expected.RootElement.GetProperty("floor").GetDecimal();

        Assert.Equal(84, announcements.Length);
        Assert.Equal(named.Select(miss => (miss.Index, miss.Ticker)), misses);
        Assert.True((decimal)(announcements.Length - misses.Count) / announcements.Length >= floor, $"{announcements.Length - misses.Count} of {announcements.Length} stand.");
        Assert.All(announcements, row => Assert.False(string.IsNullOrWhiteSpace(row.Read)));
    }

    static long LongCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)command.ExecuteScalar()!;
    }

    // The write over a constructed store: its snapshots, holdings and spans written whole, a left-out holding with why, the members
    // pull's later holdings read into its spans, the run before's rows removed, and no row of the pulled holdings or the membership
    // moved.
    [Fact]
    public async Task TheWriteOverAConstructedStoreWritesItsRunWholeAndMovesNoRowOfAStandingTable()
    {
        using var store = new TemporaryStore().Migrated();

        using (var connection = store.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO pulled_holding (index_code, period, holding, name, ticker, matched_by, pull, shares, value_usd) VALUES ('MID', '2018-12-31', '0', 'Kept Co', 'KEPT', 'isin', 'pull-1', '10', '100');
                INSERT INTO membership (index_code, ticker, joined, left, observed_at) VALUES ('GSPC', 'BIGC', '2015-03-02', NULL, '2026-11-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }

        string Standing()
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT group_concat(index_code || period || holding || name || ticker || pull, '|') FROM pulled_holding UNION ALL SELECT group_concat(index_code || ticker || joined || observed_at, '|') FROM membership;";
            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
            }

            return string.Join("/", rows);
        }

        var before = Standing();
        var period = new DateOnly(2018, 9, 30);
        LongSchedule[] schedules = [new("MID", period, "0001193125-18-000001", [new("Kept Co.", "Things", 10m, 100m), new("Gone Co.", "Things", 10m, 50m)])];
        var matched = new LongMembers.Matched(
            schedules,
            new Dictionary<string, IReadOnlyDictionary<(DateOnly Period, int Place), LongMatch>>(StringComparer.Ordinal)
            {
                ["MID"] = new Dictionary<(DateOnly Period, int Place), LongMatch> { [(period, 0)] = new("KEPT", LongMatching.ByName, ["KEPT"]) },
                ["GSPC"] = new Dictionary<(DateOnly Period, int Place), LongMatch>(),
                ["SML"] = new Dictionary<(DateOnly Period, int Place), LongMatch>(),
            },
            []);
        var left = new Dictionary<(string Index, string Name), LongLeft> { [("MID", "Gone Co.")] = new(LongLeaving.BoughtOut, "0000000000-19-000001", new DateOnly(2019, 1, 2), "a DEFM14A") };

        var at = new DateTimeOffset(2026, 11, 7, 4, 0, 0, TimeSpan.Zero);
        var clock = FixedClock.At(at, SessionZones.UnitedStates);

        await LongMembers.WriteAsync(store.DatabaseFile, "long-members-1", matched, new Dictionary<string, string>(StringComparer.Ordinal) { ["0001193125-18-000001"] = "N-Q" }, new Dictionary<(string, string), string>(), left, clock, at);

        using (var connection = store.Open())
        {
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_snapshot WHERE form = 'N-Q' AND holdings = 2 AND value_usd = '150';"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_holding WHERE ticker = 'KEPT' AND matched_by = 'name' AND left_as IS NULL;"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_holding WHERE ticker IS NULL AND left_as = 'bought out' AND left_read LIKE 'a DEFM14A, accession 0000000000-19-000001 of 2019-01-02';"));

            // KEPT runs from the long schedule into the pull's later snapshot, open; the provider's S&P 500 span from 2015 read as dated.
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_span WHERE index_code = 'MID' AND ticker = 'KEPT' AND joined = '2018-09-30' AND left IS NULL AND read_from = 'fund';"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_span WHERE index_code = 'GSPC' AND ticker = 'BIGC' AND joined = '2015-03-02' AND read_from = 'provider';"));
        }

        await LongMembers.WriteAsync(store.DatabaseFile, "long-members-2", matched, new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<(string, string), string>(), left, clock, at);

        using (var connection = store.Open())
        {
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM long_holding WHERE run_id = 'long-members-1';"));
            Assert.Equal(2L, LongCount(connection, "SELECT COUNT(*) FROM long_holding WHERE run_id = 'long-members-2';"));

            // Each run's row on the run log, the second's naming what it wrote.
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM run_log WHERE run_id = 'long-members-2' AND stage = 'long-members' AND outcome = 'ok' AND detail = '1 schedule(s) and 2 holding(s) written, 1 matched and 1 left out, 2 span(s)';"));
        }

        Assert.Equal(before, Standing());
    }

    // The sources 23.3 writes, none of which a rule's version pins.
    static readonly string[] LongMembershipSources =
    [
        "src/EquityBrief.Core/History/LongSchedules.cs",
        "src/EquityBrief.Core/History/LongMatching.cs",
        "src/EquityBrief.Core/History/LongFloors.cs",
        "src/EquityBrief.Core/History/LongHandMatches.cs",
        "src/EquityBrief.Core/History/LongLeaving.cs",
        "src/EquityBrief.Core/History/LongSpans.cs",
        "src/EquityBrief.Core/Providers/SecEdgarTrustScheduleFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdLongHistoryFeed.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/History/LongScheduleFilings.cs",
        "src/EquityBrief.Worker/History/LongDayFiles.cs",
        "src/EquityBrief.Worker/History/LongMembers.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.LongMembership.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Web/App/SinglePageApp.LongMembership.cs",
        "src/EquityBrief.Web/App/SinglePageApp.History.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne23Point3Writes()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(LongMembershipSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(LongMembershipSources.Intersect(pinned, StringComparer.Ordinal));

        string[] readings = [.. LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))];

        Assert.Empty(LongMembershipSources.Intersect(readings, StringComparer.Ordinal));
    }
}
