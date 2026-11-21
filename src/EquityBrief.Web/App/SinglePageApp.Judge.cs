using System.Globalization;
using System.Text;
using EquityBrief.Core.Judge;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One of the AI judge's picks as its card draws it: its rank of the night's picks, the stock and its company, the sector and
// size the judge was shown, its reason, the last close, the stop and take-profit code worked from it, the give-up distance
// where the rest follows one, and the hold and take-profit it chose.
public sealed record JudgeCardView(int Rank, int Of, string Ticker, string? Company, string? Sector, string? Size, string Reason, decimal LastClose, decimal Stop, decimal Target, decimal? GiveUp, string Hold, string Shape);

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
        region.Append(Cards.Computed("AI judge", $"<p data-summary>{Escaped(JudgeLede)}</p>{RunLine(view)}", title: "Your local AI picks its own stocks, in its own paper account", region: "judge-lede"));
        region.Append(Cards.Computed("Scoreboard", EmptyRecord(view) ?? Scoreboard(view), title: "How its live record stands", region: "judge-score"));
        region.Append(Cards.Computed("Four accounts", EmptyRecord(view) ?? "<p data-summary>The judge's account beside the rules', random picks' and the index's, each from the judge's first night.</p>" + JudgeChart(view), title: "Four accounts, same money, same sizing, same costs", lede: Escaped(JudgeReplayedLine), region: "judge-chart"));
        region.Append(Cards.Computed("Tonight", $"<p data-summary>{(view.Picks.Count == 0 ? "The judge picked no stock tonight." : $"The judge picked {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} tonight, best first.")}</p>" + PickCards(view), title: $"The AI judge's picks tonight ({view.Picks.Count})", lede: "Each card shows what the AI saw, why it picked the stock, and the plan code set for it. Each buys at the next open, at whatever price it opens; nobody knows that price beforehand.", region: "judge-picks"));
        region.Append(Cards.Computed("Where the AI looked", LookedSummary(view) + Looked(view), title: "How its picks compare with the rules', since it started", region: "judge-looked"));

        if (view.Replayed is { } replayed)
        {
            region.Append(Cards.Computed("Replayed", $"<p data-replayed=\"record\" data-summary>Replayed, {view.ReplayedNights} night{(view.ReplayedNights == 1 ? string.Empty : "s")}: {(replayed.PerDollar is { } made ? $"{replayed.Closed} trades closed, {Dollars(made)} made per $1 risked" : "no trade closed yet")}. This is not the live record and is never added to it.</p>", title: "Replayed nights, apart from the live record", region: "judge-replayed"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The line Home draws for the judge: what it picked tonight, and its live record per $1 risked beside random picks'.
    public static string JudgeHomeLine(JudgeView view) =>
        $"<p class=\"judge-home\" data-home=\"judge\"><b>AI judge</b> Picked {view.Picks.Count} stock{(view.Picks.Count == 1 ? string.Empty : "s")} tonight in its own paper account. "
        + (view.Score.PerDollar is { } made
            ? $"Since it started: {Dollars(made)} per $1 risked, against {(view.RandomPerDollar is { } random ? Dollars(random) : "nothing yet")} for random picks. "
            : "No trade of it has closed yet. ")
        + $"<a href=\"{JudgeRoute}\">See the AI judge</a></p>";

    static string RunLine(JudgeView view) =>
        view.Night is not { } night ? "<p class=\"empty\">The judge has not run yet. The night starts it after its last step.</p>"
        : view.Outcome is "picked" or "none" or null ? string.Empty
        : $"<p class=\"judge-run\" data-outcome=\"{Escaped(view.Outcome)}\">The judge's run for {night.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} was {Escaped(view.Outcome)}{(string.IsNullOrEmpty(view.Why) ? string.Empty : ": " + Escaped(view.Why))}. A night it misses is never filled in later.</p>";

    // From 24.13, until any of the judge's live picks has been bought, why the scoreboard and the four accounts hold nothing,
    // the night and the open read from its first stored picks; none once one is bought, or before it has picked.
    public static string? EmptyRecord(JudgeView view) =>
        view.Bought || view.FirstNight is not { } night || view.FirstOpen is not { } open
            ? null
            : "<p class=\"empty\" data-empty=\"record\" data-summary>" + Escaped(EmptyRecordWords(night, open)) + "</p>";

    public static string EmptyRecordWords(DateOnly night, DateOnly open) =>
        "The judge's first picks, made on the night of " + night.ToString("d MMM", CultureInfo.InvariantCulture)
        + ", are bought at the open on " + open.ToString("ddd d MMM", CultureInfo.InvariantCulture)
        + ". The accounts start from then.";

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

    static string PickCards(JudgeView view)
    {
        if (view.Picks.Count == 0)
        {
            return string.Empty;
        }

        var html = new StringBuilder();

        foreach (var pick in view.Picks)
        {
            var risk = pick.LastClose - pick.Stop;
            var moves = JudgeMenu.HoldOf(pick.Hold)?.StopMoves;

            html.Append($"<article class=\"judge-card\" data-ticker=\"{Escaped(pick.Ticker)}\">");
            html.Append($"<div class=\"judge-card-h\"><a href=\"{MarkRenderer.ChartRoute}{Escaped(pick.Ticker)}\">{Escaped(pick.Ticker)}</a> {Escaped(pick.Company ?? string.Empty)} <span class=\"tag\">AI judge</span> <span class=\"tag\">Not one of the rules' buys</span></div>");
            html.Append($"<p><b>Why the AI picked it.</b> {Escaped(pick.Reason)}</p>");
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"judge-saw\">It saw: {Escaped(pick.Sector ?? "no sector on file")}, {Escaped(SizeWords(pick.Size))}. No name, ticker or date. Ranked {pick.Rank} of {pick.Of} tonight.</p>");
            html.Append($"<p class=\"judge-buy\">{Escaped(JudgeBuysAtTheOpen)} Last close ${Figures.Price(pick.LastClose)}. The prices below are worked from the last close until it is bought, then from the price it paid.</p>");
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"judge-stop\">Sell if it closes below ${Figures.Price(pick.Stop)}, {moves:0.##} typical daily move{(moves == 1 ? string.Empty : "s")} under its buy price.</p>");
            html.Append(string.Equals(pick.Shape, JudgeMenu.Whole, StringComparison.Ordinal)
                ? $"<p class=\"judge-target\">Take profit at ${Figures.Price(pick.Target)}, 2 times the risk: ${Figures.Price(2 * risk)} up for ${Figures.Price(risk)} down.</p>"
                : $"<p class=\"judge-target\">Take profit on half at ${Figures.Price(pick.Target)}, 2 times the risk: ${Figures.Price(2 * risk)} up for ${Figures.Price(risk)} down. Sell the other half on a close under a give-up price that rises behind its highest close, ${Figures.Price(pick.GiveUp ?? risk)} under it. Its stop sits at the buy until then.</p>");
            html.Append($"<p class=\"judge-hold\">Otherwise sell after {Escaped(pick.Hold)}.</p>");
            html.Append($"<p class=\"note\">{Escaped(JudgeMenuLine)}</p>");
            html.Append("</article>");
        }

        return html.ToString();
    }

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

    // The four accounts as their return from the first session drawn, each a line, the random accounts' band shaded behind.
    static string JudgeChart(JudgeView view)
    {
        var lines = view.Lines.Where(line => line.Values.Count > 0).ToArray();

        if (lines.Length == 0)
        {
            return "<p class=\"empty\">No account has a session to draw yet.</p>";
        }

        var sessions = lines.SelectMany(line => line.Values.Select(value => value.Session)).Distinct().Order().ToArray();
        var at = sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var returns = lines.Select(line => (line.Name, Points: line.Values.Select(value => (at[value.Session], ReturnFrom(value.Value, line.Values[0].Value))).ToArray())).ToArray();
        var band = view.Band.Where(row => at.ContainsKey(row.Session)).Select(row => (X: at[row.Session], Low: ReturnFrom(row.Low, view.Band[0].Median), High: ReturnFrom(row.High, view.Band[0].Median))).ToArray();
        var all = returns.SelectMany(line => line.Points.Select(point => point.Item2)).Concat(band.SelectMany(row => new[] { row.Low, row.High })).Append(0.0).ToArray();
        var (low, high) = (all.Min(), all.Max());

        if (high - low < 0.01)
        {
            (low, high) = (low - 0.01, high + 0.01);
        }

        const double Width = 720, Height = 240;

        string X(int index) => (sessions.Length == 1 ? Width / 2 : index * Width / (sessions.Length - 1)).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double value) => (Height - ((value - low) / (high - low) * Height)).ToString("0.#", CultureInfo.InvariantCulture);

        var svg = new StringBuilder($"<svg class=\"judge-chart\" viewBox=\"0 0 {Width} {Height}\" preserveAspectRatio=\"none\" role=\"img\" aria-label=\"The four accounts' returns since the judge started\">");

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
