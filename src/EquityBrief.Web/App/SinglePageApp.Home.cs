using System.Globalization;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;
using static System.FormattableString;

namespace EquityBrief.Web.App;

// One idea Home draws: its family, its stock and company, the sentence saying why in the reader's words, the three prices
// its plan set tonight, none where the plan sets none, whether its give-up price rises with the stock in place of a
// target, and what it risks against what it aims for; and from 22.18, for a plan selling on a rising give-up price with no
// target, the distance it rises behind the highest close at and its rule's winning trades; whether an exit changed on the
// Loop page moves its give-up price; for the Fundamentals family the setting its index's rule stands at; and from the 22.6
// correction of 2026-11-09 its setup's record over the long history.
public sealed record HomeIdea(string Family, string Ticker, string? Company, string Why, decimal? Buy, decimal? Stop, decimal? Target, bool Rises, decimal? RewardPerRisk, decimal? Trail = null, EquityBrief.Core.Cards.PaperWinners? Record = null, EquityBrief.Core.EntryCheck.EntryReading? EntryCheck = null, bool StopMoves = false, ImprovingSweepSetting? Setting = null, SetupLongRecord? LongRecord = null, ZoneBuy? Zone = null, AlternativePlan? Alternative = null)
{
    // From the operator's order of 2026-11-14 the price it takes profit at and where it came from, off its card.
    // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
    public TakeProfitView? TakeProfit { get; init; }
}

// How past ideas have done on Home: the day the rules first went live and the system's trades bought and sold since and how
// many made money; the live setups' accounts averaged, their return, the day every live rule took effect and the index's
// fund over the same days; and whether a live rule still holds fewer finished trades than a verdict needs.
// see: Every count of trades names its group and one function works the four groups
public sealed record HomePast(DateOnly Since, int Closed, int Won, double? Ours, double? Index, bool Early, string Fund = "", DateOnly? AccountSince = null)
{
    // From the operator's correction of 2026-11-24 how many running rules' accounts the figure averages.
    public int? Accounts { get; init; }
}

// Home as code draws it from the newest night: the index, the night drawn and whether a later night is still running; the
// share of the index's stocks above their 200-day average with the floor the rules buy at and whether the market check
// opened, or on the S&P 400 and 600, where each rule checks its own market level, how many of its rules are open to buy
// and no floor; the index's move on the day and the other two's; the VIX's move on the day and whether it stands above its
// median of the year before; tonight's ideas, the session they are bought at and why there are none; and past ideas.
public sealed record HomeView(
    UniverseChoice Reading,
    DateOnly Night,
    bool Running,
    double? Share,
    double? Floor,
    bool Open,
    double? Moved,
    IReadOnlyList<(string Name, double? Moved)> Others,
    double? VixMoved,
    bool? VixAbove,
    IReadOnlyList<HomeIdea> Ideas,
    int IdeasCount,
    string OpensOn,
    string? NoIdeas,
    HomePast? Past,
    int? RulesOpen = null,
    int? Rules = null)
{
    // The buys whose setup lost over the long history, in Tonight's order, drawn as watched cards after the ideas to buy.
    public IReadOnlyList<HomeIdea> Watching { get; init; } = [];

    // The monthly books' holdings bought at tonight's reshuffle, each with its book's family, in Tonight's order.
    public IReadOnlyList<(string Family, string Ticker)> Reshuffled { get; init; } = [];

    // From the 23.10 correction of 2026-11-13 the orders a plan placed on an earlier night still waiting for their price, which
    // the heading does not count and a line beneath it names.
    public IReadOnlyList<WaitingOrderView> StillWaiting { get; init; } = [];

    // The orders that left the waiting list in the last five trading days, and each family's revisions, which every pick's
    // tag names the one that placed it from.
    public IReadOnlyList<LeftOrderView> LeftOrders { get; init; } = [];

    // From 24.6 the AI stock picker's line: what it picked tonight and its live record per $1 risked beside random picks'.
    public JudgeView? Judge { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<RevisionView>> Revisions { get; init; } = new Dictionary<string, IReadOnlyList<RevisionView>>(StringComparer.Ordinal);

    // From the operator's order of 2026-11-14 the cards of the stocks a setup's rules met again while a trade holds them.
    public IReadOnlyList<DecisionCardView> HeldAgain { get; init; } = [];

    // From the operator's correction of 2026-11-23 each such stock's company, which a held stock's one-line row names.
    public IReadOnlyDictionary<string, string> HeldCompanies { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    // From the operator's word of 2026-11-17 the public copy's newest upload and attempt, none on the copy's own Home.
    public SiteUploadView? Upload { get; init; }

    // From 24.15 the newest night where Home is drawn for an earlier one, none for tonight's; every night the listings hold,
    // which the picks' night picker offers; and on a past night each card's line on what has happened since, by its family
    // and stock.
    public DateOnly? Newest { get; init; }

    public IReadOnlyList<DateOnly> Held { get; init; } = [];

    public IReadOnlyDictionary<string, string> Since { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

// The record of the public copy's uploads as the app's Home reads it, none where no run has written one, and the zone its
// instants are said in.
// see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
public sealed record SiteUploadView(EquityBrief.Core.Cloudflare.SiteUpload? Record, TimeZoneInfo Zone)
{
    // "The public copy was last uploaded at 00:58 ET on 12 Nov 2026, with the night of 11 Nov 2026." and, where it holds an
    // earlier night than the page or the newest attempt failed, a sentence saying so.
    public string Line(DateOnly night)
    {
        string When(DateTimeOffset instant) =>
            TimeZoneInfo.ConvertTime(instant, Zone).ToString("HH:mm", CultureInfo.InvariantCulture) + " ET on " + TimeZoneInfo.ConvertTime(instant, Zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        var said = Record?.Uploaded is { } uploaded
            ? "The public copy was last uploaded at " + When(uploaded) + (Record.Night is { } held ? ", with the night of " + Day(held) : string.Empty) + "."
            : "No upload of the public copy is recorded yet.";

        if (Record?.Uploaded is not null && Record.Night is { } carried && carried < night)
        {
            said += " It is behind this page, which shows the night of " + Day(night) + ".";
        }

        if (Record is { Failed: { } why } attempt)
        {
            said += " The newest attempt, at " + When(attempt.Attempted) + ", failed: " + why + ".";
        }

        return said;
    }

    public string State(DateOnly night) =>
        Record is { Failed: not null } ? "failed"
        : Record?.Uploaded is null ? "none"
        : Record.Night is { } carried && carried < night ? "behind"
        : "uploaded";
}

// Home, from 22.16: the landing page for a reader new to investing, the day, tonight's ideas, how past ideas have done and
// how the rules work, then the way to Tonight and the page's disclaimer. Every sentence is written here from templates over
// the newest night's figures; no model writes any of it, and Tonight is drawn as it was, one click away.
// see: Home is the landing page for a reader new to investing, drawn by code from the newest night's figures in plain words
public sealed partial class SinglePageApp
{
    // A day the index fell by at least this much, in percent, is a shaky one.
    public const double ShakyFall = 1.0;

    // A rule needs about this many finished trades for a fair verdict.
    public const int FairVerdictTrades = 300;

    public const string NoFigure = "No figure stored for today";

    public const string IdeasNote = "These are ideas found by fixed rules from prices and company filings, not advice. A rule's record is shown on its own page and is never a promise about any one stock.";

    public const string RisesLine = "The give-up price rises as the stock climbs, so a winner can keep running.";

    // Why there is no idea to buy on a night every pick is watched.
    public const string WatchedOnly = "Every pick tonight is watched, not offered as an idea to buy: each card below says why.";

    // What the Fundamentals family's card says in a give-up price's place: it sets none and sells on its filings; and a
    // monthly book's, which sells at a reshuffle.
    public const string FilingsWeaken = "its filings weaken";

    public const string ReshuffleDrops = "a monthly reshuffle drops it";

    // The line under a card's sell price, from its family's own exit rule: the Fundamentals family's filings and time limit,
    // from the 22.17 correction of 2026-11-09 with its figures on the read-only copy as on the app, a give-up price rising or
    // staying where it is; none where a plan holds no give-up price or an exit changed on the Loop page moves it.
    // see: The read-only copy draws each pick's record and the picks watched as the app does, and its check still refuses the method
    public static string? SellLine(HomeIdea idea, bool copying)
    {
        // On the operator's ruling of 2026-11-15 on the review of PR 699 a book's buy with its stop and take-profit placed sells
        // on them as well as by its own rule.
        // see: A book's pick draws the stop and the take-profit its exit places, worked from the close it was chosen at
        var opens = idea.Stop is null ? "No price is set in advance: it sells when " : "It also sells when ";

        if (idea.Family == ImprovingRule.Name)
        {
            var setting = idea.Setting ?? ImprovingGrid.Starting;

            return Invariant($"{opens}a new filing drops its score below {MarkRenderer.InEveryNine(setting.HoldInNine)},{(setting.SoldUnderAverage ? " on a close under its 200-day average," : string.Empty)} or after {setting.Sessions} trading days.");
        }

        if (Planless(idea.Family))
        {
            return opens + ReshuffleDrops + ".";
        }

        return idea.Rises || idea.Trail is not null ? RisesLine
            : idea.Stop is null || idea.StopMoves ? null
            : MarkRenderer.StaysWords;
    }

    // The lines of Home's idea cards that open mid-sentence, on a small letter or on punctuation, as a reader would read them. A
    // ticker or a company's name is no line, since a company may spell itself with a small letter.
    // see: A plan with a rising give-up price on Home draws no fixed target and one line, and no line on Home's cards starts mid-sentence
    public static IReadOnlyList<string> HomeLinesStartingMidSentence(string page) =>
        [.. System.Text.RegularExpressions.Regex.Matches(page, "<article class=\"home-idea.*?</article>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .SelectMany(card => System.Text.RegularExpressions.Regex.Matches(card.Value, "<(p|div)(?: class=\"([^\"]*)\")?[^>]*>((?:(?!</?(?:p|div)\\b).)*)</\\1>", System.Text.RegularExpressions.RegexOptions.Singleline))
            .Where(line => line.Groups[2].Value is not ("home-ticker" or "home-co"))
            .Select(line => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(line.Groups[3].Value, "<[^>]+>", string.Empty)).Trim())
            .Where(line => line.Length > 0 && (char.IsLower(line[0]) || char.IsPunctuation(line[0]) && line[0] is not ('(' or '"' or '\'')))];

    // On the operator's ruling of 2026-11-15 on the review of PR 699 each book's pick a page draws with no price it takes profit
    // at: a card of Home's with no profit row, and a row of Tonight's buys with no take-profit in its target's column.
    // see: A book's pick draws the stop and the take-profit its exit places, worked from the close it was chosen at
    public static IReadOnlyList<string> BookPicksWithNoProfitRow(string page) =>
    [
        .. System.Text.RegularExpressions.Regex.Matches(page, "<article class=\"home-idea[^\"]*\" data-family=\"(improving|quality|heavyweight)\" data-ticker=\"([^\"]+)\">.*?</article>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Where(card => !card.Value.Contains("<span>" + TakeProfitView.Label + "</span>", StringComparison.Ordinal) && !card.Value.Contains("<span>" + TakeProfitView.HalfLabel + "</span>", StringComparison.Ordinal))
            .Select(card => "Home " + card.Groups[1].Value + " " + card.Groups[2].Value),
        .. System.Text.RegularExpressions.Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-family=\"([a-z]+)\" data-place=\"[0-9]+\" data-planless>.*?</tr>", System.Text.RegularExpressions.RegexOptions.Singleline)
            .Where(row => !System.Text.RegularExpressions.Regex.IsMatch(row.Value, "data-target=\"[0-9]"))
            .Select(row => "Tonight " + row.Groups[2].Value + " " + row.Groups[1].Value),
    ];

    public const string FootLine = "Want every number, every rule and every change to one being tested?";

    // From 22.17 the foot's line on the read-only copy, which draws no version.
    public const string CopyFootLine = "Want every pick with its plan, and how each rule has done?";

    public const string Disclaimer = "Nothing on this site is financial advice. EquityBrief is a personal experiment: it tests fixed rules on paper, with no real money, and a rule's past results, on paper or over history, are no promise about what comes next. Make your own decisions, or ask a licensed adviser, before you invest.";

    // The family's tag and its sentence, in the reader's words.
    public static string IdeaTag(string family) => family switch
    {
        SetupFamilies.Pullback => "Bounce in an uptrend",
        BreakoutRule.Name => "New high",
        DriftRule.Name => "Strong earnings",
        ImprovingRule.Name => "Getting stronger as a business",
        DipRule.Name => "Quick rebound",
        HeavyweightRule.Name => "Leader of its sector",
        QualityRule.Name => "Strong business at a fair price",
        _ => "Idea",
    };

    // Tickers in their order, the stocks of one family together with its tag after them: "CVS and ODFL (Getting stronger as a
    // business), NUE (Quick rebound)".
    static string Grouped(IEnumerable<(string Family, string Ticker)> named)
    {
        static string Listed(IReadOnlyList<string> names) =>
            names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

        var groups = new List<(string Tag, List<string> Tickers)>();

        foreach (var (family, ticker) in named)
        {
            if (groups.Count > 0 && groups[^1].Tag == IdeaTag(family))
            {
                groups[^1].Tickers.Add(ticker);
            }
            else
            {
                groups.Add((IdeaTag(family), [ticker]));
            }
        }

        return string.Join(", ", groups.Select(group => Listed(group.Tickers) + " (" + group.Tag + ")"));
    }

    // Every one of tonight's picks, split into the ideas to buy and the picks whose setup's record over the long history lost,
    // each group in the order given; none is left out.
    // see: Home draws every pick as a card in Tonight's order, the ideas to buy first and the watched after them with each card saying why
    public static (HomeIdea[] Buy, HomeIdea[] Watch) Split(IReadOnlyList<HomeIdea> picks) =>
        ([.. picks.Where(pick => pick.LongRecord is not { Lost: true })], [.. picks.Where(pick => pick.LongRecord is { Lost: true })]);

    // The line above every card: how each kind of pick is tested, and which kinds are ideas to buy and which are watched.
    // see: Home says in plain words why a pick is an idea to buy or watched and how it was tested, too few past picks said in the operator's words
    public static string SplitLine(UniverseChoice reading, bool watching) =>
        Invariant($"We test every kind of pick against simply buying other {reading.Name} stocks on the same days. Kinds that beat it, or are too new to test, are ideas to buy. Kinds that lost to it are {(watching ? "shown below as watched, followed" : "watched and followed")} on paper only.");

    // The heading of the picks watched, above their cards: how many, and that the rules picked them but their kind of pick
    // lost the test; none on a night no pick is watched.
    // see: Home says in plain words why a pick is an idea to buy or watched and how it was tested, too few past picks said in the operator's words
    public static string? WatchingLine(IReadOnlyList<HomeIdea> watching, UniverseChoice reading)
    {
        if (watching.Count == 0)
        {
            return null;
        }

        var kinds = watching.Select(idea => idea.Family).Distinct(StringComparer.Ordinal).Count();
        var since = watching[0].LongRecord?.From ?? EquityBrief.Core.History.LongLedger.StandingFrom.Year;
        var why = Invariant($"The rules picked {(watching.Count == 1 ? "it" : "these")}, but {(kinds == 1 ? "this kind of pick has" : "these kinds of pick have")} done worse than simply buying other {reading.Name} stocks since {since}.");

        return Invariant($"<p class=\"home-watch\" data-watching=\"{watching.Count}\"><b>Watching, not buying: {watching.Count} {(watching.Count == 1 ? "pick" : "picks")} tonight.</b> {Escaped(why)}</p>");
    }

    // The labels of a watched card's two sentences: what qualified it, and why it is not bought.
    public const string PickedLabel = "Why the rule picked it:";

    public const string NotBuyingLabel = "Why we're not buying it:";

    // Why a watched pick is not bought: the years its kind of pick was tested over, that it did worse than buying the index's
    // other stocks on the same days with the same sell rules and by how much for each dollar put at risk.
    // see: Home says in plain words why a pick is an idea to buy or watched and how it was tested, too few past picks said in the operator's words
    public static string NotBuyingLine(HomeIdea idea, UniverseChoice reading) =>
        idea.LongRecord is { Lost: true, EdgePerPick: { } edge } record
            ? Invariant($"We tested this kind of pick on {reading.Name} stocks from {record.From} to {record.Through}. It did worse than simply buying other {reading.Name} stocks on the same days with the same sell rules: {Worse(edge)} for every $1 put at risk. So we follow it on paper instead of suggesting it.")
            : Invariant($"This kind of pick did worse than simply buying other {reading.Name} stocks on the same days with the same sell rules, so we follow it on paper instead of suggesting it.");

    // How far a kind of pick fell short for each dollar put at risk, in cents below a dollar.
    public static string Worse(double edge) => Cents(edge, "worse");

    // The line opening a pick's card, saying whether it is an idea to buy or watched, and for an idea why, from its setup's
    // record over the long history: one that lost is watched, its reasons in the lines beneath; one that made money, holds too
    // few picks, reaches back too few years or is unread is an idea to buy.
    // see: Home says in plain words why a pick is an idea to buy or watched and how it was tested, too few past picks said in the operator's words
    // From the operator's order of 2026-11-14, item 5, in their words, for a kind of pick whose record cannot be measured yet.
    public const string TooFewToMeasure = "Too few past picks of this kind to measure yet.";

    public static string VerdictLine(HomeIdea idea, UniverseChoice reading)
    {
        var record = idea.LongRecord;
        var (verdict, head, why) = record switch
        {
            { Lost: true } => ("watch", "Watching, not buying.", (string?)null),
            { Made: true } => ("buy", "An idea to buy.", Invariant($"We tested this kind of pick on {reading.Name} stocks from {record.From} to {record.Through}, and it did better than buying other {reading.Name} stocks on the same days with the same sell rules.")),
            { Short: true } => ("buy", "An idea to buy.", TooFewToMeasure),
            { ReachesBack: not null } => ("buy", "An idea to buy.", TooFewToMeasure),
            _ => ("buy", "An idea to buy.", TooFewToMeasure),
        };

        return Invariant($"<p class=\"home-verdict\" data-verdict=\"{verdict}\"><b>{Escaped(head)}</b>{(why is null ? string.Empty : " " + Escaped(why))}</p>");
    }

    // The monthly books' buys at tonight's reshuffle, named apart from the new picks: "The monthly books reshuffled tonight
    // and also buy 3: XOM and CVX (Leader of its sector), AAPL (Strong business at a fair price). These come from the
    // reshuffle, not from tonight's new picks."; none on a night no book buys.
    // see: On a night the monthly books reshuffle Home names their buys apart from the new picks and says they come from the reshuffle
    public static string? ReshuffleLine(IReadOnlyList<(string Family, string Ticker)> reshuffled) =>
        reshuffled.Count == 0
            ? null
            : Invariant($"The monthly books reshuffled tonight and also buy {reshuffled.Count}: {Grouped(reshuffled)}. {(reshuffled.Count == 1 ? "It comes" : "These come")} from the reshuffle, not from tonight's new picks.");

    // A family whose buys sell on no price set in advance: the Fundamentals family's on its filings and a monthly book's at a
    // reshuffle, which draw How it sells in a plan's place.
    public static bool Planless(string family) => family is ImprovingRule.Name or HeavyweightRule.Name or QualityRule.Name;

    // Which headline the day reads: weak where no rule buys, shaky where the rules buy after a fall of a percent or more,
    // mixed where some of an S&P 400's or 600's rules buy and healthy where all do.
    public static string HomeMarket(HomeView view)
    {
        if (view.Rules is { } rules && view.RulesOpen is { } open)
        {
            return open == 0 ? "weak" : view.Moved is { } fell && fell <= -ShakyFall ? "shaky" : open < rules ? "mixed" : "healthy";
        }

        var buying = view.Share is { } share && view.Floor is { } floor ? share >= floor : view.Open;

        return !buying ? "weak" : view.Moved is { } moved && moved <= -ShakyFall ? "shaky" : "healthy";
    }

    // The headline code chooses: a night still running says so, and otherwise the market's word.
    public static string HomeHeadline(HomeView view) =>
        view.Running
            ? "Tonight's run hasn't finished yet. Showing " + view.Night.ToString("dddd", CultureInfo.InvariantCulture) + "'s results."
            : HomeMarket(view) switch
            {
                "weak" => "A weak market: our rules are staying out tonight.",
                "shaky" => "A shaky day, but still a market our rules will buy in.",
                "mixed" => "A mixed market: some of our rules are buying.",
                _ => "A healthy market: our rules are buying.",
            };

    // The line saying how many of an S&P 400's or 600's rules are open to buy.
    static string OpenLine(int open, int rules) => Invariant($"Each rule here checks its own market level; tonight {open} of {rules} are open to buy.");

    // The day in two sentences: how the index moved and whether investors got more nervous, then how many stocks are in
    // long-term uptrends against the share the rules need.
    public static string HomeSummary(HomeView view)
    {
        var said = new StringBuilder();

        said.Append(view.Moved is { } moved
            ? Math.Round(moved, 1) switch
            {
                > 0 => Invariant($"The {view.Reading.Name} rose {moved:0.0}% today"),
                < 0 => Invariant($"The {view.Reading.Name} fell {-moved:0.0}% today"),
                _ => Invariant($"The {view.Reading.Name} was flat today"),
            }
            : Invariant($"No close of the {view.Reading.Name} is stored for today"));
        said.Append(view.Moved is null ? string.Empty
            : view.VixMoved is > 0 ? ", and investors got more nervous"
            : view.VixMoved is < 0 ? ", and investors were calmer"
            : string.Empty);
        said.Append(". ");

        if (view.Share is { } share && view.Floor is { } floor)
        {
            said.Append(share >= 0.5 ? "Most stocks are"
                : share >= floor ? Invariant($"{Percent(share)} of stocks are")
                : Invariant($"Fewer than {Percent(floor)} of stocks are"));
            said.Append(Invariant($" in long-term uptrends, and the rules buy only when at least {Percent(floor)} are."));
        }
        else if (view.Share is { } own && view.Rules is { } rules && view.RulesOpen is { } open)
        {
            said.Append(Invariant($"{(own >= 0.5 ? "Most stocks are" : Percent(own) + " of stocks are")} in long-term uptrends. {OpenLine(open, rules)}"));
        }
        else
        {
            said.Append("No share of stocks in long-term uptrends is stored for today.");
        }

        return said.ToString();
    }

    static string Percent(double share) => Invariant($"{Math.Round(share * 100):0}%");

    static string Moved(string name, double moved) =>
        Invariant($"{name} {(Math.Round(moved, 1) >= 0 ? "up" : "down")} {Math.Abs(moved):0.0}%");

    static string Dollars(decimal price) => "$" + price.ToString("#,##0.00", CultureInfo.InvariantCulture);

    // A link to Tonight on the index read, at a family's card where one is named and at a stock's row, opened, where one is.
    static string TonightHref(UniverseChoice reading, string? setup = null, string? name = null) => TonightHref(reading.Word, setup, name);

    public static string TonightHref(string word, string? setup, string? name)
    {
        var query = new List<string>();

        if (word != Universes.Large.Word)
        {
            query.Add(Universes.Query + "=" + word);
        }

        if (setup is not null)
        {
            query.Add(HomeSetupKey + "=" + setup);
        }

        if (name is not null)
        {
            query.Add(NameKey + "=" + Uri.EscapeDataString(name));
        }

        return TonightRoute + (query.Count > 0 ? "?" + string.Join("&amp;", query) : string.Empty);
    }

    // The key a link to Tonight names a stock's row under, which Tonight opens and scrolls to once drawn.
    public const string NameKey = "name";

    // The key a link to Tonight names a family's card under, which Tonight scrolls to once drawn.
    public const string HomeSetupKey = "setup";

    public string HomeRegion(MarkRenderer marks, HomeView view, string selector)
    {
        using var tags = RevisionTags.Use(view.Reading.Word, marks.Copying ? new Dictionary<string, IReadOnlyList<RevisionView>>(StringComparer.Ordinal) : view.Revisions);
        var region = new StringBuilder();
        var heading = "Home: " + view.Reading.Titled;
        var stamp = Cards.Night(view.Night);

        region.Append(Invariant($"<section class=\"home\" data-universe=\"{view.Reading.Word}\" data-night=\"{Cards.Day(view.Night)}\" data-running=\"{(view.Running ? "true" : "false")}\">"));
        region.Append(Cards.Masthead(heading, $"<span class=\"m-screen\">{Escaped(heading)}</span>", Invariant($"Night of {Cards.Day(view.Night)}, computed after the close")));

        // The public copy's newest upload, beside the masthead on the app's Home alone.
        // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
        if (view.Upload is { } upload)
        {
            var state = upload.State(view.Night);

            region.Append(Invariant($"<p class=\"site-upload{(state is "failed" or "behind" ? " degraded" : string.Empty)}\" data-upload=\"{state}\">{Escaped(upload.Line(view.Night))}</p>"));
        }

        region.Append(selector);

        // The day.
        region.Append(Invariant($"<section class=\"card home-day\" data-headline=\"{(view.Running ? "running" : HomeMarket(view))}\"><div class=\"card-h\"><div><div class=\"lbl\">{Escaped(view.Night.ToString("dddd d MMM", CultureInfo.InvariantCulture))} · after the market closed</div><h2>{Escaped(HomeHeadline(view))}</h2></div><div>{stamp}</div></div><div class=\"card-b\">"));
        region.Append(Invariant($"<p class=\"card-summary\">{Escaped(HomeSummary(view))}</p>"));

        if (view.Share is { } share && view.Floor is { } floor)
        {
            var filled = Math.Clamp(share, 0, 1) * 100;
            var marked = Math.Clamp(floor, 0, 1) * 100;

            region.Append(Invariant($"<div class=\"market-bar\"><svg viewBox=\"0 0 100 6\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{Percent(share)} above their 200-day average; the rules need {Percent(floor)}\"><rect class=\"bar-track\" x=\"0\" y=\"1\" width=\"100\" height=\"4\" rx=\"2\"/><rect class=\"bar-fill{(share >= floor ? " open" : " closed")}\" x=\"0\" y=\"1\" width=\"{filled.ToString("0.##", CultureInfo.InvariantCulture)}\" height=\"4\" rx=\"2\"/><rect class=\"bar-floor\" x=\"{marked.ToString("0.##", CultureInfo.InvariantCulture)}\" y=\"0\" width=\"0.6\" height=\"6\"/></svg><p class=\"bar-labels\"><span>0%</span><span>needs {Percent(floor)}</span><span>100%</span></p></div>"));
        }
        else if (view.Share is { } own && view.Rules is not null)
        {
            var filled = Math.Clamp(own, 0, 1) * 100;

            region.Append(Invariant($"<div class=\"market-bar\"><svg viewBox=\"0 0 100 6\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{Percent(own)} above their 200-day average\"><rect class=\"bar-track\" x=\"0\" y=\"1\" width=\"100\" height=\"4\" rx=\"2\"/><rect class=\"bar-fill{(view.RulesOpen > 0 ? " open" : " closed")}\" x=\"0\" y=\"1\" width=\"{filled.ToString("0.##", CultureInfo.InvariantCulture)}\" height=\"4\" rx=\"2\"/></svg><p class=\"bar-labels\"><span>0%</span><span>100%</span></p></div>"));
        }

        region.Append("<div class=\"home-signals\">");
        region.Append(view.Share is { } trend && view.Floor is { } needs
            ? Signal("trend", "Long-term trend", trend >= needs ? "good" : "bad", trend >= needs ? "Healthy" : "Weak", Invariant($"{Percent(trend)} of these companies trade above their 200-day average. The rules buy only above {Percent(needs)}."))
            : view.Share is { } held && view.Rules is { } rules && view.RulesOpen is { } open
            ? Signal("trend", "Long-term trend", open == rules ? "good" : open > 0 ? "care" : "bad", open == rules ? "Healthy" : open > 0 ? "Mixed" : "Weak", Invariant($"{Percent(held)} of these companies trade above their 200-day average. {OpenLine(open, rules)}"))
            : Signal("trend", "Long-term trend", "care", "No figure", NoFigure));
        region.Append(view.VixAbove is { } above
            ? Signal("vix", "Nervousness (the VIX)", above ? "care" : "good", above ? "Higher than usual" : "Calmer than usual", above ? "The market's fear gauge is above its typical level for the past year. Expect bigger swings." : "The market's fear gauge is below its typical level for the past year.")
            : Signal("vix", "Nervousness (the VIX)", "care", "No figure", NoFigure));
        var others = view.Others.Where(other => other.Moved is not null).Select(other => Moved(other.Name, other.Moved!.Value)).ToArray();
        region.Append(view.Moved is { } today
            ? Signal("today", "Today", Math.Round(today, 1) >= 0 ? "good" : "bad", Moved(view.Reading.Name, today), others.Length > 0 ? string.Join(" and ", others) + "." : NoFigure, plainWord: true)
            : Signal("today", "Today", "care", "No figure", NoFigure));
        region.Append("</div></div></section>");

        // Tonight's ideas, those bought at the next session's open and those bought only on a dip counted apart.
        var title = IdeasTitle(view);
        var onDip = view.Ideas.Count(idea => idea.Zone is not null);

        // From 24.15 the same night picker Tonight uses, and on a past night a banner saying whose picks these are.
        // see: Home shows a past night's picks as they were listed with what has happened since, and tonight's as before
        var picker = marks.Copying ? string.Empty : Cards.NightPicker(view.Night, view.Held, HomeNightRoute, HomeRoute);

        region.Append(Invariant($"<section class=\"card\" id=\"ideas\" data-ideas=\"{view.IdeasCount}\" data-at-open=\"{view.IdeasCount - onDip}\" data-on-dip=\"{onDip}\"><div class=\"card-h\"><div><div class=\"lbl\">Tonight's ideas</div><h2>{Escaped(title)}</h2></div><div>{stamp}{picker}</div></div><div class=\"card-b\">"));

        if (view.Newest is not null)
        {
            region.Append(Invariant($"<p class=\"home-past-night\" data-past-night=\"{Cards.Day(view.Night)}\">{Escaped(PastNightWords(view.Night))} <a href=\"{HomeRoute}\">Back to tonight</a></p>"));
        }

        // Every pick is a card, the ideas to buy first and the picks watched after them, each in Tonight's order; the heading
        // counts the ideas to buy, and the line above the cards says what splits them.
        // see: Home draws every pick as a card in Tonight's order, the ideas to buy first and the watched after them with each card saying why
        if (view.IdeasCount == 0)
        {
            region.Append(Invariant($"<p class=\"card-summary\" data-ideas=\"none\">No new ideas tonight. {Escaped(view.Watching.Count > 0 ? WatchedOnly : view.NoIdeas ?? "Nothing qualified tonight.")}</p>"));
        }
        else
        {
            // From the 23.10 correction of 2026-11-13, in the operator's words, since a plan may sell in parts or on its time limit.
            region.Append("<p class=\"card-summary\">Each idea comes with what to pay, where to give up if it goes wrong, and how it sells.</p>");
        }

        if (view.IdeasCount + view.Watching.Count > 0)
        {
            region.Append(Invariant($"<p class=\"home-split\">{Escaped(SplitLine(view.Reading, view.Watching.Count > 0))}</p>"));
        }

        // From the operator's orders of 2026-11-24, each setup present tonight under one header saying once what its cards share,
        // its ideas a carousel beneath it; from their correction of that evening one setup at a time, chosen from a row of chips.
        // see: Home draws each setup's ideas under one header saying once what they share, its cards a carousel beneath it
        // see: Home's ideas show one setup at a time, chosen from a row of chips each with its setup's colour and count
        region.Append(SetupSwitcher(view, marks.Copying));

        // From the operator's order of 2026-11-24, item 10, the way to every idea's full plan under the rules' ideas, never read
        // as the AI stock picker's picks.
        if (view.IdeasCount + view.Watching.Count > 0)
        {
            region.Append(Invariant($"<p class=\"home-all\" style=\"margin:12px 0 0\"><a href=\"{TonightHref(view.Reading)}\">{Escaped(AllIdeasLink(view.IdeasCount + view.Watching.Count))}</a></p>"));
        }

        // The orders earlier nights placed still waiting for their price, under tonight's ideas: cards nearest their buy price
        // first, in no count of tonight's buys.
        // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
        // From the operator's correction of 2026-11-23 a stock met again while its order waits is drawn once, on that order's card
        // with the mark; one held on a bought trade is a row of its own beneath.
        // see: On Home a stock its setup's rules met again is drawn once, on its waiting order's card or as a bought trade's one-line row
        var (waitingAgain, heldAgain) = MetAgainOf(view);

        region.Append(MarkRenderer.WaitingCarousel(view.StillWaiting, view.Reading.Name, view.Night, view.LeftOrders, marks.Copying ? null : waitingAgain));

        // From 24.6 the AI stock picker's one line, its own account and never one of the rules' buys; from 24.15 on a past night that
        // night's picks, and none where the judge did not run that night.
        if (view.Judge is { } judge && (view.Newest is null || judge.Night == view.Night || judge.AcrossNight?.Night == view.Night))
        {
            region.Append(JudgeHomeLine(judge, view.Newest is not null));
        }

        if (!marks.Copying)
        {
            region.Append(HeldAgainRows(heldAgain, view.Reading, view.HeldCompanies));
        }

        region.Append(ReshuffleLine(view.Reshuffled) is { } reshuffle ? Invariant($"<p class=\"home-more home-reshuffle\" data-reshuffled=\"{view.Reshuffled.Count}\">{Escaped(reshuffle)}</p>") : string.Empty);

        region.Append(Invariant($"<p class=\"home-note\">{Escaped(IdeasNote)}</p>"));
        region.Append("</div></section>");

        // How past ideas have done.
        region.Append("<section class=\"card\" id=\"past\"><div class=\"card-h\"><div><div class=\"lbl\">How it's doing</div><h2>How past ideas have done</h2></div></div><div class=\"card-b home-results\">");

        if (view.Past is { } past)
        {
            // The live setups' accounts averaged since today's rules all took effect, with the index's fund over the same days;
            // the system's every version apart from it and labelled.
            // see: Home's paper account counts since today's rules all took effect, and the system's every version stands apart and labelled
            region.Append(Invariant($"<div><p class=\"card-summary\" data-account-since=\"{(past.AccountSince is { } from ? Cards.Day(from) : "none")}\">{Escaped(AccountLine(past))}</p>"));
            region.Append(past.Early ? Invariant($"<p style=\"margin:0;color:var(--soft);font-size:14px\">Still early: each rule needs about {FairVerdictTrades} finished trades for a fair verdict.</p>") : string.Empty);
            region.Append(Invariant($"<p style=\"margin:10px 0 0\"><a href=\"{PicksRoute}{(view.Reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + view.Reading.Word)}\">See every past idea</a>, or the rules as they stand now on <a href=\"{PaperRoute}{(view.Reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + view.Reading.Word)}\">Paper trading</a></p></div>"));

            var widest = Math.Max(Math.Abs(past.Ours ?? 0), Math.Abs(past.Index ?? 0));

            region.Append("<div>");
            region.Append(Bar("ours", AveragedLabel(past), past.Ours, widest));
            region.Append(Bar("index", Invariant($"The index's fund, {past.Fund}, over the same days"), past.Index, widest));
            region.Append("</div>");
            region.Append(Invariant($"<p class=\"home-system\" data-system-closed=\"{past.Closed}\" data-made-money=\"{past.Won}\">{Escaped(SystemLine(past))}</p>"));
        }
        else
        {
            region.Append("<div><p class=\"card-summary\" data-past=\"none\">No idea has been bought on paper on this index yet.</p></div>");
        }

        region.Append("</div></section>");

        // How it works.
        region.Append("<section class=\"card\" id=\"how\"><div class=\"card-h\"><div><div class=\"lbl\">How it works</div><h2>The ideas the rules look for, in one picture each</h2></div></div><div class=\"card-b home-learn\">");
        region.Append(Invariant($"<a href=\"{TonightHref(view.Reading, SetupFamilies.Pullback)}\"><svg viewBox=\"0 0 200 56\" aria-hidden=\"true\"><polyline class=\"ln pullback\" points=\"0,50 30,38 60,24 85,14 105,30 120,36 135,28 165,16 200,6\"></polyline><line class=\"lvl\" x1=\"0\" y1=\"36\" x2=\"200\" y2=\"36\"></line></svg><b>Bounce in an uptrend</b><span>A rising stock dips to a price where buyers stepped in before.</span></a>"));
        region.Append(Invariant($"<a href=\"{TonightHref(view.Reading, BreakoutRule.Name)}\"><svg viewBox=\"0 0 200 56\" aria-hidden=\"true\"><polyline class=\"ln breakout\" points=\"0,34 25,28 50,35 75,29 100,33 125,28 150,31 170,16 200,6\"></polyline><line class=\"lvl\" x1=\"0\" y1=\"26\" x2=\"200\" y2=\"26\"></line></svg><b>New high</b><span>A stock breaks above months of ceiling on heavy buying.</span></a>"));
        region.Append(Invariant($"<a href=\"{TonightHref(view.Reading, DriftRule.Name)}\"><svg viewBox=\"0 0 200 56\" aria-hidden=\"true\"><polyline class=\"ln drift\" points=\"0,46 40,44 80,47 95,22 120,20 150,15 175,11 200,8\"></polyline><line class=\"lvl\" x1=\"88\" y1=\"2\" x2=\"88\" y2=\"54\"></line></svg><b>Strong earnings</b><span>A company beats expectations and the price keeps drifting up.</span></a>"));
        region.Append(Invariant($"<a href=\"{TonightHref(view.Reading, ImprovingRule.Name)}\"><svg viewBox=\"0 0 200 56\" aria-hidden=\"true\"><rect class=\"bar\" x=\"10\" y=\"34\" width=\"26\" height=\"20\" opacity=\".45\"></rect><rect class=\"bar\" x=\"52\" y=\"26\" width=\"26\" height=\"28\" opacity=\".6\"></rect><rect class=\"bar\" x=\"94\" y=\"18\" width=\"26\" height=\"36\" opacity=\".8\"></rect><rect class=\"bar\" x=\"136\" y=\"8\" width=\"26\" height=\"46\"></rect></svg><b>Getting stronger as a business</b><span>Its official filings show sales, profits and finances improving.</span></a>"));
        region.Append("</div></section>");

        // The way to Tonight, and the disclaimer beneath everything.
        region.Append(Invariant($"<div class=\"home-foot\"><span>{Escaped(marks.Copying ? CopyFootLine : FootLine)}</span><a class=\"go\" href=\"{TonightHref(view.Reading)}\">Open the full Tonight page</a></div>"));
        region.Append(Invariant($"<p class=\"home-disclaimer\">{Escaped(Disclaimer)}</p>"));
        region.Append("</section>");

        // see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
        return ReaderText.Revisions(region.ToString());
    }

    // On the operator's answers of 2026-11-16, item 1a: tonight's ideas counted apart, those bought at the next session's open,
    // named from the calendar, and those bought only on a dip; as it read before where tonight holds none.
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
    public static string IdeasTitle(HomeView view)
    {
        var onDip = view.Ideas.Count(idea => idea.Zone is not null);

        return MarkRenderer.ToBuyCounted(view.IdeasCount - onDip, onDip, view.Night) is { } counted
            ? "Tonight's ideas: " + counted
            : Invariant($"Tonight's ideas: {view.IdeasCount} {(view.IdeasCount == 1 ? "stock" : "stocks")} to buy {view.OpensOn} morning");
    }

    // From the operator's correction of 2026-11-23 the cards of the stocks met again tonight split in two: those whose earlier
    // order still waits, which their order's card in the carousel carries, and the rest, each a row of its own.
    // see: On Home a stock its setup's rules met again is drawn once, on its waiting order's card or as a bought trade's one-line row
    public static (IReadOnlyList<DecisionCardView> Waiting, IReadOnlyList<DecisionCardView> Held) MetAgainOf(HomeView view)
    {
        DecisionCardView[] waiting = [.. view.HeldAgain.Where(card => view.StillWaiting.Any(order => MarkRenderer.AgainOf(order, [card]) is not null))];
        DecisionCardView[] held = [.. view.HeldAgain.Where(card => card.HeldAgain is not null && !waiting.Contains(card))];

        return (waiting, held);
    }

    // The rows of the stocks met again while a trade holds them: the stock and its company, the setup and the day it bought
    // it, "Already held, not bought again" and the way to its trade on Paper trading; the heading counts them, and nothing is
    // drawn where none is held.
    public static string HeldAgainRows(IReadOnlyList<DecisionCardView> cards, UniverseChoice reading, IReadOnlyDictionary<string, string> companies)
    {
        if (cards.Count == 0)
        {
            return string.Empty;
        }

        var heading = cards.All(card => card.HeldAgain is { Bought: true }) ? HeldAgainView.Group : HeldAgainView.NoteGroup;
        var paper = PaperRoute + (reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + reading.Word);
        var body = new StringBuilder(Invariant($"<div class=\"held-again held-rows\" data-held-again=\"{cards.Count}\"><h5>{Escaped(heading)} ({cards.Count})</h5><ul class=\"held-list\">"));

        foreach (var card in cards)
        {
            var held = card.HeldAgain!;
            var setup = SetupFamilies.Named(held.Family)?.Label ?? held.Family;
            var when = held.Bought
                ? (held.BoughtOn ?? held.Night) is { } bought ? "bought " + bought.ToString("d MMM", CultureInfo.InvariantCulture) : "bought on an earlier night"
                : held.Night is { } placed ? "order placed " + placed.ToString("d MMM", CultureInfo.InvariantCulture) : "order placed on an earlier night";

            body.Append(Invariant($"<li class=\"held-row\" data-ticker=\"{Escaped(card.Ticker)}\" data-family=\"{Escaped(card.Family)}\" data-held-by=\"{Escaped(held.Family)}\" data-bought=\"{(held.Bought ? "true" : "false")}\"><a class=\"home-ticker name-link\" href=\"#/chart/{Uri.EscapeDataString(card.Ticker)}\">{Escaped(card.Ticker)}</a>"));
            body.Append(companies.TryGetValue(card.Ticker, out var company) ? Invariant($"<span class=\"held-co\">{Escaped(company)}</span>") : string.Empty);
            body.Append(Invariant($"<span class=\"held-by\">{Escaped(setup)}, {Escaped(when)}</span><b class=\"held-label\">{Escaped(held.Labelled)}</b><a class=\"held-go\" href=\"{paper}\">See its trade on Paper trading</a></li>"));
        }

        return body.Append("</ul></div>").ToString();
    }

    // One of the day's three tiles: a dot and a word, so colour is never the only sign, and the line saying what it means.
    static string Signal(string key, string label, string tone, string word, string meaning, bool plainWord = false) =>
        Invariant($"<div class=\"home-signal\" data-signal=\"{key}\" data-tone=\"{tone}\"><span class=\"st-label\"><span class=\"home-dot {tone}\" aria-hidden=\"true\"></span>{Escaped(label)}</span><span class=\"home-word{(plainWord ? string.Empty : " " + tone)}\">{Escaped(word)}</span><span class=\"home-meaning\">{Escaped(meaning)}</span></div>");

    // "Showing the picks of the night of 20 Nov 2026, not tonight's."
    public static string PastNightWords(DateOnly night) =>
        "Showing the picks of the night of " + night.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ", not tonight's.";

    // "See all 5 of tonight's ideas with their full plans", or one idea's.
    public static string AllIdeasLink(int every) =>
        every == 1 ? "See it with its full plan" : Invariant($"See all {every} of tonight's ideas with their full plans");

    // The heading of a setup's record on Home, in the operator's words.
    public static string HeaderRecordHeading(UniverseChoice reading) =>
        Invariant($"Its record on the {reading.Name}, against buying the index's other stocks the same way");

    // Tonight's setups one at a time: a row of chips, each with its setup's colour, its name as Home words it and its count, the
    // ideas to buy's setups first and the watched after them in the page's order, and at its right how many ideas in how many
    // setups; beneath it each setup's header and cards, the first chosen and the rest hidden until their chip is. A watched
    // setup says above its header what watching means.
    // see: Home's ideas show one setup at a time, chosen from a row of chips each with its setup's colour and count
    public static string SetupSwitcher(HomeView view, bool copying)
    {
        var groups = view.Ideas.GroupBy(idea => idea.Family, StringComparer.Ordinal).Select(group => (Family: group.Key, Watched: false, Ideas: group.ToArray()))
            .Concat(view.Watching.GroupBy(idea => idea.Family, StringComparer.Ordinal).Select(group => (Family: group.Key, Watched: true, Ideas: group.ToArray())))
            .ToArray();

        if (groups.Length == 0)
        {
            return string.Empty;
        }

        var watchingLine = WatchingLine(view.Watching, view.Reading);
        var html = new StringBuilder(Invariant($"<div class=\"setup-switch\" data-setups=\"{groups.Length}\"><div class=\"setup-switch-bar\" role=\"radiogroup\" aria-label=\"Choose a setup\">"));

        for (var at = 0; at < groups.Length; at++)
        {
            var (family, watched, ideas) = groups[at];
            var key = Escaped(family + (watched ? "-watched" : string.Empty));
            var fam = Escaped(family);

            html.Append(Invariant($"<input type=\"radio\" class=\"setup-switch-input\" name=\"home-setup\" id=\"setup-switch-{key}\" value=\"{key}\"{(at == 0 ? " checked" : string.Empty)}>"));
            html.Append(Invariant($"<label class=\"setup-switch-chip\" for=\"setup-switch-{key}\" data-family=\"{fam}\" data-setup=\"{key}\"><span class=\"fam-swatch\" data-family=\"{fam}\"></span>{Escaped(IdeaTag(family))}{(watched ? " (watched)" : string.Empty)}<span class=\"setup-switch-count\">{ideas.Length}</span></label>"));
        }

        html.Append(Invariant($"<span class=\"setup-switch-hint\">{Escaped(SwitcherHint(view.Ideas.Count, groups.Count(group => !group.Watched), view.Watching.Count, groups.Count(group => group.Watched)))}</span></div>"));

        for (var at = 0; at < groups.Length; at++)
        {
            var (family, watched, ideas) = groups[at];

            html.Append(Invariant($"<div class=\"setup-switch-panel\" data-setup=\"{Escaped(family + (watched ? "-watched" : string.Empty))}\"{(at == 0 ? string.Empty : " hidden")}>"));
            html.Append(watched && watchingLine is not null ? watchingLine : string.Empty);
            html.Append(Invariant($"<div class=\"home-ideas\" data-group=\"{(watched ? "watch" : "buy")}\">"));
            html.Append(SetupGroups(ideas, copying, view.Reading, watched, view.Night, watched ? null : view.Since));
            html.Append("</div></div>");
        }

        return html.Append("</div>").ToString();
    }

    // "4 ideas tonight in 3 setups", with the picks watched after them where any are.
    public static string SwitcherHint(int ideas, int setups, int watched, int watchedSetups) =>
        (ideas > 0 ? Invariant($"{ideas} {(ideas == 1 ? "idea" : "ideas")} tonight in {setups} {(setups == 1 ? "setup" : "setups")}") : string.Empty)
        + (watched > 0 ? Invariant($"{(ideas > 0 ? ", " : string.Empty)}{watched} {(watched == 1 ? "pick" : "picks")} watched in {watchedSetups} {(watchedSetups == 1 ? "setup" : "setups")}") : string.Empty);

    // Each setup's picks under one header, in the order the page gives them: its tag and colour, its revision and how many it
    // picked tonight, the sentence its cards share, its verdict, for a watched setup why it is not bought, and its record; then
    // its cards, a carousel where it holds more than one, four across on a wide screen and one at a time on a phone.
    // see: Home draws each setup's ideas under one header saying once what they share, its cards a carousel beneath it
    public static string SetupGroups(IReadOnlyList<HomeIdea> ideas, bool copying, UniverseChoice reading, bool watched, DateOnly? night, IReadOnlyDictionary<string, string>? since)
    {
        var html = new StringBuilder();

        foreach (var group in ideas.GroupBy(idea => idea.Family, StringComparer.Ordinal))
        {
            HomeIdea[] cards = [.. group];
            var first = cards[0];
            var family = Escaped(group.Key);
            var header = "idea-setup-" + group.Key + (watched ? "-watched" : string.Empty);
            var shared = cards.Select(idea => idea.Why).Distinct(StringComparer.Ordinal).Count() == 1 ? first.Why : null;
            var rolling = cards.Length > 1;
            var counted = Invariant($"{cards.Length} {(watched ? cards.Length == 1 ? "pick" : "picks" : cards.Length == 1 ? "idea" : "ideas")} tonight");

            html.Append(Invariant($"<div class=\"idea-setup wo\" id=\"{Escaped(header)}\" data-family=\"{family}\" data-setup-ideas=\"{cards.Length}\" data-carousel=\"{(rolling ? "true" : "false")}\">"));
            html.Append(Invariant($"<div class=\"idea-setup-h fam-edge\" data-family=\"{family}\"><div class=\"idea-setup-t\"><span class=\"fam-tag\" data-family=\"{family}\"><span class=\"fam-swatch\" data-family=\"{family}\"></span>{Escaped(IdeaTag(group.Key))}</span>{RevisionTags.For(group.Key, night ?? DateOnly.MaxValue)}<span class=\"idea-setup-n\">{Escaped(counted)}</span></div>"));
            html.Append(shared is null ? string.Empty : Invariant($"<p class=\"home-why\" data-shared>{(watched ? Invariant($"<b>{PickedLabel}</b> ") : string.Empty)}{Escaped(shared)}</p>"));
            html.Append(VerdictLine(first, reading));

            // A watched setup says once why its picks are not bought.
            // see: Home says in plain words why a pick is an idea to buy or watched and how it was tested, too few past picks said in the operator's words
            html.Append(watched ? Invariant($"<p class=\"home-why-not\"><b>{Escaped(NotBuyingLabel)}</b> {Escaped(NotBuyingLine(first, reading))}</p>") : string.Empty);
            html.Append(first.LongRecord is { Periods: true } record && record.State != "unread"
                ? MarkRenderer.PeriodsBox(record, HeaderRecordHeading(reading), MarkRenderer.HeaderRecordNote)
                : MarkRenderer.SetupRecordBox(first.LongRecord));
            html.Append("</div>");

            if (rolling)
            {
                html.Append(Invariant($"<div class=\"wo-head idea-setup-nav\"><span></span><div class=\"wo-nav\"><span class=\"wo-count\" aria-live=\"polite\" data-wo-count>1 to {Math.Min(MarkRenderer.WaitingInView, cards.Length)} of {cards.Length}</span><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"-1\" aria-label=\"Show the ideas before these\" disabled>&#8249;</button><button type=\"button\" class=\"wo-arrow\" data-wo-step=\"1\" aria-label=\"Show the ideas after these\">&#8250;</button></div></div>"));
            }

            html.Append(rolling
                ? Invariant($"<div class=\"wo-track\" tabindex=\"0\" role=\"group\" aria-label=\"{Escaped(IdeaTag(group.Key))}: tonight's {(watched ? "picks watched" : "ideas")}\" data-wo-track>")
                : "<div class=\"wo-grid\">");

            foreach (var idea in cards)
            {
                html.Append(Idea(idea, copying, reading, watched, header, shared is not null, night, since?.GetValueOrDefault(idea.Family + "|" + idea.Ticker)));
            }

            html.Append("</div></div>");
        }

        return html.ToString();
    }

    // A pick's card under its setup's header: what is its own, an idea to buy or a pick watched whose buy price reads as bought
    // on paper, its setup's record in one line with the way up to the header; from 24.15 on a past night with its line on what
    // has happened since.
    static string Idea(HomeIdea idea, bool copying, UniverseChoice reading, bool watched, string header, bool sharedWhy, DateOnly? night = null, string? since = null)
    {
        var card = new StringBuilder();
        var family = Escaped(idea.Family);
        var buying = watched ? "Paper buy" : "Buy";

        card.Append(Invariant($"<article class=\"home-idea fam-edge{(watched ? " watched" : string.Empty)}\" data-family=\"{family}\" data-ticker=\"{Escaped(idea.Ticker)}\">"));
        card.Append(Invariant($"<div class=\"home-idea-h\"><div><div class=\"home-ticker\">{Escaped(idea.Ticker)}</div>{(idea.Company is { } company ? Invariant($"<div class=\"home-co\">{Escaped(company)}</div>") : string.Empty)}</div></div>"));
        card.Append(since is { } happened ? Invariant($"<p class=\"home-since\" data-since><b>Since then:</b> {Escaped(happened)}</p>") : string.Empty);

        // A card's sentence where its setup's cards do not all share one.
        card.Append(sharedWhy
            ? string.Empty
            : watched
                ? Invariant($"<p class=\"home-why\"><b>{PickedLabel}</b> {Escaped(idea.Why)}</p>")
                : Invariant($"<p class=\"home-why\">{Escaped(idea.Why)}</p>"));

        // A watched card's plan that its kind's test recommends, priced on its own levels and not traded.
        // see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
        card.Append(watched && idea.Alternative is { } alternative ? AlternativeBlock(alternative, reading) : string.Empty);

        // From the 22.16 correction of 2026-11-09 a plan that buys at a zone: the price it waits for, the close that sells it,
        // its target or its time limit, and beneath them how long the buy waits from tonight's close.
        // see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
        // From the 23.10 correction of 2026-11-10 a plan whose stock's levels set no buy price tonight draws no prices, only why.
        if (idea.Zone is { NoBuy: true })
        {
            card.Append(Invariant($"<p class=\"home-cond\" data-no-buy=\"true\">{Escaped(MarkRenderer.NoBuyTonight)}</p>"));
        }
        else if (idea.Zone is { } zone)
        {
            // On the operator's answers of 2026-11-16, item 1b: a new pick that waits for a price says in a box that it is bought
            // only if the price falls to it by its last day.
            // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
            card.Append(!watched && night is { } placed ? Invariant($"<p class=\"wo-box\" data-zone-box>{Escaped(MarkRenderer.ZoneBox(zone, placed))}</p>") : string.Empty);
            card.Append(zone.Sale is { } parted ? MarkRenderer.SalePartsMark(parted) : string.Empty);
            card.Append("<div class=\"home-prices\">");
            card.Append(Invariant($"<div><span>{buying} if it dips to about</span><b>{Dollars(zone.Price)}</b></div>"));
            card.Append(Invariant($"<div><span>Sell if it closes below</span><b class=\"stop\">{(idea.Stop is { } below ? Dollars(below) : NoFigure)}</b></div>"));

            // From the 23.10 correction of 2026-11-13 the sale the plan was registered with, worked from the price it waits for.
            // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
            if (zone.Sale is { } sale)
            {
                foreach (var line in MarkRenderer.PlanSaleLines(sale))
                {
                    card.Append(Invariant($"<div data-sale=\"{line.Key}\"><span>{Escaped(line.Label)}</span><b class=\"aim\">{Escaped(line.Value)}</b>{(line.Note is { } note ? Invariant($"<span class=\"sale-note\">{Escaped(note)}</span>") : string.Empty)}</div>"));
                }
            }
            else
            {
                card.Append(idea.Target is { } aimed
                    ? Invariant($"<div><span>{TakeProfitView.Label}</span><b class=\"aim\">{Dollars(aimed)}</b></div>")
                    : Invariant($"<div><span>Otherwise sell after</span><b class=\"aim\">{(zone.Sessions is { } sessions ? Invariant($"{sessions} trading days") : NoFigure)}</b></div>"));
            }

            card.Append("</div>");
            card.Append(zone.Sale is { HoldsToItsLimit: true } ? Invariant($"<p class=\"home-cond\" data-time-record>{Escaped(MarkRenderer.TimeLimitRecordWords(idea.LongRecord))}</p>") : string.Empty);

            // From the operator's order of 2026-11-24, item 7, the close and the wait said once, in the box above, on a new idea's
            // card; a watched card, which draws no box, keeps the line.
            card.Append(watched || night is null ? Invariant($"<p class=\"home-cond\">{Escaped(MarkRenderer.ZoneWaitLine(zone))}</p>") : string.Empty);
        }
        else
        {
            card.Append(PricesOf(idea, buying));
        }

        card.Append(MarkRenderer.HomeEntryCheckLine(idea.EntryCheck, idea.Ticker));
        card.Append(MarkRenderer.SetupRecordShort(idea.LongRecord, header));

        return Rest(idea, card, copying, reading);
    }

    // An idea's three prices bought at the next open, its buy named as a paper buy on a watched pick's card.
    static string PricesOf(HomeIdea idea, string buying)
    {
        var card = new StringBuilder();

        card.Append("<div class=\"home-prices\">");
        // From the 24.0 correction of 2026-11-20 the buy at the open with the last close beside it, never a price it opens at.
        // see: No line states or implies the price a stock will open at, and a buy at the open buys at whatever price it opens
        card.Append(Invariant($"<div><span>{buying} at the next open, at whatever price it opens; last close</span><b>{(idea.Buy is { } buy ? Dollars(buy) : NoFigure)}</b></div>"));
        // On the operator's ruling of 2026-11-15 on the review of PR 699 a book's buy draws the stop its card placed, and how it
        // sells only on a night before its card held one.
        // see: A book's pick draws the stop and the take-profit its exit places, worked from the close it was chosen at
        card.Append(Planless(idea.Family) && idea.Stop is { } placed
            ? Invariant($"<div><span>{TakeProfitView.AddedStopLabel}</span><b class=\"stop\">{Dollars(placed)}</b>{(idea.TakeProfit?.AddedStopLine is { } under ? Invariant($"<span class=\"aim-from\">{Escaped(under)}</span>") : string.Empty)}</div>")
            : idea.Family == ImprovingRule.Name
            ? Invariant($"<div><span>Sell when</span><b class=\"stop\">{FilingsWeaken}</b></div>")
            : Planless(idea.Family)
            ? Invariant($"<div><span>Sell when</span><b class=\"stop\">{ReshuffleDrops}</b></div>")
            : Invariant($"<div><span>Sell if it falls to</span><b class=\"stop\">{(idea.Stop is { } stop ? Dollars(stop) : NoFigure)}</b></div>"));

        // From the operator's order of 2026-11-14 the price it takes profit at, and the line saying where it came from.
        // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
        card.Append(idea.TakeProfit is { } taking
            ? Invariant($"<div><span>{taking.LabelText}</span><b class=\"aim\">{Dollars(taking.Price)}</b>{(taking.Line is { } said ? Invariant($"<span class=\"aim-from\">{Escaped(said)}</span>") : string.Empty)}</div>")
                + (taking is { Half: true, OtherHalf: { } other } ? Invariant($"<div><span>{TakeProfitView.OtherHalfLabel}</span><b class=\"aim\">{Escaped(other)}</b>{(taking.OtherHalfLine is { } moves ? Invariant($"<span class=\"aim-from\">{Escaped(moves)}</span>") : string.Empty)}</div>") : string.Empty)
            : idea.Rises
                ? Invariant($"<div><span>{MarkRenderer.TrailLabel}</span><b class=\"aim\">{MarkRenderer.TrailValue}</b></div>")
                : idea.Target is { } target ? Invariant($"<div><span>{TakeProfitView.Label}</span><b class=\"aim\">{Dollars(target)}</b></div>") : string.Empty);
        card.Append("</div>");

        return card.ToString();
    }

    // An idea's lines beneath its record: how it sells, the close past which it cannot lose and its rule's winners or what it
    // risks for what it aims at, and its link to Tonight.
    static string Rest(HomeIdea idea, StringBuilder card, bool copying, UniverseChoice reading)
    {
        // see: A plan with a rising give-up price on Home draws no fixed target and one line, and no line on Home's cards starts mid-sentence
        card.Append(SellLine(idea, copying) is { } line ? Invariant($"<div class=\"home-odds\" data-sell-line>{Escaped(line)}</div>") : string.Empty);

        if (idea.Trail is { } trail && idea.Buy is { } entered && idea.Stop is not null)
        {
            card.Append(Invariant($"<div class=\"home-odds home-noloss\" data-no-loss=\"{EquityBrief.Core.Families.TrailingExit.NoLossAt(entered, trail).ToString(CultureInfo.InvariantCulture)}\">{Escaped(MarkRenderer.NoLossLine(entered, trail))}</div>"));
            card.Append(idea.Record is { } record ? Invariant($"<div class=\"home-odds home-winners\" data-finished=\"{record.Finished}\">{Escaped(MarkRenderer.WinnersLine(idea.Family, record))}</div>") : string.Empty);
        }
        // From the 23.10 correction of 2026-11-13 a plan buying at a price aims at the sale it was registered with, its staged
        // levels' mean for staged sales, and names no aim on its time limit alone.
        // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
        else if (!idea.Rises && idea.Buy is { } bought && idea.Stop is { } give && (idea.Zone?.Sale is { } sale ? sale.Aim : idea.Target) is { } aim && bought > give && aim > bought)
        {
            var said = Invariant($"Risks $1 to make about ${(idea.RewardPerRisk ?? (aim - bought) / (bought - give)):0.00}");

            card.Append(Invariant($"<div class=\"home-rr\" role=\"img\" aria-label=\"{said}\"><div class=\"risk\" style=\"flex:{(bought - give).ToString("0.####", CultureInfo.InvariantCulture)}\"></div><div class=\"reward\" style=\"flex:{(aim - bought).ToString("0.####", CultureInfo.InvariantCulture)}\"></div></div>"));
            card.Append(Invariant($"<div class=\"home-odds\">{said}</div>"));
        }

        // One no plan sells opens its row on its own card; from the operator's correction of 2026-11-24, item 7, a plan's idea
        // opens the stock's chart page, its pick's plan drawn on it, never another stock's card.
        // see: See the plan opens Tonight at the stock's row with its plan in view, and a buy no plan sells opens its row on its own card
        // see: See the plan opens the stock's chart page with its pick's plan drawn on it
        card.Append(Planless(idea.Family)
            ? Invariant($"<a class=\"home-how\" href=\"{TonightHref(reading, idea.Family, idea.Ticker)}\">How it sells</a>")
            : Invariant($"<a class=\"home-plan\" href=\"{ChartHref(idea.Ticker)}\">See the plan</a>"));
        card.Append("</article>");

        return card.ToString();
    }

    // What the paper account counts, as Home says it: the live setups' accounts averaged from the day every live rule on the
    // index had its current version, the latest of their starts, in the operator's words.
    // see: Home's paper account counts since today's rules all took effect, and the system's every version stands apart and labelled
    // From the operator's correction of 2026-11-24, item 6, the line and the bar name the account Paper trading draws the same
    // trades as: every running rule's own account, an equal share each, averaged.
    // see: Home names its account figure as the running rules' accounts averaged, the same trades Paper trading draws
    public static string AccountLine(HomePast past) =>
        past.AccountSince is not { } from
            ? "Our paper account holds no trade of the rules as they stand now yet."
            : Invariant($"Since today's rules all took effect on {from.ToString("d MMM", CultureInfo.InvariantCulture)}, this account has followed {(past.Accounts is { } held ? Invariant($"all {held} running rules") : "every running rule")} with an equal share each: Paper trading's \"{PaperAveragedName}\", from the same trades. A rule with nothing bought holds its share in cash.");

    public const string PaperAveragedName = "The running revisions' accounts, averaged";

    // A stock's chart page, where a pick's plan is drawn.
    public static string ChartHref(string ticker) => MarkRenderer.ChartRoute + Uri.EscapeDataString(ticker);

    public static string AveragedLabel(HomePast past) =>
        past.AccountSince is { } counted
            ? Invariant($"The running rules' accounts averaged, since {counted.ToString("d MMM", CultureInfo.InvariantCulture)}")
            : "The running rules' accounts averaged";

    // The system's every trade, apart from the account and labelled.
    public static string SystemLine(HomePast past) =>
        Invariant($"Every revision since {past.Since.ToString("d MMM", CultureInfo.InvariantCulture)}: {past.Closed} {(past.Closed == 1 ? "idea" : "ideas")} bought and sold on paper, {past.Won} made money.");

    // One of the two bars comparing the paper account with holding the index, its length the share of the larger of the two.
    public static string Bar(string key, string label, double? value, double widest)
    {
        var width = value is { } held && widest > 0 ? Math.Abs(held) / widest * 100 : 0;
        var drawn = value is { } made && Math.Round(made * 100, 1) != 0 ? made * 100 : 0;
        var shown = value is null ? NoFigure : Invariant($"{drawn:+0.0;-0.0;0.0}%");

        return Invariant($"<div class=\"home-bar\" data-bar=\"{key}\"><div class=\"row\"><span>{Escaped(label)}</span><b{(key == "ours" && drawn != 0 ? Invariant($" style=\"color:var(--{(drawn > 0 ? "up" : "down")})\"") : string.Empty)}>{shown}</b></div><div class=\"track\"><div class=\"fill {key}\" style=\"width:{width.ToString("0.#", CultureInfo.InvariantCulture)}%\"></div></div></div>");
    }
}
