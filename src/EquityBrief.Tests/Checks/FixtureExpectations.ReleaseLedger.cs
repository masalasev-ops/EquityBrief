using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.8: the release readings in the ledger's catalogue. Each of the four is worked by hand as a
// catalogue reading at a constructed session, at the window's edge, at the late slot's start and over a refused newest
// release; what each answer came to is worked by hand over constructed settled setups on both sides of the model's
// cutoff; a change's test years are split at the cutoff's year; the per-rule test's walk of the drift's tone variant keeps
// a listing by the release its night reads for its print; the readings' step writes the answers with its run over a
// constructed store; the point-in-time check rebuilds a setup's release readings from the releases its night could read;
// and the month's report states the split for a rule reading a release reading.
// see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
public partial class FixtureExpectations
{
    // The claim 20.8 adds that this check reaches: section 17's window row.
    internal static readonly string[] ReleaseLedgerClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Ledger's release readings' window"),
    ];

    static readonly DateOnly ReleaseCutoff = new(2025, 1, 31);

    // A release as the ledger reads it, its four words joined by bars, or none where its reading was refused or is not held.
    static LedgerRelease ReleaseOn(DateTimeOffset accepted, string? words) =>
        new(accepted, words?.Split('|') is { } four ? new ReleaseAnswer(four[0], four[1], four[2], four[3]) : null);

    [Fact]
    public void EachReleaseReadingIsWorkedByHandAsACatalogueReadingAtAConstructedSession()
    {
        // The four stand together in the catalogue in the questions' order, the last before the short-term dip's of 22.2.
        Assert.Equal(["release_results", "release_outlook", "release_tone", "release_demand"], LedgerReleases.Columns);
        Assert.Equal(LedgerReleases.Columns, LedgerReadings.All.Skip(LedgerReadings.IndexOf("release_results")).Take(4).Select(reading => reading.Column));
        Assert.Equal("rsi_two", LedgerReadings.All[LedgerReadings.IndexOf("release_demand") + 1].Column);

        // On Friday 2025-05-02 the window's 95 days reach back to 2025-01-27: a release the SEC accepted on that day is
        // read, every word turned into its reading, and one accepted the second before it is not.
        var session = new DateOnly(2025, 5, 2);
        var first = ReleaseOn(Utc(2025, 1, 27, 0, 0, 0), "better|raised|confident|stronger");
        var dayBefore = ReleaseOn(Utc(2025, 1, 26, 23, 59, 59), "worse|lowered or withdrawn|cautious|weaker");

        Assert.Equal(new DateOnly(2025, 1, 27), session.AddDays(-LedgerReleases.WindowDays));
        Assert.Equal<double?>([1.0, 1.0, 1.0, 1.0], LedgerReleases.Readings(LedgerReleases.On([first], session)));
        Assert.Equal<double?>([null, null, null, null], LedgerReleases.Readings(LedgerReleases.On([dayBefore], session)));

        // The newest the night may read: one accepted at the late slot's start on the session is read from the next
        // session's night and leaves the older one read, one accepted a second before it is read, its words saying
        // nothing none and never nought.
        var atTheSlot = ReleaseOn(Utc(2025, 5, 2, 21, 40, 0), "worse|lowered or withdrawn|cautious|weaker");
        var aSecondBefore = ReleaseOn(Utc(2025, 5, 2, 21, 39, 59), "in line|none given|neutral|not stated");

        Assert.Same(first, LedgerReleases.On([first, atTheSlot], session));
        Assert.Same(atTheSlot, LedgerReleases.On([first, atTheSlot], session.AddDays(3)));
        Assert.Equal<double?>([0.0, null, 0.0, null], LedgerReleases.Readings(LedgerReleases.On([first, aSecondBefore], session)));

        // A newest release whose reading was refused or is not held reads none, and not the older one's, which answers for
        // another quarter; a stock holding no release reads none.
        var refused = ReleaseOn(Utc(2025, 4, 29, 21, 5, 0), null);

        Assert.Equal<double?>([null, null, null, null], LedgerReleases.Readings(LedgerReleases.On([first, refused], session)));
        Assert.Equal<double?>([null, null, null, null], LedgerReleases.Readings(LedgerReleases.On([], session)));

        // Through the ledger's context: a stock whose releases it holds and one it holds none of.
        var context = new LedgerContext(NoIncome, NoSectors, [], [], NoFacts)
        {
            Releases = new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal) { ["AAA"] = [first, atTheSlot] },
        };

        Assert.Equal<double?>([1.0, 1.0, 1.0, 1.0], context.ReleasesOf("AAA", session));
        Assert.Equal<double?>([-1.0, -1.0, -1.0, -1.0], context.ReleasesOf("AAA", session.AddDays(3)));
        Assert.Equal<double?>([null, null, null, null], context.ReleasesOf("ZZZ", session));
        Assert.Equal<double?>([null, null, null, null], new LedgerContext(NoIncome, NoSectors, [], [], NoFacts).ReleasesOf("AAA", session));
    }

    [Fact]
    public void WhatEachAnswerCameToIsWorkedByHandOverSettledSetupsOnBothSidesOfTheCutoff()
    {
        // A release accepted on the cutoff's day is before it and one accepted the day after is after it.
        Assert.Equal(ReleaseUsefulness.Before, ReleaseUsefulness.SideOf(Utc(2025, 1, 31, 23, 59, 59), ReleaseCutoff));
        Assert.Equal(ReleaseUsefulness.After, ReleaseUsefulness.SideOf(Utc(2025, 2, 1, 0, 0, 0), ReleaseCutoff));

        // Four pullback setups and one drift setup: the first's release better before the cutoff, won by half a risk; the
        // second's better too, after it, lost by a quarter; the third's worse after it, won by a risk and a half; the fourth's
        // reading refused, before it, an edge of nothing, which is no win; and the drift's better after it.
        SettledRelease[] setups =
        [
            new("pullback", ReleaseOn(Utc(2025, 1, 31, 21, 0, 0), "better|raised|confident|stronger"), 1.0, 0.5),
            new("pullback", ReleaseOn(Utc(2025, 2, 1, 1, 0, 0), "better|kept|neutral|steady"), -1.0, -0.25),
            new("pullback", ReleaseOn(Utc(2025, 3, 1, 21, 0, 0), "worse|lowered or withdrawn|cautious|weaker"), 2.0, 1.5),
            new("pullback", ReleaseOn(Utc(2024, 6, 1, 21, 0, 0), null), 0.5, 0.0),
            new("drift", ReleaseOn(Utc(2025, 6, 1, 21, 0, 0), "better|none given|confident|not stated"), 3.0, 2.0),
        ];

        var rows = ReleaseUsefulness.Of(setups, ReleaseCutoff);

        string Row(AnswerEdge row) => FormattableString.Invariant($"{row.Family}|{row.Question}|{row.Answer}|{row.Side}|{row.Setups}|{row.Winners}|{row.MeanResult}|{row.MeanEdge}");

        // Each question's words in their order, then not read, each before the cutoff and then after it, a family at a time.
        Assert.Equal(
            [
                "pullback|results|better|before|1|1|1|0.5",
                "pullback|results|better|after|1|0|-1|-0.25",
                "pullback|results|worse|after|1|1|2|1.5",
                "pullback|results|not read|before|1|0|0.5|0",
                "pullback|outlook|raised|before|1|1|1|0.5",
                "pullback|outlook|kept|after|1|0|-1|-0.25",
                "pullback|outlook|lowered or withdrawn|after|1|1|2|1.5",
                "pullback|outlook|not read|before|1|0|0.5|0",
                "pullback|tone|confident|before|1|1|1|0.5",
                "pullback|tone|neutral|after|1|0|-1|-0.25",
                "pullback|tone|cautious|after|1|1|2|1.5",
                "pullback|tone|not read|before|1|0|0.5|0",
                "pullback|demand|stronger|before|1|1|1|0.5",
                "pullback|demand|steady|after|1|0|-1|-0.25",
                "pullback|demand|weaker|after|1|1|2|1.5",
                "pullback|demand|not read|before|1|0|0.5|0",
                "drift|results|better|after|1|1|3|2",
                "drift|outlook|none given|after|1|1|3|2",
                "drift|tone|confident|after|1|1|3|2",
                "drift|demand|not stated|after|1|1|3|2",
            ],
            rows.Select(Row));

        // Two setups holding one answer on one side are counted together, their means worked by hand.
        var together = ReleaseUsefulness.Of(
            [
                new("pullback", ReleaseOn(Utc(2025, 3, 1, 21, 0, 0), "better|kept|neutral|steady"), 1.0, 0.5),
                new("pullback", ReleaseOn(Utc(2025, 4, 1, 21, 0, 0), "better|kept|neutral|steady"), -2.0, -1.0),
            ],
            ReleaseCutoff);

        Assert.Equal("pullback|results|better|after|2|1|-0.5|-0.25", Row(together[0]));
        Assert.Equal(4, together.Count);
    }

    [Fact]
    public void AChangesTestYearsAreSplitAtTheCutoffsYearAndAChangeReadingAReleaseReadingIsToldFromOneReadingNone()
    {
        // Five test years: before 2025, 2022 to 2024, the proposal's 5.0 against the rule's 3.5 over 30 units; from it,
        // 2025 and 2026, 3.75 against 1.25 over 26.
        var sides = LedgerReleases.Split([(2022, 10, 2.0, 1.0), (2023, 12, 1.5, 2.0), (2024, 8, 1.5, 0.5), (2025, 20, 3.0, 1.0), (2026, 6, 0.75, 0.25)], ReleaseCutoff);

        Assert.Equal([2022, 2023, 2024], sides.BeforeYears);
        Assert.Equal((30, 5.0, 3.5), (sides.BeforeUnits, sides.BeforeProposed, sides.BeforeCurrent));
        Assert.Equal([2025, 2026], sides.AfterYears);
        Assert.Equal((26, 3.75, 1.25), (sides.AfterUnits, sides.AfterProposed, sides.AfterCurrent));
        Assert.Equal(
            "Before the local model's cutoff of 2025-01-31, which it may have known the outcome of: 2022 to 2024, +1.500 risks over 30 units; from it, the years relied on: 2025 to 2026, +2.500 risks over 26 units.",
            MonthlyRun.CutoffLine(sides, ReleaseCutoff, "risks"));
        Assert.Equal(
            "Before the local model's cutoff of 2025-01-31, which it may have known the outcome of: no test year; from it, the years relied on: 2025, +2.000 points over 20 units.",
            MonthlyRun.CutoffLine(LedgerReleases.Split([(2025, 20, 3.0, 1.0)], ReleaseCutoff), ReleaseCutoff, "points"));

        // A condition or a weight naming a release reading, a tree model fitted over the whole catalogue and the tone
        // condition read one; a condition on another reading, an exit alone and no change read none.
        string? Words(params (string Name, double Value)[] hooks) => RuleHooks.Of(hooks.ToDictionary(hook => hook.Name, hook => hook.Value, StringComparer.Ordinal)).Words();

        Assert.True(LedgerReleases.Reads(Words(("also_release_outlook_above", 0))));
        Assert.True(LedgerReleases.Reads(Words(("score_release_tone", 0.5), ("score_rsi", 0.25))));
        Assert.True(LedgerReleases.Reads(Words(("score_trees", 7))));
        Assert.True(LedgerReleases.Reads("the drift, its print's " + ReleaseTone.Words));
        Assert.False(LedgerReleases.Reads(Words(("also_rsi_above", 60))));
        Assert.False(LedgerReleases.Reads(Words(("exit", 1))));
        Assert.False(LedgerReleases.Reads(null));
    }

    [Fact]
    public void AProposalReadingAReleaseReadingIsHeldBackWhereItsTotalLessTheRulesFromTheCutoffsYearIsNotAboveNothing()
    {
        // From the 20.8 correction of 2026-10-19, on the operator's ruling of that day, worked by hand, the years before the
        // cutoff's year standing at +16 each time. From 2025, 3.75 against 1.25, +2.50, is put, and 1.01 against 1.0 a
        // hundredth above nothing is put; exactly nothing, -0.25 over two years, and no year from 2025 at all are held back.
        // No cutoff, a change reading no release reading and no change hold nothing back.
        const string Reads = "also requires release_results at or above 1";
        (int, int, double, double)[] before = [(2022, 10, 9.0, 1.0), (2023, 12, 9.0, 1.0)];

        Assert.False(LoopDecisions.HeldByCutoff(Reads, [.. before, (2025, 20, 3.0, 1.0), (2026, 6, 0.75, 0.25)], ReleaseCutoff));
        Assert.False(LoopDecisions.HeldByCutoff(Reads, [.. before, (2025, 20, 1.01, 1.0)], ReleaseCutoff));
        Assert.True(LoopDecisions.HeldByCutoff(Reads, [.. before, (2025, 20, 1.0, 1.0)], ReleaseCutoff));
        Assert.True(LoopDecisions.HeldByCutoff(Reads, [.. before, (2025, 20, 0.5, 1.0), (2026, 6, 0.25, 0.0)], ReleaseCutoff));
        Assert.True(LoopDecisions.HeldByCutoff(Reads, before, ReleaseCutoff));
        Assert.False(LoopDecisions.HeldByCutoff(Reads, [.. before, (2025, 20, 0.5, 1.0)], null));
        Assert.False(LoopDecisions.HeldByCutoff("also requires rsi at or above 60", [.. before, (2025, 20, 0.5, 1.0)], ReleaseCutoff));
        Assert.False(LoopDecisions.HeldByCutoff(null, [(2025, 20, 0.5, 1.0)], ReleaseCutoff));

        // The put passes over a stronger proposal the cutoff holds back to the next that passed, and puts none where it was
        // the only one; the words a held proposal says.
        Assert.Equal("the second", LoopDecisions.PutOf([("the first", 0.001, true, true, true), ("the second", 0.002, true, true, false)]));
        Assert.Null(LoopDecisions.PutOf([("the first", 0.001, true, true, true)]));
        Assert.Equal(
            "Held back: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, 2025, is not above nothing, so it is not put to you.",
            LoopDecisions.HeldByCutoffWords(ReleaseCutoff));
    }

    // One stock's series over the calendar given for the tone walk, its prints the surprises given and its newest print on
    // each bar the one given, every other reading a sweep series holds left at nothing.
    static SweepSeries ToneSeries(string ticker, DateOnly[] calendar, IReadOnlyList<SweepSurprise> surprises, int[] newest) =>
        new()
        {
            Name = new SweepName(ticker, [], [(null, null)], [], SurprisesFiled: surprises),
            Bars = [.. calendar.Select(day => new SweepBar(day, 101m, 99m, 100m, 1000))],
            SessionAt = [.. Enumerable.Range(0, calendar.Length)],
            WindowStart = new int[calendar.Length],
            Sma20 = new double[calendar.Length],
            Sma50 = new double[calendar.Length],
            Sma200 = new double[calendar.Length],
            Atr = new double[calendar.Length],
            Rsi = new double[calendar.Length],
            Volume50 = new double[calendar.Length],
            Swings = [],
            Confirmed = new int[calendar.Length],
            Member = [.. Enumerable.Repeat(true, calendar.Length)],
            Gap = new bool[calendar.Length],
            Label = new string[calendar.Length],
            Uptrend = new byte[calendar.Length],
            Return63 = new double[calendar.Length],
            Return126 = new double[calendar.Length],
            ReturnTwelveLessOne = new double[calendar.Length],
            Depth = new double[calendar.Length * 3],
            DryUp = new double[calendar.Length * 3],
            High252 = new double[calendar.Length],
            Tightness = new double[calendar.Length],
            VolumeRatio = new double[calendar.Length],
            RsiUp = new bool[calendar.Length],
            SinceHigh = new int[calendar.Length * 3],
            GapDown = new double[calendar.Length * 3],
            RsiLow = new double[calendar.Length * 3],
            SurpriseBar = [2],
            NewestSurprise = newest,
        };

    [Fact]
    public void ThePerRuleTestKeepsATonesListingByTheReleaseItsNightReadsForItsPrint()
    {
        // Six sessions about a print reported on Monday 2025-02-03, its window opening at the start in New York of the
        // session before, Friday 2025-01-31 at 05:00 UTC; the listing on Wednesday 2025-02-05, its night reading a release
        // accepted before 21:40 UTC that day. The stock's newest print is none on the first two bars and the print from the
        // third.
        DateOnly[] calendar = [new(2025, 1, 29), new(2025, 1, 30), new(2025, 1, 31), new(2025, 2, 3), new(2025, 2, 4), new(2025, 2, 5)];
        var series = ToneSeries("AAA", calendar, [new SweepSurprise(new DateOnly(2025, 2, 3), true, 4.0)], [-1, -1, 0, 0, 0, 0]);
        var read = new LoopRead("GSPC", null!, [series], [], null!, null!, NoIncome, calendar, 0, []);
        var tones = new ToneReleases(new Dictionary<long, IReadOnlyDictionary<string, IReadOnlyList<LedgerRelease>>>(), SessionZones.ResolveSessionZone(SessionZones.UnitedStates), new ReleaseCalendar(calendar));
        var listing = new RuleListing(0, 5, 5, null, null);

        bool Holds(params LedgerRelease[] releases) =>
            RuleTests.ToneHolds(read, listing, new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal) { ["AAA"] = releases }, tones);

        var lastQuarter = ReleaseOn(Utc(2024, 11, 1, 21, 0, 0), "better|raised|confident|stronger");

        // Its print's release read better with its outlook kept or none given passes; lowered or withdrawn, results in
        // line, a refused reading and the quarter before's release alone each fail.
        Assert.True(Holds(lastQuarter, ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "better|kept|neutral|steady")));
        Assert.True(Holds(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "better|none given|cautious|weaker")));
        Assert.False(Holds(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "better|lowered or withdrawn|neutral|steady")));
        Assert.False(Holds(ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), "in line|raised|neutral|steady")));
        Assert.False(Holds(lastQuarter, ReleaseOn(Utc(2025, 2, 3, 21, 5, 0), null)));
        Assert.False(Holds(lastQuarter));

        // The window at its opening second and the second before it, and the listing's night at the late slot's start.
        Assert.True(Holds(ReleaseOn(Utc(2025, 1, 31, 5, 0, 0), "better|raised|neutral|steady")));
        Assert.False(Holds(ReleaseOn(Utc(2025, 1, 31, 4, 59, 59), "better|raised|neutral|steady")));
        Assert.True(Holds(ReleaseOn(Utc(2025, 2, 5, 21, 39, 59), "better|raised|neutral|steady")));
        Assert.False(Holds(ReleaseOn(Utc(2025, 2, 5, 21, 40, 0), "better|raised|neutral|steady")));

        // A listing on a bar holding no print, and a stock holding no release under the version, pass none.
        Assert.False(RuleTests.ToneHolds(read, new RuleListing(0, 1, 1, null, null), new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal) { ["AAA"] = [ReleaseOn(Utc(2025, 1, 29, 21, 0, 0), "better|raised|neutral|steady")] }, tones));
        Assert.False(RuleTests.ToneHolds(read, listing, new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal), tones));
    }

    [Fact]
    public void ASetupsReleaseReadingsRebuiltFromTheHistoryCutAtItsSessionAreTheOnesItsNightCouldRead()
    {
        var (calendar, inputs, market) = PointInTimeHistory();
        var at = 60;
        var night = calendar[at];

        // AAA's release of February read better, and one accepted on the night after the late slot's start read worse,
        // which the night could not read.
        var releases = new Dictionary<string, IReadOnlyList<LedgerRelease>>(StringComparer.Ordinal)
        {
            ["AAA"] =
            [
                ReleaseOn(Utc(2026, 2, 10, 21, 0, 0), "better|raised|confident|stronger"),
                ReleaseOn(new DateTimeOffset(night.ToDateTime(new TimeOnly(21, 45)), TimeSpan.Zero), "worse|lowered or withdrawn|cautious|weaker"),
            ],
        };

        var cut = LedgerPointInTime.Cut(inputs, night);
        var cutMarket = LedgerPointInTime.Cut(market, night);
        var rebuilt = LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, "AAA", releases: releases)!;
        var first = LedgerReadings.IndexOf(LedgerReleases.Columns[0]);

        Assert.Equal<double?>([1.0, 1.0, 1.0, 1.0], rebuilt[first..(first + 4)]);

        // Read without the releases the same history reads none of them, and a stored row taken from the release the night
        // could not read is named by each column with both figures.
        Assert.Equal<double?>([null, null, null, null], LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, "AAA")![first..(first + 4)]);

        var ahead = (double?[])rebuilt.Clone();

        for (var column = 0; column < 4; column++)
        {
            ahead[first + column] = -1.0;
        }

        Assert.Equal(
            [("release_results", (double?)-1.0, (double?)1.0), ("release_outlook", -1.0, 1.0), ("release_tone", -1.0, 1.0), ("release_demand", -1.0, 1.0)],
            LedgerPointInTime.Differences(ahead, rebuilt));
    }

    // A settled setup on the S&P 500 with its result and edge, as the readings' step reads it.
    static void SettledSetup(TemporaryStore store, string family, string ticker, string session, double result, double edge) =>
        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, edge, end, settled, source, pin) VALUES "
            + FormattableString.Invariant($"('GSPC', '{family}', '{ticker}', '{session}', 'the live rule', 1, NULL, '100', '95', '110', NULL, 21, {result.ToString("R", CultureInfo.InvariantCulture)}, {edge.ToString("R", CultureInfo.InvariantCulture)}, 'target', 1, 'history', 'x');"));

    // A stored release of a filer and its reading under the first version, its four words or refused.
    static void StoredRelease(TemporaryStore store, string accession, string cik, string accepted, string? words)
    {
        store.Execute(
            "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) "
            + $"VALUES ('{accession}', '{cik}', '8-K', '{accepted[..10]}', '{accepted}', 'ex99.htm', 'https://www.sec.gov/Archives/edgar/data/1/ex99.htm', zeroblob(1), 'h', 1, 'constructed');");

        var four = words?.Split('|');

        store.Execute(
            "INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) "
            + (four is null
                ? $"VALUES ('{accession}', 'ex99.htm', 1, 'refused', 'the answer was not one object', NULL, NULL, NULL, NULL, 3.0, 5000, '2026-10-20T02:00:00Z', 'constructed');"
                : $"VALUES ('{accession}', 'ex99.htm', 1, 'read', NULL, '{four[0]}', '{four[1]}', '{four[2]}', '{four[3]}', 3.0, 5000, '2026-10-20T02:00:00Z', 'constructed');"));
    }

    [Fact]
    public async Task TheReadingsStepWritesWhatEachAnswerCameToWithItsRunOnceAVersionsHistoryReadIsComplete()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAA', '2026-08-01T23:40:00.000Z', '0000000101'), ('BBB', '2026-08-01T23:40:00.000Z', '0000000102');");

        // AAA's December release read better before the cutoff and its February one worse after it; BBB's February one
        // refused; CCC files no filer the store knows.
        StoredRelease(store, "0000000101-24-000001", "0000000101", "2024-12-02T21:00:00Z", "better|raised|confident|stronger");
        StoredRelease(store, "0000000101-25-000001", "0000000101", "2025-02-05T21:00:00Z", "worse|lowered or withdrawn|cautious|weaker");
        StoredRelease(store, "0000000102-25-000001", "0000000102", "2025-02-20T21:00:00Z", null);
        SettledSetup(store, "pullback", "AAA", "2025-01-10", 1.0, 0.5);
        SettledSetup(store, "pullback", "AAA", "2025-03-10", -1.0, -0.25);
        SettledSetup(store, "pullback", "BBB", "2025-03-10", 2.0, 1.5);
        SettledSetup(store, "pullback", "CCC", "2025-03-10", 0.5, 0.1);
        SettledSetup(store, "drift", "AAA", "2025-03-11", 3.0, 2.0);

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // A version whose history read is not complete: no answer is worked and the row says why.
        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
            + "VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', NULL, 'constructed');");

        Assert.Equal(0, await new WalkForwardTester(clock, store.DatabaseFile, store.Root, new StringWriter()).LinksAsync("GSPC", "2026-10"));
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM release_answer_edge;"));
        Assert.EndsWith("; no reading version's history read is complete, so no release answer was worked", Query(store, "SELECT detail FROM run_log WHERE stage = 'loop-links';").Single(), StringComparison.Ordinal);

        // Once it is complete, the answers are worked over the setups whose release the ledger read, CCC's left out, and
        // written with the readings' rows under one run, the row naming the cutoff.
        store.Execute("UPDATE reading_pin SET history_completed_at = '2026-10-21T00:00:00Z';");

        var output = new StringWriter();

        Assert.Equal(0, await new WalkForwardTester(FixedClock.At(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile, store.Root, output).LinksAsync("GSPC", "2026-10"));
        Assert.Equal(
            [
                "pullback|results|better|before|1|1|1.0|0.5",
                "pullback|results|worse|after|1|0|-1.0|-0.25",
                "pullback|results|not read|after|1|1|2.0|1.5",
                "pullback|outlook|raised|before|1|1|1.0|0.5",
                "pullback|outlook|lowered or withdrawn|after|1|0|-1.0|-0.25",
                "pullback|outlook|not read|after|1|1|2.0|1.5",
                "pullback|tone|confident|before|1|1|1.0|0.5",
                "pullback|tone|cautious|after|1|0|-1.0|-0.25",
                "pullback|tone|not read|after|1|1|2.0|1.5",
                "pullback|demand|stronger|before|1|1|1.0|0.5",
                "pullback|demand|weaker|after|1|0|-1.0|-0.25",
                "pullback|demand|not read|after|1|1|2.0|1.5",
                "drift|results|worse|after|1|1|3.0|2.0",
                "drift|outlook|lowered or withdrawn|after|1|1|3.0|2.0",
                "drift|tone|cautious|after|1|1|3.0|2.0",
                "drift|demand|weaker|after|1|1|3.0|2.0",
            ],
            Query(store, "SELECT family || '|' || question || '|' || answer || '|' || side || '|' || setups || '|' || winners || '|' || mean_result || '|' || mean_edge FROM release_answer_edge ORDER BY rowid;"));
        Assert.Equal(["2025-01-31"], Query(store, "SELECT DISTINCT cutoff FROM release_answer_edge;"));
        Assert.Equal(
            Query(store, "SELECT DISTINCT run_id FROM loop_link WHERE run_id LIKE 'loop-links-GSPC-20261023%';"),
            Query(store, "SELECT DISTINCT run_id FROM release_answer_edge;"));
        Assert.EndsWith(
            "; 16 release answer row(s) split at the model's cutoff of 2025-01-31",
            Query(store, "SELECT detail FROM run_log WHERE stage = 'loop-links' ORDER BY rowid DESC LIMIT 1;").Single(),
            StringComparison.Ordinal);
        Assert.Contains("pullback, results worse after the cutoff: 1 setup(s), 0 winning, a mean edge of -0.250", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMonthsReportStatesTheSplitForAProposalPutAndARuleReadingAReleaseReading()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 11, 7, 14, 0, 0, TimeSpan.Zero));

        // The S&P 500's November run puts the drift's condition on the release's results, its test years five.
        LoopRun(store, "loop-test-GSPC-20261107T120000Z", "GSPC", "2026-11");
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + $"VALUES ('loop-test-GSPC-20261107T120000Z', 'GSPC', 'drift', 'winners against losers, ranked 1', 'also requires release_results at or above 1', 'the rule today', 'risks', 56, 19, 0.001, 1, 1, 4, 3, 1.5, 1, 0.1, 3, 1, NULL, '{LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_release_results_above"] = 1 }).Json}');");

        foreach (var (year, units, proposed, current) in new[] { (2022, 10, 2.0, 1.0), (2023, 12, 1.5, 2.0), (2024, 8, 1.5, 0.5), (2025, 20, 3.0, 1.0), (2026, 6, 0.75, 0.25) })
        {
            store.Execute(
                "INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total) "
                + FormattableString.Invariant($"VALUES ('loop-test-GSPC-20261107T120000Z', 'GSPC', 'drift', 'winners against losers, ranked 1', {year}, 1, 'a', {units}, {units}, {current}, {proposed});"));
        }

        // The month's per-rule test: the tone variant walked and not passing, its years two before the cutoff's year and
        // two from it; another tone variant whose version is still being read, not testable; and the live drift, which
        // reads no release reading.
        const string Rules = "loop-rules-GSPC-20261107T130000Z";

        store.Execute(
            "INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why) VALUES "
            + $"('{Rules}', '2026-11', 'GSPC', 'drift', 'the tone variant', 'variant', 'the drift; its print''s {ReleaseTone.Words}', 'the live drift', 'risks', 40, 19, 0.2, 0, 0, 4, 2, 0.5, 0, 0, NULL), "
            + $"('{Rules}', '2026-11', 'GSPC', 'drift', 'a tone variant being read', 'variant', 'the drift; its print''s {ReleaseTone.Words}', 'the live drift', 'risks', 0, 0, NULL, 0, 0, 0, 0, NULL, 0, 0, '{RuleTests.ReleaseReading.Replace("'", "''", StringComparison.Ordinal)}'), "
            + $"('{Rules}', '2026-11', 'GSPC', 'drift', 'the live drift', 'live', 'the drift', 'its benchmark', 'risks', 300, 19, 0.01, 0, 0, 4, 2, 0.5, 1, 0, NULL);");

        foreach (var (year, units, total, compared) in new[] { (2023, 10, 1.0, 1.5), (2024, 10, 2.0, 1.0), (2025, 10, 0.5, 0.25), (2026, 10, 0.75, 1.0) })
        {
            store.Execute(
                "INSERT INTO loop_rule_year (run_id, index_code, family, rule, year, complete, units, total, compared_units, compared_total) "
                + FormattableString.Invariant($"VALUES ('{Rules}', 'GSPC', 'drift', 'the tone variant', {year}, 1, {units}, {total}, {units}, {compared});"));
        }

        var run = new MonthlyRun(clock, store.DatabaseFile, store.Root, output, []);
        var report = Path.Combine(store.Root, MonthlyRun.Folder, "2026-11", MonthlyRun.ReportFile);

        // No reading version's history read is complete: no cutoff is read and nothing is split.
        await run.ReportAsync("2026-11");

        Assert.DoesNotContain("cutoff", File.ReadAllText(report), StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
            + "VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', '2026-10-21T00:00:00Z', 'constructed');");

        await run.ReportAsync("2026-11");

        var text = File.ReadAllText(report);

        // The put proposal: 2022 to 2024, 5.0 against 3.5 over 30; 2025 and 2026, 3.75 against 1.25 over 26. The tone
        // variant: 2023 and 2024, 3.0 against 2.5 over 20; 2025 and 2026, 1.25 against 1.25 over 20.
        Assert.Contains(
            "  drift: put to you, winners against losers, ranked 1: also requires release_results at or above 1; its adjusted p 0.001 against 0.0042"
            + ", better in 3 of 4 counted years, over 56 units in 19 blocks." + Environment.NewLine
            + "    Before the local model's cutoff of 2025-01-31, which it may have known the outcome of: 2022 to 2024, +1.500 risks over 30 units; from it, the years relied on: 2025 to 2026, +2.500 risks over 26 units.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "  drift, the tone variant: reads the release readings; its per-rule test did not pass. Before the local model's cutoff of 2025-01-31, which it may have known the outcome of: 2023 to 2024, +0.500 risks over 20 units; from it, the years relied on: 2025 to 2026, +0.000 risks over 20 units.",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "  drift, a tone variant being read: reads the release readings; its per-rule test names it not testable, its reading version's history read is not complete.",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("the live drift: reads", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMonthsReportHoldsBackAProposalReadingAReleaseReadingWhoseYearsFromTheCutoffStandAtNothingOrUnder()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 11, 7, 14, 0, 0, TimeSpan.Zero));
        const string Run = "loop-test-GSPC-20261107T120000Z";

        // From the 20.8 correction of 2026-10-19: the S&P 500's November run, the drift's condition on the release's results
        // the stronger of two passing, its years from the cutoff's year 1.0 and 0.25 against the rule's 3.0 and 0.75, and a
        // condition on the RSI beside it.
        LoopRun(store, Run, "GSPC", "2026-11");

        foreach (var (proposal, words, hook, adjusted) in new[] { ("winners against losers, ranked 1", "also requires release_results at or above 1", "also_release_results_above", 0.001), ("winners against losers, ranked 2", "also requires rsi at or above 61.5", "also_rsi_above", 0.002) })
        {
            store.Execute(
                "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
                + FormattableString.Invariant($"VALUES ('{Run}', 'GSPC', 'drift', '{proposal}', '{words}', 'the rule today', 'risks', 56, 19, {adjusted}, 1, 1, 4, 3, 1.5, 1, 0.1, 3, 1, NULL, '{LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [hook] = 1 }).Json}');"));

            foreach (var (year, units, proposed, current) in new[] { (2022, 10, 2.0, 1.0), (2023, 12, 1.5, 2.0), (2024, 8, 1.5, 0.5), (2025, 20, 1.0, 3.0), (2026, 6, 0.25, 0.75) })
            {
                store.Execute(
                    "INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total) "
                    + FormattableString.Invariant($"VALUES ('{Run}', 'GSPC', 'drift', '{proposal}', {year}, 1, 'a', {units}, {units}, {current}, {proposed});"));
            }
        }

        // A third, the release's tone between the two in strength, its years from the cutoff's year totalling the rule's
        // exactly, 3.0 and 0.75 against 3.0 and 0.75.
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + FormattableString.Invariant($"VALUES ('{Run}', 'GSPC', 'drift', 'winners against losers, ranked 3', 'also requires release_tone at or above 1', 'the rule today', 'risks', 56, 19, 0.0015, 1, 1, 4, 3, 1.5, 1, 0.1, 3, 1, NULL, '{LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_release_tone_above"] = 1 }).Json}');"));

        foreach (var (year, units, proposed, current) in new[] { (2022, 10, 2.0, 1.0), (2023, 12, 1.5, 2.0), (2024, 8, 1.5, 0.5), (2025, 20, 3.0, 3.0), (2026, 6, 0.75, 0.75) })
        {
            store.Execute(
                "INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total) "
                + FormattableString.Invariant($"VALUES ('{Run}', 'GSPC', 'drift', 'winners against losers, ranked 3', {year}, 1, 'a', {units}, {units}, {current}, {proposed});"));
        }

        var run = new MonthlyRun(clock, store.DatabaseFile, store.Root, output, []);
        var report = Path.Combine(store.Root, MonthlyRun.Folder, "2026-11", MonthlyRun.ReportFile);

        // No reading version's history read is complete: nothing is held back, and the stronger is put.
        await run.ReportAsync("2026-11");

        Assert.Contains("  drift: put to you, winners against losers, ranked 1: also requires release_results at or above 1;", File.ReadAllText(report), StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
            + "VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', '2026-10-21T00:00:00Z', 'constructed');");

        await run.ReportAsync("2026-11");

        var text = File.ReadAllText(report);

        // Worked by hand: before the cutoff's year, 2022 to 2024, 5.0 against 3.5 over 30, +1.500; from it, 2025 and 2026,
        // 1.25 against 3.75 over 26, -2.500, not above nothing, so it is held back, said so with both sides, and the RSI's
        // condition is put.
        Assert.Contains("  drift: put to you, winners against losers, ranked 2: also requires rsi at or above 61.5;", text, StringComparison.Ordinal);
        Assert.Contains(
            "  drift: winners against losers, ranked 1: also requires release_results at or above 1. Held back: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, 2025, is not above nothing, so it is not put to you." + Environment.NewLine
            + "    Before the local model's cutoff of 2025-01-31, which it may have known the outcome of: 2022 to 2024, +1.500 risks over 30 units; from it, the years relied on: 2025 to 2026, -2.500 risks over 26 units.",
            text,
            StringComparison.Ordinal);

        // The third, at nothing from the cutoff's year, is held back too, so the RSI's condition is still the one put.
        Assert.Contains(
            "  drift: winners against losers, ranked 3: also requires release_tone at or above 1. Held back: it reads a release reading, and its total less the rule's over the test years from the cutoff's year, 2025, is not above nothing, so it is not put to you.",
            text,
            StringComparison.Ordinal);
    }

    // From 20.8's merge after the 20.3 correction: the ledger reads the exhibit each filing holds as its release, the one a
    // check replaced kept beside it marked no release read for nothing, and a reading of the replaced exhibit answering for
    // nothing.
    [Fact]
    public async Task TheLedgerReadsTheExhibitEachFilingHoldsAsItsReleaseAndAReadingOnlyOfThatExhibit()
    {
        using var store = new TemporaryStore().Migrated();

        // Both filings' supplements were read, the first worse and the second better, and each was then replaced by the
        // press release its page names; only the first filing's press release has been read since.
        StoredRelease(store, "0000000101-26-000001", "0000000101", "2026-02-03T21:05:00Z", "worse|lowered or withdrawn|cautious|weaker");
        StoredRelease(store, "0000000101-26-000002", "0000000101", "2026-05-04T21:05:00Z", "better|raised|confident|stronger");
        store.Execute("UPDATE release_document SET release = 0;");
        store.Execute(
            "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id, release) VALUES "
            + "('0000000101-26-000001', '0000000101', '8-K', '2026-02-03', '2026-02-03T21:05:00Z', 'press.htm', 'https://www.sec.gov/Archives/edgar/data/1/press.htm', zeroblob(1), 'h', 1, 'exhibits', 1), "
            + "('0000000101-26-000002', '0000000101', '8-K', '2026-05-04', '2026-05-04T21:05:00Z', 'press.htm', 'https://www.sec.gov/Archives/edgar/data/1/press.htm', zeroblob(1), 'h', 1, 'exhibits', 1);");
        store.Execute(
            "INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) "
            + "VALUES ('0000000101-26-000001', 'press.htm', 1, 'read', NULL, 'in line', 'kept', 'neutral', 'steady', 3.0, 5000, '2026-10-21T02:00:00Z', 'constructed');");

        await using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        await connection.OpenAsync();

        var releases = (await ReleaseReadings.ForLedgerAsync(connection, 1))["0000000101"];

        Assert.Equal(
            ["2026-02-03T21:05:00Z in line|kept|neutral|steady", "2026-05-04T21:05:00Z none"],
            releases
                .OrderBy(release => release.Accepted)
                .Select(release => release.Accepted.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + " " + (release.Answer is { } answer ? $"{answer.Results}|{answer.Outlook}|{answer.Tone}|{answer.Demand}" : "none")));
    }
}
