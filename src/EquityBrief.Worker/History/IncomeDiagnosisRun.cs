using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// From 24.12, read-only: for a stored night, each reading built on operating income on each index, the members it did not
// read or read as failing for want of the line, each with its cause. The interest cover and the ledger's operating margin
// change are read as the night stored them; the cards' business word, the entry check's cover and the report's operating
// margins read each quarter's line from the filing and then the release, so they are counted by the cause itself. It opens
// the store for reading alone and writes nothing.
// see: Where operating income is not filed a reading takes income before taxes plus interest expense, else revenue less costs and expenses
public static class IncomeDiagnosisRun
{
    public const string Verb = "income-readings";

    public const string Cover = "the interest cover the rules' profit gate reads, not passed";

    public const string MarginChange = "the ledger's operating margin change, not read";

    public const string FromTheFiling = "the cards' business word, the entry check's cover and the report's operating margins, not read";

    public sealed record Member(string Index, string Ticker, string? Sector, bool? Coverage, bool MarginRead, IncomeCause Cause);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<(DateOnly Session, IReadOnlyList<Member> Members)> ReadAsync(string databaseFile, DateOnly? session, CancellationToken cancellation = default)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;
        builder.Pooling = false;

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellation);

        async Task<List<object?[]>> RowsAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var rows = new List<object?[]>();

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var row = new object?[reader.FieldCount];

                for (var at = 0; at < row.Length; at++)
                {
                    row[at] = reader.IsDBNull(at) ? null : reader.GetValue(at);
                }

                rows.Add(row);
            }

            return rows;
        }

        var night = session ?? ((await RowsAsync("SELECT MAX(session_date) FROM member_reading;")).Single()[0] is string newest ? Day(newest) : default);
        var day = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var companies = (await RowsAsync("SELECT ticker, cik, sector FROM company c WHERE fetched_at = (SELECT MAX(fetched_at) FROM company d WHERE d.ticker = c.ticker);"))
            .ToDictionary(row => (string)row[0]!, row => (Cik: row[1] as string, Sector: row[2] as string), StringComparer.Ordinal);
        var concepts = string.Join(", ", FiledFacts.NetIncome.Concat(FiledFacts.OperatingIncome).Select(concept => $"'{concept}'"));
        var facts = (await RowsAsync($"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE concept IN ({concepts});"))
            .GroupBy(row => (string)row[0]!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FiledFactRow>)[.. group.Select(row => new FiledFactRow((string)row[1]!, Day((string)row[2]!), Day((string)row[3]!), decimal.Parse((string)row[4]!, CultureInfo.InvariantCulture), Day((string)row[5]!), (string)row[6]!, (string)row[7]!))],
                StringComparer.Ordinal);
        var margin = LedgerReadings.IndexOf("operating_margin_change");
        var margins = (await RowsAsync("SELECT index_code, ticker, readings FROM ledger_reading WHERE session_date = $session;", ("$session", day)))
            .ToDictionary(row => ((string)row[0]!, (string)row[1]!), row => System.Text.Json.JsonSerializer.Deserialize<double?[]>((string)row[2]!) is { } list && margin < list.Length && list[margin] is not null);
        var members = new List<Member>();

        foreach (var row in await RowsAsync("SELECT index_code, ticker, coverage FROM member_reading WHERE session_date = $session ORDER BY index_code, ticker;", ("$session", day)))
        {
            var (index, ticker) = ((string)row[0]!, (string)row[1]!);
            var company = companies.GetValueOrDefault(ticker);
            var filed = company.Cik is { } cik ? facts.GetValueOrDefault(cik) ?? [] : null;

            members.Add(new Member(index, ticker, company.Sector, row[2] is long flag ? flag != 0 : null, margins.GetValueOrDefault((index, ticker)), IncomeDiagnosis.CauseOf(filed, night)));
        }

        return (night, members);
    }

    // The members each reading did not read or read as failing on an index, with its cause, the line's own causes first.
    public static IReadOnlyList<(string Reading, Member Member)> Failing(IReadOnlyList<Member> members) =>
    [
        .. members.Where(member => member.Coverage == false).Select(member => (Cover, member)),
        .. members.Where(member => !member.MarginRead).Select(member => (MarginChange, member)),
        .. members.Where(member => member.Cause is IncomeCause.NoOperatingLine or IncomeCause.SomeQuarters).Select(member => (FromTheFiling, member)),
    ];

    public static IReadOnlyList<string> Counts(IReadOnlyList<Member> members) =>
    [
        .. Failing(members).GroupBy(one => (one.Reading, one.Member.Index)).Select(group =>
            FormattableString.Invariant($"{group.Key.Index}, {group.Key.Reading}: {group.Count()} of {members.Count(member => member.Index == group.Key.Index)}; ")
            + string.Join("; ", group.GroupBy(one => one.Member.Cause).OrderBy(cause => cause.Key).Select(cause => FormattableString.Invariant($"{IncomeDiagnosis.Words(cause.Key)}: {cause.Count()}")))),
        "the scorecard's checks read no operating income: net income, cash, revenue, gross margin, assets, the current ratio, debt and shares",
    ];

    public static string Line((string Reading, Member Member) one) =>
        $"{one.Member.Index} {one.Member.Ticker} ({one.Member.Sector ?? "no sector"}), {one.Reading}: {IncomeDiagnosis.Words(one.Member.Cause)}";
}
