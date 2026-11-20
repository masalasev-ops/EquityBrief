using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// From 24.12, read-only: for a stored night, each reading built on operating income on each index, the members it reads now
// and those it does not for want of the line, each with its cause; and, given the folder of the SEC's answers the long facts
// pull kept, what each fallback line would add, income before taxes plus interest expense first and then revenue less costs
// and expenses, and who stays unread and why. The interest cover and the ledger's operating margin change are read as the
// night stored them; the cards' business word, the entry check's cover line and the report's margins read each quarter's
// line from the filing, so they are counted by the cause itself. It opens the store for reading alone and writes nothing.
// see: Where operating income is not filed a reading takes income before taxes plus interest expense, else revenue less costs and expenses
public static class IncomeDiagnosisRun
{
    public const string Verb = "income-readings";

    public const string Cover = "the interest cover the rules' profit gate reads";

    public const string MarginChange = "the ledger's operating margin change";

    public const string FromTheFiling = "the cards' business word, the entry check's cover line and the report's margins";

    public sealed record Member(string Index, string Ticker, string? Sector, bool? Coverage, bool MarginRead, IncomeCause Cause, IncomeLine Line, bool? CoversWithLine, bool MarginWithLine);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static async Task<(DateOnly Session, IReadOnlyList<Member> Members)> ReadAsync(string databaseFile, DateOnly? session, string? kept = null, CancellationToken cancellation = default)
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
        var concepts = string.Join(", ", FiledFacts.NetIncome.Concat(FiledFacts.OperatingIncome).Concat(FiledFacts.Revenue).Concat(FiledFacts.InterestExpense).Select(concept => $"'{concept}'"));
        var facts = (await RowsAsync($"SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE concept IN ({concepts});"))
            .GroupBy(row => (string)row[0]!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<FiledFactRow>)[.. group.Select(row => new FiledFactRow((string)row[1]!, Day((string)row[2]!), Day((string)row[3]!), decimal.Parse((string)row[4]!, CultureInfo.InvariantCulture), Day((string)row[5]!), (string)row[6]!, (string)row[7]!))],
                StringComparer.Ordinal);
        var margin = LedgerReadings.IndexOf("operating_margin_change");
        var margins = (await RowsAsync("SELECT index_code, ticker, readings FROM ledger_reading WHERE session_date = $session;", ("$session", day)))
            .ToDictionary(row => ((string)row[0]!, (string)row[1]!), row => System.Text.Json.JsonSerializer.Deserialize<double?[]>((string)row[2]!) is { } list && margin < list.Length && list[margin] is not null);
        var fallback = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);

        IReadOnlyList<FiledFactRow> Fallback(string cik)
        {
            if (kept is null)
            {
                return [];
            }

            if (!fallback.TryGetValue(cik, out var rows))
            {
                var file = LongFactPull.FileOf(kept, cik);

                fallback[cik] = rows = File.Exists(file) ? AsFirstFiled(ConceptAnswers.FromFacts(LongFactPull.Read(file), cik, IncomeDiagnosis.FallbackConcepts)) : [];
            }

            return rows;
        }

        var members = new List<Member>();

        foreach (var row in await RowsAsync("SELECT index_code, ticker, coverage FROM member_reading WHERE session_date = $session ORDER BY index_code, ticker;", ("$session", day)))
        {
            var (index, ticker) = ((string)row[0]!, (string)row[1]!);
            var company = companies.GetValueOrDefault(ticker);
            var filed = company.Cik is { } cik ? facts.GetValueOrDefault(cik) ?? [] : null;
            var cause = IncomeDiagnosis.CauseOf(filed, night);
            var (line, withLine) = filed is null ? (IncomeLine.None, []) : IncomeDiagnosis.WithLine([.. filed, .. Fallback(company.Cik!)], night);

            members.Add(new Member(
                index,
                ticker,
                company.Sector,
                row[2] is long flag ? flag != 0 : null,
                margins.GetValueOrDefault((index, ticker)),
                cause,
                line,
                line is IncomeLine.None ? null : IncomeDiagnosis.Covers(withLine, night),
                line is not IncomeLine.None && FiledFacts.Read(withLine, night).OperatingMarginChange is not null));
        }

        return (night, members);
    }

    // A kept answer's figures for the fallback concepts, one a concept a period as first filed over the spans the standing
    // facts keep, as the long facts are kept.
    public static IReadOnlyList<FiledFactRow> AsFirstFiled(IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> facts) =>
    [
        .. facts.SelectMany(concept => concept.Value
            .Where(fact => FiledFacts.Kept(fact.Start, fact.End))
            .GroupBy(fact => (fact.Start, fact.End))
            .Select(period => period.OrderBy(fact => fact.Filed).ThenBy(fact => fact.Accession, StringComparer.Ordinal).First())
            .Select(fact => new FiledFactRow(concept.Key, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))),
    ];

    static bool WantsTheLine(Member member) => member.Cause is IncomeCause.NoOperatingLine or IncomeCause.SomeQuarters;

    // The members a reading does not read for want of the line, or reads as failing for it, by reading.
    public static IReadOnlyList<(string Reading, Member Member)> Failing(IReadOnlyList<Member> members) =>
    [
        .. members.Where(member => member.Coverage == false && WantsTheLine(member)).Select(member => (Cover, member)),
        .. members.Where(member => !member.MarginRead && WantsTheLine(member)).Select(member => (MarginChange, member)),
        .. members.Where(WantsTheLine).Select(member => (FromTheFiling, member)),
    ];

    // Each index's line a reading: the members it reads now, those each fallback adds, and those it still does not read
    // and why, its other causes counted apart.
    public static IReadOnlyList<string> Counts(IReadOnlyList<Member> members)
    {
        var lines = new List<string>();

        foreach (var index in members.Select(member => member.Index).Distinct(StringComparer.Ordinal))
        {
            var on = members.Where(member => member.Index == index).ToList();

            string Added(IEnumerable<Member> wanting, Func<Member, bool> reads)
            {
                var all = wanting.ToList();
                var pretax = all.Count(member => member.Line == IncomeLine.PretaxPlusInterest && reads(member));
                var costs = all.Count(member => member.Line == IncomeLine.RevenueLessCosts && reads(member));

                return FormattableString.Invariant($"{all.Count} for want of the line; income before taxes plus interest adds {pretax}, revenue less costs and expenses {costs}; still unread {all.Count - pretax - costs}");
            }

            var cover = on.Where(member => member.Coverage == false && WantsTheLine(member)).ToList();

            lines.Add(FormattableString.Invariant($"{index}, {Cover}: passed now {on.Count(member => member.Coverage == true)} of {on.Count}; not passed {on.Count(member => member.Coverage == false)}, ")
                + Added(cover, member => member.CoversWithLine is not null)
                + FormattableString.Invariant($"; of those read with a line, passing {cover.Count(member => member.CoversWithLine == true)}"));
            lines.Add(FormattableString.Invariant($"{index}, {MarginChange}: read now {on.Count(member => member.MarginRead)} of {on.Count}; not read {on.Count(member => !member.MarginRead)}, ")
                + Added(on.Where(member => !member.MarginRead && WantsTheLine(member)), member => member.MarginWithLine));
            lines.Add(FormattableString.Invariant($"{index}, {FromTheFiling}: read now {on.Count(member => member.Cause == IncomeCause.Read)} of {on.Count}; ")
                + Added(on.Where(WantsTheLine), member => member.Line is not IncomeLine.None)
                + "; other causes: "
                + string.Join("; ", on.Where(member => member.Cause is IncomeCause.NoFiler or IncomeCause.FewerQuarters).GroupBy(member => member.Cause).OrderBy(group => group.Key).Select(group => FormattableString.Invariant($"{IncomeDiagnosis.Words(group.Key)}: {group.Count()}"))));
        }

        lines.Add("the scorecard's checks read no operating income: net income, cash, revenue, gross margin, assets, the current ratio, debt and shares");

        return lines;
    }

    public static string Line((string Reading, Member Member) one) =>
        $"{one.Member.Index} {one.Member.Ticker} ({one.Member.Sector ?? "no sector"}), {one.Reading}: {IncomeDiagnosis.Words(one.Member.Cause)}; {IncomeDiagnosis.Words(one.Member.Line)}";
}
