using System.Globalization;
using System.Net;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 23.4 correction's marking page read back over a constructed store: each unexplained jump and each end no filing settles
// with the sessions around it, its volume against its median and the filings beside it, the mark proposed by its rule, and the
// confirm writing only what was chosen, one row or every row shown, a second word keeping the first, refused without the
// page's header or for a kind the marks do not hold.
// see: The marking page proposes a mark for each unexplained jump and unsettled end from its evidence, and writes none without the operator's confirm
public partial class ReadSurface
{
    internal static string[] MarkingPageRows => [CheckReach.Key("15.25 History", "Marks owed")];

    [Fact]
    public async Task TheMarkingPageProposesEachMarkAndWritesOnlyWhatIsConfirmed()
    {
        using var store = new TemporaryStore().Migrated();

        // Sixty weekday sessions from 2008-01-02, 1,000 shares each; JMP jumps on its 56th on 500 shares and no filing,
        // KEP on its 56th on 1,000 shares with an 8-K two days after, and GONE ends on 2008-03-31.
        var days = Enumerable.Range(0, 120).Select(day => new DateOnly(2008, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).Take(60).ToArray();
        var jumpDay = days[55];
        var rows = new List<string>();

        foreach (var ticker in new[] { "JMP", "KEP", "GONE" })
        {
            foreach (var (day, at) in days.Select((day, at) => (day, at)))
            {
                var close = ticker != "GONE" && at >= 55 ? "15.00" : "10.00";
                var volume = ticker == "JMP" && at == 55 ? 500 : 1000;

                rows.Add(FormattableString.Invariant($"('{ticker}', '{day:yyyy-MM-dd}', '{close}', '{close}', '{close}', '{close}', NULL, {volume}, 'long-prices-1')"));
            }
        }

        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES " + string.Join(", ", rows) + ";");
        store.Execute(FormattableString.Invariant($@"
            INSERT INTO data_check (ticker, session_date, class, kind, era, says, run_id) VALUES
                ('JMP', '{jumpDay:yyyy-MM-dd}', 'jump', 'unexplained', '2005 to 2009', 'a jump of 50% nothing stored explains', 'data-check-long-1'),
                ('KEP', '{jumpDay:yyyy-MM-dd}', 'jump', 'unexplained', '2005 to 2009', 'a jump of 50% nothing stored explains', 'data-check-long-1');
            INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id) VALUES
                ('KEP', '1', '0000000001-08-000001', '8-K', '2.02', NULL, '{jumpDay.AddDays(2):yyyy-MM-dd}', 'long-prices-1');
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES
                ('long-prices-1', 'long-prices', '2026-11-07T10:00:00Z', '2026-11-07T10:20:00Z', 'ok', 5, 0, 0, '0', '{{""codes"":3,""priced"":3,""bars"":180,""ends"":1,""marked"":0,""cutover"":""2018-10-01"",""apartAtMost"":0,""lowest"":[],""nothing"":[],""unsettled"":[""GONE {days[^1]:yyyy-MM-dd}""]}}');"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking"));

        Assert.Contains("data-card=\"marking\"", page, StringComparison.Ordinal);
        Assert.Contains("2 unexplained jumps and 1 unsettled ends, 3 not yet marked", page, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<tr data-mark=\"JMP|{jumpDay:yyyy-MM-dd}\">"), page, StringComparison.Ordinal);
        Assert.Contains("<b>15.00 (500)</b>", page, StringComparison.Ordinal);
        Assert.Contains("median 1K", page, StringComparison.Ordinal);
        Assert.Contains("a bad print: the move came on 500 shares, under its median of 1,000, with no filing beside it", page, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"8-K 2.02 {jumpDay.AddDays(2):yyyy-MM-dd}"), page, StringComparison.Ordinal);
        Assert.Contains("checked and kept: its filer's 8-K carrying items 2.02", page, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"its history ends on {days[^1]:yyyy-MM-dd}, its last session with volume"), page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"bad print\" selected>bad print (proposed)</option>", page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"\">leave unmarked</option>", page, StringComparison.Ordinal);

        async Task<(HttpStatusCode Status, string Said)> ConfirmAsync(bool header, params (string Name, string Value)[] fields)
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

        var jmp = FormattableString.Invariant($"JMP|{jumpDay:yyyy-MM-dd}");
        var kep = FormattableString.Invariant($"KEP|{jumpDay:yyyy-MM-dd}");
        var gone = FormattableString.Invariant($"GONE|{days[^1]:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmAsync(false, ("m|" + jmp, "bad print"), ("only", jmp))).Status);
        Assert.Empty(Marks(store));

        // One row pressed: only it is written, though the others' choices travel with the form.
        Assert.Contains("1 mark(s) written", (await ConfirmAsync(true, ("m|" + jmp, "bad print"), ("m|" + kep, "checked and kept"), ("m|" + gone, "history ends"), ("only", jmp))).Said, StringComparison.Ordinal);
        Assert.Equal([("JMP", "bad print")], Marks(store).Select(mark => (mark.Ticker, mark.Kind)));

        // Every row as chosen: KEP changed to a bad print by hand, GONE left unmarked; JMP's first word stands.
        var all = await ConfirmAsync(true, ("m|" + jmp, "checked and kept"), ("m|" + kep, "bad print"), ("m|" + gone, string.Empty), ("all", "1"));

        Assert.Contains("1 mark(s) written, 1 already marked and left as they were", all.Said, StringComparison.Ordinal);
        Assert.Equal([("JMP", "bad print"), ("KEP", "bad print")], Marks(store).Select(mark => (mark.Ticker, mark.Kind)));
        Assert.Equal("bad print, on the operator's word on the marking page, where checked and kept was proposed", Marks(store).Single(mark => mark.Ticker == "KEP").Says);

        // A kind the marks do not hold, or a session the page does not list, is refused.
        Assert.Contains("1 refused", (await ConfirmAsync(true, ("m|" + gone, "deleted"))).Said, StringComparison.Ordinal);
        Assert.Contains("1 refused", (await ConfirmAsync(true, ("m|ZZZ|2008-01-02", "bad print"))).Said, StringComparison.Ordinal);
        Assert.Equal(2, Marks(store).Count);

        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking"));

        Assert.Contains("1 not yet marked", after, StringComparison.Ordinal);
        Assert.Contains("marked: bad print", after, StringComparison.Ordinal);
    }

    // From the second 23.10 correction of 2026-11-13: a session that is both an unexplained jump and an end no filing settles, as
    // OCAI's of 2007-01-12 and DWDP's of 2019-06-28 were, draws one select under its one name, on its jump's row, offering the
    // jump's proposal and the end's, its end's row naming that choice; it is counted once among the marks owed, and its one
    // confirm writes its one mark. Before it each row drew a select of the one name, and the form sent both.
    [Fact]
    public async Task ASessionThatIsBothAJumpAndAnEndIsChosenOnceUnderOneName()
    {
        using var store = new TemporaryStore().Migrated();

        // Sixty weekday sessions from 2008-01-02, 1,000 shares each: BTH closes at 10.00 and jumps to 15.00 on its last, with an
        // 8-K carrying item 2.02 two days after, which settles no end.
        var days = Enumerable.Range(0, 120).Select(day => new DateOnly(2008, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).Take(60).ToArray();
        var last = days[^1];
        var key = FormattableString.Invariant($"BTH|{last:yyyy-MM-dd}");

        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES " + string.Join(", ", days.Select((day, at) => FormattableString.Invariant($"('BTH', '{day:yyyy-MM-dd}', '{(at == 59 ? "15.00" : "10.00")}', '{(at == 59 ? "15.00" : "10.00")}', '{(at == 59 ? "15.00" : "10.00")}', '{(at == 59 ? "15.00" : "10.00")}', NULL, 1000, 'long-prices-1')"))) + ";");
        store.Execute(FormattableString.Invariant($@"
            INSERT INTO data_check (ticker, session_date, class, kind, era, says, run_id) VALUES
                ('BTH', '{last:yyyy-MM-dd}', 'jump', 'unexplained', '2005 to 2009', 'a jump of 50% nothing stored explains', 'data-check-long-1');
            INSERT INTO long_filing (ticker, cik, accession, form, items, reported, filed, run_id) VALUES
                ('BTH', '1', '0000000001-08-000002', '8-K', '2.02', NULL, '{last.AddDays(2):yyyy-MM-dd}', 'long-prices-1');
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES
                ('long-prices-1', 'long-prices', '2026-11-07T10:00:00Z', '2026-11-07T10:20:00Z', 'ok', 5, 0, 0, '0', '{{""codes"":1,""priced"":1,""bars"":60,""ends"":1,""marked"":0,""cutover"":""2018-10-01"",""apartAtMost"":0,""lowest"":[],""nothing"":[],""unsettled"":[""BTH {last:yyyy-MM-dd}""]}}');"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking"));

        Assert.Contains("1 unexplained jumps and 1 unsettled ends, 1 not yet marked", page, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(page, $"<tr data-mark=\"{System.Text.RegularExpressions.Regex.Escape(key)}\">").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, $"<select name=\"m\\|{System.Text.RegularExpressions.Regex.Escape(key)}\""));
        Assert.Contains("<option value=\"checked and kept\" selected>checked and kept (proposed for its jump)</option>", page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"history ends\">history ends (proposed for its end)</option>", page, StringComparison.Ordinal);
        Assert.Contains("One mark covers this session and its jump in the table above: choose it there.", page, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, $"<button type=\"submit\" name=\"only\" value=\"{System.Text.RegularExpressions.Regex.Escape(key)}\">"));

        using var press = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.MarkingPostRoute)
        {
            Content = new FormUrlEncodedContent([new("m|" + key, "history ends"), new("only", key)]),
        };

        press.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);

        Assert.Contains("1 mark(s) written", WebUtility.HtmlDecode(await (await client.SendAsync(press)).Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal([("BTH", "history ends")], Marks(store).Select(mark => (mark.Ticker, mark.Kind)));

        var after = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/marking"));

        Assert.Contains("0 not yet marked", after, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(after, "marked: history ends").Count);
    }

    static IReadOnlyList<(string Ticker, string Kind, string Says)> Marks(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ticker, kind, says FROM session_mark ORDER BY ticker;";

        var marks = new List<(string, string, string)>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            marks.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return marks;
    }
}
