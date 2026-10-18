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
    // held; which of them the night may read is the core's one rule, asked of each.
    const string ForPrint = @"
        SELECT d.accession, d.accepted_at, r.outcome, r.cause, r.results, r.outlook, r.tone, r.demand
        FROM release_document d
        LEFT JOIN release_reading r ON r.accession = d.accession AND r.pin = $pin
        WHERE d.cik = $cik AND d.accepted_at >= $from
        ORDER BY d.accepted_at DESC, d.accession DESC;
    ";

    // A filer's releases read under the newest version, newest first.
    const string Newest = @"
        SELECT d.accession, d.accepted_at, r.results, r.outlook, r.tone, r.demand, r.pin
        FROM release_document d
        JOIN release_reading r ON r.accession = d.accession AND r.outcome = 'read'
        WHERE d.cik = $cik
          AND r.pin = (SELECT MAX(id) FROM reading_pin)
        ORDER BY d.accepted_at DESC, d.accession DESC;
    ";

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
