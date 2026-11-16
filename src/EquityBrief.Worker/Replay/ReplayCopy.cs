using System.Globalization;
using System.Reflection;
using EquityBrief.Core.Providers;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Replay;

// A copy of the store the replay runs a night over: a store holding the replay's one-row ceiling table, the session every
// table a deciding step reads is capped at. The operator's store holds no such table, so a step the replay runs never
// runs over it.
// see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
public static class ReplayCopy
{
    public const string CeilingTable = "replay_ceiling";

    // The session the copy is capped at, or none where the store is not a replay's copy.
    public static DateOnly? CeilingOf(string databaseFile)
    {
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        using var connection = new SqliteConnection(StoreConnection.For(databaseFile) + ";Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", CeilingTable);

        if (command.ExecuteScalar() is null)
        {
            return null;
        }

        command.CommandText = "SELECT session_date FROM " + CeilingTable + ";";
        command.Parameters.Clear();

        return command.ExecuteScalar() is string session
            ? DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
}

// The feeds a replayed night is handed: each counts no request and refuses every ask, so a deciding step that asked a
// feed would stop the replay rather than reach a network.
public class ReplayFeed : DispatchProxy
{
    public const string Refusal = "a replayed night asks nothing of a feed";

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_Requests" or "get_Symbols" => 0,
            "get_Indices" => EquityBrief.Worker.Indices.IndexFamilies.Indices,
            _ => throw new InvalidOperationException($"{Refusal}: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}"),
        };

    static T One<T>() where T : class => Create<T, ReplayFeed>();

    public static NightFeeds None() =>
        new(One<IIndexMembershipFeed>(), One<IHistoricalBarFeed>(), One<IBulkPriceFeed>(), One<ICorporateActionFeed>(), One<IEarningsCalendarFeed>(), One<INewsFeed>(), One<IFundamentalsFeed>(), One<IMarketSeriesFeed>())
        {
            Funds = One<IFundHoldingsFeed>(),
            Dividends = One<IDividendCalendarFeed>(),
        };
}
