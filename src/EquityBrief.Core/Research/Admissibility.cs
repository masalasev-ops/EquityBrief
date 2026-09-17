using System.Text.RegularExpressions;

namespace EquityBrief.Core.Research;

// Whether a fetched document may be stored, judged on the document.
//
// Having a source and having a believable source are different tests and both
// run. A search for a company by name returns machine-generated price forecasts,
// broker marketing pages, summaries written by other systems, and quote pages
// carrying no article, and every one of them would satisfy a bare rule that a
// claim must name a stored document.
// see: A stored source is not automatically an admissible one
//
// Judged per document, after the fetch, and never per publisher. That is a
// decision this file is the implementation of, and 6.3's measurement is what
// makes it a measured statement rather than a preference: one domain,
// finance.yahoo.com, delivered a page that has to be refused and a page that has
// to be admitted, and it is the same domain the four articles in the committed
// fixture arrive under.
// see: Admissibility is judged per document, after the fetch, and never per publisher
//
// Every marker below was read off a real page on 2026-09-12 and the measurement
// is recorded in PROGRESS.md with its population: eight pages asked for by hand,
// seven answered and one refused automated access, against the four captured
// articles and two release exhibits this fixture already holds. Nothing here was
// invented from what a page of that kind seemed likely to say, which is the
// mistake capture-before-parse exists to prevent, applied to a rule rather than
// to a parser.
public static class Admissibility
{
    public const string Accepted = "accepted";

    // The four denied categories section 17 names, in the order they are judged.
    public const string PriceForecast = "algorithmic price forecast";
    public const string MarketingPage = "broker or platform marketing";
    public const string AiWrittenSummary = "AI-written summary";
    public const string QuotePage = "quote or hub page with no article";

    // The date rule section 17 names, which is two refusals rather than one: a
    // document with no date at all, and one dated outside the window the pass
    // asked for. They are apart because only the second is about the pass: the
    // same document is admitted for a wide window and refused for a narrow one,
    // and a reader seeing one reason for both could not tell which had happened.
    public const string NoPublishDate = "no publish date";
    public const string OutsideTheWindow = "published outside the window";

    // Section 18's case, and the one the measurement produced rather than chose:
    // of the eight pages asked for, one refused automated access and returned no
    // body at all.
    public const string NoText = "text could not be retrieved";

    public static readonly string[] DeniedCategories =
        [PriceForecast, MarketingPage, AiWrittenSummary, QuotePage];

    // Every verdict this test can reach, which is what the store's own column
    // holds. Named so a reader of a row has somewhere to look up what it says,
    // and so the check can assert that nothing writes a verdict outside the set.
    public static readonly string[] Verdicts =
        [Accepted, .. DeniedCategories, NoPublishDate, OutsideTheWindow, NoText];

    // The order the gates are judged in, which is a decision rather than an
    // implementation detail, because a document can fail two of them.
    //
    // Retrieval first, because a document with no text is nothing to judge. Then
    // the kind, then the date. The kind before the date is the half that needed
    // measuring: three of the five refusable pages the measurement read carried
    // no publish date at all, so a build judging the date first would refuse most
    // of what it refuses for the cheapest reason available and would report
    // almost nothing about the kinds. What is stored is one verdict, and it is
    // the kind where a document fails both, because the kind is a statement about
    // what the document is and the date only about when.
    static readonly (string Category, Func<FetchedDocument, string, bool> Refuses)[] Categories =
    [
        (PriceForecast, (document, text) => IsAPriceForecast(document, text)),
        (MarketingPage, (_, text) => IsMarketing(text)),
        (AiWrittenSummary, (document, text) => IsWrittenByASystem(document, text)),
        (QuotePage, (document, text) => IsAQuotePage(document, text)),
    ];

    public static string Judge(FetchedDocument document, DateOnly from, DateOnly to)
    {
        if (document.Text is not { Length: > 0 } text || document.RetrievalFailure is { Length: > 0 })
        {
            return NoText;
        }

        foreach (var (category, refuses) in Categories)
        {
            if (refuses(document, text))
            {
                return category;
            }
        }

        if (document.PublishedOn is not { } published)
        {
            return NoPublishDate;
        }

        return published < from || published > to ? OutsideTheWindow : Accepted;
    }

    // ---- the four categories ----

    // A page whose subject is a predicted price.
    //
    // Read off the title and the url and not only off the text, which is the
    // finding that decided this one. The forecast site the measurement read
    // carries five prose paragraphs, a table of yearly figures out to 2050, and
    // no statement anywhere that an algorithm produced any of it, so a rule
    // keyed on the text would have admitted it. The other direction is why the
    // marker is a pairing rather than a word: real reporting pairs "forecast"
    // with revenue and guidance all the time, as in "Apple revenue meets
    // forecasts", and a rule keyed on that word alone would refuse the
    // reporting this system exists to read.
    static bool IsAPriceForecast(FetchedDocument document, string text) =>
        Names(document.Title, ForecastSubjects)
        || Names(Slug(document.Url), ForecastSubjects)
        || Segments(document.Url).Any(segment => ForecastPaths.Contains(segment, StringComparer.OrdinalIgnoreCase))
        || Names(text, AlgorithmicProjection);

    // A price or stock word next to a prediction word. Both halves are required
    // and the pairing is the marker.
    static readonly string[] ForecastSubjects =
    [
        "price prediction",
        "price forecast",
        "price target for 20",
        "stock forecast",
        "stock prediction",
        "share price forecast",
        "share price prediction",
        "predicted price",
    ];

    // A path segment that is the page's whole subject. The forecast site the
    // measurement read is addressed as /stocks/AAPL/forecast, where the title
    // carries the pairing and the path carries only the last word.
    static readonly string[] ForecastPaths =
        ["forecast", "forecasts", "prediction", "predictions", "price-prediction", "price-forecast", "price-target"];

    // What a page says about how its figures were produced, where it says
    // anything. The first of these is verbatim from the page the measurement
    // read, which is a page on a financial press domain with a named author and
    // twelve paragraphs of prose: the disclosure is the only thing in its text
    // that separates it from reporting.
    static readonly string[] AlgorithmicProjection =
    [
        "algorithmic projection",
        "algorithmic forecast",
        "our algorithm",
        "the algorithm predicts",
        "machine learning model predicts",
        "model projects the price",
    ];

    // A page that exists to open an account: one carrying a regulatory risk
    // warning, or a leveraged product beside an invitation to open one.
    //
    // The warning is the reliable half, because no article in the captured set
    // warns its own reader about losing money.
    //
    // An invitation on its own is not the other half, and the live run at 6.3 is
    // why. Over 411 real articles from one dated request, the rule as first
    // written refused three pieces of ordinary consumer-finance reporting. Two
    // tripped on one invitation written twice, a piece on what retirees put off
    // saying "sign up" and "signing up" about a benefit, which counting kinds
    // rather than phrases repairs. The third survived that repair: an article on
    // how savers lose a retirement pot says "opening an account" and "sign up",
    // which are two genuinely different invitations inside an article whose
    // subject is accounts. Invitation language separates an article about accounts
    // from a page selling one not at all, so nothing here rests on it alone.
    //
    // What separates them is the product. The same 411 articles carry "contract
    // for difference" 0 times, "cfd" 0, "spread bet" twice and "trading platform"
    // three times, and not one of those five carries any invitation at all, while
    // both broker pages the by-hand measurement read carry the product and the
    // invitation together. So the second net is the pairing, and it is measured on
    // the population it has to be quiet over rather than assumed to be.
    //
    // The cost of this rule is stated rather than left to be discovered: an
    // article about the regulation of these products, quoting the warning or
    // naming the product beside a sign-up line, would be refused. That is one
    // article refused against admitting pages whose whole content is marketing,
    // and the preference for primary sources below is what a pass reaches for
    // instead.
    static bool IsMarketing(string text) =>
        RiskWarningsIn(text) >= RiskWarningsThatRefuse
        || (NamesALeveragedProduct(text) && Invitations(text) >= InvitationsBesideAProduct);

    public const int RiskWarningsThatRefuse = 1;

    public const int InvitationsBesideAProduct = 1;

    // The products a page of this kind sells, which is the half an article about
    // saving does not carry. Both spellings of the first, because the plural is
    // not a superstring of the singular and one of the pages measured uses it.
    static readonly string[] LeveragedProducts =
    [
        "contract for difference",
        "contracts for difference",
        "cfd",
        "spread bet",
        "leverage of up to",
        "margin trading",
    ];

    static readonly string[] RiskWarnings =
    [
        "retail investor accounts lose money",
        "retail client accounts lose money",
        "complex instruments and come with a high risk",
        "losing money rapidly due to leverage",
        "your capital is at risk",
        "spread bets and cfds",
    ];

    public static bool NamesALeveragedProduct(string text) => Names(text, LeveragedProducts);

    // Each warning above counted once however often a text writes it, so one
    // sentence carrying two of them counts two.
    public static int RiskWarningsIn(string text) =>
        RiskWarnings.Count(warning => text.Contains(warning, StringComparison.OrdinalIgnoreCase));

    // One row per kind of invitation, each row the ways that kind is written. A
    // page asking twice in one way asked once.
    public static readonly string[][] AccountInvitations =
    [
        ["open an account", "open your account", "create account", "create an account", "opening an account"],
        ["sign up", "signing up", "signup"],
        ["start trading", "trade with us", "trade now"],
        ["demo account", "practice account"],
        ["fund your account", "fund it by card", "make a deposit"],
    ];

    public static int Invitations(string text) =>
        AccountInvitations.Count(kind => Names(text, kind));

    // A summary a system wrote and said so.
    //
    // Only a declared one is refused, and that limit is the point rather than an
    // omission. Text a system wrote and nobody disclosed is not something a rule
    // over the text can find, so this marker catches what platforms say about
    // themselves and nothing else. What stands behind it is the preference for
    // primary sources and, at 6.4, the claim checker: a number that is not in the
    // facts file cannot reach prose whatever wrote the page it came from.
    //
    // The marker is a phrase and never the bare two letters. One of the four
    // captured articles is titled "Dell vs. HPE: Which Top AI Server Stock Is the
    // Better Buy?", so a rule keyed on "ai" would refuse a real article for being
    // about the subject.
    static bool IsWrittenByASystem(FetchedDocument document, string text) =>
        Names(text, DeclaredMachineWriting) || Names(document.Title, DeclaredMachineWriting);

    static readonly string[] DeclaredMachineWriting =
    [
        "ai-generated",
        "ai generated",
        "generated by ai",
        "written by ai",
        "ai-written",
        "ai summary",
        "ai-powered platform",
        "automated summary",
        "produced by an automated system",
        "this article was generated",
    ];

    // A quote or hub page carrying no article.
    //
    // Two conditions, and both are required. A page whose address or title says
    // it is a symbol page, and no paragraph of running prose anywhere in it. A
    // page with a quote page's title and a real article under it is an article,
    // and a short page with an ordinary title is a short article, so either half
    // alone refuses something it should not.
    //
    // The hub page the measurement read is not caught by the address half at all,
    // being addressed under /news/, and it is refused as a declared summary
    // instead. That is stated rather than counted as coverage: what this rule
    // reaches is a page addressed or titled as a quote, and a hub page addressed
    // as something else rests on the rule above it.
    static bool IsAQuotePage(FetchedDocument document, string text) =>
        (LooksLikeASymbolPage(document.Url) || Names(document.Title, QuotePageTitles))
        && !CarriesRunningProse(text);

    static readonly string[] SymbolSegments = ["quote", "quotes", "symbol", "symbols", "ticker", "tickers"];

    static readonly string[] QuotePageTitles =
    [
        "stock price & overview",
        "stock price and overview",
        "stock price, quote",
        "stock quote",
        "stock overview",
        "share price and news",
    ];

    static bool LooksLikeASymbolPage(string url)
    {
        var segments = Segments(url);

        return segments.Any(segment => SymbolSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))

            // A symbol overview page addressed as the name of a thing under a
            // collection, which is /stocks/aapl/ on the quote page the
            // measurement read. Two segments and no more: an article under the
            // same collection carries a slug of its own after them.
            || (segments.Count == 2
                && segments[0].Equals("stocks", StringComparison.OrdinalIgnoreCase));
    }

    // Whether a document carries an article rather than a table of figures.
    //
    // A paragraph of at least three sentences and at least forty words, anywhere
    // in the text. The thresholds are measured against the four captured
    // articles rather than chosen: the tightest of them carries exactly one
    // paragraph that qualifies, of three sentences and fifty-two words, so a
    // threshold of four sentences would refuse a real article. The margin being
    // that thin is why the rule above requires the address or the title as well,
    // rather than resting on this alone.
    public const int SentencesInAParagraph = 3;

    public const int WordsInAParagraph = 40;

    public static bool CarriesRunningProse(string text) =>
        Paragraphs(text).Any(paragraph =>
            Sentences(paragraph) >= SentencesInAParagraph
            && Words(paragraph) >= WordsInAParagraph);

    public static IReadOnlyList<string> Paragraphs(string text) =>
    [
        .. text
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
    ];

    // A sentence ends where a terminator is followed by space or by the end of
    // the paragraph. Followed by, because a figure carries a period inside it:
    // "3.58T" and "0.77%" are one token each and a count of periods would read a
    // quote table as prose.
    public static int Sentences(string paragraph) =>
        Regex.Matches(paragraph, @"[.!?](\s|$)").Count;

    public static int Words(string paragraph) =>
        paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ---- the preference for primary sources ----

    // How close a document is to the company itself, which orders candidates and
    // refuses nothing.
    //
    // That split is what keeps this apart from judging a publisher: the test
    // above decides what may be stored and this decides what a pass reaches for
    // first, and a document at the bottom of this order is usable with the pass
    // stating why it used it. A preference that refused would be a publisher
    // list wearing another name, which the decision this file cites rules out.
    public enum Primacy
    {
        Filed = 1,
        Press = 2,
        Other = 3,
    }

    public const string SecEdgarHost = "sec.gov";

    public static Primacy Of(FetchedDocument document, IReadOnlyList<string> listedSites) =>
        document.Channel switch
        {
            // The company's own filing or release, which is the top of the order
            // by what it is rather than by where it came from.
            DocumentChannel.FilingsArchive => Primacy.Filed,

            // The licensed feed of financial press. Ranked by the channel and not
            // by the host, because the host is the aggregator: ranking these by
            // domain would put every article in the feed in one place, and that
            // place would be the wrong one.
            DocumentChannel.NewsFeed => Primacy.Press,

            _ => Host(document.Url).EndsWith(SecEdgarHost, StringComparison.OrdinalIgnoreCase)
                ? Primacy.Filed
                : listedSites.Any(site => IsOn(Host(document.Url), site))
                    ? Primacy.Press
                    : Primacy.Other,
        };

    // Ordered by primacy and then by date, newest first. Stable inside a rank, so
    // a pass handed the same documents twice reaches for them in the same order.
    public static IReadOnlyList<FetchedDocument> Preferred(
        IReadOnlyList<FetchedDocument> documents,
        IReadOnlyList<string> listedSites) =>
    [
        .. documents
            .OrderBy(document => (int)Of(document, listedSites))
            .ThenByDescending(document => document.PublishedOn ?? DateOnly.MinValue),
    ];

    // A host is on a listed site where it is that site or a subdomain of it.
    // Matched on the label boundary rather than on the ending, because
    // "notreuters.com" ends with "reuters.com".
    static bool IsOn(string host, string site) =>
        host.Equals(site, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase);

    // ---- reading a document ----

    static bool Names(string text, IReadOnlyList<string> markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

    // The path's own segments, which is what says whether a page is addressed as
    // a symbol or as an article.
    public static IReadOnlyList<string> Segments(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? [.. uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)]
            : [];

    // The path as words, so a phrase can be read out of a slug. An article about
    // a prediction is addressed with the pairing inside its slug, as
    // /news/aapl-stock-price-prediction-where-155545121.html is.
    public static string Slug(string url) =>
        string.Join(' ', Segments(url)).Replace('-', ' ').Replace('_', ' ');
}
