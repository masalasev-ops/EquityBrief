using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.3: the newest results release's four answers under what management said, read back off the researched
// fixture's pages against rows constructed beside its store: beneath KEYS's written section, the release the night may
// read under the newest reading version and not one accepted at the late slot's start on the night, each answer with its
// reading and the line naming the model and the version; MSFT, which no pass wrote for, drawing the region for the reading
// alone after the latest quarter, dated by the day in New York the SEC accepted the release; and AAPL, whose release is
// read under an older version alone, drawing no region.
// see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
// see: A release reading is readable from the first night whose late slot starts after the release was accepted
public partial class ReadSurface
{
    // The region's card to the next card, since a written section's sources stand in a section of their own inside it.
    const string ManagementCard = "<section class=\"card spined\" id=\"s-what-management-said\".*?(?=<section class=\"card|\\z)";

    [Fact]
    public async Task TheNewestReleasesReadingIsDrawnUnderWhatManagementSaidAndAloneWhereNoSectionIsWritten()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        // Two reading versions, the second the newest; Keysight's August release read under both and a release of its
        // accepted at the late slot's start on the night read under the second; Microsoft's release accepted half past
        // eight in the evening in New York, after midnight in UTC, read under the second; Apple's under the first alone.
        foreach (var (pin, model) in new[] { (1, "an older model"), (2, "google/gemma-4-26b-a4b-qat") })
        {
            store.Execute(
                "INSERT INTO reading_pin (id, model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) "
                + $"VALUES ({pin}, '{model}', 'f', 'h', 1, '2026-09-01T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-09-0{pin}T00:00:00Z', NULL, 'constructed');");
        }

        string Filer(string ticker) => Rows(store, $"SELECT cik FROM company WHERE ticker = '{ticker}' AND cik IS NOT NULL ORDER BY fetched_at DESC LIMIT 1;").Single()[0];

        void Release(string accession, string ticker, string accepted, params (int Pin, string Words)[] readings)
        {
            store.Execute(
                "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id) "
                + $"VALUES ('{accession}', '{Filer(ticker)}', '8-K', '{accepted[..10]}', '{accepted}', 'ex99.htm', 'https://www.sec.gov/Archives/edgar/data/1/ex99.htm', zeroblob(1), 'h', 1, 'constructed');");

            foreach (var (pin, words) in readings)
            {
                var four = words.Split('|');

                store.Execute(
                    "INSERT INTO release_reading (accession, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) "
                    + $"VALUES ('{accession}', {pin}, 'read', NULL, '{four[0]}', '{four[1]}', '{four[2]}', '{four[3]}', 3.0, 5000, '2026-09-08T22:00:00Z', 'constructed');");
            }
        }

        Release("0001601046-26-000029", "KEYS", "2026-08-18T20:09:53Z", (1, "in line|kept|neutral|steady"), (2, "better|raised|confident|not stated"));
        Release("0001601046-26-000031", "KEYS", "2026-09-08T21:40:00Z", (2, "worse|lowered or withdrawn|cautious|weaker"));
        Release("0000789019-26-000070", "MSFT", "2026-07-30T00:30:00Z", (2, "worse|none given|cautious|weaker"));
        Release("0000320193-26-000010", "AAPL", "2026-07-30T20:30:00Z", (1, "better|kept|neutral|steady"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Keysight: the written section and beneath it the August release's answers under the second version, the night's
        // own release at the late slot's start not read on it, dated as the section was written.
        var keys = Regex.Match(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/KEYS")), ManagementCard, RegexOptions.Singleline).Value;

        Assert.Contains("<span class=\"dl-k\">Written</span>", keys, StringComparison.Ordinal);
        Assert.Contains("<div class=\"release-reading\" data-pin=\"2\" data-accepted=\"2026-08-18\">", keys, StringComparison.Ordinal);
        Assert.True(keys.IndexOf("<div class=\"release-reading\"", StringComparison.Ordinal) > keys.IndexOf("<p class=\"prose\">", StringComparison.Ordinal), keys);
        Assert.Equal(
            ["results|1|better", "outlook|1|raised", "tone|1|confident", "demand|none|not stated"],
            Answers(keys));
        Assert.Contains("<p>The newest results release, accepted 2026-08-18, as the local model read it.</p>", keys, StringComparison.Ordinal);
        Assert.Contains("<p class=\"release-read-by\">Read by the local model, google/gemma-4-26b-a4b-qat, reading version 2, from the release with its names and dates removed.</p>", keys, StringComparison.Ordinal);

        // Microsoft: no section written, so the region holds the reading alone after the latest quarter, dated by the day
        // the SEC accepted the release in New York, the 29th, where UTC had reached the 30th.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/MSFT"));
        var msft = Regex.Match(page, ManagementCard, RegexOptions.Singleline).Value;
        string[] order = ["quarter", "s-what-management-said", "segments"];

        Assert.Equal(order, Regex.Matches(page, "<section class=\"[^\"]*\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Where(order.Contains));
        Assert.Contains("<span class=\"dl-k\">Accepted</span><b>2026-07-29</b>", msft, StringComparison.Ordinal);
        Assert.DoesNotContain("<p class=\"prose\">", msft, StringComparison.Ordinal);
        Assert.Equal(["results|-1|worse", "outlook|none|none given", "tone|-1|cautious", "demand|-1|weaker"], Answers(msft));

        // Apple: its release read under the older version alone, so no region is drawn.
        Assert.DoesNotContain("id=\"s-what-management-said\"", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL")), StringComparison.Ordinal);
    }

    // Each answer row of a region's reading: its question, the reading code holds for its word, and the word.
    static string[] Answers(string region) =>
    [
        .. Regex.Matches(region, "<tr data-question=\"([^\"]+)\"><td>[^<]+</td><td data-reading=\"([^\"]+)\">([^<]+)</td></tr>")
            .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}"),
    ];
}
