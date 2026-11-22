using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The History page's coverage of every member's readings on every session, read back over a constructed store off
// `session_reading_year`: each index's rows and members a session by year, the sessions from the nights, each index's total
// and span, an index holding none, and a store the build has not written saying so.
// see: The readings for every stock on every session are built once before the reviewer, the picker's comparison reading the same plan on every stock until then
public partial class ReadSurface
{
    internal static string[] SessionCoverageRows => [CheckReach.Key("15.25 History", "Every member on every session")];

    [Fact]
    public async Task TheHistoryPageDrawsEveryMemberOnEverySessionByYearAndIndex()
    {
        using var store = new TemporaryStore().Migrated();

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            Assert.Contains("data-card=\"session-coverage\"", empty, StringComparison.Ordinal);
            Assert.Contains("Not built yet. <code>ledger-build --every-session --index <GSPC, MID or SML> --word</code> builds it on each index.", empty, StringComparison.Ordinal);
        }

        // The S&P 500's first year 126,000 rows over 252 sessions, 500 a session; its newest 37,500 over 75, three of them a
        // night's; the S&P 400's first year 90,720 over 252, 360 a session; and no S&P 600 row.
        store.Execute(@"
            INSERT INTO session_reading_year (index_code, year, rows, sessions, night_sessions, first_session, last_session, counted_at) VALUES
                ('GSPC', 2005, 126000, 252, 0, '2005-01-03', '2005-12-30', '2026-11-22T03:00:00Z'),
                ('GSPC', 2026, 37500, 75, 3, '2026-08-03', '2026-11-20', '2026-11-22T03:00:00Z'),
                ('MID', 2005, 90720, 252, 0, '2005-01-03', '2005-12-30', '2026-11-22T03:00:00Z');");

        using var written = new Host(store.Root);
        using var reader = written.CreateClient();
        var page = WebUtility.HtmlDecode(await reader.GetStringAsync("/screens/history"));
        var card = Regex.Match(page, "data-card=\"session-coverage\"(.*?)</table>", RegexOptions.Singleline).Groups[1].Value;
        string[] Cells(string row) => [.. Regex.Matches(Regex.Match(card, row + "(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value, "<td class=\"n\"[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(match => Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty))];

        Assert.Contains("Every member on every session", card, StringComparison.Ordinal);
        Assert.Equal(["126,000", "500", "90,720", "360", "none", "none"], Cells("<tr data-year=\"2005\">"));
        Assert.Equal(["37,500 3 session(s) from the nights", "500", "none", "none", "none", "none"], Cells("<tr data-year=\"2026\">"));
        Assert.Equal(["163,500", "2005-01-03 to 2026-11-20", "90,720", "2005-01-03 to 2005-12-30", "none", ""], Cells("<tr class=\"total\">"));
    }
}
