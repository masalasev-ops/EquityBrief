using System.Globalization;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// Which readings go with winners, the monthly run's step a month an index after the point-in-time checks: each family's
// settled setups on the index read from the ledger, every reading's row worked over them by the core's arithmetic and
// written whole under a run of its own with its run log row, in one transaction under the drain's lock; with print,
// printed and nothing written. It reads the ledger alone, makes no request, calls no model and changes no rule.
// see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
public sealed partial class WalkForwardTester
{
    public const string LinksVerb = "loop-links";

    // The families an index's ledger holds setups of, in the one family order.
    const string LinkedFamilies = "SELECT DISTINCT family FROM setup WHERE index_code = $index AND settled = 1 AND result IS NOT NULL;";

    // A family's settled setups on an index, each with its result and every reading, in the ledger's own order.
    static readonly string LinkedSetups =
        "SELECT session_date, result, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND family = $family AND settled = 1 AND result IS NOT NULL ORDER BY session_date, ticker;";

    public async Task<int> LinksAsync(string index, string? month, CancellationToken cancellation = default, bool print = false)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{LinksVerb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (month is not null && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            output.WriteLine($"{LinksVerb}: '--month' takes a month as yyyy-MM, not '{month}'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{LinksVerb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{LinksVerb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var runMonth = month ?? FormattableString.Invariant($"{started:yyyy-MM}");
        var links = new List<(string Family, ReadingLink Link)>();

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            var families = new List<string>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = LinkedFamilies;
                command.Parameters.AddWithValue("$index", index);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    families.Add(reader.GetString(0));
                }
            }

            foreach (var family in families.OrderBy(SetupFamilies.OrderOf).ThenBy(family => family, StringComparer.Ordinal))
            {
                var setups = new List<LinkedSetup>();

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = LinkedSetups;
                    command.Parameters.AddWithValue("$index", index);
                    command.Parameters.AddWithValue("$family", family);

                    await using var reader = await command.ExecuteReaderAsync(cancellation);

                    while (await reader.ReadAsync(cancellation))
                    {
                        var readings = new double?[LedgerReadings.Count];

                        for (var at = 0; at < readings.Length; at++)
                        {
                            readings[at] = reader.IsDBNull(2 + at) ? null : reader.GetDouble(2 + at);
                        }

                        setups.Add(new LinkedSetup(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetDouble(1), readings));
                    }
                }

                links.AddRange(ReadingLinks.Of(setups).Select(link => (family, link)));
            }
        }

        var runId = FormattableString.Invariant($"{LinksVerb}-{index}-{started:yyyyMMddTHHmmssZ}");

        foreach (var (family, link) in links.Where(one => one.Link.Words is ReadingLinks.Winners or ReadingLinks.Losers))
        {
            output.WriteLine(FormattableString.Invariant($"{family}, {link.Reading}: {link.Words}, {link.Correlation:0.000} over {link.Years} test year(s), its size above {link.Shuffled:0.000}"));
        }

        if (!print)
        {
            await WriteLinksAsync(runId, runMonth, index, started, links, cancellation);
        }

        output.WriteLine(print
            ? FormattableString.Invariant($"{LinksVerb}: {LinksDetail(links, index, runMonth)}, printed and not written")
            : FormattableString.Invariant($"{LinksVerb}: run {runId}, {LinksDetail(links, index, runMonth)}"));

        return 0;
    }

    static string LinksDetail(IReadOnlyList<(string Family, ReadingLink Link)> links, string index, string month) =>
        FormattableString.Invariant(
            $"{links.Select(one => one.Family).Distinct(StringComparer.Ordinal).Count()} family(ies) on the {DecisionCards.NameOf(index)} for {month}: {links.Count(one => one.Link.Words == ReadingLinks.Winners)} reading(s) going with winners, {links.Count(one => one.Link.Words == ReadingLinks.Losers)} with losers, {links.Count(one => one.Link.Words == ReadingLinks.NoLink)} with no link beyond the shuffle and {links.Count(one => one.Link.Words == ReadingLinks.TooFew)} too few to read");

    // The table's rows and the run log row, in one transaction under the drain's lock.
    async Task WriteLinksAsync(string runId, string month, string index, DateTimeOffset started, IReadOnlyList<(string Family, ReadingLink Link)> links, CancellationToken cancellation)
    {
        using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var transaction = connection.BeginTransaction();

        static object Or(double? value) => value is { } held ? held : DBNull.Value;

        foreach (var (family, link) in links)
        {
            await ExecuteAsync(connection, transaction, InsertLink,
            [
                ("$run_id", runId),
                ("$month", month),
                ("$index_code", index),
                ("$family", family),
                ("$reading", link.Reading),
                ("$setups", link.Setups),
                ("$years", link.Years),
                ("$correlation", Or(link.Correlation)),
                ("$same_way", Or(link.SameWay)),
                ("$shuffled", Or(link.Shuffled)),
                ("$words", link.Words),
            ], cancellation);
        }

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", LinksVerb),
            ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$rows_written", links.Count),
            ("$detail", LinksDetail(links, index, month)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);
    }
}
