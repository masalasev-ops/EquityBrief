using System.Globalization;
using EquityBrief.Core.Readings;

namespace EquityBrief.Api.Reading;

// The quarters a member's interest cover reads on a night where they are older than those its profit check reads: the end
// of the newest quarter the cover reads, and whether the profit check's newest quarter is read from its results release,
// its filing not in yet, rather than from a filing stating no interest.
public sealed record CoverRead(DateOnly To, bool FilingNotIn);

// From 23.10's second part the stock page says when the cover reads older quarters than the profit check: each quarter as
// the rules read it on the night, from its filing and then its results release, the profit check over the four newest and
// the cover over the four newest whose filing states their interest.
// see: The rules read each quarter from its filing and then its release from the moment the SEC made it public, and the cover from the four newest quarters stating interest
public sealed partial class ReadApi
{
    // The quarters the provider's fetches stored by the night label, newest fetch first, as the rules read them.
    const string CoverLabelsOf = @"
        SELECT period_end, fetched_at FROM reported_quarter
        WHERE ticker = $ticker AND session_date <= $night
        ORDER BY fetched_at DESC, period_end DESC;
    ";

    const string CoverSectorOf = "SELECT sector FROM company WHERE ticker = $ticker AND sector IS NOT NULL ORDER BY fetched_at DESC LIMIT 1;";

    // The filer's results releases accepted after a day and by the end of the night, each with its text.
    const string CoverReleasesOf = @"
        SELECT d.text_gzip, d.accepted_at FROM release_document d
        WHERE d.cik = (SELECT c.cik FROM company c WHERE c.ticker = $ticker AND c.cik IS NOT NULL ORDER BY c.fetched_at DESC LIMIT 1)
          AND d.release = 1 AND d.accepted_at > $from AND substr(d.accepted_at, 1, 10) <= $night
        ORDER BY d.accepted_at;
    ";

    // A member's readings for a night, or its newest, with the quarters its cover reads where they are older than its
    // profit check's.
    public async Task<MemberReadingRow?> MemberReadingWithCoverAsync(string ticker, DateOnly? on = null) =>
        await MemberReadingAsync(ticker, on) is { } row ? row with { Cover = await CoverReadAsync(ticker, row.SessionDate) } : null;

    // None where the two read the same newest quarter, where the cover reads no quarter stating interest, and for a financial
    // company, whose cover is not read.
    public async Task<CoverRead?> CoverReadAsync(string ticker, DateOnly night)
    {
        var fetched = new List<(DateOnly End, string Fetch)>();
        string? sector;

        await using (var connection = Open())
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = CoverLabelsOf;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                await using var reader = await command.ExecuteReaderAsync();

                // A label within a month of one a newer fetch made is that quarter, read as the newer fetch labels it.
                while (await reader.ReadAsync())
                {
                    var (end, fetch) = (Day(reader.GetString(0)), reader.GetString(1));

                    if (fetched.All(held => held.Fetch == fetch || Math.Abs(held.End.DayNumber - end.DayNumber) > QuartersAsRead.LabelWithin))
                    {
                        fetched.Add((end, fetch));
                    }
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = CoverSectorOf;
                command.Parameters.AddWithValue("$ticker", ticker);

                sector = await command.ExecuteScalarAsync() as string;
            }
        }

        var labels = fetched.Select(one => one.End).Distinct().ToList();

        if (labels.Count == 0 || string.Equals(sector, MemberReadings.Financials, StringComparison.Ordinal))
        {
            return null;
        }

        var filed = await FiledFactsAsync(ticker, labels.Min().AddYears(-2).AddDays(-2 * QuartersAsRead.LabelWithin), night);
        var releases = new List<ReleaseAccepted>();

        if (QuartersAsRead.Unfiled(labels, filed, night) is [var oldest, ..])
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();

            command.CommandText = CoverReleasesOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                using var compressed = new MemoryStream((byte[])reader.GetValue(0));
                using var unzipped = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionMode.Decompress);
                using var text = new StreamReader(unzipped, System.Text.Encoding.UTF8);

                releases.Add(new ReleaseAccepted(await text.ReadToEndAsync(), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
            }
        }

        var read = QuartersAsRead.Of(labels, filed, releases, night);
        var cover = MemberReadings.CoverQuarters(read.Select(quarter => quarter.Income), night);

        return read.Count == 0 || cover.Count == 0 || cover[0].PeriodEnd >= read[0].PeriodEnd
            ? null
            : new CoverRead(cover[0].PeriodEnd, read[0].From == QuarterFrom.Release);
    }
}
