using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.7: the reviews compared on the local model. Over the fixture's research pass, each review
// the run log holds is asked again as the review asked it, over the pass's version, its night's facts file and the
// documents it cites, each round checked in memory; a review whose version is not stored or whose night's facts file is
// no longer held whole is named and not asked; each outcome and each sentence the drafts handed held worked by hand; the
// file written under the data root and every table's rows as they were.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheReviewComparisonAsksEachStoredReviewAgainAndStatesTheAgreementWritingNoStoreRow()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        const string Cases = "The two cases";
        const string Risks = "The risks, each with what would confirm it";

        string Stored(string section, string column) =>
            Query(store, $"SELECT {column} FROM research_section WHERE ticker = 'KEYS' AND section = '{section}' AND version = 1;").Single();

        var cases = Stored(Cases, "prose");
        var risks = Stored(Risks, "prose");
        var riskFields = Stored(Risks, "parts");
        var asOf = Stored(Cases, "as_of");
        var caseSentences = ClaimRules.Sentences(cases).Select(sentence => sentence.Text).ToArray();

        // The stored review of the two cases changed its draft's last sentence and passed first time; the stored review of
        // the risks was refused and then kept its draft whole, and was left out.
        var changedLast = string.Join(" ", caseSentences[..^1].Append("The case against turns on a quarter nobody has reported yet."));

        // A version the store does not hold, and a version written after a night whose facts file the retention emptied.
        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason, parts) " +
            $"SELECT ticker, section, 8, '2026-09-10', model, status, prose, source_ids, reject_reason, parts FROM research_section WHERE ticker = 'KEYS' AND section = '{Cases}' AND version = 1;");
        store.Execute("INSERT INTO facts (ticker, session_date, payload, payload_hash, material_changes) VALUES ('KEYS', '2026-09-09', '', '', '[]');");

        static string Detail(string section, int version, string handed, string outcome, params string?[] rounds) =>
            JsonSerializer.Serialize(new
            {
                ticker = "KEYS",
                section,
                mode = SectionTrial.ReviewRound,
                profile = "deepseek",
                model = "deepseek-flash",
                compared = new { version, model = "deepseek-flash", status = "accepted", prose = handed },
                rounds = rounds.Select((prose, at) => new { round = at + 1, prose, verdict = "as stored", price = "0" }),
                outcome,
                calls = rounds.Length,
                cost = "0",
            });

        void Review(string at, string section, int version, string handed, string outcome, params string?[] rounds) =>
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
                $"('research-{at}-KEYS', '{SectionTrial.ReviewStageFor(section)}', '2026-09-30T{at}Z', '2026-09-30T{at}Z', '{outcome}', 0, 0, 0, '0', '{Detail(section, version, handed, outcome, rounds).Replace("'", "''", StringComparison.Ordinal)}');");

        Review("14:00:00", Cases, 1, cases, SectionTrial.FirstTime, changedLast);
        Review("14:10:00", Risks, 1, risks, SectionTrial.LeftOut, "A first draft the review refused.", risks);
        Review("14:20:00", Cases, 7, cases, SectionTrial.FirstTime, cases);
        Review("14:30:00", Cases, 8, cases, SectionTrial.FirstTime, cases);

        // Two nights on the run log, the second writing the facts file the retention later emptied; and a version written
        // after both whose review was asked between them, over the first night's facts, which stand whole, so it is asked
        // and the second night's empty row is not read in their place.
        static string Night(string session) =>
            $"('night-{session.Replace("-", "", StringComparison.Ordinal)}T230000Z', 'fetch', '{session}T23:00:00Z', '{session}T23:05:00Z', 'ok', 0, 0, 1, '0', '{{\"members\":4,\"session\":\"{session}\"}}'), " +
            $"('night-{session.Replace("-", "", StringComparison.Ordinal)}T230000Z', 'facts', '{session}T23:10:00Z', '{session}T23:12:00Z', 'ok', 4, 0, 0, '0', '4 name(s), 4 file(s) written')";

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            Night("2026-09-08") + ", " + Night("2026-09-09") + ";");
        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason, parts) " +
            $"SELECT ticker, section, 9, '2026-09-10', model, status, prose, source_ids, reject_reason, parts FROM research_section WHERE ticker = 'KEYS' AND section = '{Cases}' AND version = 1;");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('research-early-KEYS', '{SectionTrial.ReviewStageFor(Cases)}', '2026-09-09T15:00:00Z', '2026-09-09T15:00:00Z', '{SectionTrial.FirstTime}', 0, 0, 0, '0', '{Detail(Cases, 9, cases, SectionTrial.FirstTime, cases).Replace("'", "''", StringComparison.Ordinal)}');");

        var before = RowsByTable(store);
        var settings = new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", Gemma, 30, 50176, 30);
        var runtime = AnsweringRuntime(loaded: false, chat: _ => throw new InvalidOperationException("the comparison's answers are the test's own"));
        var answers = new Queue<string>([cases, cases, "Not the fields the section asks for.", riskFields]);
        var feed = new LabelAnsweringFeed(ReadingFeedOver(runtime, settings), _ => Reply(answers.Dequeue()));
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var said = new StringWriter();

        var outcome = await new LocalComparison(feed, settings, clock, store.DatabaseFile, store.Root, NoWait).ReviewsAsync(said);

        // Worked by hand: five stored and three asked; the review between the nights and the two cases passed first time as
        // stored, and the risks, refused for an answer that is not its fields, passed on its retry where the stored review
        // left it out.
        Assert.Equal((5, 3, 2), (outcome.Stored, outcome.Asked, outcome.OutcomesAgreeing));
        Assert.Equal(
            [
                $"KEYS, {Cases}, version 7: {LocalComparison.VersionNotHeld}",
                $"KEYS, {Cases}, version 8: {LocalComparison.FactsNotHeld}",
            ],
            outcome.NotAsked);

        // The sentences: every one of the two cases' kept by both reviews between the nights; every one kept by the local
        // model and all but the last by the stored review on the later day; and every one of the risks' kept by both.
        var riskCount = ClaimRules.Sentences(risks).Count;

        Assert.Equal(
            (2 * caseSentences.Length + riskCount, 2 * caseSentences.Length - 1 + riskCount, 0, 1),
            (outcome.Sentences, outcome.BothKept, outcome.BothChanged, outcome.Differ));

        // Asked as the review asked: its first round handed the draft and the section's checks under the pass's own request,
        // at the readings' seed and the review's budget, and the risks asked once more after its refusal.
        Assert.Equal(4, feed.Asked.Count);

        var facts = EquityBrief.Core.Facts.FactsFile.Read(Query(store, $"SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date <= '{asOf}' AND payload != '' ORDER BY session_date DESC LIMIT 1;").Single());

        Assert.Contains(ReviewBrief.For(Cases, cases), feed.Asked[0].Text, StringComparison.Ordinal);
        Assert.All(feed.Asked, request => Assert.Equal((Gemma, ReadingSettings.Seed, LocalComparison.ReviewAnswerTokens), (request.Model, request.Seed, request.AnswerTokens)));
        Assert.True(facts.Count > 0);

        // Loaded where it was not and unloaded at the end, its line and its file, and every table's rows as they were.
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/load", StringComparison.Ordinal));
        Assert.Contains(runtime.Asked, request => request.StartsWith("POST /api/v1/models/unload", StringComparison.Ordinal));
        Assert.Contains($"compare-reviews: 5 reviews are stored; {Gemma} on {Runtime256} {RuntimeVersion} was asked 3 again and not 2, ", said.ToString(), StringComparison.Ordinal);
        Assert.Contains("; the outcome agreed on 2 of 3; ", said.ToString(), StringComparison.Ordinal);
        Assert.Equal("comparisons/reviews-20261019T120000Z.json", outcome.File);

        using (var written = JsonDocument.Parse(File.ReadAllText(Path.Combine(store.Root, "comparisons", "reviews-20261019T120000Z.json"))))
        {
            var rows = written.RootElement.GetProperty("reviews").EnumerateArray().Select(review => (review.GetProperty("section").GetString(), review.GetProperty("stored").GetString(), review.GetProperty("local").GetString())).ToArray();

            Assert.Equal([(Cases, SectionTrial.FirstTime, SectionTrial.FirstTime), (Cases, SectionTrial.FirstTime, SectionTrial.FirstTime), (Risks, SectionTrial.LeftOut, SectionTrial.OnRetry)], rows);
            Assert.Equal(["2026-09-08", "2026-09-08", "2026-09-08"], written.RootElement.GetProperty("reviews").EnumerateArray().Select(review => review.GetProperty("night").GetString()));
        }

        Assert.Equal(before, RowsByTable(store));
    }

    [Fact]
    public void TwoReviewsOfADraftAreSetSideBySideSentenceBySentence()
    {
        // Worked by hand: the first sentence kept by both, the second changed by the first review alone, the third by the
        // second alone and the fourth by both.
        var agreement = ReviewAgreement.Of(
            "Revenue rose. Margins held. Orders slowed. Cash fell.",
            "Revenue rose. Margins widened. Orders slowed. Cash was steady.",
            "Revenue rose. Margins held. Orders picked up. Cash was steady.");

        Assert.Equal(new ReviewAgreement(4, 1, 1, 2), agreement);
        Assert.Equal(2, agreement!.Agreeing);

        // A review that came to no draft is set beside nothing.
        Assert.Null(ReviewAgreement.Of("Revenue rose.", null, "Revenue rose."));
        Assert.Null(ReviewAgreement.Of("Revenue rose.", "Revenue rose.", null));
    }
}
