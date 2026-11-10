using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.4's History page: the prices back to 2004 read back over a constructed store off the long price pull's row on the run log,
// the run's counts, the day the S&P 500 reads the provider's dates from, the lowest coverage, the codes priced by nothing and the
// ends no filing settles, and a store the pull has not written saying so.
// see: A departed company's end no filing settles reads its last session with volume and goes to the operator on one list
public partial class ReadSurface
{
    internal static string[] LongPricesPageRows => [CheckReach.Key("15.25 History", "Prices back to 2004")];

    [Fact]
    public async Task TheHistoryPageDrawsThePricesBackTo2004AndTheEndsNoFilingSettles()
    {
        using var store = new TemporaryStore().Migrated();

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            Assert.Contains("The prices back to 2004 have not been written.", empty, StringComparison.Ordinal);
        }

        // An older run and the newest, which alone is drawn. The newest wrote its prices again over ends already marked, so its own
        // count is nought, and the page reads the one end mark standing, not the bad print beside it.
        store.Execute(@"
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES
                ('long-prices-1', 'long-prices', '2026-11-06T10:00:00Z', '2026-11-06T10:10:00Z', 'ok', 5, 0, 0, '0', '{""codes"":9,""priced"":8,""bars"":1000,""ends"":0,""marked"":0,""cutover"":""2012-01-01"",""apartAtMost"":0,""lowest"":[],""nothing"":[],""unsettled"":[]}'),
                ('long-prices-2', 'long-prices', '2026-11-07T10:00:00Z', '2026-11-07T10:20:00Z', 'ok', 5, 0, 0, '0', '{""codes"":4,""priced"":3,""bars"":12345,""ends"":2,""marked"":0,""cutover"":""2018-10-01"",""apartAtMost"":7,""lowest"":[""MID 2004-12-31 92.0% of holdings, 93.0% of value""],""nothing"":[""ZZZ (Zed Co.)""],""unsettled"":[""GONE 2009-01-14"",""LOST 2007-05-01""]}');
            INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES
                ('MER', '2008-12-31', 'history ends', 'its history ends here', '2026-11-06T10:10:00Z', 'long-prices-1'),
                ('BAD', '2010-05-06', 'bad print', 'a bad print', '2026-11-04T00:00:00Z', 'mark-1');");

        using var written = new Host(store.Root);
        using var reader = written.CreateClient();
        var page = WebUtility.HtmlDecode(await reader.GetStringAsync("/screens/history"));

        Assert.Contains("data-card=\"long-prices\"", page, StringComparison.Ordinal);
        Assert.Contains("long-prices-2", page, StringComparison.Ordinal);
        Assert.DoesNotContain("long-prices-1", page, StringComparison.Ordinal);

        var read = Regex.Match(page, "<p class=\"read\" data-read=\"long-prices\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Equal("3 of 4 codes priced, 12,345 sessions. 2 ends read from filings, 1 marked where the provider's prices run on past them. The S&P 500 reads the provider's dates from 2018-10-01, its count at most 7 from IVV's.", read);
        Assert.Contains("Lowest coverage over the spans: MID 2004-12-31 92.0% of holdings, 93.0% of value.", page, StringComparison.Ordinal);
        Assert.Matches("<details data-list=\"nothing\"><summary>Priced by nothing, left out: 1</summary><p class=\"named\">ZZZ \\(Zed Co.\\)</p></details>", page);
        Assert.Matches("<details data-list=\"unsettled\"><summary>Ends no filing settles, to the operator: 2</summary><p class=\"named\">GONE 2009-01-14; LOST 2007-05-01</p></details>", page);
    }
}
