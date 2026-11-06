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
// Loop page moves its give-up price; and for the Fundamentals family the setting its index's rule stands at.
public sealed record HomeIdea(string Family, string Ticker, string? Company, string Why, decimal? Buy, decimal? Stop, decimal? Target, bool Rises, decimal? RewardPerRisk, decimal? Trail = null, EquityBrief.Core.Cards.PaperWinners? Record = null, EquityBrief.Core.EntryCheck.EntryReading? EntryCheck = null, bool StopMoves = false, ImprovingSweepSetting? Setting = null);

// How past ideas have done on Home: the day the rules first went live and the system's trades bought and sold since and how
// many made money; the combined paper account's return, the day it counts from and the index's fund over the same days; and
// whether a live rule still holds fewer finished trades than a verdict needs.
// see: Every count of trades names its group and one function works the three groups
public sealed record HomePast(DateOnly Since, int Closed, int Won, double? Ours, double? Index, bool Early, string Fund = "", DateOnly? AccountSince = null)
{
    // Whether the account counts from before the version of the rule its first day is that rule's own start, carried
    // back by a replay that reproduced every trade.
    public bool CarriedBack { get; init; }
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
    // The buys Tonight draws after the ideas Home draws, in Tonight's order.
    public IReadOnlyList<HomeIdea> LeftOut { get; init; } = [];

    // The monthly books' holdings bought at tonight's reshuffle, each with its book's family, in Tonight's order.
    public IReadOnlyList<(string Family, string Ticker)> Reshuffled { get; init; } = [];
}

// Home, from 22.16: the landing page for a reader new to investing, the day, tonight's ideas, how past ideas have done and
// how the rules work, then the way to Tonight and the page's disclaimer. Every sentence is written here from templates over
// the newest night's figures; no model writes any of it, and Tonight is drawn as it was, one click away.
// see: Home is the landing page for a reader new to investing, drawn by code from the newest night's figures in plain words
public sealed partial class SinglePageApp
{
    // The ideas Home draws before the link to the rest.
    public const int HomeIdeas = 6;

    // A day the index fell by at least this much, in percent, is a shaky one.
    public const double ShakyFall = 1.0;

    // A rule needs about this many finished trades for a fair verdict.
    public const int FairVerdictTrades = 300;

    public const string NoFigure = "No figure stored for today";

    public const string IdeasNote = "These are ideas found by fixed rules from prices and company filings, not advice. A rule's record is shown on its own page and is never a promise about any one stock.";

    public const string RisesLine = "The give-up price rises as the stock climbs, so a winner can keep running.";

    // What the Fundamentals family's card says in a give-up price's place: it sets none and sells on its filings; and a
    // monthly book's, which sells at a reshuffle.
    public const string FilingsWeaken = "its filings weaken";

    public const string ReshuffleDrops = "a monthly reshuffle drops it";

    // The line under a card's sell price, from its family's own exit rule: the Fundamentals family's filings and time limit,
    // its figures left off the read-only copy, a give-up price rising or staying where it is; none where a plan holds no
    // give-up price or an exit changed on the Loop page moves it.
    // see: A card's line under its sell price is worded from its family's own exit rule
    public static string? SellLine(HomeIdea idea, bool copying)
    {
        if (idea.Family == ImprovingRule.Name)
        {
            var setting = idea.Setting ?? ImprovingGrid.Starting;

            return copying
                ? "No price is set in advance: it sells when a new filing weakens its score, or at its time limit."
                : Invariant($"No price is set in advance: it sells when a new filing drops its score below {MarkRenderer.InEveryNine(setting.HoldInNine)},{(setting.SoldUnderAverage ? " on a close under its 200-day average," : string.Empty)} or after {setting.Sessions} trading days.");
        }

        if (Planless(idea.Family))
        {
            return "No price is set in advance: it sells when " + ReshuffleDrops + ".";
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

    public const string FootLine = "Want every number, every rule and every version being tested?";

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

    // The buys Home leaves out, named in Tonight's order with their families' tags: "3 more on Tonight: CVS and ODFL (Getting
    // stronger as a business), NUE (Quick rebound), not drawn here to keep this page short."
    // see: Home's ideas are Tonight's buys in Tonight's order, those left out named with their families
    public static string? LeftOutLine(IReadOnlyList<HomeIdea> left)
    {
        if (left.Count == 0)
        {
            return null;
        }

        return Invariant($"{left.Count} more on Tonight: {Grouped(left.Select(idea => (idea.Family, idea.Ticker)))}, not drawn here to keep this page short.");
    }

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
        var region = new StringBuilder();
        var heading = "Home: " + view.Reading.Titled;
        var stamp = Cards.Night(view.Night);

        region.Append(Invariant($"<section class=\"home\" data-universe=\"{view.Reading.Word}\" data-night=\"{Cards.Day(view.Night)}\" data-running=\"{(view.Running ? "true" : "false")}\">"));
        region.Append(Cards.Masthead(heading, $"<span class=\"m-screen\">{Escaped(heading)}</span>", Invariant($"Night of {Cards.Day(view.Night)}, computed after the close")));
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

        // Tonight's ideas.
        var title = Invariant($"Tonight's ideas: {view.IdeasCount} {(view.IdeasCount == 1 ? "stock" : "stocks")} to buy {view.OpensOn} morning");

        region.Append(Invariant($"<section class=\"card\" id=\"ideas\" data-ideas=\"{view.IdeasCount}\"><div class=\"card-h\"><div><div class=\"lbl\">Tonight's ideas</div><h2>{Escaped(title)}</h2></div><div>{stamp}</div></div><div class=\"card-b\">"));

        if (view.IdeasCount == 0)
        {
            region.Append(Invariant($"<p class=\"card-summary\" data-ideas=\"none\">No new ideas tonight. {Escaped(view.NoIdeas ?? "Nothing qualified tonight.")}</p>"));
        }
        else
        {
            region.Append("<p class=\"card-summary\">Each idea comes with three prices decided tonight: what to pay, where to give up if it goes wrong, and where to take the profit.</p>");
            region.Append("<div class=\"home-ideas\">");

            foreach (var idea in view.Ideas)
            {
                region.Append(Idea(idea, marks.Copying, view.Reading));
            }

            region.Append("</div>");
            region.Append(LeftOutLine(view.LeftOut) is { } more ? Invariant($"<p class=\"home-more\" data-more=\"{view.LeftOut.Count}\">{Escaped(more)}</p>") : string.Empty);
            region.Append(Invariant($"<p style=\"margin:12px 0 0\"><a href=\"{TonightHref(view.Reading)}\">See all {view.IdeasCount} with their full plans</a></p>"));
        }

        region.Append(ReshuffleLine(view.Reshuffled) is { } reshuffle ? Invariant($"<p class=\"home-more home-reshuffle\" data-reshuffled=\"{view.Reshuffled.Count}\">{Escaped(reshuffle)}</p>") : string.Empty);

        region.Append(Invariant($"<p class=\"home-note\">{Escaped(IdeasNote)}</p>"));
        region.Append("</div></section>");

        // How past ideas have done.
        region.Append("<section class=\"card\" id=\"past\"><div class=\"card-h\"><div><div class=\"lbl\">How it's doing</div><h2>How past ideas have done</h2></div></div><div class=\"card-b home-results\">");

        if (view.Past is { } past)
        {
            // The account as the rules stand now from the day their first trade counts, with the index's fund over the same
            // days; the system's every version apart from it and labelled.
            // see: Home's paper account counts from the day the current rules' first trade counts, and the system's every version stands apart and labelled
            region.Append(Invariant($"<div><p class=\"card-summary\" data-account-since=\"{(past.AccountSince is { } from ? Cards.Day(from) : "none")}\">{Escaped(AccountLine(past, marks.Copying))}</p>"));
            region.Append(past.Early ? Invariant($"<p style=\"margin:0;color:var(--soft);font-size:14px\">Still early: each rule needs about {FairVerdictTrades} finished trades for a fair verdict.</p>") : string.Empty);
            region.Append(Invariant($"<p style=\"margin:10px 0 0\"><a href=\"{PicksRoute}{(view.Reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + view.Reading.Word)}\">See every past idea</a>, or the rules as they stand now on <a href=\"{PaperRoute}{(view.Reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + view.Reading.Word)}\">Paper trading</a></p></div>"));

            var widest = Math.Max(Math.Abs(past.Ours ?? 0), Math.Abs(past.Index ?? 0));

            region.Append("<div>");
            region.Append(Bar("ours", past.AccountSince is { } counted ? Invariant($"Our paper account, as the rules stand now, since {counted.ToString("d MMM", CultureInfo.InvariantCulture)}") : "Our paper account", past.Ours, widest));
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

        return region.ToString();
    }

    // One of the day's three tiles: a dot and a word, so colour is never the only sign, and the line saying what it means.
    static string Signal(string key, string label, string tone, string word, string meaning, bool plainWord = false) =>
        Invariant($"<div class=\"home-signal\" data-signal=\"{key}\" data-tone=\"{tone}\"><span class=\"st-label\"><span class=\"home-dot {tone}\" aria-hidden=\"true\"></span>{Escaped(label)}</span><span class=\"home-word{(plainWord ? string.Empty : " " + tone)}\">{Escaped(word)}</span><span class=\"home-meaning\">{Escaped(meaning)}</span></div>");

    static string Idea(HomeIdea idea, bool copying, UniverseChoice reading)
    {
        var card = new StringBuilder();
        var family = Escaped(idea.Family);

        card.Append(Invariant($"<article class=\"home-idea fam-edge\" data-family=\"{family}\" data-ticker=\"{Escaped(idea.Ticker)}\">"));
        card.Append(Invariant($"<div class=\"home-idea-h\"><div><div class=\"home-ticker\">{Escaped(idea.Ticker)}</div>{(idea.Company is { } company ? Invariant($"<div class=\"home-co\">{Escaped(company)}</div>") : string.Empty)}</div><span class=\"fam-tag\" data-family=\"{family}\"><span class=\"fam-swatch\" data-family=\"{family}\"></span>{Escaped(IdeaTag(idea.Family))}</span></div>"));
        card.Append(Invariant($"<p class=\"home-why\">{Escaped(idea.Why)}</p>"));
        card.Append("<div class=\"home-prices\">");
        card.Append(Invariant($"<div><span>Buy at about</span><b>{(idea.Buy is { } buy ? Dollars(buy) : NoFigure)}</b></div>"));
        card.Append(idea.Family == ImprovingRule.Name
            ? Invariant($"<div><span>Sell when</span><b class=\"stop\">{FilingsWeaken}</b></div>")
            : Planless(idea.Family)
            ? Invariant($"<div><span>Sell when</span><b class=\"stop\">{ReshuffleDrops}</b></div>")
            : Invariant($"<div><span>Sell if it falls to</span><b class=\"stop\">{(idea.Stop is { } stop ? Dollars(stop) : NoFigure)}</b></div>"));

        card.Append(Invariant($"<div><span>Aim to sell at</span><b class=\"aim\">{(idea.Rises || idea.Target is null ? MarkRenderer.NoFixedTarget : Dollars(idea.Target.Value))}</b></div>"));
        card.Append("</div>");

        card.Append(MarkRenderer.HomeEntryCheckLine(idea.EntryCheck, idea.Ticker));

        // see: A plan with a rising give-up price on Home draws no fixed target and one line, and no line on Home's cards starts mid-sentence
        card.Append(SellLine(idea, copying) is { } line ? Invariant($"<div class=\"home-odds\" data-sell-line>{Escaped(line)}</div>") : string.Empty);

        if (idea.Trail is { } trail && idea.Buy is { } entered && idea.Stop is not null)
        {
            card.Append(Invariant($"<div class=\"home-odds home-noloss\" data-no-loss=\"{EquityBrief.Core.Families.TrailingExit.NoLossAt(entered, trail).ToString(CultureInfo.InvariantCulture)}\">{Escaped(MarkRenderer.NoLossLine(entered, trail))}</div>"));
            card.Append(idea.Record is { } record ? Invariant($"<div class=\"home-odds home-winners\" data-finished=\"{record.Finished}\">{Escaped(MarkRenderer.WinnersLine(idea.Family, record))}</div>") : string.Empty);
        }
        else if (!idea.Rises && idea.Buy is { } bought && idea.Stop is { } give && idea.Target is { } aim && bought > give && aim > bought)
        {
            var said = Invariant($"Risks $1 to make about ${(idea.RewardPerRisk ?? (aim - bought) / (bought - give)):0.00}");

            card.Append(Invariant($"<div class=\"home-rr\" role=\"img\" aria-label=\"{said}\"><div class=\"risk\" style=\"flex:{(bought - give).ToString("0.####", CultureInfo.InvariantCulture)}\"></div><div class=\"reward\" style=\"flex:{(aim - bought).ToString("0.####", CultureInfo.InvariantCulture)}\"></div></div>"));
            card.Append(Invariant($"<div class=\"home-odds\">{said}</div>"));
        }

        // A plan's idea opens Tonight at its row, its plan opened and in view; one no plan sells opens its row on its own card.
        // see: See the plan opens Tonight at the stock's row with its plan in view, and a buy no plan sells opens its row on its own card
        card.Append(Planless(idea.Family)
            ? Invariant($"<a class=\"home-how\" href=\"{TonightHref(reading, idea.Family, idea.Ticker)}\">How it sells</a>")
            : Invariant($"<a class=\"home-plan\" href=\"{TonightHref(reading, null, idea.Ticker)}\">See the plan</a>"));
        card.Append("</article>");

        return card.ToString();
    }

    // What the paper account counts, as Home says it: the rules as they stand now from the day their first trade counts,
    // and where that day comes before the version's start that its earlier trades replay the same; the read-only copy
    // names no version.
    public static string AccountLine(HomePast past, bool copying) =>
        past.AccountSince is not { } from
            ? "Our paper account holds no trade of the rules as they stand now yet."
            : Invariant($"Our paper account counts the rules as they stand now from {from.ToString("d MMM", CultureInfo.InvariantCulture)}, the day their first trade counts")
                + (past.CarriedBack && !copying ? ": its earlier trades replay the same under this version." : ".");

    // The system's every trade, apart from the account and labelled.
    public static string SystemLine(HomePast past) =>
        Invariant($"All versions since {past.Since.ToString("d MMM", CultureInfo.InvariantCulture)}: {past.Closed} {(past.Closed == 1 ? "idea" : "ideas")} bought and sold on paper, {past.Won} made money.");

    // One of the two bars comparing the paper account with holding the index, its length the share of the larger of the two.
    public static string Bar(string key, string label, double? value, double widest)
    {
        var width = value is { } held && widest > 0 ? Math.Abs(held) / widest * 100 : 0;
        var drawn = value is { } made && Math.Round(made * 100, 1) != 0 ? made * 100 : 0;
        var shown = value is null ? NoFigure : Invariant($"{drawn:+0.0;-0.0;0.0}%");

        return Invariant($"<div class=\"home-bar\" data-bar=\"{key}\"><div class=\"row\"><span>{Escaped(label)}</span><b{(key == "ours" && drawn != 0 ? Invariant($" style=\"color:var(--{(drawn > 0 ? "up" : "down")})\"") : string.Empty)}>{shown}</b></div><div class=\"track\"><div class=\"fill {key}\" style=\"width:{width.ToString("0.#", CultureInfo.InvariantCulture)}%\"></div></div></div>");
    }
}
