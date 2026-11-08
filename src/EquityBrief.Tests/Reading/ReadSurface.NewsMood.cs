using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.9's labels row on the History page: on each index the members holding a stored story, those holding one stored before
// the newest session no label stands on, the day every such story stood labelled from and the sessions after it of the 126
// the industry news mood split waits for, read back over a constructed store.
// see: The sector splits read a sector as it stood off the funds' industry headings through a stated map, and the filed GICS sector after the schedules end
public partial class ReadSurface
{
    internal static string[] NewsMoodRows => [CheckReach.Key("15.25 History", "Labels for the industry news mood")];

    [Fact]
    public async Task TheHistoryPageSaysHowFarEachIndexStandsFromTheNewsMoodSplit()
    {
        using var store = new TemporaryStore().Migrated();

        // Two S&P 500 members holding stories, every one labelled, the backlog's last label written on 2026-11-02 for a story
        // stored on 2026-10-29; an S&P 400 member holding one story no label stands on; and three sessions after 2026-11-02.
        store.Execute(@"
            INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at) VALUES
                ('GSPC', 'AAA', '2020-01-02', NULL, '2026-11-01T00:00:00Z'),
                ('GSPC', 'BBB', '2020-01-02', NULL, '2026-11-01T00:00:00Z'),
                ('MID', 'CCC', '2020-01-02', NULL, '2026-11-01T00:00:00Z');
            INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES
                ('AAA', '2026-11-02', '1', '1', '1', '1', 1, 'test', '2026-11-02T22:00:00Z', '1'),
                ('AAA', '2026-11-03', '1', '1', '1', '1', 1, 'test', '2026-11-03T22:00:00Z', '1'),
                ('AAA', '2026-11-04', '1', '1', '1', '1', 1, 'test', '2026-11-04T22:00:00Z', '1'),
                ('AAA', '2026-11-05', '1', '1', '1', '1', 1, 'test', '2026-11-05T22:00:00Z', '1');
            INSERT INTO news_article (ticker, article_id, link, title, source, published_at, text, length, admissibility, session_date) VALUES
                ('AAA', 'a1', 'l', 't', 's', '2026-10-29T12:00:00Z', 'x', 1, 'admitted', '2026-10-29'),
                ('BBB', 'b1', 'l', 't', 's', '2026-11-03T12:00:00Z', 'x', 1, 'admitted', '2026-11-03'),
                ('CCC', 'c1', 'l', 't', 's', '2026-11-03T12:00:00Z', 'x', 1, 'admitted', '2026-11-03');
            INSERT INTO news_label (ticker, article_id, profile, instruction_version, model, outcome, cause, kind, direction, reason, labelled_at, run_id) VALUES
                ('AAA', 'a1', 'local', 2, 'm', 'labelled', NULL, 'results', 'positive', 'r', '2026-11-02T10:00:00Z', 'r'),
                ('BBB', 'b1', 'local', 2, 'm', 'labelled', NULL, 'results', 'negative', 'r', '2026-11-04T10:00:00Z', 'r');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

        Assert.Contains("data-card=\"news-mood\"", page, StringComparison.Ordinal);

        string Row(string index) => Regex.Replace(Regex.Match(page, $"<tr data-mood=\"{index}\"[^>]*>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value, "<[^>]+>", "|");

        Assert.Equal("|S&P 500||2||0||2026-11-02||3|", Row("GSPC"));
        Assert.Equal("|S&P 400||1||1||not yet||none|", Row("MID"));
        Assert.Contains("<tr data-mood=\"GSPC\" data-ready=\"no\">", page, StringComparison.Ordinal);
    }
}
