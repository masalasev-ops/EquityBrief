using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Members;

// What a names refresh came to: whether it asked, the members it read a name for and those the list named none of, the
// requests, the refusal that stopped it, none where none did, and the day it last read the list.
public sealed record NamesOutcome(bool Asked, int Named, IReadOnlyList<string> Unnamed, int Requests, string? Refused, DateOnly? ReadOn);

// The names refresh. A process of its own the off-peak run starts after the short interest reader, never the night: once a
// week it asks the provider's list of the US exchanges' symbols, one request at a weight of one inside the subscription, and
// keeps each member of the three indices' company name as the list spells it, the newest read standing; a member the list
// names none of keeps what it held and is counted. Inside the week since its last read it asks nothing. A refusal stops the
// run with what it held kept, named on its row. Writes one row of its own and calls no model.
// see: A member's company is named as the provider's symbol list spells it, read once a week outside the night
public sealed class NamesRefresh(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.ListedName, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.SymbolList]);

    public const string Stage = "names";

    public const string RunPrefix = Stage + "-";

    // The days after a read before the list is asked again.
    public const int WeekDays = 7;

    const string NewestRead = "SELECT MAX(read_on) FROM listed_name;";

    // Every stock the three indices hold today, each once, a member announced to join among them.
    const string HeldToday = @"
        SELECT DISTINCT ticker FROM membership
        WHERE ""left"" IS NULL OR ""left"" > $on
        ORDER BY ticker;
    ";

    const string Keep = @"
        INSERT INTO listed_name (ticker, name, read_on, run_id) VALUES ($ticker, $name, $read_on, $run_id)
        ON CONFLICT (ticker) DO UPDATE SET name = excluded.name, read_on = excluded.read_on, run_id = excluded.run_id;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<NamesOutcome> RunAsync(ISymbolListFeed feed, TextWriter? output = null, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = RunPrefix + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var today = clock.SessionDateAt(started);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var newest = await NewestAsync(connection, cancellation);
        var members = await HeldAsync(connection, today, cancellation);
        var before = feed.Requests;
        NamesOutcome outcome;

        if (newest is { } read && today < read.AddDays(WeekDays))
        {
            outcome = new NamesOutcome(false, 0, [], 0, null, read);
        }
        else
        {
            var (named, unnamed) = (0, new List<string>());
            string? refused = null;

            try
            {
                var names = Names(await feed.SymbolsAsync(false, cancellation));

                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

                foreach (var ticker in members)
                {
                    if (!names.TryGetValue(ticker, out var name))
                    {
                        unnamed.Add(ticker);

                        continue;
                    }

                    await using var command = connection.CreateCommand();

                    command.Transaction = transaction;
                    command.CommandText = Keep;
                    command.Parameters.AddWithValue("$ticker", ticker);
                    command.Parameters.AddWithValue("$name", name);
                    command.Parameters.AddWithValue("$read_on", Day(today));
                    command.Parameters.AddWithValue("$run_id", runId);
                    await command.ExecuteNonQueryAsync(cancellation);
                    named++;
                }

                await transaction.CommitAsync(cancellation);
            }
            catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
            {
                refused = failure is OperationCanceledException ? "the provider did not answer in time on any try" : failure.Message;
            }

            outcome = new NamesOutcome(true, named, unnamed, feed.Requests - before, refused, refused is null ? today : newest);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", outcome.Refused is null ? "ok" : "partial");
            command.Parameters.AddWithValue("$rows_written", outcome.Named);
            command.Parameters.AddWithValue("$network_requests", outcome.Requests);
            command.Parameters.AddWithValue("$detail", Detail(outcome, members.Count));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        output?.WriteLine("names: " + Detail(outcome, members.Count));

        return outcome;
    }

    // Each code's name on the list, its common stock's where the list files the code more than once, a code with no name
    // left out.
    public static IReadOnlyDictionary<string, string> Names(IReadOnlyList<ListedSymbol> symbols) =>
        symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Name))
            .GroupBy(symbol => symbol.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (group.FirstOrDefault(symbol => symbol.Type == ProviderSymbols.CommonStock) ?? group.First()).Name.Trim(),
                StringComparer.Ordinal);

    // The run's row in the operator's words: what it read, or why it asked nothing.
    public static string Detail(NamesOutcome outcome, int members) =>
        !outcome.Asked
            ? FormattableString.Invariant($"the symbol list was read on {outcome.ReadOn:yyyy-MM-dd}, so nothing is asked until {outcome.ReadOn!.Value.AddDays(WeekDays):yyyy-MM-dd}")
            : outcome.Refused is { } refused
                ? FormattableString.Invariant($"the symbol list was refused, every member keeping the name it held: {refused}")
                : FormattableString.Invariant($"{outcome.Named} of {members} member(s) named as the symbol list spells them, {outcome.Unnamed.Count} the list names none of, in {outcome.Requests} request(s)")
                    + (outcome.Unnamed.Count > 0 ? ": " + string.Join(", ", outcome.Unnamed) : string.Empty);

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestRead;

        return await command.ExecuteScalarAsync(cancellation) is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    static async Task<IReadOnlyList<string>> HeldAsync(SqliteConnection connection, DateOnly on, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = HeldToday;
        command.Parameters.AddWithValue("$on", Day(on));

        var held = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
