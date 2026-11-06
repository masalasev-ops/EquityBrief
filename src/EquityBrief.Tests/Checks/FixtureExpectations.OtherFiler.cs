using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Facts;

namespace EquityBrief.Tests.Checks;

// A newest fundamentals row read under a filer other than the company's own hands a writer none of the archive's parts, and a
// research pass reads no results release by that filer, as IR's row read under Trane's would have handed Trane's.
// see: A stored fundamentals row read under a filer other than the company's own is not on file until its figures are fetched again
public partial class FixtureExpectations
{
    const string AnotherFiler = "0001466258";

    // KEYS's newest row read under another filer, the company's own named by a quarters fetch after it.
    static void ReadUnderAnotherFiler(TemporaryStore store)
    {
        store.Execute($"UPDATE fundamentals SET payload = json_set(payload, '$.cik', '{AnotherFiler}') WHERE ticker = 'KEYS' AND filing_date = (SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('KEYS', '2099-01-01T00:00:00Z', '0001601046');");
    }

    static int SegmentFacts(TemporaryStore store) =>
        System.Text.Json.JsonDocument.Parse(Query(store, "SELECT payload FROM facts WHERE ticker = 'KEYS';").Single()).RootElement
            .GetProperty("facts").EnumerateArray()
            .Count(fact => fact.GetProperty("name").GetString()!.StartsWith(EquityBrief.Core.Facts.SegmentPeriods.Prefix, StringComparison.Ordinal));

    [Fact]
    public async Task AFactsFileIsHandedNoArchivePartOfARowReadUnderAnotherFiler()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();
        var night = FixedClock.At(FixtureReplay.Night, SessionZones.UnitedStates);

        Assert.True(SegmentFacts(store) > 0, "KEYS's facts file carries its segment table's figures while its row is its own filer's.");

        ReadUnderAnotherFiler(store);
        await new FactsAssembler(night, store.DatabaseFile).RunAsync("facts-under-another-filer");

        Assert.Equal(0, SegmentFacts(store));
    }

    [Fact]
    public async Task AResearchPassReadsNoReleaseByARowReadUnderAnotherFiler()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        ReadUnderAnotherFiler(store);

        var paid = new ScriptedModel([.. Enumerable.Repeat("The section, in words and citing its document [D1].", 40)]);
        var archive = new HoldsReleases(new FiledRelease("https://www.sec.gov/another.htm", "another.htm", new DateOnly(2026, 7, 30), "Revenue was reported."));
        var read = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: archive, news: new NoArticles(), search: new NoResults()).RunAsync("KEYS", "research-another-filer");

        Assert.Contains("filings archive: not on file, read under the filer 0001466258 and not the company's own, 0001601046, until the company's figures are fetched again", read.Reason, StringComparison.Ordinal);
        Assert.Empty(Query(store, "SELECT id FROM source_document WHERE url = 'https://www.sec.gov/another.htm';"));
    }
}
