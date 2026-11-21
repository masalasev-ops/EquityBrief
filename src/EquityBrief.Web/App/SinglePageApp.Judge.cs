using System.Globalization;
using System.Text;
using EquityBrief.Core.Judge;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One of the AI judge's picks as its card draws it: its rank of the night's picks, the stock and its company, the sector and
// size the judge was shown, its reason, the last close, the stop and take-profit code worked from it, the give-up distance
// where the rest follows one, and the hold and take-profit it chose.
public sealed record JudgeCardView(int Rank, int Of, string Ticker, string? Company, string? Sector, string? Size, string Reason, decimal LastClose, decimal Stop, decimal Target, decimal? GiveUp, string Hold, string Shape)
{
    // From 24.14 the facts the judge named as deciding it, with their values, none on a pick made before it named its facts;
    // and the rest of the sheet it was shown.
    public IReadOnlyList<JudgeFactView>? Named { get; init; }

    public IReadOnlyList<JudgeFactView> Rest { get; init; } = [];

    // From the 24.14 correction of 2026-11-21, on an earlier version's card, the night it was picked on and what that
    // version's account did with it.
    public DateOnly? Night { get; init; }

    public JudgeHeld? Held { get; init; }
}

// What an account did with one of the judge's picks: the session it bought at the open and the price it paid, its stop,
// take-profit and give-up distance from that price, the session and price it sold at with its result per $1 risked once its
// walk ended, and why the account skipped it where it did.
public sealed record JudgeHeld(DateOnly Bought, decimal Paid, decimal? Stop, decimal? Target, decimal? Trail, DateOnly? Ended, decimal? Sold, double? Result, string? Skipped);

// From the 24.14 correction of 2026-11-21 an earlier judge version as its page draws it apart from the record: its number in
// the order the versions first ran, its first and last nights, its picks' cards, its own account's scoreboard and its line.
public sealed record JudgeVersionView(int Number, DateOnly First, DateOnly Last, IReadOnlyList<JudgeCardView> Picks, JudgeScoreboard Score, IReadOnlyList<(DateOnly Session, decimal Value)> Values);

// One fact of a pick's sheet as its card draws it: its words, its value as the judge was shown it, and a plain comparison
// where its value's sign says one.
public sealed record JudgeFactView(string Words, string Value, string? Comparison)
{
    // The fields whose value is a signed figure against something, and what a value above and below it says.
    static readonly Dictionary<string, (string Up, string Down)> Signed = new(StringComparer.Ordinal)
    {
        ["return_month"] = ("Up over the month.", "Down over the month."),
        ["return_quarter"] = ("Up over three months.", "Down over three months."),
        ["return_twelve_less_one"] = ("Up over the year to a month ago.", "Down over the year to a month ago."),
        ["strength"] = ("Stronger than its index.", "Weaker than its index."),
        ["close_over_fifty"] = ("Above its 50-day average.", "Below its 50-day average."),
        ["close_over_long"] = ("Above its 200-day average.", "Below its 200-day average."),
        ["volume_ratio"] = ("More volume than it usually trades.", "Less volume than it usually trades."),
        ["estimate_revision_month"] = ("Analysts raised their estimates.", "Analysts cut their estimates."),
        ["revenue_growth"] = ("Sales grew on a year before.", "Sales fell on a year before."),
        ["growth_change"] = ("Growth sped up.", "Growth slowed."),
        ["gross_margin_change"] = ("Its gross margin widened.", "Its gross margin narrowed."),
        ["operating_margin_change"] = ("Its operating margin widened.", "Its operating margin narrowed."),
        ["return_on_assets_change"] = ("Its return on assets rose.", "Its return on assets fell."),
        ["sector_month"] = ("Ahead of its sector over the month.", "Behind its sector over the month."),
        ["sector_quarter"] = ("Ahead of its sector over three months.", "Behind its sector over three months."),
        ["surprise"] = ("Beat the estimate.", "Missed the estimate."),
        ["high_ratio"] = ("Above its 52-week high.", "Under its 52-week high."),
    };

    public static string? Compared(string key, string value) =>
        Signed.TryGetValue(key, out var words) && value.Length > 0
            ? value[0] == '+' ? words.Up : value[0] == '-' ? words.Down : null
            : null;
}

// One account's line on the chart: its name and its value on each session.
public sealed record JudgeLineView(string Name, IReadOnlyList<(DateOnly Session, decimal Value)> Values);

// What the AI judge's page draws: the newest night it ran for and what came of it, that night's picks, the live record's
// scoreboard, whether it beats random so far and what random picks made per $1 risked, where it looked, the four accounts'
// lines and the random accounts' band, and a replayed record apart from the live one; from 24.13 the night of its first live
// picks, the open they buy at, and whether any of its picks has been bought.
public sealed record JudgeView(
    DateOnly? Night,
    string? Outcome,
    string? Why,
    IReadOnlyList<JudgeCardView> Picks,
    JudgeScoreboard Score,
    string AgainstRandom,
    double? RandomPerDollar,
    IReadOnlyList<JudgeLooked> Looked,
    IReadOnlyList<JudgeLineView> Lines,
    IReadOnlyList<JudgeBand> Band,
    JudgeScoreboard? Replayed,
    int ReplayedNights)
{
    public DateOnly? FirstNight { get; init; }

    public DateOnly? FirstOpen { get; init; }

    public bool Bought { get; init; }

    // From 24.14 the first night of the newest judge version where an earlier version picked before it, the night its
    // record counts from.
    public DateOnly? CountedFrom { get; init; }

    // From the 24.14 correction of 2026-11-21 each earlier version apart from the record, the record's own number where
    // there is one, and the earlier version whose picks the drawn night's are.
    public IReadOnlyList<JudgeVersionView> Earlier { get; init; } = [];

    public int? RecordNumber { get; init; }

    public JudgeVersionView? PicksFrom { get; init; }
}

// The AI judge's page: the four accounts' chart, the scoreboard of its live record, each pick's card in the words of the rule
// on the open, where it looked, and one line on Home. A replayed figure stands apart from the live record, which alone counts.
// see: No line states or implies the price a stock will open at, and a buy at the open buys at whatever price it opens
// see: The AI judge's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the judge waits for that run
// owes: The AI judge's live record read at 300 closed picks
public sealed partial class SinglePageApp
{
    public const string JudgeRoute = "#/judge";

    public const string JudgeLede =
        "Each night, straight after the night run finishes, your local model reads a short set of facts about every one of the 1,500 stocks, with names, tickers and dates removed, and picks up to 5. It chooses how long to hold and how to sell from a fixed menu; code sets every price. Its picks are never the rules' buys. They trade only in this account, so we can see whether it beats the rules and beats random picks.";

    public const string JudgeBuysAtTheOpen = "Buys at the next open, at whatever price it opens.";

    public const string JudgeMenuLine = "The AI chose how long to hold and how to sell, from a fixed menu. Code sets every price from the stock's typical daily move and the price it buys at.";

    public const string JudgeReplayedLine = "Nights replayed from before the judge started are drawn apart and labelled replayed. Only the live record counts.";

    public static IReadOnlyList<string> JudgeLineNames { get; } = ["AI judge", "The rules (every live rule's trades)", "Random picks: the middle of 1,000 random accounts", "Just holding the index"];

    public string JudgeRegion(JudgeView view)
    {
        var region = new StringBuilder();

        region.Append("<section class=\"judge\">");
        region.Append(Cards.Masthead("AI judge", "<span class=\"m-screen\">AI judge</span>", view.Night is { } night ? "Computed for " + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Not run yet"));
        region.Append(Cards.Computed("AI judge", $"<p data-summary>{Escaped(JudgeLede)}</p>{RunLine(view)}{CountedFromLine(view)}", title: "Your local AI picks its own stocks, in its own paper account", region: "judge-lede"));
        region.Append(Cards.Computed("Scoreboard", EmptyRecord(view) ?? Scoreboard(view), title: "How its live record stands", region: "judge-score"));
        region.Append(Cards.Computed("Four accounts", (EmptyRecord(view) ?? $"<p data-summary>The judge's account beside the rules', random picks' and the index's, each from {(view.RecordNumber is { } number ? $"version {number}'s" : "the judge's")} first night.</p>" + JudgeChart(view.Lines, view.Band, "The four accounts' returns since the judge started")) + ApartCharts(view), title: "Four accounts, same money, same sizing, same costs", lede: Escaped(JudgeReplayedLine), region: "judge-chart"));
        region.Append(Cards.Computed("Tonight", $"<p data-summary>{(view.Picks.Count == 0 ? "The judge picked no stock tonight." : $"The judge picked {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} tonight, best first.")}</p>" + PickCards(view.Picks, view.Score, null), title: $"The AI judge's picks tonight ({view.Picks.Count})", lede: "Each card shows what the AI saw, why it picked the stock, and the plan code set for it. Each buys at the next open, at whatever price it opens; nobody knows that price beforehand.", region: "judge-picks"));
        region.Append(Cards.Computed("Where the AI looked", LookedSummary(view) + Looked(view), title: "How its picks compare with the rules', since it started", region: "judge-looked"));

        foreach (var earlier in view.Earlier)
        {
            region.Append(Cards.Computed(VersionWords(earlier), VersionSummary(earlier, view.RecordNumber) + PickCards(earlier.Picks, earlier.Score, earlier), title: VersionWords(earlier) + ": its own account, apart from the record", region: string.Create(CultureInfo.InvariantCulture, $"judge-version-{earlier.Number}")));
        }

        if (view.Replayed is { } replayed)
        {
            region.Append(Cards.Computed("Replayed", $"<p data-replayed=\"record\" data-summary>Replayed, {view.ReplayedNights} night{(view.ReplayedNights == 1 ? string.Empty : "s")}: {(replayed.PerDollar is { } made ? $"{replayed.Closed} trades closed, {Dollars(made)} made per $1 risked" : "no trade closed yet")}. This is not the live record and is never added to it.</p>", title: "Replayed nights, apart from the live record", region: "judge-replayed"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The line Home draws for the judge: what it picked tonight, or from 24.15 on a past night that night, and its live record
    // per $1 risked beside random picks'.
    public static string JudgeHomeLine(JudgeView view, bool past = false) =>
        $"<p class=\"judge-home\" data-home=\"judge\"><b>AI judge</b> Picked {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} {(past ? "that night" : "tonight")} {(view.PicksFrom is { } from ? $"in the account of {Escaped(VersionWords(from))}, kept apart from its record" : "in its own paper account")}. "
        + (view.Score.PerDollar is { } made
            ? $"Since it started: {Dollars(made)} per $1 risked, against {(view.RandomPerDollar is { } random ? Dollars(random) : "nothing yet")} for random picks. "
            : "No trade of it has closed yet. ")
        + $"<a href=\"{JudgeRoute}\">See the AI judge</a></p>";

    static string RunLine(JudgeView view) =>
        view.Night is not { } night ? "<p class=\"empty\">The judge has not run yet. The night starts it after its last step.</p>"
        : view.Outcome is "picked" or "none" or null ? string.Empty
        : $"<p class=\"judge-run\" data-outcome=\"{Escaped(view.Outcome)}\">The judge's run for {night.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} was {Escaped(view.Outcome)}{(string.IsNullOrEmpty(view.Why) ? string.Empty : ": " + Escaped(view.Why))}. A night it misses is never filled in later.</p>";

    // From 24.14 where an earlier judge version picked before the newest, the night the record counts from; from the 24.14
    // correction of 2026-11-21 with the newest version's number, each earlier version's picks walked in an account of its own.
    static string CountedFromLine(JudgeView view) =>
        view.CountedFrom is { } from
            ? $"<p class=\"judge-version\" data-version=\"counted\">{Escaped(CountedFromWords(from, view.RecordNumber ?? 2))}</p>"
            : string.Empty;

    public static string CountedFromWords(DateOnly from, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"Its record counts version {number}'s picks alone, from the night of ")
        + from.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        + ". Each earlier version's picks are bought and walked to their exits in an account of their own, drawn apart under its number and never added to the record.";

    // An earlier version's name: its number and the nights it picked on.
    public static string VersionWords(JudgeVersionView version) =>
        string.Create(CultureInfo.InvariantCulture, $"Version {version.Number}, picked ")
        + version.First.ToString("d MMM", CultureInfo.InvariantCulture)
        + (version.Last > version.First ? " to " + version.Last.ToString("d MMM", CultureInfo.InvariantCulture) : string.Empty);

    // What an earlier version's account holds: its picks, where each was bought, and its closed trades.
    public static string VersionSummaryWords(JudgeVersionView version, int? recordNumber)
    {
        var count = version.Picks.Count;
        var bought = version.Picks.Count(pick => pick.Held is { Skipped: null });
        var opens = version.First == version.Last ? "at the open on " + Open(version.First).ToString("ddd d MMM", CultureInfo.InvariantCulture) : "each at the open after its night";
        var closed = version.Score.Closed == 0
            ? "None of its trades has closed yet."
            : string.Create(CultureInfo.InvariantCulture, $"{version.Score.Closed} of its trades {(version.Score.Closed == 1 ? "has" : "have")} closed, {version.Score.Winners} of them winners") + (version.Score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked." : ".");

        return string.Create(CultureInfo.InvariantCulture, $"{VersionWords(version)}: {count} pick{(count == 1 ? string.Empty : "s")}, bought {opens} in this version's own account and walked to their exits by their plans, the same money, sizing and costs as the record's. {bought} of {count} bought so far. They are never added to ")
            + (recordNumber is { } number ? string.Create(CultureInfo.InvariantCulture, $"version {number}'s record. ") : "the record. ")
            + closed;
    }

    static string VersionSummary(JudgeVersionView version, int? recordNumber) =>
        $"<p class=\"judge-version\" data-version=\"{version.Number}\" data-summary>{Escaped(VersionSummaryWords(version, recordNumber))}</p>";

    // The first session the exchange trades after a night, the open its picks buy at.
    static DateOnly Open(DateOnly night)
    {
        var day = night.AddDays(1);

        while (!Core.Bars.ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }

    // Each earlier version's account drawn apart beneath the four, labelled with its number and never added to them.
    static string ApartCharts(JudgeView view) =>
        string.Concat(view.Earlier.Select(version =>
            $"<div class=\"judge-apart\" data-apart=\"{version.Number.ToString(CultureInfo.InvariantCulture)}\"><p class=\"judge-apart-h\"><b>{Escaped(VersionWords(version))}, apart from the record.</b> Its own account, the same money, sizing and costs, never added to the four above.</p>"
            + JudgeChart([new JudgeLineView(VersionWords(version) + ", its own account", version.Values)], [], VersionWords(version) + "'s own account") + "</div>"));

    // From 24.13, until any of the judge's live picks has been bought, why the scoreboard and the four accounts hold nothing,
    // the night and the open read from its first stored picks; none once one is bought, or before it has picked.
    public static string? EmptyRecord(JudgeView view) =>
        view.Bought || view.FirstNight is not { } night || view.FirstOpen is not { } open
            ? null
            : "<p class=\"empty\" data-empty=\"record\" data-summary>" + Escaped(EmptyRecordWords(night, open, view.RecordNumber)) + "</p>";

    public static string EmptyRecordWords(DateOnly night, DateOnly open, int? number = null) =>
        (number is { } version ? string.Create(CultureInfo.InvariantCulture, $"Version {version}'s first picks") : "The judge's first picks")
        + ", made on the night of " + night.ToString("d MMM", CultureInfo.InvariantCulture)
        + ", are bought at the open on " + open.ToString("ddd d MMM", CultureInfo.InvariantCulture)
        + (number is null ? ". The accounts start from then." : ". Its record and the four accounts start from then.");

    static string Scoreboard(JudgeView view)
    {
        var score = view.Score;
        var html = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<p data-summary>{(score.Closed == 0 ? "None of the judge's trades has closed yet" : $"{score.Closed} of the judge's trades have closed, {score.Winners} of them winners")}; a firm verdict waits for {score.Needed}.</p>"));

        html.Append("<div class=\"judge-tiles\">");

        html.Append(CultureInfo.InvariantCulture, $"<div class=\"tile\" data-tile=\"closed\"><b>{score.Closed}</b> trades closed<span>{score.Closed} of {score.Needed} needed for a firm verdict</span></div>");
        html.Append(score.Closed == 0
            ? "<div class=\"tile\" data-tile=\"winners\"><b>None</b> closed yet</div>"
            : string.Create(CultureInfo.InvariantCulture, $"<div class=\"tile\" data-tile=\"winners\"><b>{100.0 * score.Winners / score.Closed:0}%</b> were winners</div>"));
        html.Append(score.PerDollar is { } made ? $"<div class=\"tile\" data-tile=\"made\"><b>{Dollars(made)}</b> made per $1 risked</div>" : string.Empty);
        html.Append(score.OverSamePlan is { } over ? $"<div class=\"tile\" data-tile=\"over\"><b>{Dollars(over)}</b> {(over < 0 ? "less" : "more")} per $1 than the same plans bought on every stock those nights</div>" : string.Empty);
        html.Append("</div>");
        html.Append($"<p class=\"judge-verdict\" data-verdict=\"{Escaped(view.AgainstRandom)}\"><b>{Escaped(view.AgainstRandom)}</b> against random picks so far. Reads \"Better\", \"Worse\" or \"Not told apart yet\".</p>");

        return html.ToString();
    }

    // The cards of the record's newest night, or from the 24.14 correction of 2026-11-21 of an earlier version, each of those
    // saying what that version's account did with it and its prices worked from the price it paid once bought.
    static string PickCards(IReadOnlyList<JudgeCardView> picks, JudgeScoreboard score, JudgeVersionView? version)
    {
        if (picks.Count == 0)
        {
            return string.Empty;
        }

        var html = new StringBuilder("<div class=\"home-ideas judge-ideas\">");

        foreach (var pick in picks)
        {
            var held = pick.Held is { Skipped: null } taken ? taken : null;
            var from = held?.Paid ?? pick.LastClose;
            var stop = held?.Stop ?? pick.Stop;
            var target = held?.Target ?? pick.Target;
            var risk = from - stop;
            var moves = JudgeMenu.HoldOf(pick.Hold)?.StopMoves;
            var whole = string.Equals(pick.Shape, JudgeMenu.Whole, StringComparison.Ordinal);
            var night = pick.Night is { } on ? "on the night of " + on.ToString("d MMM", CultureInfo.InvariantCulture) : "tonight";

            // From 24.14 Home's pick card: its head and tags, the sentence in quotes, the facts the judge named and the rest of
            // its sheet folded, the price boxes, the judge's record and the gain and loss bar.
            html.Append($"<article class=\"home-idea judge-card\" data-ticker=\"{Escaped(pick.Ticker)}\">");
            html.Append($"<div class=\"home-idea-h\"><div><div class=\"home-ticker\"><a href=\"{MarkRenderer.ChartRoute}{Escaped(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></div><div class=\"home-co\">{Escaped(pick.Company ?? string.Empty)}</div></div><span class=\"tag-stack\"><span class=\"ai-tag\">AI judge</span><span class=\"ai-note\">Not one of the rules' buys</span></span></div>");
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"home-verdict judge-saw\"><b>The AI judge's pick, ranked {pick.Rank} of {pick.Of} {night}.</b> It saw {Escaped(pick.Sector is null or JudgeFacts.NotOnFile ? "no sector on file" : pick.Sector)}, {Escaped(SizeWords(pick.Size))}, with no name, ticker or date.</p>");

            if (version is not null)
            {
                html.Append($"<p class=\"judge-since\" data-since>{Escaped(HeldWords(pick, version))}</p>");
            }

            html.Append("<p class=\"facts-h\">Why the AI picked it, in its words</p>");
            html.Append($"<p class=\"ai-quote\">\"{Escaped(pick.Reason)}\"<small>Its sentence, as the local model wrote it.</small></p>");

            if (pick.Named is { } named)
            {
                html.Append("<p class=\"facts-h\">The AI's reasons: the facts it named, with their values</p>");
                html.Append("<ul class=\"ai-facts\" data-facts=\"named\">").Append(string.Concat(named.Select(fact => FactItem(fact, "why")))).Append("</ul>");
                html.Append(CultureInfo.InvariantCulture, $"<details class=\"all-facts\"><summary>Everything else it saw ({pick.Rest.Count} facts)</summary><ul class=\"ai-facts\" data-facts=\"rest\">").Append(string.Concat(pick.Rest.Select(fact => FactItem(fact, string.Empty)))).Append("</ul></details>");
            }
            else
            {
                html.Append($"<p class=\"facts-h\" data-facts=\"before\">{Escaped(JudgeBeforeNamed)}</p>");
                html.Append("<ul class=\"ai-facts\" data-facts=\"sheet\">").Append(string.Concat(pick.Rest.Select(fact => FactItem(fact, string.Empty)))).Append("</ul>");
            }

            html.Append("<div class=\"home-prices\">");
            html.Append(held is not null
                ? $"<div><span>Bought at</span><b>${Figures.Price(held.Paid)}</b><span class=\"sale-note judge-buy\">At the open on {held.Bought.ToString("ddd d MMM", CultureInfo.InvariantCulture)}. Last close before it ${Figures.Price(pick.LastClose)}.</span></div>"
                : $"<div><span>Buys at</span><b>the next open</b><span class=\"sale-note judge-buy\">At whatever price it opens. Last close ${Figures.Price(pick.LastClose)}.</span></div>");
            html.Append(CultureInfo.InvariantCulture, $"<div><span>Sell if it closes below</span><b class=\"stop\">${Figures.Price(stop)}</b><span class=\"sale-note judge-stop\">{moves:0.##} typical daily move{(moves == 1 ? string.Empty : "s")} under the price it {(held is null ? "pays" : "paid")}.</span></div>");
            html.Append($"<div data-sale=\"target\"><span>{(whole ? "Take profit at" : "Take profit on half at")}</span><b class=\"aim\">${Figures.Price(target)}</b><span class=\"sale-note judge-target\">2 times the risk: ${Figures.Price(2 * risk)} up for ${Figures.Price(risk)} down.</span></div>");

            if (!whole)
            {
                html.Append($"<div data-sale=\"trail\"><span>The other half</span><b class=\"aim\">Trails</b><span class=\"sale-note\">Sells on a close under a give-up price that rises behind its highest close, ${Figures.Price(held?.Trail ?? pick.GiveUp ?? risk)} under it. Its stop moves up to the price it paid once the first half sells.</span></div>");
            }

            html.Append($"<div data-sale=\"limit\"><span>Otherwise sell after</span><b class=\"aim judge-hold\">{Escaped(pick.Hold)}</b></div>");
            html.Append("</div>");
            html.Append($"<p class=\"home-cond\">{(held is null ? "The prices are worked from the last close until it is bought, then again from the price it actually paid." : "The prices are worked from the price it paid.")} {Escaped(JudgeMenuLine)}</p>");
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"setup-record\"><span class=\"rec-h\">{(version is null ? "The AI judge's record" : Escaped(VersionWords(version)) + ": its own account")}</span><span>{(score.Closed == 0 ? $"Too early to judge: <b>0</b> of {score.Needed} trades closed." : $"<b>{score.Closed}</b> of {score.Needed} trades closed{(score.PerDollar is { } made ? $", {Dollars(made)} made per $1 risked" : string.Empty)}.")} {(version is null ? "It is measured against the rules, 1,000 random picks and holding the index, with the same money, sizing and costs." : "Kept apart from the record, with the same money, sizing and costs.")}</span><span class=\"rec-sub\">{(version is null ? "The judge's record over every pick it made, not this stock's chance." : "This version's record over every pick it made, not this stock's chance.")}</span></div>");
            html.Append($"<div class=\"home-rr\" role=\"img\" aria-label=\"Risks $1 to make about $2.00\"><div class=\"risk\" style=\"flex:1\"></div><div class=\"reward\" style=\"flex:2\"></div></div>");
            html.Append($"<div class=\"home-odds\">Risks $1 to make about $2.00{(whole ? string.Empty : " on the first half")}</div>");
            html.Append("</article>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    public const string JudgeBeforeNamed = "Made before the judge named its facts: everything it saw";

    // What an earlier version's account did with a pick: waiting for its open, skipped and why, bought and still open, or
    // bought and sold with its result per $1 risked.
    public static string HeldWords(JudgeCardView pick, JudgeVersionView version)
    {
        var account = string.Create(CultureInfo.InvariantCulture, $"version {version.Number}'s own account");

        static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);

        return pick.Held switch
        {
            null => $"Buys at the next open after its night in {account}.",
            { Skipped: { } why } => $"Not bought in {account}: {why}.",
            { Ended: { } ended, Sold: { } sold } held => $"Bought on {Day(held.Bought)} at ${Figures.Price(held.Paid)} and sold on {Day(ended)} at ${Figures.Price(sold)}"
                + (held.Result is { } made ? $", {(made < 0 ? "losing" : "making")} {Dollars(Math.Abs(made))} per $1 risked" : string.Empty) + $", in {account}.",
            { } held => $"Bought on {Day(held.Bought)} at ${Figures.Price(held.Paid)} and still open in {account}.",
        };
    }

    static string FactItem(JudgeFactView fact, string kind) =>
        $"<li class=\"{kind}\"><span>{Escaped(fact.Words)}</span><b>{Escaped(fact.Value)}</b>{(fact.Comparison is { } said ? $"<span class=\"cmp\">{Escaped(said)}</span>" : string.Empty)}</li>";

    static string SizeWords(string? size) => size switch
    {
        null or "" or JudgeFacts.NotOnFile => "a company whose size is not on file",
        _ => "a " + size + " company",
    };

    static string LookedSummary(JudgeView view)
    {
        int Picks(string group) => view.Looked.FirstOrDefault(row => row.Group == group)?.Picks ?? 0;

        return string.Create(CultureInfo.InvariantCulture, $"<p data-summary>{Picks(JudgeScore.NoSetupFound)} of its picks were stocks no rule found, it shared {Picks(JudgeScore.Shared)} with the rules, and it passed over {Picks(JudgeScore.PassedOver)} of theirs.</p>");
    }

    static string Looked(JudgeView view)
    {
        var html = new StringBuilder("<table class=\"judge-looked\"><thead><tr><th>Group</th><th>Picks</th><th>Closed</th><th>Per $1 risked</th></tr></thead><tbody>");

        foreach (var group in view.Looked)
        {
            var words = group.Group switch
            {
                JudgeScore.NoSetupFound => "Stocks the AI chose that no rule picked that night.",
                JudgeScore.Shared => "Stocks the AI and a rule both chose.",
                _ => "Stocks a rule chose that the AI did not keep.",
            };

            html.Append(CultureInfo.InvariantCulture, $"<tr data-group=\"{Escaped(group.Group)}\"><td><b>{Escaped(group.Group)}</b><br>{Escaped(words)}</td><td>{group.Picks}</td><td>{group.Closed}</td><td>{(group.PerDollar is { } made ? Dollars(made) : "none closed")}</td></tr>");
        }

        html.Append("</tbody></table>");

        return html.ToString();
    }

    // The four accounts as their return from the first session drawn, each a line, the random accounts' band shaded behind; or
    // from the 24.14 correction of 2026-11-21 an earlier version's own account alone.
    static string JudgeChart(IReadOnlyList<JudgeLineView> drawn, IReadOnlyList<JudgeBand> drawnBand, string label)
    {
        var lines = drawn.Where(line => line.Values.Count > 0).ToArray();

        if (lines.Length == 0)
        {
            return "<p class=\"empty\">No account has a session to draw yet.</p>";
        }

        var sessions = lines.SelectMany(line => line.Values.Select(value => value.Session)).Distinct().Order().ToArray();
        var at = sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var returns = lines.Select(line => (line.Name, Points: line.Values.Select(value => (at[value.Session], ReturnFrom(value.Value, line.Values[0].Value))).ToArray())).ToArray();
        var band = drawnBand.Where(row => at.ContainsKey(row.Session)).Select(row => (X: at[row.Session], Low: ReturnFrom(row.Low, drawnBand[0].Median), High: ReturnFrom(row.High, drawnBand[0].Median))).ToArray();
        var all = returns.SelectMany(line => line.Points.Select(point => point.Item2)).Concat(band.SelectMany(row => new[] { row.Low, row.High })).Append(0.0).ToArray();
        var (low, high) = (all.Min(), all.Max());

        if (high - low < 0.01)
        {
            (low, high) = (low - 0.01, high + 0.01);
        }

        const double Width = 720, Height = 240;

        string X(int index) => (sessions.Length == 1 ? Width / 2 : index * Width / (sessions.Length - 1)).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double value) => (Height - ((value - low) / (high - low) * Height)).ToString("0.#", CultureInfo.InvariantCulture);

        var svg = new StringBuilder($"<svg class=\"judge-chart\" viewBox=\"0 0 {Width} {Height}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"{Escaped(label)}\">");

        if (band.Length > 0)
        {
            var edge = band.Select(row => $"{X(row.X)},{Y(row.High)}").Concat(band.Reverse().Select(row => $"{X(row.X)},{Y(row.Low)}"));

            svg.Append($"<polygon class=\"judge-band\" points=\"{string.Join(' ', edge)}\"/>");
        }

        svg.Append($"<line class=\"judge-zero\" x1=\"0\" x2=\"{Width}\" y1=\"{Y(0)}\" y2=\"{Y(0)}\"/>");

        for (var line = 0; line < returns.Length; line++)
        {
            svg.Append($"<polyline class=\"judge-line judge-line-{line}\" points=\"{string.Join(' ', returns[line].Points.Select(point => $"{X(point.Item1)},{Y(point.Item2)}"))}\"><title>{Escaped(returns[line].Name)}</title></polyline>");
        }

        svg.Append("</svg>");

        var legend = string.Concat(returns.Select((line, index) => string.Create(CultureInfo.InvariantCulture, $"<li class=\"judge-key-{index}\">{Escaped(line.Name)}: {Figures.Share(line.Points[^1].Item2)}</li>")));

        return $"{svg}<ul class=\"judge-key\">{legend}</ul>";
    }

    // An account's value over the value it started at, less one, as the chart's return: the judge's chart's one crossing.
    static double ReturnFrom(decimal value, decimal start) => start > 0m ? Core.Prices.Statistic.FromRatio((value / start) - 1m) : 0.0;

    static string Dollars(double value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture);
}
