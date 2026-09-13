using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// claim-admissibility. Each inadmissible document class is refused, and nothing
// resting on one is written.
//
// Rostered from 6.3, which is where the test and the store it writes into are
// built. Its roster row named 6.1 until 6.0 and 6.1 builds no admissibility.
//
// The poisoned paragraph and the unsourced claim are the other half of the
// roster row's sentence and they belong to the claim checker at 6.4. What is
// asserted here is the document half: the six kinds the test refuses, the window
// the pass asked for, the row a refusal leaves, and the run log line that makes
// a refusal visible to a person.
//
// The population is stated in both directions on purpose. A check that only ever
// read documents written to be refused would measure nothing about what it
// admits, and the admitting half is where the cost of a wrong rule falls: a
// marker that fires on ordinary reporting takes the reporting this system exists
// to read out of every pass, silently, and the pass looks like a quiet week.
public class ClaimAdmissibility
{
    internal static CheckReach Reach => new(
        "claim-admissibility",
        ["docs/ARCHITECTURE.html"],
        [
            CheckReach.Key(Scope.LimitsTable, "Source admissibility"),
            CheckReach.Key(Scope.FailureTable, "A source is returned but its text cannot be retrieved"),
            CheckReach.Key(Scope.FailureTable, "A document's publish date falls outside the window the pass asked for"),

            // Section 19.1's three rows, the first of them read as six claims
            // rather than one. The expectation is reached here rather than by
            // `fixture-expectations` for the reason the run page's is reached by
            // the check that draws the page: what it holds is what the rules
            // produce over documents, and the check that reads it is the one that
            // judges them.
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a machine-generated price forecast"),
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a broker marketing page"),
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a summary written by another AI system"),
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a quote page carrying no article"),
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a page with no publish date"),
            CheckReach.Key(Scope.FixtureTable, "an inadmissible document, a page whose text cannot be retrieved"),
            CheckReach.Key(Scope.FixtureTable, "source documents"),
            CheckReach.Key(Scope.FixtureTable, "refused documents"),
        ]);

    static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    // The window the tests ask for, wide enough to hold every dated document the
    // fixture carries. The window's own effect is asserted on its own below, by
    // narrowing it rather than by moving this.
    static readonly DateOnly From = new(2026, 1, 1);

    static readonly DateOnly To = new(2026, 9, 30);

    static readonly DateTimeOffset FetchedAt = new(2026, 9, 13, 2, 0, 0, TimeSpan.Zero);

    // ---- the documents the fixture holds ----

    internal sealed record Constructed(string Name, FetchedDocument Document, string Carries);

    internal static IReadOnlyList<Constructed> Refusable()
    {
        var file = Path.Combine(Folder(), "inadmissible-documents.json");
        using var document = JsonDocument.Parse(File.ReadAllText(file));

        return
        [
            .. document.RootElement.GetProperty("documents").EnumerateArray().Select(entry =>
                new Constructed(
                    entry.GetProperty("name").GetString()!,
                    new FetchedDocument(
                        Channel(entry.GetProperty("channel").GetString()!),
                        entry.GetProperty("url").GetString()!,
                        entry.GetProperty("title").GetString()!,
                        entry.GetProperty("publishedOn") is { ValueKind: JsonValueKind.String } published
                            ? DateOnly.Parse(published.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                            : null,
                        entry.GetProperty("text") is { ValueKind: JsonValueKind.String } text
                            ? text.GetString()
                            : null,
                        entry.TryGetProperty("retrievalFailure", out var failure) ? failure.GetString() : null),
                    entry.GetProperty("carries").GetString()!)),
        ];
    }

    static DocumentChannel Channel(string named) => named switch
    {
        "search tool" => DocumentChannel.SearchTool,
        "news feed" => DocumentChannel.NewsFeed,
        "filings archive" => DocumentChannel.FilingsArchive,
        _ => throw new InvalidOperationException(
            $"'{named}' is not a channel a document can arrive through. A fixture naming one nothing " +
            "fetches would be judged as whichever the reader defaulted to."),
    };

    // The real documents, which are the other half of the population and the half
    // a wrong rule costs something: the four captured articles the licensed feed
    // delivered, and the two release exhibits the filings archive holds.
    internal static IReadOnlyList<FetchedDocument> Real()
    {
        var articles = RecordedNewsFeed
            .Parse(File.ReadAllText(Directory
                .GetFiles(Folder(), RecordedNewsFeed.FilePrefix + "*.json")
                .Single()))
            .Select(article => new FetchedDocument(
                DocumentChannel.NewsFeed,
                article.Url,
                article.Title,
                DateOnly.FromDateTime(article.Published.UtcDateTime),
                article.Text));

        return [.. articles, .. Exhibits()];
    }

    // A filed release as a document. The text is the exhibit's own prose through
    // the archive's reader, and the url is the address the manifest recorded the
    // capture against, so the primacy rule is asserted over a real address rather
    // than over one written here.
    internal static IReadOnlyList<FetchedDocument> Exhibits()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "manifest.json")));

        var captured = manifest.RootElement.GetProperty("inputs").EnumerateArray()
            .Where(input => input.GetProperty("file").GetString()!.StartsWith("release-", StringComparison.Ordinal))
            .Select(input => (
                File: input.GetProperty("file").GetString()!,
                Url: input.GetProperty("query").GetString()!))
            .ToArray();

        Assert.Equal(2, captured.Length);

        return
        [
            .. captured.Select(capture => new FetchedDocument(
                DocumentChannel.FilingsArchive,
                capture.Url,
                "Results release exhibit, " + capture.File,
                new DateOnly(2026, 8, 20),
                SecEdgarArchive.Plain(File.ReadAllText(Path.Combine(Folder(), capture.File))))),
        ];
    }

    // ---- each kind refused, and nothing resting on it written ----

    [Fact]
    public void EachKindTheFixtureHoldsIsRefusedByTheRuleItsOwnNameStates()
    {
        var refusable = Refusable();

        // Seven documents over six kinds, stated in advance. The seventh is the
        // one that fails two gates, and it is what asserts the order.
        Assert.Equal(7, refusable.Count);

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a machine-generated price forecast"] = Admissibility.PriceForecast,
            ["a broker marketing page"] = Admissibility.MarketingPage,
            ["a summary written by another AI system"] = Admissibility.AiWrittenSummary,
            ["a quote page carrying no article"] = Admissibility.QuotePage,
            ["a page with no publish date"] = Admissibility.NoPublishDate,
            ["a page whose text cannot be retrieved"] = Admissibility.NoText,
            ["a broker marketing page with no publish date"] = Admissibility.MarketingPage,
        };

        // Both directions. A document the map does not name is one this test
        // would silently not judge, and a name the fixture does not carry is a
        // rule nothing exercises.
        Assert.Empty(refusable.Select(one => one.Name).Except(expected.Keys, StringComparer.Ordinal));
        Assert.Empty(expected.Keys.Except(refusable.Select(one => one.Name), StringComparer.Ordinal));

        foreach (var (name, document, _) in refusable)
        {
            Assert.Equal(expected[name], Admissibility.Judge(document, From, To));
        }

        // Every one of the four denied categories is reached by a document here,
        // which is the clause the done condition states. Counted rather than
        // assumed: five of the seven are categories and two are the date and the
        // text, so a category left without a document would pass every line
        // above by not being named.
        var reached = refusable
            .Select(one => Admissibility.Judge(one.Document, From, To))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(Admissibility.DeniedCategories.Except(reached, StringComparer.Ordinal));
        Assert.Contains(Admissibility.NoPublishDate, reached);
        Assert.Contains(Admissibility.NoText, reached);
        Assert.DoesNotContain(Admissibility.Accepted, reached);
    }

    [Fact]
    public void ADocumentThatFailsTwoGatesIsRefusedByTheKindAndNotByTheDate()
    {
        // The order, which is a decision rather than an implementation detail:
        // three of the five refusable pages the measurement read carried no
        // publish date at all, so a build that judged the date first would
        // refuse most of what it refuses for the cheapest reason available and
        // would report almost nothing about the kinds.
        var both = Refusable().Single(one => one.Name == "a broker marketing page with no publish date");

        Assert.Null(both.Document.PublishedOn);
        Assert.Equal(Admissibility.MarketingPage, Admissibility.Judge(both.Document, From, To));

        // And the two halves are each real, so the assertion above is about the
        // order rather than about one of them not firing. The same text with a
        // date is still marketing, and the same absence of a date with the
        // marketing removed is the date refusal.
        Assert.Equal(
            Admissibility.MarketingPage,
            Admissibility.Judge(both.Document with { PublishedOn = new DateOnly(2026, 8, 1) }, From, To));

        Assert.Equal(
            Admissibility.NoPublishDate,
            Admissibility.Judge(
                both.Document with { Text = "A paragraph about the company with nothing promotional in it at all." },
                From,
                To));
    }

    [Fact]
    public void NothingRestingOnARefusedDocumentCanBeWritten()
    {
        // The other half of the done condition. A refusal is kept as a row so it
        // is visible, and the row carries no body, so there is nothing for a
        // claim to rest on even though the row exists. The intake says the same
        // thing in one word for the pass to read.
        var intake = SourceDocuments.Of(
            [.. Refusable().Select(one => one.Document)],
            From,
            To,
            FetchedAt);

        Assert.Equal(7, intake.Rows.Count);
        Assert.Empty(intake.Admitted);
        Assert.Equal(7, intake.Refused.Count);
        Assert.True(intake.NothingAdmissible);

        Assert.All(intake.Refused, row => Assert.Null(row.Body));
        Assert.All(intake.Refused, row => Assert.False(row.Admitted));
        Assert.All(intake.Rows, row => Assert.Contains(row.Admissibility, Admissibility.Verdicts));

        // The refusal keeps its reason and the row that carries it keeps enough
        // to be read: a row with no reason would be a refusal nobody can see,
        // which is the only reason it is stored at all.
        Assert.All(intake.Refused, row => Assert.NotEqual(Admissibility.Accepted, row.Admissibility));
        Assert.All(intake.Refused, row => Assert.False(string.IsNullOrWhiteSpace(row.Url)));
        Assert.All(intake.Refused, row => Assert.False(string.IsNullOrWhiteSpace(row.Title)));

        // And the one refused for having no date is stored with none, which is
        // the pair of columns SCHEMA's own note was wrong about until 6.3.
        var undated = intake.Refused.Single(row => row.Admissibility == Admissibility.NoPublishDate);

        Assert.Null(undated.PublishedOn);
    }

    [Fact]
    public void TheRealDocumentsTheFixtureHoldsAreAdmitted()
    {
        // The direction a wrong rule costs something. Six real documents, four
        // articles the licensed feed delivered and two release exhibits, and the
        // population is stated because a run that read none of them would pass
        // every assertion here by having nothing to admit.
        var real = Real();

        Assert.Equal(6, real.Count);

        var verdicts = real
            .Select(document => (document.Url, Verdict: Admissibility.Judge(document, From, To)))
            .ToArray();

        Assert.All(verdicts, pair => Assert.Equal(Admissibility.Accepted, pair.Verdict));

        // One of the four articles is titled for a subject a bare marker would
        // have caught, which is the near miss this direction exists to hold.
        Assert.Contains(real, document => document.Title.Contains("AI Server Stock", StringComparison.Ordinal));

        // And the domain finding, measured rather than argued: the articles
        // admitted here arrive under the same host that delivered the price
        // prediction page the measurement had to refuse.
        Assert.Contains(
            real,
            document => Admissibility.Host(document.Url).EndsWith("yahoo.com", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRowAnAdmissionLeavesCarriesTheBodyAndTheRowARefusalLeavesDoesNot()
    {
        var admitted = SourceDocuments.Of([.. Real()], From, To, FetchedAt);

        Assert.Equal(6, admitted.Admitted.Count);
        Assert.Empty(admitted.Refused);
        Assert.False(admitted.NothingAdmissible);

        Assert.All(admitted.Admitted, row => Assert.False(string.IsNullOrWhiteSpace(row.Body)));
        Assert.All(admitted.Admitted, row => Assert.NotNull(row.PublishedOn));
        Assert.All(admitted.Admitted, row => Assert.Equal(FetchedAt, row.FetchedAt));

        // The body is the document's own text rather than a summary of it, which
        // is what makes a claim checkable against the document a reader opens.
        foreach (var document in Real())
        {
            var row = admitted.Rows.Single(one => one.Url == document.Url);

            Assert.Equal(document.Text, row.Body);
        }
    }

    [Fact]
    public void TheIdIsTheUrlHashedSoASecondFetchOfTheSameAddressIsTheSameRow()
    {
        var document = Real()[0];
        var again = document with { Title = "the same document, retitled by the publisher" };

        Assert.Equal(
            SourceDocuments.Stored(document, Admissibility.Accepted, FetchedAt).Id,
            SourceDocuments.Stored(again, Admissibility.Accepted, FetchedAt.AddDays(1)).Id);

        // And a different address is a different row, which is the direction an
        // id that returned a constant would pass.
        Assert.NotEqual(
            SourceDocuments.Id("https://example.test/a"),
            SourceDocuments.Id("https://example.test/b"));

        Assert.Equal(SourceDocuments.IdCharacters, SourceDocuments.Id("https://example.test/a").Length);
    }

    // ---- the window the pass asked for ----

    [Fact]
    public void ThePublishDateIsJudgedAgainstTheWindowThePassAskedForAndNotAgainstTheDocument()
    {
        // Section 18's row. The same document is admitted for a window that holds
        // it and refused for one that does not, which is what makes the rule
        // about the pass rather than about the document: a rule keyed on the
        // document's own age would be a second staleness rule in a system that
        // has one.
        var document = Real().First(one => one.Channel == DocumentChannel.NewsFeed);
        var published = document.PublishedOn!.Value;

        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(document, published, published));

        Assert.Equal(
            Admissibility.OutsideTheWindow,
            Admissibility.Judge(document, published.AddDays(1), published.AddDays(30)));

        Assert.Equal(
            Admissibility.OutsideTheWindow,
            Admissibility.Judge(document, published.AddDays(-30), published.AddDays(-1)));

        // The edges are inside, asserted on both of them, because a boundary that
        // excluded its own day would refuse everything published on the day the
        // pass asked about, which is most of what a pass reads.
        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(document, published, published.AddDays(5)));
        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(document, published.AddDays(-5), published));
    }

    // ---- the run log ----

    [Fact]
    public void TheRunLogLineNamesTheUrlAndTheReasonForADocumentWithNoText()
    {
        // Section 18's other row. The result is discarded rather than cited, and
        // what makes the discarding visible is the run log naming the address and
        // the reason: a count of refusals would say a document was dropped and
        // not which, and the one thing a person can act on is the address.
        var refusable = Refusable();
        var intake = SourceDocuments.Of([.. refusable.Select(one => one.Document)], From, To, FetchedAt);

        var unretrievable = refusable.Single(one => one.Name == "a page whose text cannot be retrieved");

        Assert.Contains(unretrievable.Document.Url, intake.Detail, StringComparison.Ordinal);
        Assert.Contains(unretrievable.Document.RetrievalFailure!, intake.Detail, StringComparison.Ordinal);
        Assert.Contains("No text from", intake.Detail, StringComparison.Ordinal);

        // A count per category rather than one number of refusals, because four
        // of one kind is a search returning marketing and one of each is an
        // ordinary evening.
        Assert.Contains("7 document(s) fetched, 0 admitted", intake.Detail, StringComparison.Ordinal);
        Assert.Contains(Admissibility.MarketingPage + " 2", intake.Detail, StringComparison.Ordinal);
        Assert.Contains(Admissibility.PriceForecast + " 1", intake.Detail, StringComparison.Ordinal);

        // And a clean intake says what it did rather than nothing at all.
        var clean = SourceDocuments.Of([.. Real()], From, To, FetchedAt);

        Assert.Contains("6 document(s) fetched, 6 admitted", clean.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", clean.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("No text from", clean.Detail, StringComparison.Ordinal);
    }

    // ---- the hard rule about request urls ----

    [Fact]
    public void AUrlCarryingACredentialIsRefusedRatherThanStored()
    {
        // The hard rule, at the one place a url enters a store row. A document's
        // address is public and this provider's request address carries its key in
        // the query string, so a url of that shape is a fetcher handing over its
        // own request.
        var handed = new FetchedDocument(
            DocumentChannel.SearchTool,
            "https://provider.test/eod/AAPL.US?api_token=abc123&fmt=json",
            "a request rather than a document",
            new DateOnly(2026, 9, 1),
            "A paragraph of text long enough to be prose, with three sentences in it. This is the second. And the third, which takes the paragraph past forty words so the shape rule is not what refuses it here.");

        var refusal = Assert.Throws<InvalidOperationException>(
            () => SourceDocuments.Stored(handed, Admissibility.Accepted, FetchedAt));

        // The message names the path and withholds the query, because a refusal
        // that quoted the whole url would put the key in the log line the refusal
        // is written to.
        Assert.Contains("provider.test/eod/AAPL.US", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(SourceDocuments.ProviderCredentialsMarker, refusal.Message, StringComparison.Ordinal);

        // Every document the fixture holds passes it, in both directions, so the
        // guard is asserted to be letting the ordinary case through as well as
        // stopping the other one.
        Assert.All(Real(), document => Assert.False(SourceDocuments.CarriesACredential(document.Url)));
        Assert.All(Refusable(), one => Assert.False(SourceDocuments.CarriesACredential(one.Document.Url)));
    }

    [Fact]
    public void EveryQueryMarkerTheFixtureCheckScansForIsOneTheIntakeRefusesAUrlFor()
    {
        // The two lists reconciled rather than left to agree by hand. The
        // manifest check scans a captured query for these and this refuses a
        // stored url for them, and the two are about the same thing: a
        // credential travelling in a query string.
        //
        // One entry is excluded by name and the exclusion is asserted to be
        // removing something. "EquityBrief/" is the archive's user agent rather
        // than a credential, and it names the header the configured contact
        // travels in, which is not a query parameter at all.
        const string agent = "EquityBrief/";

        Assert.Contains(agent, FixtureManifest.CredentialMarkers);

        var parameters = FixtureManifest.CredentialMarkers
            .Where(marker => !string.Equals(marker, agent, StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(FixtureManifest.CredentialMarkers.Length - 1, parameters.Length);

        foreach (var marker in parameters)
        {
            Assert.True(
                SourceDocuments.CarriesACredential("https://example.test/a?" + marker + "value"),
                $"The manifest check scans a query for '{marker}' and the intake would store a url carrying it.");

            // And the same word inside a slug is not one, which is the half that
            // separates a guard from a word filter: two of these markers are
            // ordinary English and a headline is a slug.
            Assert.False(
                SourceDocuments.CarriesACredential("https://example.test/news/the-" + marker + "-of-apples-margin"),
                $"'{marker}' inside a published slug is a word in a headline and not a credential.");
        }
    }

    // ---- the preference, which orders and never refuses ----

    [Fact]
    public void ThePreferenceForPrimarySourcesOrdersAndRefusesNothing()
    {
        var listed = ListedSites();

        var filed = Exhibits()[0];
        var article = Real().First(one => one.Channel == DocumentChannel.NewsFeed);

        var press = new FetchedDocument(
            DocumentChannel.SearchTool,
            "https://www.reuters.com/business/apple-results-2026-08-01/",
            "Apple beats revenue estimates",
            new DateOnly(2026, 8, 1),
            "A paragraph of reporting with three sentences in it. Here is the second one. And a third, which carries the paragraph past forty words so nothing about its shape is what decides anything here.");

        var other = press with
        {
            Url = "https://someblog.test/apple-results/",
        };

        Assert.Equal(Admissibility.Primacy.Filed, Admissibility.Of(filed, listed));
        Assert.Equal(Admissibility.Primacy.Press, Admissibility.Of(article, listed));
        Assert.Equal(Admissibility.Primacy.Press, Admissibility.Of(press, listed));
        Assert.Equal(Admissibility.Primacy.Other, Admissibility.Of(other, listed));

        // The order, which is what a pass reaches for first.
        Assert.Equal(
            [filed.Url, press.Url, other.Url],
            Admissibility.Preferred([other, press, filed], listed).Select(document => document.Url));

        // And the half that keeps this from being a publisher list: the document
        // at the bottom of the order is admitted. A preference that refused would
        // be judging the publisher, which two decisions in this corpus rule out.
        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(other, From, To));
        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(press, From, To));

        // A host that ends with a listed site and is not one of it is not on the
        // list, which is the boundary a match on the ending gets wrong.
        Assert.Equal(
            Admissibility.Primacy.Other,
            Admissibility.Of(press with { Url = "https://notreuters.com/a" }, listed));

        Assert.Equal(
            Admissibility.Primacy.Press,
            Admissibility.Of(press with { Url = "https://uk.reuters.com/a" }, listed));
    }

    // The committed company-news list, read from the file rather than listed
    // here. It governs what a search may return, which is 6.9's business; what it
    // does here is rank a document a search brought back.
    internal static IReadOnlyList<string> ListedSites()
    {
        using var lists = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "source-lists.json")));

        var sites = lists.RootElement.GetProperty("companyNews").GetProperty("sites")
            .EnumerateArray()
            .Select(site => site.GetString()!)
            .ToArray();

        Assert.True(sites.Length >= 10, $"The company news list holds {sites.Length} sites, expected at least 10.");

        return sites;
    }

    // ---- the readers, proved over constructed text ----

    [Fact]
    public void TheForecastMarkerIsAPairingAndNotTheWordForecast()
    {
        // The permanent proof of the rule that would cost the most if it were a
        // single word. Real reporting pairs forecast with revenue and guidance
        // constantly, and every one of these headlines is a real shape.
        foreach (var title in new[]
        {
            "Apple revenue meets forecasts, shares rise",
            "Apple forecasts holiday quarter revenue above estimates",
            "Apple outlook pleases Wall Street as iPhone sales surge",
            "Apple's Quarterly Earnings Preview: What You Need to Know",
        })
        {
            Assert.Equal(
                Admissibility.Accepted,
                Admissibility.Judge(Prose(title, "https://www.reuters.com/business/apple-2026/"), From, To));
        }

        // And the two shapes it does refuse, one in the title and one in the
        // address, which are the two the measurement found. The second is the
        // page on a press domain with a named author and twelve paragraphs of
        // prose, where the address slug is what carries the pairing.
        Assert.Equal(
            Admissibility.PriceForecast,
            Admissibility.Judge(
                Prose("AAPL - Apple Inc Stock Price Forecast 2026, 2027, 2030 to 2050", "https://a.test/stocks/AAPL/report"),
                From,
                To));

        Assert.Equal(
            Admissibility.PriceForecast,
            Admissibility.Judge(
                Prose("Where Apple Could Be by 2030", "https://a.test/news/aapl-stock-price-prediction-where-155545121.html"),
                From,
                To));

        // The address whose last segment is the forecast itself, which is how the
        // forecast site the measurement read is addressed, and whose title and
        // text carry nothing at all.
        Assert.Equal(
            Admissibility.PriceForecast,
            Admissibility.Judge(Prose("Apple Inc", "https://a.test/stocks/AAPL/forecast"), From, To));

        // And the disclosure in the text, which is the third way in.
        Assert.Equal(
            Admissibility.PriceForecast,
            Admissibility.Judge(
                Prose("Apple in 2030", "https://a.test/news/apple-2030/", extra: " The range is based on algorithmic projections."),
                From,
                To));
    }

    [Fact]
    public void InvitationLanguageAloneRefusesNothingAndThePairingIsWhatDoes()
    {
        // The two defects the live run at 6.3 found, kept as the cases that would
        // have caught them. Over 411 real articles from one dated request the rule
        // as first written refused three pieces of ordinary consumer-finance
        // reporting, and it took two repairs rather than one.
        //
        // The first: one invitation written twice counted as two. A piece on what
        // retirees put off says "sign up" and "signing up" about a benefit, which
        // is one way of being asked, so the count is over kinds.
        Assert.Equal(1, Admissibility.Invitations("You can sign up at 65, and signing up late costs you."));
        Assert.Equal(2, Admissibility.Invitations("Create account in minutes. A demo account is available."));

        // The second, and the one that survived the first repair: two genuinely
        // different invitations inside an article whose subject is accounts. This
        // is the article the live run refused after the counting was fixed, and it
        // has to be admitted, which is what says invitation language separates an
        // article about accounts from a page selling one not at all.
        var article = Prose(
            "Five ways savers lose a retirement pot",
            "https://a.test/news/boomers-nest-egg/",
            extra: " Opening an account late costs more than most people think. You can sign up in a morning.");

        Assert.Equal(2, Admissibility.Invitations(article.Text!));
        Assert.Equal(Admissibility.Accepted, Admissibility.Judge(article, From, To));

        // The pairing is what refuses: the product beside an invitation. Measured
        // on the same 411 before anything rested on it, where the five articles
        // carrying a leveraged-product term carry no invitation between them.
        Assert.Equal(
            Admissibility.MarketingPage,
            Admissibility.Judge(
                Prose("Trade shares with us", "https://a.test/markets/shares/",
                    extra: " Trade Apple as a contract for difference. Create account in minutes."),
                From,
                To));

        // Each half alone admits, which is what makes the line above about the
        // pairing. An article naming the product is reporting, and an article
        // asking you to sign up for something is reporting.
        Assert.Equal(
            Admissibility.Accepted,
            Admissibility.Judge(
                Prose("The regulator looks at leveraged products", "https://a.test/news/regulator/",
                    extra: " A contract for difference is what the consultation covers."),
                From,
                To));

        // And the kinds are a partition rather than a list: every phrase sits in
        // exactly one kind, so a phrase added to two of them would make one page
        // count as two invitations again by another route.
        var phrases = Admissibility.AccountInvitations.SelectMany(kind => kind).ToArray();

        Assert.Equal(phrases.Length, phrases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(Admissibility.AccountInvitations.Length >= 4);
    }

    [Fact]
    public void ASubjectIsNotADisclosureAndAShortArticleIsNotAQuotePage()
    {
        // Two near misses, each one a rule that would have been keyed on a word.
        //
        // A document about the subject is not a document written by it. One of
        // the four captured articles is titled "Dell vs. HPE: Which Top AI Server
        // Stock Is the Better Buy?", so a marker of the bare two letters would
        // refuse a real article for what it is about.
        Assert.Equal(
            Admissibility.Accepted,
            Admissibility.Judge(
                Prose("Dell vs. HPE: Which Top AI Server Stock Is the Better Buy?", "https://a.test/news/dell-hpe/"),
                From,
                To));

        Assert.Equal(
            Admissibility.AiWrittenSummary,
            Admissibility.Judge(
                Prose("Apple results", "https://a.test/news/apple/", extra: " AI-generated summary."),
                From,
                To));

        // And a page addressed as a symbol with an article under it is an
        // article. Both halves of the quote page rule are required, and this is
        // the half that would refuse a real piece: a publisher that files its
        // reporting under /quote/AAPL/ is addressing it oddly, not writing a
        // quote table.
        Assert.Equal(
            Admissibility.Accepted,
            Admissibility.Judge(Prose("Apple (AAPL) Stock Price & Overview", "https://a.test/quote/AAPL"), From, To));

        Assert.Equal(
            Admissibility.QuotePage,
            Admissibility.Judge(
                new FetchedDocument(
                    DocumentChannel.SearchTool,
                    "https://a.test/quote/AAPL",
                    "Apple (AAPL) Stock Price & Overview",
                    new DateOnly(2026, 9, 1),
                    "Price 241.62\nChange 1.84 (0.77%)\nMarket cap 3.58T\nPE ratio 32.4"),
                From,
                To));
    }

    [Fact]
    public void TheProseReaderCountsSentencesRatherThanPeriodsAndItsThresholdsAreTheOnesMeasured()
    {
        // A quote table's figures carry periods inside them, so a count of
        // periods reads a table as prose. This is the reader that the shape half
        // of the quote page rule rests on.
        Assert.Equal(0, Admissibility.Sentences("Price 241.62 Market cap 3.58T PE ratio 32.4"));
        Assert.Equal(3, Admissibility.Sentences("One thing. Another thing. And a third."));
        Assert.Equal(2, Admissibility.Sentences("Is it? It is."));

        Assert.False(Admissibility.CarriesRunningProse("Price 241.62\nChange 1.84\nMarket cap 3.58T"));

        // The measured boundary. The tightest of the four captured articles
        // carries exactly one paragraph of three sentences and fifty-two words,
        // so the thresholds are asserted at the values that admit it and the
        // paragraph one word short is asserted not to.
        var words = string.Join(' ', Enumerable.Repeat("word", Admissibility.WordsInAParagraph - 3));

        Assert.True(Admissibility.CarriesRunningProse($"One. Two. {words} three."));
        Assert.False(Admissibility.CarriesRunningProse($"One. Two. {words[..^5]} three."));
        Assert.False(Admissibility.CarriesRunningProse($"One. {words} two."));

        Assert.Equal(3, Admissibility.SentencesInAParagraph);
        Assert.Equal(40, Admissibility.WordsInAParagraph);
    }

    [Fact]
    public void TheCapturedArticlesAreWhatTheThresholdsWereMeasuredAgainst()
    {
        // The measurement itself, kept as an assertion rather than only as a
        // sentence in a record, because the thresholds are only defensible
        // against the population they were read off. The tightest article has one
        // qualifying paragraph, so a threshold raised by one sentence would take
        // a real article out of every pass.
        var articles = Real().Where(document => document.Channel == DocumentChannel.NewsFeed).ToArray();

        Assert.Equal(4, articles.Length);

        var qualifying = articles
            .Select(article => Admissibility.Paragraphs(article.Text!)
                .Count(paragraph => Admissibility.Sentences(paragraph) >= Admissibility.SentencesInAParagraph
                    && Admissibility.Words(paragraph) >= Admissibility.WordsInAParagraph))
            .ToArray();

        Assert.All(qualifying, count => Assert.True(count >= 1, "An article carries no qualifying paragraph."));
        Assert.Contains(1, qualifying);
    }

    // A document with ordinary prose in it, so what any assertion above turns on
    // is the title or the address rather than the shape.
    static FetchedDocument Prose(string title, string url, string extra = "") =>
        new(
            DocumentChannel.SearchTool,
            url,
            title,
            new DateOnly(2026, 8, 15),
            "The company reported results for the quarter and the shares moved on the guidance rather "
            + "than on the figures themselves. Management said the next quarter would grow in the low "
            + "teens. Analysts had been looking for something closer to the high single digits, which "
            + "is where the move came from." + extra);

    // ---- the document this check is asserted against ----

    [Fact]
    public void TheDocumentsOwnRowStatesTheSameFourCategoriesAndTheSameTwoThresholds()
    {
        // Section 17's row against the code, in both directions. The row is the
        // claim this check reaches a verdict on, so what it says has to be what
        // runs: a category named there and absent here is a rule nothing applies,
        // and one here and not there is a refusal the document does not admit to.
        var row = Row(Scope.LimitsTable, "Source admissibility");
        var value = row[1];

        var named = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["algorithmic or AI-generated price forecasts"] = Admissibility.PriceForecast,
            ["broker and platform marketing pages"] = Admissibility.MarketingPage,
            ["AI-written summaries"] = Admissibility.AiWrittenSummary,
            ["quote or hub pages with no article"] = Admissibility.QuotePage,
        };

        Assert.Equal(Admissibility.DeniedCategories.Length, named.Count);

        foreach (var (phrase, category) in named)
        {
            Assert.Contains(phrase, value, StringComparison.Ordinal);
            Assert.Contains(category, Admissibility.DeniedCategories);
        }

        // The three numbers the row states, read off it rather than repeated
        // here, which is what pins them.
        Assert.Contains(
            $"at least {Admissibility.SentencesInAParagraph} sentences and at least {Admissibility.WordsInAParagraph} words",
            value,
            StringComparison.Ordinal);

        Assert.Contains(
            "or a leveraged-product term beside an invitation to open one",
            value,
            StringComparison.Ordinal);

        Assert.Contains("Invitation language alone is not a marker", value, StringComparison.Ordinal);

        // And the order, which the row states because a document failing two
        // gates is refused by one of them and the reader of a row needs to know
        // which.
        Assert.Contains("the kind first and the date second", value, StringComparison.Ordinal);
    }

    internal static IReadOnlyList<string> Row(string heading, string subject)
    {
        var table = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(candidate => candidate.Heading == heading);

        return table.Body.Single(row => row.Count > 1 && row[0] == subject);
    }

    // ---- the fixture's own expectation ----

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Folder(), "expectations", stage + ".json"))).RootElement;

    [Fact]
    public void TheVerdictsAreWhatTheFixturesOwnExpectationSaysTheRulesProduce()
    {
        // Done condition 7's derived expectation, and the diff it exists for. The
        // file was written from the rules and from the marker each document states
        // it carries rather than from a run of this code, so a rule that changed
        // meaning shows up here as a disagreement rather than as a new number
        // everything agrees with.
        var expected = Expected("admissibility");
        var refusable = Refusable();

        Assert.Equal(expected.GetProperty("documentsHeld").GetInt32(), refusable.Count);

        var verdicts = refusable.ToDictionary(
            one => one.Name,
            one => Admissibility.Judge(one.Document, From, To),
            StringComparer.Ordinal);

        foreach (var stated in expected.GetProperty("refusedBy").EnumerateObject())
        {
            Assert.Equal(stated.Value.GetString(), verdicts[stated.Name]);
        }

        // Both directions over the file, so a document the file leaves out is as
        // visible as a verdict it gets wrong.
        Assert.Equal(
            expected.GetProperty("refusedBy").EnumerateObject().Count(),
            verdicts.Count);

        Assert.Equal(
            expected.GetProperty("kindsRefused").GetInt32(),
            verdicts.Values.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(expected.GetProperty("verdicts").GetInt32(), Admissibility.Verdicts.Length);

        // The order, read off the file as the four gates in the order they are
        // judged, and asserted against the document that fails two of them.
        var gates = expected.GetProperty("gatesInOrder").EnumerateArray().Select(gate => gate.GetString()!).ToArray();

        Assert.Equal(Admissibility.NoText, gates[0]);
        Assert.Equal(Admissibility.NoPublishDate, gates[2]);
        Assert.Equal(Admissibility.OutsideTheWindow, gates[3]);

        var both = expected.GetProperty("documentFailingTwoGates");

        Assert.Equal(
            both.GetProperty("refusedBy").GetString(),
            verdicts[both.GetProperty("name").GetString()!]);

        Assert.Contains("cheapest reason", both.GetProperty("why").GetString()!, StringComparison.Ordinal);

        // The admitted half, which the file states as a count and a composition
        // rather than as a list, because the documents are real and what matters
        // is that none of them is refused.
        var real = Real();
        var admitted = SourceDocuments.Of([.. real], From, To, FetchedAt);

        Assert.Equal(expected.GetProperty("realDocumentsAdmitted").GetInt32(), admitted.Admitted.Count);

        var composition = expected.GetProperty("realDocuments");

        Assert.Equal(
            composition.GetProperty("articles").GetInt32(),
            real.Count(document => document.Channel == DocumentChannel.NewsFeed));

        Assert.Equal(
            composition.GetProperty("releaseExhibits").GetInt32(),
            real.Count(document => document.Channel == DocumentChannel.FilingsArchive));

        // The host that delivered both a refused page and an admitted one, which
        // is the measured case behind judging per document. Read off the file and
        // asserted against the documents rather than restated as a sentence.
        var shared = composition.GetProperty("sharedHostWithARefusedPage").GetString()!;

        Assert.Contains(
            real,
            document => Admissibility.Host(document.Url).EndsWith(shared, StringComparison.Ordinal));

        // The row each verdict leaves, read off the file rather than described in
        // a test's own words.
        var refusedRow = expected.GetProperty("rowAfterARefusal");
        var refused = SourceDocuments.Of([.. refusable.Select(one => one.Document)], From, To, FetchedAt);

        Assert.Equal(JsonValueKind.Null, refusedRow.GetProperty("body").ValueKind);
        Assert.All(refused.Refused, row => Assert.Null(row.Body));

        Assert.Equal(JsonValueKind.Null, refusedRow.GetProperty("publishedOnWhereTheRefusalIsTheDate").ValueKind);
        Assert.Null(refused.Refused.Single(row => row.Admissibility == Admissibility.NoPublishDate).PublishedOn);

        Assert.Contains("category", refusedRow.GetProperty("admissibility").GetString()!, StringComparison.Ordinal);

        var admittedRow = expected.GetProperty("rowAfterAnAdmission");

        Assert.Equal(Admissibility.Accepted, admittedRow.GetProperty("admissibility").GetString());
        Assert.Contains("whole", admittedRow.GetProperty("body").GetString()!, StringComparison.Ordinal);
        Assert.All(admitted.Admitted, row => Assert.False(string.IsNullOrWhiteSpace(row.Body)));

        // The measured boundary the thresholds rest on, stated in the file as the
        // number of qualifying paragraphs the tightest captured article carries.
        // One, so a threshold raised by a sentence would refuse a real article.
        Assert.Contains(
            expected.GetProperty("tightestArticleQualifyingParagraphs").GetInt32(),
            real
                .Where(document => document.Channel == DocumentChannel.NewsFeed)
                .Select(article => Admissibility.Paragraphs(article.Text!)
                    .Count(paragraph => Admissibility.Sentences(paragraph) >= Admissibility.SentencesInAParagraph
                        && Admissibility.Words(paragraph) >= Admissibility.WordsInAParagraph)));
    }

    [Fact]
    public void TheScopeThisCheckRanOver()
    {
        // The coverage record, in numbers, as every check here states one. The
        // population carrying the property is the documents judged: seven written
        // to be refused and six real ones, and the second number is the one a
        // narrowing would quietly drop.
        Assert.Equal(7, Refusable().Count);
        Assert.Equal(6, Real().Count);
        Assert.Equal(4, Admissibility.DeniedCategories.Length);
        Assert.Equal(8, Admissibility.Verdicts.Length);
    }
}
