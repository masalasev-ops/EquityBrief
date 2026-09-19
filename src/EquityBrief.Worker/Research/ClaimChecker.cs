using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// One section the checker moved, and where it moved it.
public sealed record CheckedSection(
    string Subject,
    string Section,
    int Version,
    string Status,
    string? Reason,
    string? EarlierReason);

public sealed record ClaimCheckOutcome(IReadOnlyList<CheckedSection> Checked)
{
    public int Accepted => Checked.Count(section => section.Status == ClaimChecker.Accepted);

    public int Rejected => Checked.Count(section => section.Status == ClaimChecker.Rejected);

    public int FellBack => Checked.Count(section => section.Status == ClaimChecker.Fallback);
}

// The claim checker. Every pending section, in both the research store and the
// theme store, read against its facts file and the stored documents it cites, and
// moved to accepted, rejected or fallback.
//
// Check every claim, then store it: a writer inserts a section as pending and only
// this component moves it, which is the per-operation split SCHEMA declares, so no
// section a reader is shown was ever judged by the thing that wrote it.
// see: Every number in written prose must exist in the facts file
// see: Every researched claim must name a stored source document
// see: A claim is a sentence, every sentence in a researched section names the document it rests on, and a window written in words is read as its number
//
// It reads the admissibility verdict on each cited row and never applies the test,
// because the test runs on a document as it is fetched and the runner is what
// fetches. It makes no request and no model call: the retry is the writer's,
// and what this component decides is whether a second refusal is a retry or the
// end of the pass.
public sealed class ClaimChecker(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ResearchSection, Touch.Read | Touch.Update),
            new StoreTouch(Store.ThemeSection, Touch.Read | Touch.Update),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "claims";

    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Fallback = "fallback";

    // What a fallback after a second refusal says before the refusal itself, so
    // a reader of one row can tell it from a section that never had a source.
    public const string RejectedTwice = "rejected twice";

    // A source list a writer stored that is not a list of ids. A defect in the
    // writer rather than a property of the prose, and refused as its own reason
    // rather than read as an empty list, which would say no admissible source was
    // found about a section whose sources nobody could read.
    public const string UnreadableSources = "a source list that is not a JSON list of document ids";

    const string PendingResearch = @"
        SELECT ticker, section, version, as_of, prose, source_ids
        FROM research_section
        WHERE status = 'pending'
        ORDER BY ticker, section, version;
    ";

    const string PendingThemes = @"
        SELECT theme, section, version, as_of, prose, source_ids
        FROM theme_section
        WHERE status = 'pending'
        ORDER BY theme, section, version;
    ";

    // The version before this one, which is what decides whether a refusal is a
    // first attempt or the retry.
    const string EarlierResearch = @"
        SELECT status, as_of, reject_reason
        FROM research_section
        WHERE ticker = $subject AND section = $section AND version < $version
        ORDER BY version DESC
        LIMIT 1;
    ";

    const string EarlierTheme = @"
        SELECT status, as_of, reject_reason
        FROM theme_section
        WHERE theme = $subject AND section = $section AND version < $version
        ORDER BY version DESC
        LIMIT 1;
    ";

    // The newest facts file on or before the day the section was written that the
    // retention has not emptied. A section is checked against the file it could
    // have been written from, and a later file carrying tomorrow's close would
    // refuse a sentence that was right when it was written.
    const string FactsFor = @"
        SELECT payload, session_date
        FROM facts
        WHERE ticker = $ticker AND session_date <= $as_of AND payload != ''
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string Document = @"
        SELECT id, url, title, published_on, fetched_at, body, admissibility
        FROM source_document
        WHERE id = $id;
    ";

    // Two columns and no more, and only a pending row. The prose, the model, the
    // date and the source list a writer inserted are the ones a reader is shown,
    // and a section already moved is not judged twice.
    const string MoveResearch = @"
        UPDATE research_section
        SET status = $status, reject_reason = $reason
        WHERE ticker = $subject AND section = $section AND version = $version AND status = 'pending';
    ";

    const string MoveTheme = @"
        UPDATE theme_section
        SET status = $status, reject_reason = $reason
        WHERE theme = $subject AND section = $section AND version = $version AND status = 'pending';
    ";

    const string Moved = @"
        SELECT (SELECT COUNT(*) FROM research_section WHERE status != 'pending')
             + (SELECT COUNT(*) FROM theme_section WHERE status != 'pending');
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    // The stage a theme pass's check writes its row under. A name's pass refreshes its theme
    // inside its own run, so the theme's check and the name's own are two rows of one run and
    // cannot share a stage, which the 6.11 production run found when MSFT's theme wrote its
    // cycle and the name's first check then had no row it could write.
    public const string ThemeStage = "theme claims";

    // The stage a round of a pass writes its row under, for the reason the prose
    // writer's is: one row per run per stage, and a pass checks more than once.
    public static string StageFor(string? round, bool theme = false) =>
        (theme ? ThemeStage : Stage) + (round is null ? string.Empty : ", " + round);

    public Task<ClaimCheckOutcome> RunAsync(string runId, string? round = null, CancellationToken cancellation = default) =>
        CheckAllAsync(runId, StageFor(round), cancellation);

    // The check a theme pass runs. It moves every pending section, as any check does, and
    // writes its row under the theme's stage.
    public Task<ClaimCheckOutcome> RunForThemeAsync(string runId, string? round = null, CancellationToken cancellation = default) =>
        CheckAllAsync(runId, StageFor(round, theme: true), cancellation);

    async Task<ClaimCheckOutcome> CheckAllAsync(string runId, string stage, CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var before = await CountAsync(connection, Moved, cancellation);
        var checkedSections = new List<CheckedSection>();

        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            var sqlite = (SqliteTransaction)transaction;

            foreach (var pending in await PendingAsync(connection, sqlite, PendingResearch, cancellation))
            {
                checkedSections.Add(await CheckAsync(connection, sqlite, pending, theme: false, cancellation));
            }

            foreach (var pending in await PendingAsync(connection, sqlite, PendingThemes, cancellation))
            {
                checkedSections.Add(await CheckAsync(connection, sqlite, pending, theme: true, cancellation));
            }

            await transaction.CommitAsync(cancellation);
        }

        var outcome = new ClaimCheckOutcome(checkedSections);

        // Measured from the store rather than counted here, because the halt
        // condition keys on this number and a stage's own count of what it wrote
        // is the stage's opinion.
        var written = await CountAsync(connection, Moved, cancellation) - before;

        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", runId);
        record.Parameters.AddWithValue("$stage", stage);
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$rows_written", written);
        record.Parameters.AddWithValue("$detail", Detail(outcome));

        await record.ExecuteNonQueryAsync(cancellation);

        return outcome;
    }

    sealed record PendingSection(string Subject, string Section, int Version, string AsOf, string Prose, string SourceIds);

    async Task<CheckedSection> CheckAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingSection pending,
        bool theme,
        CancellationToken cancellation)
    {
        var (status, reason, earlier) = await DecideAsync(connection, transaction, pending, theme, cancellation);

        await using var move = connection.CreateCommand();

        move.Transaction = transaction;
        move.CommandText = theme ? MoveTheme : MoveResearch;
        move.Parameters.AddWithValue("$status", status);
        move.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        move.Parameters.AddWithValue("$subject", pending.Subject);
        move.Parameters.AddWithValue("$section", pending.Section);
        move.Parameters.AddWithValue("$version", pending.Version);

        await move.ExecuteNonQueryAsync(cancellation);

        return new CheckedSection(pending.Subject, pending.Section, pending.Version, status, reason, earlier);
    }

    async Task<(string Status, string? Reason, string? Earlier)> DecideAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingSection pending,
        bool theme,
        CancellationToken cancellation)
    {
        if (!TryIds(pending.SourceIds, out var ids))
        {
            return (Fallback, UnreadableSources, null);
        }

        var sources = new List<StoredDocument?>();

        foreach (var id in ids)
        {
            sources.Add(await DocumentAsync(connection, transaction, id, cancellation));
        }

        // A theme has no facts file, because a facts file is one per name, and
        // nothing the store holds is computed for an industry. So a theme section
        // is held against an empty one and every figure in it is refused, which is
        // the rule 6.9 settled when it wrote the first theme section rather than a
        // gap in it.
        // see: A theme section states no figure, because nothing the store holds is computed for an industry
        var (facts, night) = theme
            ? ([], null)
            : await FactsAsync(connection, transaction, pending.Subject, pending.AsOf, cancellation);

        var verdict = ClaimRules.Check(pending.Section, pending.Prose, facts, sources, night);

        if (verdict.Passes)
        {
            return (Accepted, null, null);
        }

        // Straight to fallback, because a rewrite cannot create a source.
        if (verdict.NoAdmissibleSource)
        {
            return (Fallback, verdict.Reason, null);
        }

        var previous = await EarlierAsync(connection, transaction, pending, theme, cancellation);

        // The retry, which is a refusal immediately after a refusal written on the
        // same day. A version after a fallback, or a refusal on a later day, is a
        // fresh first attempt, so the bound is one retry per pass rather than one
        // for the section for all time.
        return previous is { Status: Rejected } earlier
            && string.Equals(earlier.AsOf, pending.AsOf, StringComparison.Ordinal)
            ? (Fallback, $"{RejectedTwice}: {verdict.Reason}", earlier.Reason)
            : (Rejected, verdict.Reason, null);
    }

    static bool TryIds(string json, out IReadOnlyList<string> ids)
    {
        ids = [];

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            {
                return false;
            }

            ids = [.. document.RootElement.EnumerateArray().Select(item => item.GetString()!)];

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static async Task<IReadOnlyList<PendingSection>> PendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        var pending = new List<PendingSection>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            pending.Add(new PendingSection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return pending;
    }

    sealed record Earlier(string Status, string AsOf, string? Reason);

    static async Task<Earlier?> EarlierAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PendingSection pending,
        bool theme,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = theme ? EarlierTheme : EarlierResearch;
        command.Parameters.AddWithValue("$subject", pending.Subject);
        command.Parameters.AddWithValue("$section", pending.Section);
        command.Parameters.AddWithValue("$version", pending.Version);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new Earlier(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))
            : null;
    }

    // The facts file with the night it was computed for, which the calendar section's
    // dates are held after.
    static async Task<(IReadOnlyList<Fact> Facts, DateOnly? Night)> FactsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string ticker,
        string asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = FactsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (FactsFile.Read(reader.GetString(0)), DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture))
            : ([], null);
    }

    static async Task<StoredDocument?> DocumentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = Document;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation))
        {
            return null;
        }

        return new StoredDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3)
                ? null
                : DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6));
    }

    static async Task<long> CountAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    // The run log's line. Every section moved is named with its outcome, and a
    // fallback after a second refusal carries both attempts' offending text, which
    // is what section 18 promises: the first attempt's reason is on its own row
    // and would otherwise be a row a person has to know to look for.
    public static string Detail(ClaimCheckOutcome outcome)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.Checked.Count} section(s) checked, {outcome.Accepted} accepted, {outcome.Rejected} rejected, {outcome.FellBack} fell back");

        foreach (var section in outcome.Checked.Where(section => section.Status != Accepted))
        {
            line += string.Create(
                CultureInfo.InvariantCulture,
                $". {section.Subject} {section.Section} version {section.Version} {section.Status}: {section.Reason}");

            if (section.EarlierReason is { } earlier)
            {
                line += "; the first attempt: " + earlier;
            }
        }

        return line;
    }

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
