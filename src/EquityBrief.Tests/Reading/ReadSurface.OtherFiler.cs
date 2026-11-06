using EquityBrief.Core.Report;

namespace EquityBrief.Tests.Reading;

// IR's newest fundamentals row, fetched on 2026-09-27, was read under 0001466258, Trane Technologies' filer, where the SEC's
// ticker file names Ingersoll Rand's own, 0001699150, and its page drew Trane's segment table and Trane's results release as
// its own. A row read under a filer other than the company's own draws its segments and management's words not on file.
// see: A stored fundamentals row read under a filer other than the company's own is not on file until its figures are fetched again
public partial class ReadSurface
{
    const string TraneFiler = "0001466258";

    const string IngersollFiler = "0001699150";

    [Fact]
    public void AFilerIsReadByItsDigitsAndOnlyAFilerOtherThanTheCompanysOwnIsNamed()
    {
        using var row = System.Text.Json.JsonDocument.Parse($"{{\"cik\":\"{TraneFiler}\"}}");

        Assert.Equal(TraneFiler, OtherFiler.Of(row.RootElement, IngersollFiler));
        Assert.Null(OtherFiler.Of(row.RootElement, "1466258"));
        Assert.Null(OtherFiler.Of(row.RootElement, null));
        Assert.Null(OtherFiler.Of("{\"cik\":null}", IngersollFiler));
        Assert.Equal(
            "not on file, read under the filer 0001466258 and not the company's own, 0001699150, until the company's figures are fetched again",
            OtherFiler.Why(TraneFiler, "1699150"));

        // Handed to a writer without the archive's parts, the provider's own standing.
        Assert.Equal(
            "{\"cik\":\"0001466258\",\"quarter\":{\"revenue\":\"1\"}}",
            OtherFiler.Without("{\"cik\":\"0001466258\",\"quarter\":{\"revenue\":\"1\"},\"segments\":{},\"revenueTables\":[],\"tableGrowth\":[],\"guidance\":{},\"facts\":{}}"));
    }

    [Fact]
    public async Task ARowReadUnderAnotherFilerDrawsItsSegmentTableAndWhatManagementSaidNotOnFile()
    {
        using var store = await WithFundamentals(withTheArchive: true);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        store.Execute($"UPDATE fundamentals SET payload = json_set(payload, '$.cik', '{TraneFiler}') WHERE ticker = '{Name}' AND filing_date = (SELECT MAX(filing_date) FROM fundamentals WHERE ticker = '{Name}');");
        store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{Name}', '2026-11-05T21:00:00Z', '{IngersollFiler}');");

        var filings = await Api(store).FundamentalsAsync(Name);

        Assert.Equal(OtherFiler.Why(TraneFiler, IngersollFiler), filings[0].OtherFiler);
        Assert.Null(EquityBrief.Api.Reading.NameScreen.RawSegments(filings));

        var page = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Contains("data-segments=\"old\"", page, StringComparison.Ordinal);
        Assert.Contains("The filing's segment table is left out: not on file, read under the filer 0001466258 and not the company's own, 0001699150, until the company's figures are fetched again.", page, StringComparison.Ordinal);
        Assert.Contains("data-other-filer=\"true\">What management said is left out: not on file, read under the filer 0001466258", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-segment-figure=", page, StringComparison.Ordinal);

        // Fetched again under the company's own filer, the row's parts stand.
        store.Execute($"UPDATE fundamentals SET payload = json_set(payload, '$.cik', '{IngersollFiler}') WHERE ticker = '{Name}';");

        var own = await client.GetStringAsync($"/screens/name/{Name}");

        Assert.Null((await Api(store).FundamentalsAsync(Name))[0].OtherFiler);
        Assert.DoesNotContain("data-other-filer=", own, StringComparison.Ordinal);
        Assert.Contains("data-segment-figure=", own, StringComparison.Ordinal);
    }
}
