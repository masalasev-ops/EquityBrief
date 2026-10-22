using System.Globalization;
using EquityBrief.Core.Releases;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// The reading of the release that answers for a print on a night, under a reading version, and why there is none where
// there is none: the calendar places no session before the print's report date, no release the SEC accepted from the
// print's window is readable on the night, the release is not yet read under the version, or its reading was refused
// with a cause.
public sealed record PrintReading(string? Accession, DateTimeOffset? Accepted, ReleaseAnswer? Answer, string? Why);

// The newest release's reading for a filer, as a name's page draws it: the release, when the SEC accepted it, its four
// words under the reading version, and the version's number.
public sealed record NewestReading(string Accession, DateTimeOffset Accepted, ReleaseAnswer Answer, long Pin);

// The one reader of the release readings, from 20.3: every reader of what the local model read reads it here, so the
// rule of which release answers and from which night it is readable is written once.
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public static class ReleaseReadings
{
    public const string NotPlaced = "the calendar places no session before the print's report date";
    public const string NoRelease = "no release the SEC accepted from the print's window is readable on the night";
    public const string NotRead = "the release is not yet read under the reading version";
    public const string RefusedWhy = "the release's reading was refused";

    // A filer's releases from the print's window on, newest first, each with its reading under the version where one is
    // held; which of them the night may read is the core's one rule, asked of each. From the 20.3 correction of
    // 2026-10-22 a filing's release is the exhibit marked so, and a reading answers for it only where it read that
    // exhibit, so a reading of the exhibit it replaced answers for none.
    // see: A results release is the exhibit its filing's index page names as the release, and the lowest exhibit only where none is named so
    const string ForPrint = @"
        SELECT d.accession, d.accepted_at, r.outcome, r.cause, r.results, r.outlook, r.tone, r.demand
        FROM release_document d
        LEFT JOIN release_reading r ON r.accession = d.accession AND r.exhibit = d.exhibit AND r.pin = $pin
        WHERE d.cik = $cik AND d.accepted_at >= $from AND d.release = 1
        ORDER BY d.accepted_at DESC, d.accession DESC;
    ";

    // A filer's releases read under the newest version, newest first.
    const string Newest = @"
        SELECT d.accession, d.accepted_at, r.results, r.outlook, r.tone, r.demand, r.pin
        FROM release_document d
        JOIN release_reading r ON r.accession = d.accession AND r.exhibit = d.exhibit AND r.outcome = 'read'
        WHERE d.cik = $cik AND d.release = 1
          AND r.pin = (SELECT MAX(id) FROM reading_pin)
        ORDER BY d.accepted_at DESC, d.accession DESC;
    ";

    const string CompleteVersions = "SELECT id FROM reading_pin WHERE history_completed_at IS NOT NULL ORDER BY id DESC;";

    // Each stock's filer as its newest company row files it.
    const string Filers = "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;";

    public const string NoFiler = "the store matches no filer to the stock";

    // From 20.4 each stock's filer padded to ten digits, by its newest company row filing one.
    public static async Task<IReadOnlyDictionary<string, string>> FilersAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Filers;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var filers = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            filers[reader.GetString(0)] = EquityBrief.Core.Providers.SecEdgarArchive.Padded(reader.GetString(1));
        }

        return filers;
    }

    // From 20.4 a print's release as a rule reading it is handed under one reading version: no reading where the
    // version's history read is not complete or the store matches the stock no filer, and the reader's own reason where
    // no reading answers for the print on the night.
    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
    public static async Task<PrintRelease> ForRuleAsync(
        SqliteConnection connection,
        string? cik,
        DateOnly reportDate,
        DateOnly night,
        long version,
        bool complete,
        ReleaseCalendar calendar,
        TimeZoneInfo zone,
        CancellationToken cancellation = default)
    {
        if (!complete)
        {
            return new PrintRelease(version, null, ReleaseTone.HistoryOpen);
        }

        if (cik is null)
        {
            return new PrintRelease(version, null, NoFiler);
        }

        var read = await ForPrintAsync(connection, cik, reportDate, night, version, calendar, zone, cancellation);

        return new PrintRelease(version, read.Answer, read.Why);
    }

    // From 20.4 the reading versions whose history read is complete, newest first: the versions a registration may name
    // and the night reads readings under.
    public static async Task<IReadOnlyList<long>> CompleteVersionsAsync(SqliteConnection connection, SqliteTransaction? transaction = null, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        // A command is handed the connection's own transaction where one is open, so none given leaves it in place.
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = CompleteVersions;

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var versions = new List<long>();

        while (await reader.ReadAsync(cancellation))
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }

    // The release answering for a print reported on a day, read on a night, under a reading version, the print's window
    // placed by the calendar the caller reads sessions by.
    public static async Task<PrintReading> ForPrintAsync(
        SqliteConnection connection,
        string cik,
        DateOnly reportDate,
        DateOnly night,
        long pin,
        ReleaseCalendar calendar,
        TimeZoneInfo zone,
        CancellationToken cancellation = default)
    {
        if (ReleaseAvailability.WindowFrom(reportDate, zone, calendar) is not { } from)
        {
            return new PrintReading(null, null, null, NotPlaced);
        }

        await using var command = connection.CreateCommand();

        command.CommandText = ForPrint;
        command.Parameters.AddWithValue("$cik", cik);
        command.Parameters.AddWithValue("$pin", pin);
        command.Parameters.AddWithValue("$from", Instant(from));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var accepted = Parse(reader.GetString(1));

            if (!ReleaseAvailability.ReadableOn(accepted, night))
            {
                continue;
            }

            var accession = reader.GetString(0);

            if (reader.IsDBNull(2))
            {
                return new PrintReading(accession, accepted, null, NotRead);
            }

            return reader.GetString(2) == "read"
                ? new PrintReading(accession, accepted, new ReleaseAnswer(reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)), null)
                : new PrintReading(accession, accepted, null, RefusedWhy + ": " + (reader.IsDBNull(3) ? "no cause stated" : reader.GetString(3)));
        }

        return new PrintReading(null, null, null, NoRelease);
    }

    // The newest release a filer's page may draw on a night, read under the newest reading version, or none.
    public static async Task<NewestReading?> NewestAsync(SqliteConnection connection, string cik, DateOnly night, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Newest;
        command.Parameters.AddWithValue("$cik", cik);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var accepted = Parse(reader.GetString(1));

            if (ReleaseAvailability.ReadableOn(accepted, night))
            {
                return new NewestReading(reader.GetString(0), accepted, new ReleaseAnswer(reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)), reader.GetInt64(6));
            }
        }

        return null;
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
