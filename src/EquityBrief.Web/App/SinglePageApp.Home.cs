using System.Globalization;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;
using static System.FormattableString;

namespace EquityBrief.Web.App;

// One idea Home draws: its family, its stock and company, the sentence saying why in the reader's words, the three prices
// its plan set tonight, none where the plan sets none, whether its give-up price rises with the stock in place of a
// target, and what it risks against what it aims for.
public sealed record HomeIdea(string Family, string Ticker, string? Company, string Why, decimal? Buy, decimal? Stop, decimal? Target, bool Rises, decimal? RewardPerRisk);

// How past ideas have done on Home: the day the combined paper account counts from, the ideas it bought and sold and how
// many made money, its return and an equal-weight hold of the index's stocks over the same days, and whether a live rule
// still holds fewer finished trades than a verdict needs.
public sealed record HomePast(DateOnly Since, int Closed, int Won, double? Ours, double? Index, bool Early);

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
    int? Rules = null);

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
        _ => "Idea",
    };

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

    // A link to Tonight on the index read, at a family's card where one is named.
    static string TonightHref(UniverseChoice reading, string? setup = null)
    {
        var query = new List<string>();

        if (reading != Universes.Large)
        {
            query.Add(Universes.Query + "=" + reading.Word);
        }

        if (setup is not null)
        {
            query.Add(HomeSetupKey + "=" + setup);
        }

        return TonightRoute + (query.Count > 0 ? "?" + string.Join("&amp;", query) : string.Empty);
    }

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
                region.Append(Idea(idea));
            }

            region.Append("</div>");
            region.Append(Invariant($"<p style=\"margin:12px 0 0\"><a href=\"{TonightHref(view.Reading)}\">See all {view.IdeasCount} with their full plans</a></p>"));
        }

        region.Append(Invariant($"<p class=\"home-note\">{Escaped(IdeasNote)}</p>"));
        region.Append("</div></section>");

        // How past ideas have done.
        region.Append("<section class=\"card\" id=\"past\"><div class=\"card-h\"><div><div class=\"lbl\">How it's doing</div><h2>How past ideas have done</h2></div></div><div class=\"card-b home-results\">");

        if (view.Past is { } past)
        {
            region.Append(Invariant($"<div><p class=\"card-summary\">Since the rules went live on {past.Since.ToString("d MMM", CultureInfo.InvariantCulture)}, {past.Closed} {(past.Closed == 1 ? "idea has" : "ideas have")} been bought and sold on paper. {past.Won} made money.</p>"));
            region.Append(past.Early ? Invariant($"<p style=\"margin:0;color:var(--soft);font-size:14px\">Still early: each rule needs about {FairVerdictTrades} finished trades for a fair verdict.</p>") : string.Empty);
            region.Append(Invariant($"<p style=\"margin:10px 0 0\"><a href=\"{PicksRoute}{(view.Reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + view.Reading.Word)}\">See every past idea</a></p></div>"));

            var widest = Math.Max(Math.Abs(past.Ours ?? 0), Math.Abs(past.Index ?? 0));

            region.Append("<div>");
            region.Append(Bar("ours", "Our paper account", past.Ours, widest));
            region.Append(Bar("index", "Just holding the whole index", past.Index, widest));
            region.Append("</div>");
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

    static string Idea(HomeIdea idea)
    {
        var card = new StringBuilder();
        var family = Escaped(idea.Family);

        card.Append(Invariant($"<article class=\"home-idea fam-edge\" data-family=\"{family}\" data-ticker=\"{Escaped(idea.Ticker)}\">"));
        card.Append(Invariant($"<div class=\"home-idea-h\"><div><div class=\"home-ticker\">{Escaped(idea.Ticker)}</div>{(idea.Company is { } company ? Invariant($"<div class=\"home-co\">{Escaped(company)}</div>") : string.Empty)}</div><span class=\"fam-tag\" data-family=\"{family}\"><span class=\"fam-swatch\" data-family=\"{family}\"></span>{Escaped(IdeaTag(idea.Family))}</span></div>"));
        card.Append(Invariant($"<p class=\"home-why\">{Escaped(idea.Why)}</p>"));
        card.Append("<div class=\"home-prices\">");
        card.Append(Invariant($"<div><span>Buy at about</span><b>{(idea.Buy is { } buy ? Dollars(buy) : NoFigure)}</b></div>"));
        card.Append(Invariant($"<div><span>Sell if it falls to</span><b class=\"stop\">{(idea.Stop is { } stop ? Dollars(stop) : idea.Rises ? "below its 200-day average" : NoFigure)}</b></div>"));
        card.Append(Invariant($"<div><span>Aim to sell at</span><b class=\"aim\">{(idea.Rises || idea.Target is null ? "No fixed target" : Dollars(idea.Target.Value))}</b></div>"));
        card.Append("</div>");

        if (idea.Rises || idea.Target is null)
        {
            card.Append(Invariant($"<div class=\"home-odds\">{Escaped(RisesLine)}</div>"));
        }
        else if (idea.Buy is { } bought && idea.Stop is { } give && idea.Target is { } aim && bought > give && aim > bought)
        {
            var said = Invariant($"Risks $1 to make about ${(idea.RewardPerRisk ?? (aim - bought) / (bought - give)):0.00}");

            card.Append(Invariant($"<div class=\"home-rr\" role=\"img\" aria-label=\"{said}\"><div class=\"risk\" style=\"flex:{(bought - give).ToString("0.####", CultureInfo.InvariantCulture)}\"></div><div class=\"reward\" style=\"flex:{(aim - bought).ToString("0.####", CultureInfo.InvariantCulture)}\"></div></div>"));
            card.Append(Invariant($"<div class=\"home-odds\">{said}</div>"));
        }

        card.Append(Invariant($"<a href=\"{ChartRoute}{Uri.EscapeDataString(idea.Ticker)}\">See the plan</a>"));
        card.Append("</article>");

        return card.ToString();
    }

    // One of the two bars comparing the paper account with holding the index, its length the share of the larger of the two.
    static string Bar(string key, string label, double? value, double widest)
    {
        var width = value is { } held && widest > 0 ? Math.Abs(held) / widest * 100 : 0;
        var shown = value is { } made ? Invariant($"{(made >= 0 ? "+" : "-")}{Math.Abs(made * 100):0.0}%") : NoFigure;

        return Invariant($"<div class=\"home-bar\" data-bar=\"{key}\"><div class=\"row\"><span>{Escaped(label)}</span><b{(key == "ours" && value is { } own ? Invariant($" style=\"color:var(--{(own >= 0 ? "up" : "down")})\"") : string.Empty)}>{shown}</b></div><div class=\"track\"><div class=\"fill {key}\" style=\"width:{width.ToString("0.#", CultureInfo.InvariantCulture)}%\"></div></div></div>");
    }
}
