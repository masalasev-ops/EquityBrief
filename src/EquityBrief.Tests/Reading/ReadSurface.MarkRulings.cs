using System.Net;
using EquityBrief.Core.History;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 24.19 correction's rulings read back over a constructed store: a code whose history-ends mark falls inside a span it trades
// on past, its filer's reports stopping while its price carries on, is listed among the marks the evidence leaves to the
// operator, the page and the long reader listing the same mark; a code whose filer files on both sides and whose price carries
// on is read through and not listed; a confirm without the page's header is refused; a ruling writes once, a second keeping
// the first; and the reader reads the ruled mark as ruled, its history read past the mark.
// see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
public partial class ReadSurface
{
    internal static string[] MarkRulingRows => [CheckReach.Key("15.25 History", "History ends the evidence leaves to you")];

    [Fact]
    public async Task AMarkTheEvidenceLeavesToTheOperatorIsListedRuledOnceAndReadAsRuled()
    {
        using var store = new TemporaryStore().Migrated();

        // Ninety weekday sessions from 2010-01-04; each code's mark on its 60th, ten sessions trading after it, the price
        // moving a quarter of a percent a day. STP's filer's 10-Q comes 100 days before and nothing after; ONE's comes before
        // and its 10-K 60 days after.
        var days = Enumerable.Range(0, 140).Select(day => new DateOnly(2010, 1, 4).AddDays(day)).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).Take(90).ToArray();
        var mark = days[59];
        var rows = new List<string>();

        foreach (var ticker in new[] { "STP", "ONE" })
        {
            foreach (var (day, at) in days[..70].Select((day, at) => (day, at)))
            {
                var close = FormattableString.Invariant($"{(at % 2 == 0 ? 10.00m : 10.025m):0.000}");

                rows.Add(FormattableString.Invariant($"('{ticker}', '{day:yyyy-MM-dd}', '{close}', '{close}', '{close}', '{close}', NULL, 1000, 'r')"));
            }
        }

        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES " + string.Join(", ", rows) + ";");
        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => FormattableString.Invariant($"('GSPC', '{day:yyyy-MM-dd}', '1', '1', '1', '1', 'r')"))) + ";");
        store.Execute(FormattableString.Invariant($@"
            INSERT INTO long_span (index_code, ticker, joined, ""left"", read_from, run_id) VALUES
                ('MID', 'STP', '{days[0]:yyyy-MM-dd}', NULL, 'fund', 'r'), ('MID', 'ONE', '{days[0]:yyyy-MM-dd}', NULL, 'fund', 'r');
            INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES
                ('STP', '{mark:yyyy-MM-dd}', 'history ends', 'e', '2026-11-08T00:00:00Z', 'r'),
                ('ONE', '{mark:yyyy-MM-dd}', 'history ends', 'e', '2026-11-08T00:00:00Z', 'r');
            INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id) VALUES
                ('STP', '7', 'a1', '10-Q', NULL, NULL, '{mark.AddDays(-100):yyyy-MM-dd}', 'r'),
                ('ONE', '8', 'a2', '10-Q', NULL, NULL, '{mark.AddDays(-100):yyyy-MM-dd}', 'r'),
                ('ONE', '8', 'a3', '10-K', NULL, NULL, '{mark.AddDays(60):yyyy-MM-dd}', 'r');"));

        var reader = new Worker.History.LongHistoryReader(store.DatabaseFile);
        var settled = await reader.OddMarksAsync(days[^1]);

        Assert.Equal([("ONE", LongMarks.ReadThrough), ("STP", LongMarks.ToOperator)], settled.Select(one => (one.Ticker, one.Outcome)));
        Assert.Equal((LongMarks.FilerStops, LongMarks.Continues), (settled[1].Filer, settled[1].Price));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking"));
        var key = FormattableString.Invariant($"STP|{mark:yyyy-MM-dd}");

        Assert.Contains("History ends the evidence leaves to you: 1", page, StringComparison.Ordinal);
        Assert.Contains($"<tr data-rule=\"{key}\">", page, StringComparison.Ordinal);
        Assert.Contains($"Its filer {LongMarks.FilerStops}; its price {LongMarks.Continues}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rule=\"ONE|", page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"\" selected>not ruled yet</option>", page, StringComparison.Ordinal);

        async Task<(HttpStatusCode Status, string Said)> RuleAsync(bool header, params (string Name, string Value)[] fields)
        {
            using var press = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.MarkingPostRoute)
            {
                Content = new FormUrlEncodedContent(fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))),
            };

            if (header)
            {
                press.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
            }

            var response = await client.SendAsync(press);

            return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await RuleAsync(false, ("r|" + key, LongMarks.ReadThrough), ("only", key))).Status);
        Assert.Empty(Rulings(store));
        Assert.Contains("1 refused", (await RuleAsync(true, ("r|" + key, "deleted"), ("only", key))).Said, StringComparison.Ordinal);
        Assert.Contains("1 mark(s) ruled", (await RuleAsync(true, ("r|" + key, LongMarks.ReadThrough), ("only", key))).Said, StringComparison.Ordinal);
        Assert.Contains("1 ruled already", (await RuleAsync(true, ("r|" + key, LongMarks.Kept), ("only", key))).Said, StringComparison.Ordinal);
        Assert.Equal([("STP", LongMarks.ReadThrough, LongMarks.FilerStops, LongMarks.Continues)], Rulings(store));

        Assert.Contains("ruled: read through", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking")), StringComparison.Ordinal);

        // A mark is ruled once: an update and a delete are each refused.
        Assert.Throws<SqliteException>(() => store.Execute("UPDATE mark_ruling SET ruling = 'kept';"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM mark_ruling;"));

        // Ruled read through, the reader reads STP's bars past its mark.
        var after = await reader.OddMarksAsync(days[^1]);

        Assert.Equal((LongMarks.ReadThrough, true), (after.Single(one => one.Ticker == "STP").Outcome, after.Single(one => one.Ticker == "STP").Ruled));
        Assert.Equal(70, (await reader.ReadAsync("MID", days[^1])).Names.Single(name => name.Ticker == "STP").Bars.Length);
    }

    static IReadOnlyList<(string Ticker, string Ruling, string Filer, string Price)> Rulings(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ticker, ruling, filer, price FROM mark_ruling ORDER BY ticker;";

        using var reader = command.ExecuteReader();
        var rows = new List<(string, string, string, string)>();

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }
}
