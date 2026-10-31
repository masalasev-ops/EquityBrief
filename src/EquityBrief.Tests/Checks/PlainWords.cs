using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Releases;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// plain-words, 22.5, Tonight from 22.6 and the fundamentals card from 22.8. Each page section 15.23 names speaks in the
// reader's words: rendered, no card of it draws a term of the word list in its visible text outside a glossary and the
// tooltips, and every card's first visible element is its summary sentence. The code's own lists are held to section
// 15.23's tables word for word in both directions, every step each setup's rule stores to its row, and the headings to the
// chart of what is behind a stock.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public partial class PlainWords
{
    internal const string TermsTable = "The build's terms in the reader's words";

    internal const string StepsTable = "Each step a setup stores, in the reader's words";

    internal const string HeadingsTable = "What higher means under each heading";

    internal const string PagesTable = "The pages the plain-words check covers";

    internal static CheckReach Reach => new(
        "plain-words",
        ["docs/ARCHITECTURE.html", "fixtures/membership-2026-09-05"],
        [TermsTable, StepsTable, HeadingsTable, PagesTable]);

    // The pages the check covers, by the name section 15.23's last table gives each, the checkpoint it is covered from,
    // the card regions each is read from, and whether the page is covered whole, every card it draws being of one of them,
    // or is one card among others the check does not read, as the brief read is on the chart page.
    internal static readonly (string Page, string From, string[] Regions, bool Whole)[] Covered =
    [
        (BriefPage, "22.5", ["chart-page-brief"], false),
        (TonightPage, "22.6", ["one-minute", "buy-points", "close-to-buy", "setups", "family", "heavyweights", "quality", "improving", "words"], true),
        (FundamentalsPage, "22.8", ["chart-page-fundamentals", "fundamentals"], false),
        (PaperPage, "21.13", ["account", "paper-chart", "paper-combined", "paper-bought", "paper-variants", "paper-scorecard", "paper-yours"], true),
        (LoopPage, "21.13", ["loop-none", "loop-summary", "loop-pullback", "loop-breakout", "loop-drift", "loop-dip", "loop-heavyweight", "loop-quality", "loop-improving", "loop-decisions", "loop-alarm"], true),
        (LedgerPage, "21.13", ["ledger-none", "ledger-pullback", "ledger-breakout", "ledger-drift", "ledger-dip", "ledger-heavyweight", "ledger-quality", "ledger-improving", "ledger-fundamentals", "ledger-path", "reading-links", "release-answers"], true),
    ];

    internal const string PaperPage = "Paper trading";

    internal const string LoopPage = "The Loop page";

    internal const string LedgerPage = "The Ledger";

    internal const string BriefPage = "The chart page's brief read";

    internal const string TonightPage = "Tonight";

    internal const string FundamentalsPage = "The fundamentals card";

    // The card regions a covered page is read from.
    internal static string[] RegionsOf(string page) => Assert.Single(Covered, covered => covered.Page == page).Regions;

    // What the check finds wrong with a page drawn for a page it covers, read as that page's row says.
    internal static IReadOnlyList<string> FaultsOn(string covered, string page, IReadOnlyList<string> terms)
    {
        var row = Assert.Single(Covered, one => one.Page == covered);

        return Faults(page, row.Regions, terms, row.Whole);
    }

    // Every step each setup's rule stores, by its constants: the S&P 500's swing filter, breakout and drift; the parts the
    // S&P 400's and 600's families store, the fundamentals-first family's on every index; the dip's; and the steps the
    // market switches and the release reading add to a version.
    internal static readonly (string Setup, string[] Steps)[] Stored =
    [
        (SetupFamilies.Pullback, [.. SwingGates.Order, RuleRows.Floors, RuleRows.Profit, RuleRows.Cover]),
        (BreakoutRule.Name, [.. BreakoutRule.Order, RuleRows.Floors, RuleRows.Profit, RuleRows.Setup, MarketSwitches.IndexGate, MarketSwitches.VixGate]),
        (DriftRule.Name, [.. DriftRule.Order, RuleRows.Floors, RuleRows.Profit, RuleRows.Revenue, RuleRows.Setup, ReleaseTone.Gate, ReleaseTone.ReadStep, MarketSwitches.IndexGate, MarketSwitches.VixGate]),
        (FundamentalsRule.Name, [RuleRows.Market, RuleRows.Setup, RuleRows.Floors, RuleRows.Readings, RuleRows.Profit, RuleRows.Revenue, RuleRows.Margin, RuleRows.Cash, RuleRows.Trend]),
        (DipRule.Name, [.. DipRule.Gates]),
    ];

    static readonly Regex Tag = new(@"<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)(?<attributes>[^>]*)>", RegexOptions.CultureInvariant);

    static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    // A term as a whole word or phrase in any case, with its plural, a space and a hyphen read alike.
    internal static Regex Pattern(string term) => new(
        @"(?<![\w'’-])" + Regex.Escape(term).Replace(@"\ ", "[ -]", StringComparison.Ordinal) + @"(?:s|es)?(?![\w'’-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static bool Opens(Match tag) => tag.Groups["close"].Length == 0 && !Void.Contains(tag.Groups["name"].Value) && !tag.Value.EndsWith("/>", StringComparison.Ordinal);

    // A word of more than one letter written all in capitals.
    static bool Ticker(string word) => word.Length > 1 && word.All(letter => !char.IsLetter(letter) || char.IsUpper(letter));

    // Each element whose opening tag the test accepts, from its opening tag to the end of its closing tag, read by depth
    // over the markup's tags so an element of its own name nested inside it closes inside it.
    internal static IReadOnlyList<(int Start, int End)> Elements(string markup, Func<Match, bool> accepts)
    {
        var tags = Tag.Matches(markup);
        var found = new List<(int, int)>();
        var at = 0;

        while (at < tags.Count)
        {
            var tag = tags[at];

            if (Opens(tag) && accepts(tag))
            {
                var name = tag.Groups["name"].Value;
                var depth = 0;
                var closed = false;

                for (var next = at; next < tags.Count; next++)
                {
                    var other = tags[next];

                    if (!string.Equals(other.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase) || Void.Contains(name) || other.Value.EndsWith("/>", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    depth += other.Groups["close"].Length == 0 ? 1 : -1;

                    if (depth == 0)
                    {
                        found.Add((tag.Index, other.Index + other.Length));
                        at = next;
                        closed = true;
                        break;
                    }
                }

                if (!closed)
                {
                    throw new InvalidOperationException($"The <{name}> opened at {tag.Index} never closes, so what it holds cannot be read.");
                }
            }

            at++;
        }

        return found;
    }

    // A glossary or a tooltip, which a reader opens to read a term beside its meaning.
    static bool Aside(Match tag) =>
        Regex.IsMatch(tag.Groups["attributes"].Value, @"\brole=""tooltip""|\bdata-glossary\b|\bclass=""(?:[^""]*\s)?gloss(?:\s[^""]*)?""", RegexOptions.CultureInvariant);

    // The markup less every glossary and tooltip, each removed whole with what it holds.
    internal static string WithoutAsides(string markup)
    {
        var asides = Elements(markup, Aside);

        if (asides.Count == 0)
        {
            return markup;
        }

        var kept = new System.Text.StringBuilder();
        var from = 0;

        foreach (var (start, end) in asides.Where(aside => aside.Start >= 0).OrderBy(aside => aside.Start))
        {
            if (start < from)
            {
                continue;
            }

            kept.Append(markup, from, start - from);
            from = end;
        }

        return kept.Append(markup, from, markup.Length - from).ToString();
    }

    // The text a reader sees: scripts, styles and comments dropped, tags removed, entities read and space collapsed.
    internal static string Visible(string markup)
    {
        var text = Regex.Replace(markup, "<!--.*?-->|<script\\b.*?</script>|<style\\b.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        text = Regex.Replace(text, "<[^>]+>", " ");

        return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    }

    // What the check finds wrong with the cards a page draws for a region: a term of the list in a card's visible text
    // outside its glossary and tooltips, a card whose body opens on anything visible before its summary sentence, a card
    // with no summary sentence or an empty one, and a page drawing no card for the region.
    internal static IReadOnlyList<string> Faults(string page, string region, IReadOnlyList<string> terms) => Faults(page, [region], terms, whole: false);

    // The same over every card of the regions a page's row names, the page drawing at least one; on a page covered whole
    // every card must be of one of them, so a card added to it and to no row is a fault rather than a card the check never
    // reads, and on a page covered in part a card of another region is left unread.
    internal static IReadOnlyList<string> Faults(string page, IReadOnlyList<string> regions, IReadOnlyList<string> terms, bool whole = true)
    {
        var faults = new List<string>();
        var cards = Elements(page, tag => tag.Groups["name"].Value == "section" && Regex.IsMatch(tag.Groups["attributes"].Value, @"\bclass=""card(?:\s[^""]*)?""") && tag.Groups["attributes"].Value.Contains("data-card=\"", StringComparison.Ordinal));

        if (!cards.Any(card => regions.Contains(RegionOf(page[card.Start..card.End]), StringComparer.Ordinal)))
        {
            faults.Add($"No card of the region{(regions.Count == 1 ? string.Empty : "s")} '{string.Join("', '", regions)}' is drawn on the page.");
        }

        foreach (var (start, end) in cards)
        {
            var card = page[start..end];
            var region = RegionOf(card);

            if (!regions.Contains(region, StringComparer.Ordinal))
            {
                if (whole)
                {
                    faults.Add($"A card of the region '{region}' is drawn on the page, and the check reads no card of it.");
                }

                continue;
            }

            var said = Visible(WithoutAsides(card));

            foreach (var term in terms)
            {
                // A word written all in capitals is a ticker, which the reader's words leave as it stands, HELD among them.
                foreach (Match found in Pattern(term).Matches(said).Where(match => !Ticker(match.Value)))
                {
                    faults.Add($"'{term}' in the card '{region}': ...{said[Math.Max(0, found.Index - 40)..Math.Min(said.Length, found.Index + found.Length + 40)]}...");
                }
            }

            if (Elements(card, tag => tag.Groups["name"].Value == "div" && Regex.IsMatch(tag.Groups["attributes"].Value, @"\bclass=""card-b""")).FirstOrDefault() is not { End: > 0 } body)
            {
                faults.Add($"The card '{region}' draws no body.");
                continue;
            }

            var inside = card[body.Start..body.End];

            if (Elements(inside, tag => Regex.IsMatch(tag.Groups["attributes"].Value, @"(?<![\w-])data-summary(?![\w-])")).FirstOrDefault() is not { End: > 0 } summary)
            {
                faults.Add($"The card '{region}' draws no summary sentence.");
                continue;
            }

            if (Visible(WithoutAsides(inside[..summary.Start])).Length > 0)
            {
                faults.Add($"The card '{region}' draws '{Visible(WithoutAsides(inside[..summary.Start]))}' before its summary sentence.");
            }

            if (Visible(inside[summary.Start..summary.End]).Length == 0)
            {
                faults.Add($"The card '{region}' draws an empty summary sentence.");
            }
        }

        return faults;
    }

    // A card's region, as its opening tag names it.
    static string RegionOf(string card) => Regex.Match(card, @"^<section[^>]*\bdata-card=""([^""]*)""").Groups[1].Value;

    static IReadOnlyList<ArchitectureTable> Tables() => ArchitectureTables.In(File.ReadAllText(Repository.Architecture));

    static IReadOnlyList<IReadOnlyList<string>> Rows(string heading) =>
        Assert.Single(Tables(), table => table.Heading == heading).Body;

    internal static IReadOnlyList<string> Terms() => [.. Rows(TermsTable).Select(row => row[0])];

    [Fact]
    public void TheCodesListsAreSection1523sTablesWordForWordInBothDirections()
    {
        Assert.Equal(
            ReaderWords.Terms.Select(term => $"{term.Term}|{term.Words}"),
            Rows(TermsTable).Select(row => string.Join("|", row)));
        Assert.Equal(
            ReaderWords.Steps.Select(step => $"{step.Setup}|{step.Step}|{step.Passed}|{step.Missed}"),
            Rows(StepsTable).Select(row => string.Join("|", row)));
        Assert.Equal(
            ReaderWords.Headings.Select(heading => $"{heading.Heading}|{heading.Named}|{heading.Higher}"),
            Rows(HeadingsTable).Select(row => string.Join("|", row)));
        Assert.Equal(
            Covered.Select(page => $"{page.Page}|{page.From}"),
            Rows(PagesTable).Select(row => $"{row[0]}|{row[1]}"));

        // The steps passed and missed are said in words of their own, and a heading's name on a pick's card is its own.
        Assert.All(ReaderWords.Steps, step => Assert.NotEqual(step.Passed, step.Missed));
        Assert.Equal(ReaderWords.Headings.Count, ReaderWords.Headings.Select(heading => heading.Named).Distinct(StringComparer.Ordinal).Count());

        // The headings are the chart's, heading for heading, and no term or step is listed twice.
        Assert.Equal(LedgerHeadings.All.Select(heading => heading.Name), ReaderWords.Headings.Select(heading => heading.Heading));
        Assert.Equal(ReaderWords.Terms.Count, ReaderWords.Terms.Select(term => term.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ReaderWords.Steps.Count, ReaderWords.Steps.Select(step => (step.Setup, step.Step)).Distinct().Count());

        // No reader's words of the list draw a term of it.
        Assert.All(
            ReaderWords.Terms.Select(term => term.Words)
                .Concat(ReaderWords.Steps.SelectMany(step => new[] { step.Passed, step.Missed }))
                .Concat(ReaderWords.Headings.SelectMany(heading => new[] { heading.Named, heading.Higher })),
            words => Assert.DoesNotContain(ReaderWords.Terms, term => Pattern(term.Term).IsMatch(words)));
    }

    [Fact]
    public void EveryStepEachSetupsRuleStoresHasItsRowAndNoRowNamesAnotherStep()
    {
        var stored = Stored.SelectMany(setup => setup.Steps.Distinct(StringComparer.Ordinal).Select(step => (setup.Setup, step))).ToArray();

        Assert.Equal(
            stored.Order(),
            ReaderWords.Steps.Select(step => (step.Setup, step.Step)).Order());
        Assert.All(stored, step => Assert.NotNull(ReaderWords.Missed(step.Setup, step.step)));
        Assert.Null(ReaderWords.Missed(DipRule.Name, "a step no rule stores"));
        Assert.Null(ReaderWords.Missed(DipRule.Name, null));
    }

    // Over the fixture's whole night, each name's chart page draws its brief in the reader's words, opening on its summary.
    [Fact]
    public async Task EachPageItCoversSpeaksInTheReadersWordsOverTheFixture()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        using var host = new Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var terms = Terms();

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            var page = await client.GetStringAsync($"/screens/chart/{ticker}");

            Assert.Empty(FaultsOn(BriefPage, page, terms));

            // From 22.8 the fundamentals card beneath the brief, and the same card on the stock's full page.
            Assert.Empty(FaultsOn(FundamentalsPage, page, terms));
            Assert.Empty(FaultsOn(FundamentalsPage, await client.GetStringAsync($"/screens/name/{ticker}"), terms));
        }

        // From 22.6 Tonight over the fixture's night, every card of it in the reader's words and opening on its summary.
        Assert.Empty(FaultsOn(TonightPage, await client.GetStringAsync("/screens/tonight/2026-09-08"), terms));
    }

    // The brief drawn as the chart page draws its card, over constructed figures.
    static string Card(BriefView view) =>
        Cards.Computed("In brief", new MarkRenderer().BriefRead("XYZ", view), title: "XYZ in brief", stamp: Cards.Night(new DateOnly(2026, 10, 23)), id: "brief", region: "chart-page-brief");

    // The brief in every state it draws speaks in the reader's words and opens on its summary: listed under each setup
    // with its card's plan, or with no card stored; on no list with the levels plan, with no zone stored, or on an index
    // working no ladder; sized and not; each chip's state, one step short at every step each setup stores; the line of
    // what to watch for with each of its three; and every figure missing.
    [Fact]
    public void TheBriefSpeaksInTheReadersWordsInEveryStateItDraws()
    {
        var terms = Terms();
        var night = new DateOnly(2026, 10, 23);
        var account = new AccountSettings(100_000m, 1m, 0.5m);
        IReadOnlyList<PlanRow> levels =
        [
            new PlanRow(95m, 97m, PlanKind.Tranche, "buy on a touch, stop on a daily close below 92.00", true, "a touch", 92m),
            new PlanRow(92m, 92m, PlanKind.Stop, "stop for the 95.00 zone", true),
            new PlanRow(108m, 108m, PlanKind.Exit, "sell a third", true),
        ];

        DecisionCardRow CardOf(string family, decimal? target, decimal? trail = null, int? cap = 21) =>
            new("GSPC", night, family, "XYZ", 1, 100m, family == HeavyweightRule.Name ? null : 96m, target, "the live rule", "{}", "[]", null, Trail: trail, Cap: cap);

        var views = new List<BriefView>
        {
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), Reading.ReadSurface.PullbackListed(), account: account, report: new DateOnly(2026, 11, 5), moves: [-6.0, 8.0], sold: 250_000m)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(BreakoutRule.Name, CardOf(BreakoutRule.Name, null, trail: 3m, cap: 63)), account: account)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(DriftRule.Name, CardOf(DriftRule.Name, 110m, cap: 60)))),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(FundamentalsRule.Name, CardOf(FundamentalsRule.Name, 110m)))),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(DipRule.Name, CardOf(DipRule.Name, null, cap: 10), 5), account: account)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(HeavyweightRule.Name, CardOf(HeavyweightRule.Name, null, cap: null)), account: account)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), new BriefListed(DriftRule.Name, null))),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), levels: levels, account: account)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), levels: levels)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember())),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), ladder: false)),
            BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(bands: false))),
            BriefScreen.Of(Reading.ReadSurface.BriefOf() with { Latest = new Dictionary<string, double?>(), Business = null, HalfYear = null, Analysts = null }),
        };

        // Every chip's state, and one step short at every step each setup stores, each the closest setup in its summary.
        string[] states = [BriefStates.Listed, BriefStates.Passed, BriefStates.NotClose, BriefStates.NoReport, BriefStates.NotRead];

        views.Add(BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), standing: [.. states.Select(state => new BriefStanding(SetupFamilies.Pullback, state)), new BriefStanding(HeavyweightRule.Name, BriefStates.Held)])));
        views.Add(BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), standing: [new BriefStanding(HeavyweightRule.Name, BriefStates.NotHeld), new BriefStanding(SetupFamilies.Pullback, BriefStates.Passed)])));

        // From 22.13 the Fundamentals family's four states, its buy with the way in it reads, and each in its summary.
        foreach (var state in new[] { BriefStates.Bought, BriefStates.Held, BriefStates.Flagged, BriefStates.NotHeld })
        {
            views.Add(BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), standing: [new BriefStanding(ImprovingRule.Name, state)], account: account)));
        }

        foreach (var (setup, step, _, _) in ReaderWords.Steps)
        {
            views.Add(BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), standing: [new BriefStanding(setup, BriefStates.Short, step, 1)])));
        }

        Assert.All(views, view => Assert.Empty(Faults(Card(view), "chart-page-brief", terms)));
        Assert.True(views.Count >= 60, $"Drew {views.Count} brief(s), expected one a state and one a step at least.");

        // The steps are said in their own words, so a stock one step short names what it missed.
        var shortOfVolume = BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), standing: [new BriefStanding(DriftRule.Name, BriefStates.Short, DriftRule.Volume, 1)]));

        Assert.Contains("one step short: volume too light", Card(shortOfVolume), StringComparison.Ordinal);
    }

    // The sources 22.5 writes.
    static readonly string[] Sources =
    [
        "src/EquityBrief.Core/Cards/Management.cs",
        "src/EquityBrief.Api/Reading/BriefScreen.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Brief.cs",
        "src/EquityBrief.Web/Marks/ReaderWords.cs",
    ];

    // No source a rule's version pins is one the reader's words write, so no rule's version moves, the reader shown to find
    // a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheReadersWordsWrite()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(Sources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(Sources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    // The check fails a term planted in a card's visible text, its plural, a hyphen for its space and any case among them,
    // and a card whose first visible element is not its summary sentence; and passes the same term in a glossary, in a
    // tooltip and in an attribute, and a word the term only begins.
    [Fact]
    public void TheCheckFailsAPlantedTermAndACardNotOpeningOnItsSummaryAndPassesTheTermInAGlossaryOrATooltip()
    {
        var terms = Terms();
        var card = Card(BriefScreen.Of(Reading.ReadSurface.BriefOf(Reading.ReadSurface.BriefMember(), Reading.ReadSurface.PullbackListed())));
        const string Line = "<li data-line=\"trend\" data-figure=\"uptrend\">Trend: an uptrend.</li>";

        Assert.Contains(Line, card, StringComparison.Ordinal);
        Assert.Empty(Faults(card, "chart-page-brief", terms));

        string Planted(string words) => card.Replace(Line, Line.Replace("Trend: an uptrend.", words, StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.Contains(Faults(Planted("Trend: held above its low."), "chart-page-brief", terms), fault => fault.StartsWith("'held'", StringComparison.Ordinal));
        Assert.Contains(Faults(Planted("Sold after 21 sessions."), "chart-page-brief", terms), fault => fault.StartsWith("'session'", StringComparison.Ordinal));
        Assert.Contains(Faults(Planted("A Near-Miss tonight."), "chart-page-brief", terms), fault => fault.StartsWith("'near miss'", StringComparison.Ordinal));
        Assert.Contains(Faults(Planted("Its Reward To Risk is 2."), "chart-page-brief", terms), fault => fault.StartsWith("'reward to risk'", StringComparison.Ordinal));

        // A glossary, a tooltip and an attribute are where a term may stand, and a word it only begins is no term.
        Assert.Empty(Faults(Planted("Trend: <details class=\"gloss\"><summary>Words</summary><p>held: still above the report day's low</p></details>"), "chart-page-brief", terms));
        Assert.Empty(Faults(Planted("Trend: <span class=\"says\" role=\"tooltip\">a print is a report <b>held</b> in the store</span>"), "chart-page-brief", terms));
        Assert.Empty(Faults(Planted("<span title=\"the trade gate\">Trend: an uptrend.</span>"), "chart-page-brief", terms));
        Assert.Empty(Faults(Planted("Trend: withheld, marketed, printer, booking and gateway."), "chart-page-brief", terms));

        // Anything visible before the summary sentence, or no sentence marked as it, fails.
        Assert.Contains(
            Faults(card.Replace("<div class=\"brief-read\"", "<p>Before it</p><div class=\"brief-read\"", StringComparison.Ordinal), "chart-page-brief", terms),
            fault => fault.Contains("before its summary sentence", StringComparison.Ordinal));
        Assert.Contains(
            Faults(card.Replace(" data-summary>", ">", StringComparison.Ordinal), "chart-page-brief", terms),
            fault => fault.Contains("draws no summary sentence", StringComparison.Ordinal));
        Assert.Contains(Faults("<section class=\"card\"></section>", "chart-page-brief", terms), fault => fault.StartsWith("No card of the region", StringComparison.Ordinal));
    }
}
