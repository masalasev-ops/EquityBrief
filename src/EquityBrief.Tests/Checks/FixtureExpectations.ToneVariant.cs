using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.4: the drift's tone variant. A rule naming a reading version lists a drift setup only where the
// release of the print it reacts to, read under that version, reads results better with an outlook not lowered or
// withdrawn, worked by hand on both sides of each reading and of the print's window over a constructed night on each
// index; a night holding no reading lists nothing under it and says so on the stage's row and the card's funnel; the
// S&P 500's drift registered again whole adds the variant at the newest version whose history read is complete; the
// 17.11 form's `--again` registers an index's variants again whole and refuses as it refused before; and the per-rule
// step keeps the variant not testable in the operator's words.
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
// see: An S&P 400 or 600 family's variants are registered again whole beside its provisional rule or its frozen live rule
public partial class FixtureExpectations
{
    // The claims the tone variant makes, which this check reaches: 15.7's row for its card and section 17's row for its
    // release window.
    internal static readonly string[] ToneVariantClaims =
    [
        CheckReach.Key("15.7 Tonight", "The drift's tone variant"),
        CheckReach.Key(Scope.LimitsTable, "Tone variant's release window"),
    ];

    static ReleaseAnswer Read(string results, string outlook) => new(results, outlook, "neutral", "steady");

    // A reading version written by hand, its history read complete or not.
    static void PinVersion(TemporaryStore store, bool complete) =>
        store.Execute(
            "INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) " +
            $"VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', {(complete ? "'2026-10-18T06:00:00Z'" : "NULL")}, 'constructed');");

    // A release's reading under a version, read with its four words.
    static void ReadRelease(TemporaryStore store, string accession, long pin, ReleaseAnswer answer) =>
        store.Execute(
            "INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) " +
            FormattableString.Invariant($"VALUES ('{accession}', 'ex99.htm', {pin}, 'read', NULL, '{answer.Results}', '{answer.Outlook}', '{answer.Tone}', '{answer.Demand}', 3.0, 5000, '2026-10-18T06:00:00Z', 'constructed');"));

    [Fact]
    public void TheToneConditionListsOnlyAReleaseReadBetterWithItsOutlookNotCutAndSaysWhyOtherwise()
    {
        var drift = (DriftCandidate)CandidateEvaluators.Find(DriftCandidate.EvaluatorName)!;
        var plain = DriftCandidate.ParametersOf(DriftRule.Live);
        var tone = DriftCandidate.ParametersOf(DriftRule.Live, MarketSwitches.None, 2);

        FamilyResult At(IReadOnlyList<PrintRelease>? releases, IReadOnlyDictionary<string, double>? parameters = null) =>
            drift.EvaluateMember(DriftMember() with { Releases = releases }, parameters ?? tone);

        // The member the drift's own test works by hand passes the live rule's seven gates, and a rule naming version 2
        // reads the condition after them as its eighth.
        Assert.True(At(null, plain).Passed);
        Assert.Equal(DriftRule.Order, At(null, plain).Gates.Select(gate => gate.Name));
        Assert.Equal([.. DriftRule.Order, ReleaseTone.Gate], At([new PrintRelease(2, Read("better", "raised"), null)]).Gates.Select(gate => gate.Name));

        // Each pair of words on both sides: results better passes with an outlook raised, kept or none given, and fails with
        // it lowered or withdrawn; results in line, worse or not stated fail whatever the outlook.
        foreach (var (results, outlook, passes) in new[]
        {
            ("better", "raised", true), ("better", "kept", true), ("better", "none given", true), ("better", "lowered or withdrawn", false),
            ("in line", "raised", false), ("worse", "raised", false), ("not stated", "kept", false),
        })
        {
            var result = At([new PrintRelease(2, Read(results, outlook), null)]);

            Assert.Equal(passes, result.Passed);
            Assert.Equal(passes ? 0 : 1, result.Missed);
        }

        Assert.Equal(
            "the release reads results better and outlook raised, under reading version 2",
            At([new PrintRelease(2, Read("better", "raised"), null)]).Gates[^1].Reason);
        Assert.Equal(
            "the release reads results in line and outlook raised, under reading version 2, and the variant lists only results better with an outlook not lowered or withdrawn",
            At([new PrintRelease(2, Read("in line", "raised"), null)]).Gates[^1].Reason);

        // A release with no reading held says why in the reader's words, a release read under another version alone is
        // none under this one, and a member handed no release says so.
        Assert.Equal(
            "no release reading under reading version 2: " + ReleaseReadings.NotRead,
            At([new PrintRelease(2, null, ReleaseReadings.NotRead)]).Gates[^1].Reason);
        Assert.Equal(
            "no release reading under reading version 2: " + ReleaseTone.NotHanded,
            At([new PrintRelease(1, Read("better", "raised"), null)]).Gates[^1].Reason);
        Assert.False(At(null).Passed);
        Assert.Equal("no release reading under reading version 2: " + ReleaseTone.NotHanded, At(null).Gates[^1].Reason);

        // A rule naming no version reads no condition, a failing release handed beside it changing nothing; and a rule
        // switched on the VIX reads the tone after its switch.
        Assert.True(At([new PrintRelease(2, Read("worse", "lowered or withdrawn"), null)], plain).Passed);
        Assert.Equal(
            [.. DriftRule.Order, MarketSwitches.VixGate, ReleaseTone.Gate],
            At([new PrintRelease(2, Read("better", "raised"), null)], DriftCandidate.ParametersOf(DriftRule.Live, new MarketSwitches(0, 10), 2)).Gates.Select(gate => gate.Name));
    }

    [Fact]
    public void TheFixturesReleaseReadingPassesTheToneConditionAsWorkedByHand()
    {
        var expected = Expected("tone-variant");

        Assert.Equal("tone-variant", expected.GetProperty("stage").GetString());
        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Contains("worked by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["source_document"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()));

        // The recorded answer over the fixture's anonymised release, read into its words by the shipped reader.
        var reply = EquityBrief.Core.Providers.LmStudioReadingFeed.Parse(Captured("local-reading-KEYS-anonymised.json"), 2.51);
        var words = ReleaseAnswers.Read(reply.Content, reply.FinishReason).Answer!;
        var answer = expected.GetProperty("answer");

        Assert.Equal((answer.GetProperty("results").GetString(), answer.GetProperty("outlook").GetString()), (words.Results, words.Outlook));

        // The gate over it as the expectation works it, and a drift setup whose print it answers for listing under a rule
        // naming version 1.
        var gate = ReleaseTone.GateOf(1, new PrintRelease(1, words, null));

        Assert.Equal((expected.GetProperty("passes").GetBoolean(), expected.GetProperty("reason").GetString()), (gate.Passed, gate.Reason));
        Assert.True(((DriftCandidate)CandidateEvaluators.Find(DriftCandidate.EvaluatorName)!).EvaluateMember(DriftMember() with { Releases = [new PrintRelease(1, words, null)] }, DriftCandidate.ParametersOf(DriftRule.Live, MarketSwitches.None, 1)).Passed);
    }

    [Fact]
    public async Task APrintsReleaseIsHandedUnderAVersionOnBothSidesOfItsWindowAndOnlyWhereTheVersionsHistoryIsComplete()
    {
        using var store = ReleaseStore();
        using var connection = store.Open();

        // A print reported on Thursday 2026-10-01: its window opens at the start in New York of Wednesday's session,
        // 04:00 in UTC. A release accepted at that second answers for it, read under version 1; one accepted a second
        // before answers for no print of the quarter.
        KeepRelease(store, "0001601046-26-000050", KeysCik, Utc(2026, 9, 30, 4, 0, 0), "at the window's start");
        ReadRelease(store, "0001601046-26-000050", 1, Read("better", "raised"));

        var report = new DateOnly(2026, 10, 1);
        var night = new DateOnly(2026, 10, 2);

        Task<PrintRelease> Handed(string? cik, long version = 1, bool complete = true) =>
            ReleaseReadings.ForRuleAsync(connection, cik, report, night, version, complete, ReleaseCalendar.Exchange, NewYork);

        Assert.Equal(Utc(2026, 9, 30, 4, 0, 0), ReleaseAvailability.WindowFrom(report, NewYork, ReleaseCalendar.Exchange));
        Assert.Equal(new PrintRelease(1, Read("better", "raised"), null), await Handed(KeysCik));

        using var before = ReleaseStore();
        using var earlier = before.Open();

        KeepRelease(before, "0001601046-26-000051", KeysCik, Utc(2026, 9, 30, 3, 59, 59), "a second before the window");
        ReadRelease(before, "0001601046-26-000051", 1, Read("better", "raised"));

        Assert.Equal(
            new PrintRelease(1, null, ReleaseReadings.NoRelease),
            await ReleaseReadings.ForRuleAsync(earlier, KeysCik, report, night, 1, true, ReleaseCalendar.Exchange, NewYork));

        // Under a version read of none of it, under one whose history read is not complete, and for a stock the store
        // matches no filer to, the reason stands in the answer's place.
        Assert.Equal(new PrintRelease(2, null, ReleaseReadings.NotRead), await Handed(KeysCik, version: 2));
        Assert.Equal(new PrintRelease(1, null, ReleaseTone.HistoryOpen), await Handed(KeysCik, complete: false));
        Assert.Equal(new PrintRelease(1, null, ReleaseReadings.NoFiler), await Handed(null));
    }

    [Fact]
    public async Task OnTheSAndP500TheDriftRegisteredAgainAddsTheToneVariantAndTheNightListsByItsPrintsRelease()
    {
        using var store = DriftStore();
        var familyAt = new DateTimeOffset(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(0, (await RegisterVerbAt(store, familyAt, RegisterVerb.Family, DriftRule.Name)).Code);

        // With no version whose history read is complete, the family registered again is the eight its freeze wrote.
        PinVersion(store, complete: false);

        var (unread, unreadSaid) = await RegisterVerbAt(store, familyAt.AddMinutes(1), RegisterVerb.FamilyAgain, DriftRule.Name, "--evidence", "no version complete");

        Assert.Equal(0, unread);
        Assert.Contains("retired 8 and registered 8 at one instant, family of 8 of 9", unreadSaid, StringComparison.Ordinal);

        // Once version 1's history read is complete the family registered again adds its ninth, the live setting with the
        // condition reading version 1, named for it.
        store.Execute("UPDATE reading_pin SET history_completed_at = '2026-10-02T17:00:00Z' WHERE id = 1;");

        var (again, againSaid) = await RegisterVerbAt(store, familyAt.AddMinutes(2), RegisterVerb.FamilyAgain, DriftRule.Name, "--evidence", "version 1 complete");

        Assert.Equal(0, again);
        Assert.Contains("retired 8 and registered 9 at one instant, family of 9 of 9", againSaid, StringComparison.Ordinal);

        var toneVariant = TheSetupFamilies.ToneVariant(1);

        Assert.EndsWith(", its print's release reading better with its outlook not cut under reading version 1", toneVariant.Candidate, StringComparison.Ordinal);
        // The name carries an apostrophe, doubled inside the query's literal.
        var quoted = toneVariant.Candidate.Replace("'", "''", StringComparison.Ordinal);

        Assert.Equal(1.0, CandidateEvaluator.Read(Text(store, $"SELECT parameters FROM candidate_register WHERE candidate = '{quoted}' AND event = 'registered';"))[ReleaseTone.VersionParameter]);

        // DA's print, reported after the close of 2026-09-29, opens its window at Monday's session, and its release is read
        // better with its outlook raised; DB's, reported on the night before the open, at Thursday's, and its release is read
        // better with its outlook withdrawn. DC's filer holds no release in its window, and DD and DE match no filer.
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('DA', '2026-08-01T00:00:00Z', '0000000101'), ('DB', '2026-08-01T00:00:00Z', '0000000102'), ('DC', '2026-08-01T00:00:00Z', '0000000103');");
        KeepRelease(store, "0000000101-26-000001", "0000000101", Utc(2026, 9, 29, 20, 5, 0), "DA's results");
        KeepRelease(store, "0000000102-26-000001", "0000000102", Utc(2026, 10, 2, 11, 0, 0), "DB's results");
        ReadRelease(store, "0000000101-26-000001", 1, Read("better", "raised"));
        ReadRelease(store, "0000000102-26-000001", 1, Read("better", "lowered or withdrawn"));

        var register = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync();
        var shadow = FamilyRuleShadow.For(register, familyAt.AddHours(1));
        var outcome = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-tone", shadow);

        Assert.Equal([1L], shadow.ReadingVersions);

        IReadOnlyList<ShadowOutcome> VerdictsOn(string ticker) =>
            FamilyRuleShadow.Read(Text(store, $"SELECT shadow FROM family_result WHERE ticker = '{ticker}' AND family = 'drift' AND session_date = '{BreakoutNight}';"));

        string FiredBy(string candidate) =>
            string.Join(",", new[] { "DA", "DB", "DC", "DD", "DE", "DF" }.Where(ticker => VerdictsOn(ticker).Single(one => one.Candidate == candidate).Fired));

        // The live rule passes DA and DB as the drift's own test works them; the tone variant DA alone, DB's tone gate
        // failing, and every rule naming no version reads no tone gate.
        Assert.Equal("DA,DB", FiredBy(TheSetupFamilies.Drifts[0].Candidate));
        Assert.Equal("DA", FiredBy(toneVariant.Candidate));
        Assert.Equal(("passed", "failed"), (VerdictsOn("DA").Single(one => one.Candidate == toneVariant.Candidate).Values[ReleaseTone.Gate], VerdictsOn("DB").Single(one => one.Candidate == toneVariant.Candidate).Values[ReleaseTone.Gate]));
        Assert.DoesNotContain(ReleaseTone.Gate, VerdictsOn("DA").Single(one => one.Candidate == TheSetupFamilies.Drifts[0].Candidate).Values.Keys);
        Assert.Empty(outcome.Faults!);

        // The stage's row says what the night's releases held under version 1: the five prints inside the widest window,
        // DA's, DB's, DC's, DD's and DE's, two read, two whose stock the store matches no filer to and one whose window holds
        // no release the night may read.
        Assert.EndsWith(
            $"; the release readings: reading version 1: 5 print(s) inside the window, 2 read, 2 where {ReleaseReadings.NoFiler}, 1 where {ReleaseReadings.NoRelease}",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'rules-tone';"),
            StringComparison.Ordinal);

        // A night whose version's history read is not complete lists nothing under the variant, and says so on the row.
        store.Execute("UPDATE reading_pin SET history_completed_at = NULL WHERE id = 1;");

        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("rules-tone-open", shadow);

        Assert.Equal(string.Empty, FiredBy(toneVariant.Candidate));
        Assert.Equal("DA,DB", FiredBy(TheSetupFamilies.Drifts[0].Candidate));
        Assert.EndsWith(
            $"; the release readings: reading version 1: 5 print(s) inside the window, 0 read, 5 where {ReleaseTone.HistoryOpen}",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'rules-tone-open';"),
            StringComparison.Ordinal);

        // The card's funnel for the variant counts its print's release read and then its tone after the drift's own gates:
        // DA and DB reach the release read, neither read, the night's version not complete.
        store.Execute($"INSERT INTO family_night (session_date, families) VALUES ('{BreakoutNight}', '[\"pullback\",\"breakout\",\"drift\"]');");

        await new RuleCards(clock, store.DatabaseFile).RunAsync("night-tone-cards", register, familyAt.AddHours(1));

        Assert.Equal(RuleCards.Ok, Text(store, "SELECT outcome FROM run_log WHERE run_id = 'night-tone-cards';"));

        Assert.Equal(
            RuleRows.GatesJson([("market", 6), ("print", 4), ("beat", 2), ("reaction", 2), ("volume", 2), ("held", 2), ("trade", 2), (ReleaseTone.ReadStep, 0), (ReleaseTone.Gate, 0)], 6),
            Text(store, $"SELECT gates FROM rule_night WHERE session_date = '{BreakoutNight}' AND rule = '{quoted}';"));
    }

    // A year of bars ending on the index's night, a calendar day a session, whose reaction to a print sits on the session
    // before the night: closes near 99 and then 100 as the index's own years run, the reaction closing at 101 from 100 on
    // 2.5 times the volume, its low at 100, and the night closing at 101.5.
    static IReadOnlyList<FamilyBar> ReactedYear()
    {
        var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
        [
            .. Enumerable.Range(0, 252).Select(index => index switch
            {
                < 211 => new FamilyBar(night.AddDays(index - 251), 100m, 98m, 99m, 1_000_000),
                < 231 => new FamilyBar(night.AddDays(index - 251), 101m, 99m, 100m, 1_000_000),
                < 250 => new FamilyBar(night.AddDays(index - 251), 100.5m, 99.5m, 100m, 1_000_000),
                250 => new FamilyBar(night.AddDays(-1), 101.5m, 100m, 101m, 2_500_000),
                _ => new FamilyBar(night, 101.8m, 100.8m, 101.5m, 1_000_000),
            }),
        ];
    }

    // The index's night of seven members, each reacting on 2026-10-01 to a print reported before that day's open that beat
    // its estimate, T1 by 6 per cent and the others by 5, each with its own filer and its release kept as the case names:
    // T1's read better with its outlook raised and T2's, accepted at the second its window opens, better with none given;
    // T3's accepted a second before its window opens; T4's read in line and T6's better with its outlook withdrawn; T5's
    // accepted at the night's late slot, read from the next night's; and T7's not read under the version.
    static TemporaryStore ToneIndexStore(string index)
    {
        var store = new TemporaryStore().Migrated();
        var releases = new (string Ticker, DateTimeOffset Accepted, ReleaseAnswer? Answer)[]
        {
            ("T1", Utc(2026, 10, 1, 11, 0, 0), Read("better", "raised")),
            ("T2", Utc(2026, 9, 30, 4, 0, 0), Read("better", "none given")),
            ("T3", Utc(2026, 9, 30, 3, 59, 59), Read("better", "raised")),
            ("T4", Utc(2026, 10, 1, 11, 0, 0), Read("in line", "raised")),
            ("T5", Utc(2026, 10, 2, 21, 40, 0), Read("better", "raised")),
            ("T6", Utc(2026, 10, 1, 11, 0, 0), Read("better", "lowered or withdrawn")),
            ("T7", Utc(2026, 10, 1, 12, 0, 0), null),
        };

        PinVersion(store, complete: true);

        foreach (var (ticker, accepted, answer) in releases)
        {
            var cik = "00000002" + ticker[1..].PadLeft(2, '0');
            var accession = cik + "-26-000001";

            StoreYear(store, ticker, ReactedYear());
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '2026-10-01', 'earnings', 'before', '{{\"surprise\":\"{(ticker == "T1" ? "6.0" : "5.0")}\"}}', '2026-10-01T12:00:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T00:00:00Z', '{cik}');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
            KeepRelease(store, accession, cik, accepted, ticker + "'s results");

            if (answer is not null)
            {
                ReadRelease(store, accession, 1, answer);
            }
        }

        return store;
    }

    [Fact]
    public async Task OnEachIndexTheDriftsToneVariantListsByItsPrintsReleaseAndANightHoldingNoReadingListsNothing()
    {
        foreach (var index in new[] { "MID", "SML" })
        {
            var drift = IndexEvaluator(index == "MID" ? "drift-400" : "drift-600");
            var provisional = IndexRules.Provisional(drift);
            var tone = new Dictionary<string, double>(provisional, StringComparer.Ordinal) { [ReleaseTone.VersionParameter] = 1 };
            var night = DateOnly.ParseExact(IndexNight, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var store = ToneIndexStore(index);

            async Task<IReadOnlyList<IndexAnswer>> AnswersAsync(IReadOnlyDictionary<string, double> parameters)
            {
                await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
                await connection.OpenAsync();

                var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, default);
                var inputs = IndexNightRead.Prepare(index, night, names, income)!;
                var levels = await IndexFamilies.LevelsAsync(connection, inputs, default, [1]);

                return IndexNightRead.Answers(inputs, DriftRule.Name, IndexRules.Read(inputs, new IndexRule("rule", drift, parameters, FreezeAt), levels));
            }

            static string Fired(IReadOnlyList<IndexAnswer> answers) =>
                string.Join(",", answers.Where(answer => answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker));

            static string Why(IReadOnlyList<IndexAnswer> answers, string ticker) => answers.Single(answer => answer.Ticker == ticker).Reason!;

            // The provisional rule lists all seven, T1 first on its larger surprise. The tone variant lists T1 and T2 alone,
            // T4 and T6 kept off by their readings and T3, T5 and T7 by holding none: T3's release answers for no print of
            // the quarter, T5's is read from the next night's and T7's is not read under the version.
            Assert.Equal("T1,T2,T3,T4,T5,T6,T7", Fired(await AnswersAsync(provisional)));

            var answers = await AnswersAsync(tone);

            Assert.Equal("T1,T2", Fired(answers));
            Assert.Equal(
                (IndexRules.ToneNotHeld, IndexRules.ToneFailed, IndexRules.ToneNotHeld, IndexRules.ToneFailed, IndexRules.ToneNotHeld),
                (Why(answers, "T3"), Why(answers, "T4"), Why(answers, "T5"), Why(answers, "T6"), Why(answers, "T7")));

            // Registered through the form, the variant's words name the condition, and the night's row says what the releases
            // held; with the version's history read not complete, the variant lists nothing and the row says so.
            var at = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
            var (code, said) = await RegisterVerbAt(store, at, RegisterVerb.IndexVariants, DriftRule.Name, "--index", index, "--variants", "readingVersion=1");

            Assert.Equal(0, code);
            Assert.Contains("its print's release reading better with its outlook not cut under reading version 1", said, StringComparison.Ordinal);

            var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
            var register = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync();

            await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-tone", default, register, at.AddHours(1));

            Assert.Contains(
                $"; the release readings: reading version 1: 7 print(s) inside the window, 4 read, 2 where {ReleaseReadings.NoRelease}, 1 where {ReleaseReadings.NotRead}",
                Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-tone';"),
                StringComparison.Ordinal);

            // The variant's card counts the provisional rule's parts and then its print's release read and its tone: all
            // seven through the floors and the profit, four reaching a release read, T1, T2, T4 and T6, and two its tone.
            var variant = register.Single(row => row.Event == CandidateFamily.Registered && row.Evaluator == drift.Name).Candidate.Replace("'", "''", StringComparison.Ordinal);

            await new RuleCards(clock, store.DatabaseFile).RunAsync("cards-tone", register, at.AddHours(1));

            Assert.Equal(
                RuleRows.GatesJson([(RuleRows.Market, 7), (RuleRows.Setup, 7), (RuleRows.Floors, 7), (RuleRows.Profit, 7), (RuleRows.Cover, 7), (ReleaseTone.ReadStep, 4), (ReleaseTone.Gate, 2)], 7),
                Text(store, $"SELECT gates FROM rule_night WHERE index_code = '{index}' AND rule = '{variant}';"));

            store.Execute("UPDATE reading_pin SET history_completed_at = NULL WHERE id = 1;");

            Assert.Equal(string.Empty, Fired(await AnswersAsync(tone)));
            Assert.All((await AnswersAsync(tone)).Where(answer => answer.Ticker.StartsWith('T')), answer => Assert.Equal(IndexRules.ToneNotHeld, answer.Reason));

            await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-tone-open", default, register, at.AddHours(1));

            Assert.Contains(
                $"; the release readings: reading version 1: 7 print(s) inside the window, 0 read, 7 where {ReleaseTone.HistoryOpen}",
                Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-tone-open';"),
                StringComparison.Ordinal);

            // And its card says where its members stopped: none reaching a release read.
            await new RuleCards(clock, store.DatabaseFile).RunAsync("cards-tone-open", register, at.AddHours(1));

            Assert.Equal(
                RuleRows.GatesJson([(RuleRows.Market, 7), (RuleRows.Setup, 7), (RuleRows.Floors, 7), (RuleRows.Profit, 7), (RuleRows.Cover, 7), (ReleaseTone.ReadStep, 0), (ReleaseTone.Gate, 0)], 7),
                Text(store, $"SELECT gates FROM rule_night WHERE index_code = '{index}' AND rule = '{variant}';"));
        }
    }

    [Fact]
    public async Task TheFormsAgainRegistersAnIndexsVariantsAgainWholeAndRefusesAsItRefusedBefore()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        PinVersion(store, complete: true);
        PinVersion(store, complete: false);

        Task<(int Code, string Said)> Variants(DateTimeOffset when, string index, string variants, params string[] more) =>
            RegisterVerbAt(store, when, [RegisterVerb.IndexVariants, DriftRule.Name, "--index", index, "--variants", variants, .. more]);

        // The variant with the market check off stands on the S&P 400, as the remedy of 19.1 registered it, from 20.11 stating
        // the floor of no check, nought.
        Assert.Equal(0, (await Variants(at, "MID", "largeBreadth=2,breadthFloor=0")).Code);

        var standing = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");

        // The form without --again refuses where a rule of the family stands, as before; --again refuses with no evidence and
        // the evidence without --again; it refuses a standing variant left out, nothing added, a version whose history read
        // is not complete or that is no whole number, and a family none of whose rules stands. Each writes nothing.
        var refusals = new[]
        {
            (await Variants(at.AddSeconds(1), "MID", "largeBreadth=2,breadthFloor=0;readingVersion=1")).Said,
            (await Variants(at.AddSeconds(2), "MID", "largeBreadth=2,breadthFloor=0;readingVersion=1", RegisterVerb.Again)).Said,
            (await Variants(at.AddSeconds(3), "MID", "largeBreadth=2,breadthFloor=0;readingVersion=1", "--evidence", "e")).Said,
            (await Variants(at.AddSeconds(4), "MID", "readingVersion=1", RegisterVerb.Again, "--evidence", "e")).Said,
            (await Variants(at.AddSeconds(5), "MID", "largeBreadth=2,breadthFloor=0", RegisterVerb.Again, "--evidence", "e")).Said,
            (await Variants(at.AddSeconds(6), "MID", "largeBreadth=2,breadthFloor=0;readingVersion=2", RegisterVerb.Again, "--evidence", "e")).Said,
            (await Variants(at.AddSeconds(7), "MID", "largeBreadth=2,breadthFloor=0;readingVersion=1.5", RegisterVerb.Again, "--evidence", "e")).Said,
            (await Variants(at.AddSeconds(8), "SML", "readingVersion=1", RegisterVerb.Again, "--evidence", "e")).Said,
        };

        Assert.Contains("rules of the drift on the S&P 400 already stand registered", refusals[0], StringComparison.Ordinal);
        Assert.Contains("--again retires the variants standing, so it takes --evidence saying why", refusals[1], StringComparison.Ordinal);
        Assert.Contains("--evidence is given without --again", refusals[2], StringComparison.Ordinal);
        Assert.Contains("stands and is not among the variants given", refusals[3], StringComparison.Ordinal);
        Assert.Contains("every variant given stands already, so none would be added", refusals[4], StringComparison.Ordinal);
        Assert.Contains("reads reading version 2, whose history read is not complete", refusals[5], StringComparison.Ordinal);
        Assert.Contains("is registered at reading version 1.5, which names no version", refusals[6], StringComparison.Ordinal);
        Assert.Contains("no rule of the drift on the S&P 600 stands registered, so there are no variants to register again", refusals[7], StringComparison.Ordinal);
        Assert.Equal(standing, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // Given both, the standing variant is replayed first, said to restart since no replay reads an index rule, retired,
        // and registered again beside the tone variant at one instant.
        var again = at.AddMinutes(1);
        var (code, said) = await Variants(again, "MID", "largeBreadth=2,breadthFloor=0;readingVersion=1", RegisterVerb.Again, "--evidence", "the tone variant joins");

        Assert.Equal(0, code);
        Assert.Contains("restarts its record at this registration: " + FamilyReplay.NotReplayedOnItsIndex, said, StringComparison.Ordinal);
        Assert.Contains("retired 1 and registered 2 at one instant, family of 2 of 9", said, StringComparison.Ordinal);

        var stamp = again.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var rows = TextRows(store, $"SELECT event || '|' || COALESCE(retires, candidate) FROM candidate_register WHERE registered_at = '{stamp}' ORDER BY id;");

        Assert.Equal(3, rows.Count);
        Assert.StartsWith("retired|the S&P 400 drift rule", rows[0], StringComparison.Ordinal);
        Assert.EndsWith(", the market check off", rows[1], StringComparison.Ordinal);
        Assert.EndsWith("its print's release reading better with its outlook not cut under reading version 1", rows[2], StringComparison.Ordinal);

        // From 20.11 a family whose live rule stands is registered again beside it: the tone variant is the live rule's setting
        // with the condition, and the live rule stands on untouched, nothing of it retired.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            $"SELECT MAX(id) + 1, 'the live S&P 600 drift rule', 'a rule', 'a test', 'drift-600', '{CandidateEvaluator.Write(IndexRules.Provisional(IndexEvaluator("drift-600")))}', '{IndexEvaluator("drift-600").Version}', 'registered', NULL, '2026-10-12T12:00:00Z', NULL FROM candidate_register;");

        var beside = await Variants(at.AddMinutes(2), "SML", "readingVersion=1", RegisterVerb.Again, "--evidence", "the tone variant joins the frozen drift");

        Assert.Equal(0, beside.Code);
        Assert.Contains("retired 0 and registered 1 at one instant, family of 2 of 9", beside.Said, StringComparison.Ordinal);
        Assert.Equal(["registered|the live S&P 600 drift rule"], TextRows(store, "SELECT event || '|' || candidate FROM candidate_register WHERE evaluator = 'drift-600' AND event = 'registered' AND candidate LIKE 'the live %';"));
    }

    [Fact]
    public void TheVariantsCardChipsNameTheConditionAndThePerRuleStepKeepsItNotTestableInTheOperatorsWords()
    {
        var tone = TheSetupFamilies.ToneVariant(2);
        var live = TheSetupFamilies.Drifts[0];
        var indexTone = new Dictionary<string, double>(IndexRules.Provisional(IndexEvaluator("drift-400")), StringComparer.Ordinal) { [ReleaseTone.VersionParameter] = 2 };

        // The card's chips name the condition in its short words, and the variant's tab is named by it alone beside the live
        // rule's chips, on the S&P 500 and on an index.
        Assert.Equal(new RuleChip(ReleaseTone.VersionParameter, "release better, outlook not cut"), RuleChips.Of(RuleKind.Drift, tone.Parameters)[^1]);
        Assert.Equal("V9 · release better, outlook not cut", RuleChips.TabWords(9, RuleChips.Differing(RuleChips.Of(RuleKind.Drift, live.Parameters), RuleChips.Of(RuleKind.Drift, tone.Parameters))));
        Assert.Equal(
            "V2 · release better, outlook not cut",
            RuleChips.TabWords(2, RuleChips.Differing(RuleChips.Of(RuleKind.IndexDrift, IndexRules.Provisional(IndexEvaluator("drift-400"))), RuleChips.Of(RuleKind.IndexDrift, indexTone))));

        // The per-rule step keeps a rule reading a release reading with the operator's words and no figure, and reads every
        // rule naming no version as it read it before.
        RegisterRow Row(Registration one) => new(1, one.Candidate, one.Rule, one.Test, one.Evaluator, CandidateEvaluator.Write(one.Parameters), "v", CandidateFamily.Registered, null, FreezeAt, null);

        var kept = RuleTests.NotWalkedYet(DriftRule.Name, Row(tone))!;

        Assert.Equal(RuleTests.ReleaseReading, kept.Why);
        Assert.Equal("not testable: its release reading joins the tester at 20.8", EquityBrief.Core.Loop.RuleVerdict.Of(false, kept.Why, null, null, null, null, null));
        Assert.Null(RuleTests.NotWalkedYet(DriftRule.Name, Row(live)));
        Assert.Equal(RuleTests.ReleaseReading, RuleTests.NotWalkedYet(DriftRule.Name, Row(new Registration("an index tone variant", "a rule", "a test", "drift-400", indexTone)))!.Why);
    }
}
