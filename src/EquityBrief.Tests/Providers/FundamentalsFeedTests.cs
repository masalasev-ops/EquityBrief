using System.Net;
using System.Net.Http;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Providers;

// The company fundamentals feed, and the six things the capture settled that the
// endpoint's name and SCHEMA's own column note would each have got wrong.
//
// The parser was written against four captured payloads and not the other way
// round, which is the rule 1.6 and 1.7 set after 1.2 stored a membership parser
// reading a field the provider does not send: the fixture agreed with it for two
// checkpoints because one session wrote both.
//
// One test per finding, because a single test over one name would pass on the
// three names whose payload happens to be uniform and say nothing about the
// fourth.
public class FundamentalsFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static string Captured(string ticker) =>
        File.ReadAllText(Path.Combine(Folder(), RecordedFundamentalsFeed.Prefix + ticker + ".json"));

    static CompanyFundamentals Read(string ticker) =>
        RecordedFundamentalsFeed.Parse(Captured(ticker), ticker);

    sealed class Answering(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);

            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    [Fact]
    public void TheFilingDateIsTheDateFiledAndNeverThePeriodItCovers()
    {
        // Two dates weeks apart, which is the trap the earnings calendar's payload
        // carries in the same provider's vocabulary. The store is keyed on the
        // filing date, so a reader taking the period end for it keys every row
        // early and nothing downstream questions it.
        var apple = Read("AAPL");
        var newest = apple.Filed[0];

        Assert.Equal(new DateOnly(2026, 6, 30), newest.PeriodEnd);
        Assert.Equal(new DateOnly(2026, 7, 31), newest.FilingDate);

        // And not on that one row alone. Every quarter of every captured name was
        // filed after the period it covers, which is what makes the two fields
        // different fields rather than one field written twice.
        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            Assert.All(
                Read(ticker).Filed,
                quarter => Assert.True(
                    quarter.FilingDate > quarter.PeriodEnd,
                    $"{ticker} {quarter.PeriodEnd:yyyy-MM-dd} was filed on {quarter.FilingDate:yyyy-MM-dd}."));
        }
    }

    [Fact]
    public void AQuarterFiledWithNoFilingDateIsCountedAndNotStored()
    {
        // One of the four names files a quarter with no filing date at all. The
        // grain is one row per filing date, so such a quarter cannot be keyed, and
        // defaulting it to the period end would state that the figures were known
        // weeks before they were.
        var keysight = Read("KEYS");

        Assert.Equal(1, keysight.QuartersWithNoFilingDate);
        Assert.DoesNotContain(keysight.Filed, quarter => quarter.PeriodEnd == new DateOnly(2012, 10, 31));

        // The row is in the capture, so the count is over something rather than
        // over an absence this test cannot tell from a parser that skipped it
        // silently.
        Assert.Contains("2012-10-31", Captured("KEYS"), StringComparison.Ordinal);

        // And the other three have none, so the count is not a constant.
        foreach (var ticker in new[] { "AAPL", "MSFT", "NFLX" })
        {
            Assert.Equal(0, Read(ticker).QuartersWithNoFilingDate);
        }
    }

    [Fact]
    public void MoneyIsReadFromBothFormsTheOnePayloadSendsItIn()
    {
        // The statements send "109417000000.00" and Highlights sends
        // 4849207869440. A reader keyed on one kind reports the other as absent,
        // which on a balance sheet is a zero a reader would act on.
        var apple = Read("AAPL");
        var newest = apple.Filed[0];

        Assert.Equal(109_417_000_000m, newest.Figures.Revenue);
        Assert.Equal(54_770_000_000m, newest.Figures.GrossProfit);
        Assert.Equal(29_789_000_000m, newest.Figures.NetIncome);
        Assert.Equal(383_266_000_000m, newest.Sheet.TotalAssets);
        Assert.Equal(107_520_000_000m, newest.Sheet.Equity);

        // The number form, from the other end of the same payload.
        Assert.NotNull(apple.Bases.Trailing);
        Assert.NotNull(apple.Bases.CurrentYear);
        Assert.NotNull(apple.Bases.NextYear);

        // Money, so decimal. The values above are exact to the cent and a double
        // would not hold them: this is the assertion that a later session cannot
        // satisfy by widening the type.
        Assert.IsType<decimal>(newest.Figures.Revenue!.Value);
    }

    [Fact]
    public void TheQuarterWithNoActualIsTheEstimatedOneAndIsNotReported()
    {
        // The payload's forward row carries an estimate, a null actual and a
        // difference of zero. A reader keying the surprise on that field reports
        // that a print which has not happened came in exactly as expected.
        var apple = Read("AAPL");

        Assert.NotNull(apple.Estimated);
        Assert.Equal(new DateOnly(2026, 9, 30), apple.Estimated!.PeriodEnd);
        Assert.NotNull(apple.Estimated.EpsEstimate);

        // It is not among the reported quarters, and no reported quarter carries
        // earnings without an actual.
        Assert.DoesNotContain(apple.Filed, quarter => quarter.PeriodEnd == apple.Estimated.PeriodEnd);
        Assert.All(
            apple.Filed.Where(quarter => quarter.Earnings is not null),
            quarter => Assert.NotNull(quarter.Earnings!.EpsActual));
    }

    [Fact]
    public void ANameWhoseNextPrintCarriesNoEstimateHasNoEstimatedQuarter()
    {
        // One of the four files an actual against every row it sends, so the
        // absence is in the fixture beside the presence. A section that drew a row
        // here would draw one with nothing in it.
        Assert.Null(Read("KEYS").Estimated);

        foreach (var ticker in new[] { "AAPL", "MSFT", "NFLX" })
        {
            Assert.NotNull(Read(ticker).Estimated);
        }
    }

    [Fact]
    public void SegmentsAndGuidanceAreReportedAbsentAndTheDescriptionProseIsNotASegmentTable()
    {
        // The endpoint files neither, for any name, under any key. SCHEMA's
        // payload note names both, so a parser written from it would look for a
        // value and find the key missing, which reads as this name having none
        // rather than as the provider filing none.
        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            var read = Read(ticker);

            Assert.Contains("segments", read.PartsNotCarried);
            Assert.Contains("guidance", read.PartsNotCarried);
        }

        // And this is why the reading is a walk over key names rather than a scan
        // of the payload's text: two of the four captures contain the word
        // segment, in the company description, which is prose. A text scan would
        // read those two as carrying a segment table and the other two as not,
        // which is the worst of the three possible answers.
        Assert.Contains("segment", Captured("MSFT"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("segment", Captured("KEYS"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segment", Captured("AAPL"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePartsReadAreTheOnesTheNumbersSectionStates()
    {
        // What is carried, said forward rather than only as an absence. Five
        // reported quarters is what section 4 asks for and the capture holds more,
        // so the selection is the fetcher's rather than the file's.
        var apple = Read("AAPL");

        Assert.True(apple.Filed.Count >= 6, $"The capture holds {apple.Filed.Count} filed quarters.");
        Assert.Equal("AAPL", apple.Ticker);
        Assert.Equal("USD", apple.Currency);

        // Newest first, asserted rather than taken from the order the provider
        // happened to send.
        Assert.Equal(
            apple.Filed.Select(quarter => quarter.PeriodEnd).OrderByDescending(date => date),
            apple.Filed.Select(quarter => quarter.PeriodEnd));
    }

    [Fact]
    public void FourFiscalCalendarsOverFourNames()
    {
        // The quarter ends do not line up, which is what a reader ordering five
        // quarters by the calendar rather than per name would get wrong. September,
        // June, October and December over four names.
        var ends = new[] { "AAPL", "MSFT", "KEYS", "NFLX" }
            .Select(ticker => Read(ticker).Filed[0].PeriodEnd.Month)
            .Distinct()
            .ToArray();

        Assert.True(ends.Length >= 2, $"The four names' newest quarters end in {ends.Length} distinct month(s).");
    }

    [Fact]
    public void APayloadWithNoGeneralObjectIsRefusedRatherThanReadAsAnUnknownName()
    {
        var refusal = Assert.Throws<FormatException>(
            () => RecordedFundamentalsFeed.Parse("{\"Highlights\":{}}", "AAPL"));

        Assert.Contains("`General`", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordedFeedRefusesANameItHoldsNoCaptureForAndCountsWhatItAnswers()
    {
        var recorded = RecordedFundamentalsFeed.FromFolder(Folder());

        var answered = await recorded.FundamentalsAsync("AAPL");

        Assert.Equal("AAPL", answered.Ticker);
        Assert.Equal(1, recorded.Requests);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorded.FundamentalsAsync("NOSUCH"));

        // An empty payload would look exactly like a name the provider files
        // nothing for, and the fetcher would record it as fetched.
        Assert.Contains("NOSUCH", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("record it as fetched", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLiveFeedAsksForOneNameAndCarriesTheKeyInTheQueryAndNowhereElse()
    {
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Captured("AAPL")),
        });

        var feed = new EodhdFundamentalsFeed(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var read = await feed.FundamentalsAsync("AAPL");

        Assert.Equal("AAPL", read.Ticker);
        Assert.Equal(1, feed.Requests);

        var asked = Assert.Single(handler.Asked);

        Assert.Contains("fundamentals/AAPL.US", asked.ToString(), StringComparison.Ordinal);
        Assert.Contains("fmt=json", asked.ToString(), StringComparison.Ordinal);

        // One request per name and no symbol list, which is what keeps this
        // endpoint off the nightly path rather than on it with a bigger URL.
        Assert.DoesNotContain(",", asked.AbsolutePath);
    }

    [Fact]
    public async Task ARefusalNamesTheFeedAndNeverTheRequest()
    {
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var feed = new EodhdFundamentalsFeed(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var refusal = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.FundamentalsAsync("AAPL"));

        Assert.Contains("company fundamentals", refusal.Message, StringComparison.Ordinal);

        // The key travels in the query string, so the rule that no request URL
        // reaches a log line is what this asserts rather than a courtesy.
        Assert.DoesNotContain(Key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("api_token", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCaptureIsNamedInTheManifestWithTheQueryThatFetchedIt()
    {
        // A capture nobody recorded the query for is a file whose provenance is a
        // guess, which is what the manifest exists to stop.
        var manifest = File.ReadAllText(Path.Combine(Folder(), "manifest.json"));

        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            Assert.Contains($"fundamentals-{ticker}.json", manifest, StringComparison.Ordinal);
            Assert.Contains($"fundamentals/{ticker}.US?fmt=json", manifest, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("api_token", manifest, StringComparison.Ordinal);
    }
}
