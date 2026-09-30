using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// A trial the research job's configuration names: a second profile, the sections it is tried on, the reports it
// runs over and the day it runs from.
public sealed record ResearchTrial(ResearchModelSettings Profile, IReadOnlyList<string> Sections, int Reports, DateOnly From);

// What one trial section came to beside the pass's own draft.
public sealed record TrialOutcome(string Section, string Outcome, int Rounds, decimal Cost);

// A second profile asked for named sections after a pass wrote them, its drafts recorded beside the report and never
// in it. The request is the one the pass built for the section's first draft: the same facts file, the documents the
// pass's version cites, and for the short version the sections the pass had accepted before it asked. The answer is
// checked in memory by the checker's own rules, a refused draft told what was refused once, as a pass's retry is,
// and each section's rounds, verdicts and prices written to the run log under the pass's run, a row a section. No
// research section row is written, so the page draws the pass's report whatever the trial wrote, and the trial stops
// by itself once it has run over the reports it names.
// see: A trial asks a second profile for named sections after a report and records its drafts beside the report, never in it
public sealed class SectionTrial(SpendCap cap, ResearchTrial trial, IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    // The stage a trial section's row is written under, the section after it, and the round its calls are asked in,
    // which the run page leaves out of a report's cost and count.
    public const string Stage = "section trial";
    public const string Round = TrialCalls.Round;

    public const string FirstTime = "first time";
    public const string OnRetry = "on retry";
    public const string LeftOut = "left out";
    public const string NotAnswered = "not answered";

    public static string StageFor(string section) => Stage + ": " + section;

    // The section written last from the others, as the pass names it.
    static string Summary => ClaimRules.Sections[^1];

    const string TrialReports = @"
        SELECT COUNT(DISTINCT run_id) FROM run_log
        WHERE stage LIKE 'section trial:%'
          AND started_at >= $from
          AND CASE WHEN json_valid(detail) THEN json_extract(detail, '$.profile') END = $profile;
    ";

    const string PassRow = @"
        SELECT detail FROM run_log WHERE run_id = $run_id AND stage = 'research';
    ";

    const string Version = @"
        SELECT version, as_of, model, status, prose, source_ids FROM research_section
        WHERE ticker = $ticker AND section = $section AND version = $version;
    ";

    const string NewestAccepted = @"
        SELECT prose FROM research_section
        WHERE ticker = $ticker AND section = $section AND status = 'accepted' AND prose != ''
        ORDER BY version DESC
        LIMIT 1;
    ";

    const string FactsFor = @"
        SELECT payload, session_date FROM facts
        WHERE ticker = $ticker AND session_date <= $as_of AND payload != ''
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string Document = @"
        SELECT id, url, title, published_on, fetched_at, body, admissibility FROM source_document WHERE id = $id;
    ";

    // The calls and their spend stand on the calls' own rows, which the caps and the run page sum, so the section's
    // row carries them in its detail alone, as a pass's own row does.
    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    public async Task<IReadOnlyList<TrialOutcome>> RunAsync(string ticker, string runId, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The trial stops by itself once it has run over the reports it names.
        if (await CountAsync(connection, cancellation) >= trial.Reports)
        {
            return [];
        }

        if (await PassAsync(connection, runId, cancellation) is not { } pass)
        {
            return [];
        }

        var outcomes = new List<TrialOutcome>();

        foreach (var section in trial.Sections)
        {
            // The pass's first draft of the section in its paid lane, which the trial is asked beside, and nothing
            // where the pass wrote none or wrote it with the trial's own profile.
            if (pass.Written.FirstOrDefault(written => written.Section == section && !written.Retry) is not { } first
                || string.Equals(first.Model, cap.Model, StringComparison.Ordinal)
                || await VersionAsync(connection, ticker, section, first.Version, cancellation) is not { } version)
            {
                continue;
            }

            var (facts, night) = await FactsAsync(connection, ticker, version.AsOf, cancellation);

            if (facts is null)
            {
                continue;
            }

            var sources = await SourcesAsync(connection, version.SourceIds, cancellation);
            var admitted = sources.OfType<StoredDocument>().Where(document => document.Admitted).ToArray();

            IReadOnlyList<(string Section, string Prose)>? summarised = section == Summary
                ? await HandedToTheSummaryAsync(connection, ticker, pass, cancellation)
                : null;

            outcomes.Add(await TryAsync(connection, ticker, section, runId, facts, night, admitted, summarised, version, cancellation));
        }

        return outcomes;
    }

    async Task<TrialOutcome> TryAsync(
        SqliteConnection connection,
        string ticker,
        string section,
        string runId,
        IReadOnlyList<Fact> facts,
        DateOnly night,
        IReadOnlyList<StoredDocument> admitted,
        IReadOnlyList<(string Section, string Prose)>? summarised,
        PassVersion version,
        CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;
        var documents = admitted.Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body!)).ToArray();
        var rounds = new List<object>();
        var cost = 0m;
        var calls = 0;
        string? retry = null;
        var outcome = NotAnswered;

        for (var round = 1; round <= 2; round++)
        {
            var request = SectionPrompt.PaidRequest(cap.Model, ticker, section, facts, documents, retry, summarised, night);
            var call = await cap.AskAsync(request, runId, round == 1 ? Round : Round + ", " + ResearchRunner.SecondRound, cancellation);

            calls++;
            cost += call.Price;

            if (call.Answer is not { } answer)
            {
                rounds.Add(new { round, prose = (string?)null, verdict = call.Failure ?? "no answer", price = Money(call.Price) });
                outcome = round == 1 ? NotAnswered : LeftOut;

                break;
            }

            var (prose, parts) = RiskFields.IsRisks(section) ? RiskFields.FromAnswer(answer.Text) : (answer.Text, null);
            var verdict = ClaimRules.Check(section, prose, facts, [.. admitted.Cast<StoredDocument?>()], night, parts);

            rounds.Add(new { round, prose, verdict = verdict.Passes ? "accepted" : verdict.Reason, price = Money(call.Price) });

            if (verdict.Passes)
            {
                outcome = round == 1 ? FirstTime : OnRetry;

                break;
            }

            outcome = LeftOut;
            retry = RetryBrief.For(verdict.Findings);
        }

        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", runId);
        record.Parameters.AddWithValue("$stage", StageFor(section));
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$outcome", outcome);
        record.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
        {
            ticker,
            section,
            profile = trial.Profile.Profile,
            model = cap.Model,
            compared = new { version = version.Number, model = version.Model, status = version.Status, prose = version.Prose },
            rounds,
            outcome,
            calls,
            cost = Money(cost),
        }));

        await record.ExecuteNonQueryAsync(cancellation);

        return new TrialOutcome(section, outcome, rounds.Count, cost);
    }

    sealed record Written(string Section, int Version, string Model, bool Retry);

    sealed record Pass(DateOnly AsOf, IReadOnlyList<Written> Written);

    sealed record PassVersion(int Number, DateOnly AsOf, string Model, string Status, string Prose, string SourceIds);

    async Task<long> CountAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = TrialReports;
        command.Parameters.AddWithValue("$from", trial.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$profile", trial.Profile.Profile);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task<Pass?> PassAsync(SqliteConnection connection, string runId, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PassRow;
        command.Parameters.AddWithValue("$run_id", runId);

        if (await command.ExecuteScalarAsync(cancellation) is not string detail)
        {
            return null;
        }

        using var parsed = JsonDocument.Parse(detail);
        var root = parsed.RootElement;

        if (!root.TryGetProperty("written", out var written) || written.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("asOf", out var asOf) || asOf.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return new Pass(
            DateOnly.ParseExact(asOf.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            [
                .. written.EnumerateArray().Select(one => new Written(
                    one.GetProperty("section").GetString()!,
                    one.GetProperty("version").GetInt32(),
                    one.GetProperty("model").GetString()!,
                    one.GetProperty("retry").GetBoolean())),
            ]);
    }

    static async Task<PassVersion?> VersionAsync(SqliteConnection connection, string ticker, string section, int version, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Version;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$section", section);
        command.Parameters.AddWithValue("$version", version);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new PassVersion(
                reader.GetInt32(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5))
            : null;
    }

    // The sections the pass had accepted before it asked for the short version: each section it wrote first in
    // this pass where the checker accepted that draft, and each it did not write standing accepted from before.
    async Task<IReadOnlyList<(string Section, string Prose)>> HandedToTheSummaryAsync(SqliteConnection connection, string ticker, Pass pass, CancellationToken cancellation)
    {
        var handed = new List<(string Section, string Prose)>();

        foreach (var section in ClaimRules.Sections.Where(section => section != Summary))
        {
            if (pass.Written.FirstOrDefault(written => written.Section == section && !written.Retry) is { } first)
            {
                if (await VersionAsync(connection, ticker, section, first.Version, cancellation) is { Status: ClaimChecker.Accepted, Prose.Length: > 0 } accepted)
                {
                    handed.Add((section, accepted.Prose));
                }

                continue;
            }

            if (pass.Written.Any(written => written.Section == section))
            {
                continue;
            }

            await using var command = connection.CreateCommand();

            command.CommandText = NewestAccepted;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$section", section);

            if (await command.ExecuteScalarAsync(cancellation) is string prose)
            {
                handed.Add((section, prose));
            }
        }

        return handed;
    }

    static async Task<(IReadOnlyList<Fact>? Facts, DateOnly Night)> FactsAsync(SqliteConnection connection, string ticker, DateOnly asOf, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FactsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (FactsFile.Read(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture))
            : (null, asOf);
    }

    static async Task<IReadOnlyList<StoredDocument?>> SourcesAsync(SqliteConnection connection, string sourceIds, CancellationToken cancellation)
    {
        var sources = new List<StoredDocument?>();

        foreach (var id in JsonSerializer.Deserialize<string[]>(sourceIds) ?? [])
        {
            await using var command = connection.CreateCommand();

            command.CommandText = Document;
            command.Parameters.AddWithValue("$id", id);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            sources.Add(await reader.ReadAsync(cancellation)
                ? new StoredDocument(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetString(6))
                : null);
        }

        return sources;
    }

    static string Money(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
