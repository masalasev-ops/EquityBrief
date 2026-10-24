using System.Text;

namespace EquityBrief.Web.Marks;

// One line of a pick's card as the night stored it: its place, its name, its verdict, being a tick, a note or a
// warning, and its reason in words.
public sealed record CardLineView(int Number, string Name, string Verdict, string Words);

// The record of the rule a card names, as the card read it: the rule in words, the trades its replay kept over the
// pulled history with the history's first and last session and whether membership was read as it stood, the share
// that ended at its target where the rule sets one, the mean result in its unit, the median sessions held and the
// sessions by which the card's share of the trades had ended, and the median of the lowest close each was held through.
public sealed record CardRecordView(
    string Rule,
    int Trades,
    double? Won,
    double? Average,
    string Unit,
    int? MedianSessions,
    int? HeldSessions,
    double HeldShare,
    double? WorstClose,
    DateOnly From,
    DateOnly Through,
    string Membership);

// A pick's plan in the operator's money as the card's one function worked it: the shares, the dollars at risk, the
// position's value and its share of the account, the round trip in dollars, whether the cap sized it, and whether the
// whole position is at risk for a holding with no stop.
public sealed record CardMoneyView(long Shares, decimal AtRisk, decimal Value, decimal ShareOfAccount, decimal? RoundTrip, bool Capped, bool WholeAtRisk, decimal RiskPercent);

// A trade the operator took from the card: when, at what fill and for which session, whether the fill is still the
// plan's buy, and its exit where one is recorded; removable only before a night has followed it.
public sealed record CardTakenView(string TakenAt, decimal Fill, DateOnly FillDate, bool Provisional, decimal? ExitPrice, DateOnly? ExitDate, bool Followed, DateOnly? EndedOn = null, string? EndReason = null, decimal? EndPrice = null)
{
    public bool Ended => ExitPrice is not null || EndedOn is not null;
}

// What could hit the trade as the night stored it: the hold's last session, the stock's reactions, the next ex-dividend
// date inside the hold and the market events inside it, with each kind whose table ends before the hold does.
public sealed record CardHitsView(
    DateOnly Through,
    int Reactions,
    double? MedianTypical,
    double? MedianRisks,
    int PastTheStop,
    DateOnly? DividendDate,
    bool DividendDeclared,
    decimal? DividendAmount,
    double? DividendRisks,
    double? DividendPercent,
    IReadOnlyList<(DateOnly Date, string Name)> Events,
    IReadOnlyList<string> PastTheTable,
    bool DividendUnread = false);

// The operator's own record of the card's family on its index, as the night's follower wrote it.
public sealed record CardOperatorRecordView(string Unit, int Won, int Lost, int Ended, int Open, double? Average, int Minimum, bool Trailing, CardSameNightsView? SameNights = null);

// The rule's own picks listed on the nights the operator took one, each bought at the next session's open: how many nights,
// how many picks, won, lost and ended, and the average once enough have ended.
public sealed record CardSameNightsView(int Nights, int Listed, int Won, int Lost, int Ended, double? Average);

// The learned score's part of a pick's card as the night stored it: the pick's rank among the setups the score learned on
// on the card's index, none until the score passed the tester there, and the setups like the pick under its rule.
public sealed record CardScoreView(int? Rank, EquityBrief.Core.Cards.CardSimilar Similar);

// A pick's card on a night: the index, the night, the family that listed it and the stock, the rule the card names, the
// plan's prices, the checklist's lines and the rule's record, none where the rule has not been replayed; and, where the
// page draws them, the plan in the operator's money or that the account is not set, the rule's management of the trade,
// the trades taken from it and whether its presses are drawn, which an export never does; for a family a learned score
// reaches, the score's part; the words of a change an approval set on the rule, whose stored record replays the
// setting before it and is drawn as no record of this rule; and the plan's time limit and its rising stop's distance, as
// the night stored them.
public sealed record DecisionCardView(
    string Index,
    DateOnly Night,
    string Family,
    string Ticker,
    string Rule,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    IReadOnlyList<CardLineView> Lines,
    CardRecordView? Record,
    IReadOnlyList<string>? Management = null,
    CardMoneyView? Money = null,
    bool AccountUnset = false,
    IReadOnlyList<CardTakenView>? Taken = null,
    bool Pressable = false,
    CardHitsView? Hits = null,
    CardOperatorRecordView? OperatorRecord = null,
    CardScoreView? Score = null,
    string? Approved = null,
    BehindItView? Behind = null,
    int? Cap = null,
    decimal? Trail = null)
{
    // The id the card's row and the control opening it share.
    public string Id => $"card-{Index}-{Family}-{Ticker}".ToLowerInvariant().Replace('.', '-').Replace(' ', '-');
}

// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
// see: Each buy point's row folds its pick's card alone, and the levels plan stays on the stock's full page
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public sealed partial class MarkRenderer
{
    // The sentences the setup's record is drawn under, which a reader of the card reads first.
    public const string RecordIsTheRules = "This is the setup's record over every stock it bought, not this stock's chance.";

    public const string RecordReadsHigh = "Its settings were chosen on these same years, so the figures are optimistic.";

    // What the card says beneath its plan, since the stock's full page draws the stock's levels as well.
    public static string OnePlanSays(string setup) =>
        $"This is the {setup}'s own plan and the only one shown. The support and resistance levels on the stock's full page are background, not a second plan.";

    // The control on a pick's row that opens its card in place beneath the row.
    public static string CardToggle(DecisionCardView card) =>
        $" <button type=\"button\" class=\"card-toggle\" aria-expanded=\"false\" aria-controls=\"{Escaped(card.Id)}\" title=\"open this pick's plan and checks\">Open plan</button>";

    // A pick's card in a row of its own beneath the pick's, hidden until its control opens it, held inside the width of the
    // box its table scrolls in.
    public string CardRow(DecisionCardView card, int columns) =>
        Formatted($"<tr class=\"card-row\" id=\"{Escaped(card.Id)}\" hidden><td colspan=\"{columns}\"><div class=\"row-open\">{DecisionCard(card, linked: true)}</div></td></tr>");

    // A pick's card in the reader's words: a sentence saying which setup picked the stock, when, and at about what price;
    // a ticked sentence for each step it passed, from the steps and values the night stored; the setup's own plan, the
    // only plan the card draws, with the reward against the risk where the pick's row states it; the card's other
    // checks, each advising and removing no pick; what could go wrong before the trade ends; the setup's record under the
    // setup's own heading and never the stock's; the setups most like it; how the stock compares with its index's stocks
    // tonight; the operator's own trades of the setup; the control recording a trade bought; and, where the card opens
    // beneath a row, the link to the stock's full page, which alone draws the stock's levels.
    public string DecisionCard(DecisionCardView card, FamilyPickCell? pick = null, bool linked = false)
    {
        var body = new StringBuilder();
        var setup = SetupOf(card.Family);

        body.Append(Invariant, $"<div class=\"decision-card\" data-index=\"{Escaped(card.Index)}\" data-family=\"{Escaped(card.Family)}\" data-ticker=\"{Escaped(card.Ticker)}\" data-night=\"{DayOf(card.Night)}\">");
        body.Append(Invariant, $"<p class=\"card-summary\" data-summary>{Escaped(CardSummary(card, setup))}</p>");
        body.Append(WhyItQualified(card));
        body.Append(ThePlan(card, setup, pick));
        body.Append(OtherChecks(card));
        body.Append(Hits(card));
        body.Append(RuleRecord(card));
        body.Append(ScorePart(card, setup));
        body.Append(card.Behind is { } behind ? Compares(behind) : string.Empty);
        body.Append(OperatorRecord(card));
        body.Append(Taken(card));
        body.Append(linked ? Formatted($"<p class=\"card-full\"><a href=\"#/name/{Uri.EscapeDataString(card.Ticker)}\">The stock's full page</a></p>") : string.Empty);
        body.Append("</div>");

        return body.ToString();
    }

    // A setup's name as a sentence carries it: the earnings drift, the sector heavyweights.
    static string SetupOf(string family) =>
        family == EquityBrief.Core.Families.HeavyweightRule.Name
            ? "sector heavyweights"
            : (EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family).ToLowerInvariant();

    // The card's opening sentence: the setup that picked the stock and the night, the buy at the next trading day's open at
    // about the plan's price, and what the setup buys, in a line.
    static string CardSummary(DecisionCardView card, string setup)
    {
        var bought = card.Entry is { } entry
            ? Formatted($" and bought at the next trading day's open, at about {Price(entry)}")
            : ", with no buy price stored for it";
        var buys = SetupWords.TryGetValue(card.Family, out var words) ? " " + words.Plain : string.Empty;

        return FormattableString.Invariant($"Picked by the {setup} on {card.Night:d MMM yyyy}{bought}.{buys}");
    }

    // A step's sentence on the card's first line, the step's name before it.
    sealed record PassedStep(string Step, string Reason);

    // The names an S&P 400 or 600 card's first line gives its steps beside those its setup's list holds, as its night's
    // settings state them.
    static readonly HashSet<string> IndexSteps = new(StringComparer.Ordinal)
    {
        "its rule",
        "its setting",
        "the business",
        "profit in the index's form",
        "its change approved on the Loop page",
    };

    // The steps a card's first line names, each with its sentence: the line's words after its colon split at each
    // semicolon, a piece opening on no step its setup's list or an index's settings name joined back to the step before
    // it, and each step's name before its first comma. A step the list holds opens on what a stock that passed it did,
    // and its stored values follow, said plainly; another is said plainly whole.
    static IReadOnlyList<(string Step, string Said)> PassedSteps(string family, string words)
    {
        var colon = words.IndexOf(": ", StringComparison.Ordinal);

        if (colon < 0)
        {
            return [(string.Empty, Capital(Said(words)))];
        }

        var steps = new List<PassedStep>();

        foreach (var piece in words[(colon + 2)..].TrimEnd('.').Split("; "))
        {
            var comma = piece.IndexOf(", ", StringComparison.Ordinal);
            var name = comma < 0 ? piece : piece[..comma];

            if (steps.Count > 0 && ReaderWords.Passed(family, name) is null && !IndexSteps.Contains(name))
            {
                steps[^1] = steps[^1] with { Reason = steps[^1].Reason + "; " + piece };

                continue;
            }

            steps.Add(new PassedStep(name, comma < 0 ? string.Empty : piece[(comma + 2)..]));
        }

        return
        [
            .. steps.Select(step => (step.Step, ReaderWords.Passed(family, step.Step) is { } passed
                ? Capital(passed) + (step.Reason.Length > 0 ? ": " + Said(step.Reason) : string.Empty) + "."
                : Capital(Said(step.Reason.Length > 0 ? step.Step + ", " + step.Reason : step.Step)) + ".")),
        ];
    }

    // Words the night stored, said as the reader reads them: its counts in words and its long figures as prices are, then
    // its phrasings in the reader's words.
    static string Said(string stored) => ReaderWords.Plain(StoredWords(stored));

    // Why it qualified: a ticked sentence for each step the setup passed, read from the card's first line.
    static string WhyItQualified(DecisionCardView card)
    {
        if (card.Lines.FirstOrDefault(line => line.Name == EquityBrief.Core.Cards.CardLines.TrendName) is not { } passed)
        {
            return string.Empty;
        }

        var body = new StringBuilder(Formatted($"<section class=\"card-why\" data-line=\"{passed.Number}\" data-verdict=\"{Escaped(passed.Verdict)}\"><h5>Why it qualified</h5><ul class=\"card-ticks\">"));

        foreach (var (step, said) in PassedSteps(card.Family, passed.Words))
        {
            body.Append(Invariant, $"<li class=\"card-tick\" data-step=\"{Escaped(step)}\">{CardMark(passed.Verdict)} <span class=\"card-words\">{WithSignedFigures(said)}</span></li>");
        }

        return body.Append("</ul></section>").ToString();
    }

    // A checklist line's mark drawn as a tick, a note's sign or a warning's sign, the verdict's word its label and its
    // title; a verdict none of the three is drawn as its word.
    static string CardMark(string verdict) =>
        Formatted($"<span class=\"card-mark\" data-verdict=\"{Escaped(verdict)}\" role=\"img\" aria-label=\"{Escaped(verdict)}\" title=\"{Escaped(verdict)}\">") + verdict switch
        {
            "tick" => "<svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M3.2 8.6l3 3 6.6-7\"/></svg>",
            "note" => "<svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><circle cx=\"8\" cy=\"8\" r=\"6.2\"/><path d=\"M8 7.3v4.2\"/><circle class=\"dot\" cx=\"8\" cy=\"4.9\" r=\"1\"/></svg>",
            "warning" => "<svg viewBox=\"0 0 16 16\" aria-hidden=\"true\"><path d=\"M8 1.9l6.6 11.6H1.4z\"/><path d=\"M8 6.4v3.6\"/><circle class=\"dot\" cx=\"8\" cy=\"11.7\" r=\"1\"/></svg>",
            _ => Escaped(verdict),
        } + "</span>";

    // The route of the settings page the plan links to where the account is not set.
    public const string AccountRoute = "#/account";

    // One tile of the plan or the record: what it says, the figure and the line beneath, with any attributes it carries.
    static string PlanTile(string tile, string label, string figure, string? beneath, string? figureClass = null, string? extra = null) =>
        Formatted($"<div class=\"plan-tile\" data-tile=\"{tile}\"{extra}><span class=\"tile-label\">{Escaped(label)}</span><b{(figureClass is null ? string.Empty : Formatted($" class=\"{figureClass}\""))}>{Escaped(figure)}</b>{(beneath is null ? string.Empty : Formatted($"<span class=\"tile-under\">{Escaped(beneath)}</span>"))}</div>");

    // The plan: the setup's own and the only one the card draws. The buy at the next trading day's open, the stop as a
    // close below it, the target or the rising stop or the exit that ends a trade with none, and the time limit with the
    // hold's last day; the reward against the risk where the pick's row states it; the plan in the operator's money where
    // the account is set and the link to the settings page where it is not; the setup's management of the trade, each
    // stop a close below it; and the line saying the stock's levels are no second plan.
    // see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
    static string ThePlan(DecisionCardView card, string setup, FamilyPickCell? pick)
    {
        var heavyweights = card.Family == EquityBrief.Core.Families.HeavyweightRule.Name;
        var dip = card.Family == EquityBrief.Core.Families.DipRule.Name;
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"card-plan\" data-entry=\"{StoredPrice(card.Entry)}\" data-stop=\"{StoredPrice(card.Stop)}\" data-target=\"{StoredPrice(card.Target)}\"><h5>The plan</h5><div class=\"plan-tiles\">");
        body.Append(PlanTile("buy", "Buy", "At the next day's open", card.Entry is { } entry ? Formatted($"at about {Price(entry)}") : "no buy price stored"));
        body.Append(card.Stop is { } stop
            ? PlanTile("stop", "Sell if it closes below", Price(stop), card.Trail is not null ? "raised after each higher close, never lowered" : dip ? "sold at the next day's open" : null, "loss")
            : PlanTile("stop", "Stop", "None", heavyweights ? "nothing sells it at a price set in advance" : "no stop stored"));
        body.Append(card.Target is { } target
            ? PlanTile("target", "Sell if it reaches", Price(target), null, "gain")
            : card.Trail is not null || pick?.Trailing == true
                ? PlanTile("target", "No target", "Rising stop", "the stop follows the price up")
                : dip
                    ? PlanTile("target", "No target", "Its exit", pick?.Exit is { } exit ? ReaderWords.Plain(exit) : "a close back above its short average")
                    : heavyweights
                        ? PlanTile("target", "No target", "Kept while it leads", "sold at a month's reshuffle once it stops leading")
                        : PlanTile("target", "Target", "None", "no target stored"));

        if (card.Cap is { } cap)
        {
            body.Append(PlanTile("limit", "Otherwise sell after", Formatted($"{cap} trading days"), card.Hits is { } hits ? FormattableString.Invariant($"about {hits.Through:d MMM yyyy}") : null));
        }

        body.Append("</div>");

        if (pick?.RewardToRisk is { } reward && card.Target is not null)
        {
            body.Append(Invariant, $"<p class=\"card-reward\" data-reward=\"{reward.ToString(Invariant)}\">You risk $1 to make ${Figures.Ratio(reward)}.</p>");
        }

        if (card.Money is { } money)
        {
            body.Append(Invariant, $"<p class=\"card-money\" data-shares=\"{money.Shares}\" data-at-risk=\"{money.AtRisk.ToString("0.00", Invariant)}\">");
            body.Append(Invariant, $"In your money: {money.Shares:N0} shares at {card.Entry?.ToString("0.00", Invariant)}, a position of ${money.Value.ToString("N2", Invariant)}, {money.ShareOfAccount:0.0%} of your account");
            body.Append(money.WholeAtRisk
                ? ". The whole position is at risk: nothing sells it at a price set in advance."
                : Formatted($", with ${money.AtRisk.ToString("N2", Invariant)} at risk to the stop at {card.Stop?.ToString("0.00", Invariant)}, the {money.RiskPercent.ToString("0.##", Invariant)}% a trade your settings give."));

            if (money.Capped)
            {
                body.Append(" Fewer shares than the risk allows: the position cap limits it.");
            }

            if (money.RoundTrip is { } trip)
            {
                body.Append(Invariant, $" Buying and selling costs about ${trip.ToString("N2", Invariant)} at the published rates.");
            }

            body.Append("</p>");
        }
        else if (card.AccountUnset)
        {
            body.Append(Invariant, $"<p class=\"card-money degraded\" data-account=\"unset\">Your account is not set, so the plan is drawn in prices alone: <a href=\"{AccountRoute}\">set your account's size and risk</a> to see it in shares and dollars.</p>");
        }

        if (card.Management is { Count: > 0 } steps)
        {
            body.Append("<ul class=\"card-management\">");

            foreach (var step in steps)
            {
                body.Append(Invariant, $"<li>{Escaped(ReaderWords.Plain(step))}</li>");
            }

            body.Append("</ul>");
        }

        body.Append(Invariant, $"<p class=\"card-note\">{Escaped(OnePlanSays(setup))}</p>");

        return body.Append("</section>").ToString();
    }

    // What each checklist line the card draws among its other checks is called on the card.
    static readonly IReadOnlyDictionary<string, string> CheckNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [EquityBrief.Core.Cards.CardLines.BusinessName] = "The business",
        [EquityBrief.Core.Cards.CardLines.MarketName] = "Market and sector",
        [EquityBrief.Core.Cards.CardLines.CostName] = "Cost to trade",
        [EquityBrief.Core.Cards.CardLines.ConcentrationName] = "Your other trades",
    };

    // Other checks: every checklist line but the steps it passed and its report's, each with its mark and the night's
    // words said plainly, none of them removing the pick.
    static string OtherChecks(DecisionCardView card)
    {
        var lines = card.Lines.Where(line => line.Name != EquityBrief.Core.Cards.CardLines.TrendName && line.Name != EquityBrief.Core.Cards.CardLines.EarningsName).ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var body = new StringBuilder("<section class=\"card-checks\"><h5>Other checks</h5><ul class=\"card-lines\">");

        foreach (var line in lines)
        {
            body.Append(Invariant, $"<li class=\"card-line\" data-line=\"{line.Number}\" data-verdict=\"{Escaped(line.Verdict)}\">");
            body.Append(CardMark(line.Verdict)).Append(' ');
            body.Append(Invariant, $"<b class=\"card-name\">{Escaped(CheckNames.GetValueOrDefault(line.Name) ?? ReaderWords.Plain(line.Name))}</b> ");
            body.Append(Invariant, $"<span class=\"card-words\">{WithSignedFigures(Said(line.Words))}</span></li>");
        }

        return body.Append("</ul><p class=\"card-note\">These advise and never remove a pick.</p></section>").ToString();
    }

    // What the control taking a trade records, said above it, since a fill box and a date box alone do not say that the
    // trade is the operator's own, that each night follows it, or what an empty box means.
    public const string TakenSays =
        "Bought this stock? Record it here, and each night follows your trade by the setup's own exits above and adds it to " +
        "your record. Leave Fill empty to use the next trading day's opening price once it is stored, and the date empty for " +
        "the trading day after this pick's night.";

    // The trades taken from the card and, where the page draws its presses, the control taking one, each taken trade
    // removable before a night has followed it and its exit recordable after.
    // see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
    static string Taken(DecisionCardView card)
    {
        var taken = card.Taken ?? [];

        if (!card.Pressable && taken.Count == 0)
        {
            return string.Empty;
        }

        var body = new StringBuilder("<section class=\"card-taken\"><h5>Your trade</h5>");

        foreach (var trade in taken)
        {
            body.Append(Invariant, $"<p class=\"card-taken-line\" data-taken-at=\"{Escaped(trade.TakenAt)}\" data-provisional=\"{(trade.Provisional ? "yes" : "no")}\">");
            body.Append(Invariant, $"You bought at {trade.Fill.ToString("0.00", Invariant)} for {DayOf(trade.FillDate)}");
            body.Append(trade.Provisional ? ", the plan's buy price until the next trading day's open is stored" : string.Empty);
            body.Append(trade.ExitPrice is { } exit && trade.ExitDate is { } on
                ? Formatted($"; you sold at {exit.ToString("0.00", Invariant)} on {DayOf(on)}.")
                : trade.EndedOn is { } ended && trade.EndPrice is { } sold
                    ? Formatted($"; the setup's own exit, its {Escaped(ReaderWords.Plain(trade.EndReason ?? "end"))}, ended it on {DayOf(ended)} at {sold.ToString("0.00", Invariant)}.")
                    : ".");
            body.Append("</p>");

            if (card.Pressable && !trade.Ended)
            {
                var path = $"{Escaped(card.Ticker)}/{Escaped(trade.TakenAt)}";

                if (!trade.Followed)
                {
                    body.Append(Invariant, $"<form class=\"card-press\" method=\"post\" action=\"/taken/remove/{path}\"><button type=\"submit\">Not taken</button></form>");
                }

                body.Append(Invariant, $"<form class=\"card-press\" method=\"post\" action=\"/taken/exit/{path}\"><label>Exit price <input name=\"price\" inputmode=\"decimal\" required></label> <label>on <input name=\"date\" type=\"date\" required></label> <button type=\"submit\">Record exit</button></form>");
            }
        }

        if (card.Pressable && taken.All(trade => trade.Ended))
        {
            body.Append(Formatted($"<p class=\"card-taken-says\">{TakenSays}</p>"));
            body.Append(Invariant, $"<form class=\"card-press\" method=\"post\" action=\"/taken/{Escaped(card.Index)}/{DayOf(card.Night)}/{Escaped(card.Family)}/{Escaped(card.Ticker)}\">");
            body.Append("<label>Fill <input name=\"price\" inputmode=\"decimal\" placeholder=\"the next open\"></label> <label>on <input name=\"date\" type=\"date\"></label> <button type=\"submit\">I bought this</button></form>");
        }

        return body.Append("<p class=\"card-said\" aria-live=\"polite\"></p></section>").ToString();
    }

    // One thing that could go wrong before the trade ends: when, what, and why it matters, with its checklist line's mark
    // where one stored it.
    static string Risk(string hit, string when, string what, string? why, string? extra = null, string? mark = null) =>
        Formatted($"<li data-hit=\"{hit}\"{extra}>{mark}<span class=\"hit-when\">{Escaped(when)}</span><span class=\"hit-what\">{WithSignedFigures(what)}{(why is null ? string.Empty : Formatted($"<span class=\"hit-why\">{Escaped(why)}</span>"))}</span></li>");

    // A market event's name in the reader's words, opening a line or inside a sentence.
    static string EventWords(string name, bool inSentence = false) => name switch
    {
        "FOMC decision" => "Fed rate decision",
        "CPI release" => inSentence ? "inflation report (CPI)" : "Inflation report (CPI)",
        _ => name,
    };

    // What could go wrong before the trade ends, each as the night stored it: the next report or that none is on file,
    // the stock's earlier reports' moves, the next ex-dividend date declared or estimated, and each market event inside the
    // hold, with a kind whose table ends first said to end.
    // see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
    static string Hits(DecisionCardView card)
    {
        var report = card.Lines.FirstOrDefault(line => line.Name == EquityBrief.Core.Cards.CardLines.EarningsName);

        if (card.Hits is not { } hits && report is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder(card.Hits is { } through
            ? FormattableString.Invariant($"<section class=\"card-hits\" data-through=\"{DayOf(through.Through)}\"><h5>What could go wrong before it ends, about {through.Through:d MMM yyyy}</h5><ul>")
            : "<section class=\"card-hits\"><h5>What could go wrong before it ends</h5><ul>");

        if (report is not null)
        {
            body.Append(Risk("report", "Next report", Said(report.Words), "Reports can move a stock past its stop in a day.", Formatted($" data-line=\"{report.Number}\" data-verdict=\"{Escaped(report.Verdict)}\""), CardMark(report.Verdict)));
        }

        if (card.Hits is { } held)
        {
            body.Append(held.Reactions == 0
                ? Risk("reactions", "Past reports", "No move of the stock on an earlier report is stored.", null)
                : Risk("reactions", "Past reports", Formatted($"Its last {Counted(held.Reactions, "report", "reports")} moved it a median {Figure(held.MedianTypical)} typical days' moves{(held.MedianRisks is { } risks ? Formatted($", {risks.ToString("0.0#", Invariant)} times the plan's risk") : string.Empty)}; {held.PastTheStop} moved it further than the stop is from the buy."), null));

            body.Append(held.DividendDate is { } paid
                ? Risk("dividend", FormattableString.Invariant($"{paid:d MMM}"), Formatted($"Ex-dividend, {(held.DividendDeclared ? "declared" : "estimated from the company's last declared date and its usual interval")}{Paid(held)}"), "The price drops by the dividend that morning.", Formatted($" data-declared=\"{(held.DividendDeclared ? "yes" : "no")}\""))
                : held.DividendUnread
                    ? Risk("dividend", "No date", "No ex-dividend date is declared for the days the calendar is asked for, and none of the company's is stored to estimate one from.", "One inside the trade is not ruled out.", " data-declared=\"unread\"")
                    : Risk("dividend", "None", "No ex-dividend date inside the trade, declared or estimated.", null));

            foreach (var (date, name) in held.Events)
            {
                body.Append(Risk("event", FormattableString.Invariant($"{date:d MMM}"), EventWords(name), "Market-wide moves are common on these days."));
            }

            if (held.Events.Count == 0)
            {
                body.Append(Risk("event", "None", "No Fed rate decision or inflation report inside the trade.", null));
            }

            foreach (var kind in held.PastTheTable)
            {
                body.Append(Risk("past", "Later", Formatted($"The trade runs past the last {EventWords(kind, inSentence: true)} date on file, so a later one is not ruled out."), null));
            }
        }

        return body.Append("</ul></section>").ToString();

        static string Figure(double? value) => value is { } read ? read.ToString("0.0#", Invariant) : "not read";

        static string Paid(CardHitsView hits) =>
            hits.DividendAmount is not { } amount ? string.Empty
            : hits.DividendRisks is { } risks ? Formatted($", about ${amount.ToString("0.00##", Invariant)} a share, {risks.ToString("0.00", Invariant)} of the plan's risk")
            : hits.DividendPercent is { } percent ? Formatted($", about ${amount.ToString("0.00##", Invariant)} a share, {percent.ToString("0.00", Invariant)}% of the buy")
            : Formatted($", about ${amount.ToString("0.00##", Invariant)} a share");
    }

    // The operator's own record of the card's family on its index, under the setup's heading and never the stock's:
    // won, lost and open, or ended for a rule setting no target, and the average result once enough have ended, a dashed
    // outline saying how many have before. Beneath it the rule's own picks listed on the same nights, bought at the next
    // session's open as a taken trade's fill is and counted the same way, so what differs between the two is the
    // operator's choices and fills.
    // see: The operator's own record states its average result once twenty of its trades in a family and index have ended
    static string OperatorRecord(DecisionCardView card)
    {
        if (card.OperatorRecord is not { } mine)
        {
            return string.Empty;
        }

        var counts = mine.Trailing
            ? Formatted($"{mine.Ended} ended and {mine.Open} open")
            : Formatted($"{mine.Won} won, {mine.Lost} lost, {mine.Ended - mine.Won - mine.Lost} ended otherwise and {mine.Open} open");
        var average = mine.Average is { } read
            ? Formatted($"<p data-average=\"{read.ToString("0.00", Invariant)}\">An average of {read.ToString("0.00", Invariant)} {(mine.Unit == "percent" ? "per cent" : "of the risk")} a trade over the {mine.Ended} that ended.</p>")
            : Formatted($"<p class=\"degraded\" data-average=\"none\">{mine.Ended} of {mine.Minimum} ended: the average is drawn once {mine.Minimum} have.</p>");

        return Formatted($"<section class=\"card-mine{(mine.Average is null ? " outline" : string.Empty)}\"><h5>Your own trades of {Escaped(InASentence(card.Rule))}</h5><p>{counts}.</p>{average}{SameNights(mine)}</section>");
    }

    static string SameNights(CardOperatorRecordView mine)
    {
        if (mine.SameNights is not { } rule)
        {
            return string.Empty;
        }

        var open = rule.Listed - rule.Ended;
        var counts = mine.Trailing
            ? Formatted($"{rule.Ended} ended and {open} open")
            : Formatted($"{rule.Won} won, {rule.Lost} lost, {rule.Ended - rule.Won - rule.Lost} ended otherwise and {open} open");
        var nights = rule.Nights == 1 ? "night" : "nights";
        var picks = rule.Listed == 1 ? "pick" : "picks";
        var average = rule.Average is { } read
            ? Formatted($" An average of {read.ToString("0.00", Invariant)} {(mine.Unit == "percent" ? "per cent" : "of the risk")} a trade over the {rule.Ended} that ended.")
            : Formatted($" {rule.Ended} of {mine.Minimum} ended: the average is drawn once {mine.Minimum} have.");

        return Formatted($"<p class=\"card-same-nights\" data-nights=\"{rule.Nights}\" data-listed=\"{rule.Listed}\" data-average=\"{(rule.Average is { } shown ? shown.ToString("0.00", Invariant) : "none")}\">The setup's own {rule.Listed} {picks} on the same {rule.Nights} {nights}, each bought at the next trading day's open: {counts}.{average}</p>");
    }

    // Setups most like this one, under the setup's heading and never as the stock's own: a plain verdict on how the setups
    // like the pick under its setup did against the setup's usual, with their figures and the readings they were matched
    // on, or why none were matched; and the learned score's rank of the pick where the score passed its test on the card's
    // index, and the words saying it has not where it has not.
    // see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
    static string ScorePart(DecisionCardView card, string setup)
    {
        if (card.Score is not { } score)
        {
            return string.Empty;
        }

        var similar = score.Similar;
        var named = new IndexMembers(card.Index, 0).Named;
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"card-score\" data-rank=\"{(score.Rank is { } held ? held.ToString(Invariant) : "none")}\">");
        body.Append("<h5>Setups most like this one</h5>");

        if (similar.Unmatched is { } why || similar.Count is not { } count || similar.Mean is not { } mean || similar.Low is not { } low || similar.High is not { } high || similar.RuleMean is not { } ruleMean)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-similar=\"none\">No past setups were matched: {Escaped(ReaderWords.Plain(similar.Unmatched ?? "the part holds no figure"))}.</p>");
        }
        else
        {
            var readings = similar.Readings ?? [];
            var defined = readings.Select(column => EquityBrief.Core.Ledger.LedgerReadings.All.FirstOrDefault(reading => reading.Column == column)?.Definition ?? column);
            var verdict = !similar.Distinguishable
                ? "did about the same as the setup's usual"
                : mean > ruleMean ? "did better than the setup's usual" : "did worse than the setup's usual";

            body.Append(Invariant, $"<p class=\"card-similar\" data-similar=\"{count}\" data-distance=\"{(similar.Distance ?? 0).ToString("R", Invariant)}\" data-readings=\"{Escaped(string.Join(",", readings))}\" data-mean=\"{mean.ToString("R", Invariant)}\" data-low=\"{low.ToString("R", Invariant)}\" data-high=\"{high.ToString("R", Invariant)}\" data-rule-mean=\"{ruleMean.ToString("R", Invariant)}\">");
            body.Append(Invariant, $"The {count:N0} past {Escaped(setup)} setups on the {Escaped(named)} most like this one {verdict}: they came to {PerHundred(mean)} for every $100 risked beyond the same plan on every stock that day, before costs, against {PerHundred(ruleMean)} for all {similar.RuleSetups ?? 0:N0} of its finished setups. Nine times in ten the figure for setups like these falls between {PerHundred(low)} and {PerHundred(high)}.</p>");
            body.Append(Invariant, $"<p class=\"card-verdict\" data-distinguishable=\"{(similar.Distinguishable ? "true" : "false")}\">{(similar.Distinguishable ? "That range leaves out the setup's usual, so these setups are told apart from its record." : "That range holds the setup's usual, so nothing here says this pick is better or worse than an average one.")}</p>");
            body.Append(Invariant, $"<p class=\"card-note\">Matched on {Counted(readings.Count, "reading", "readings")}: {Escaped(ReaderWords.Plain(string.Join("; ", defined)))}.</p>");
        }

        body.Append(score.Rank is { } rank
            ? Formatted($"<p class=\"card-rank\" data-rank=\"{rank}\">The learned score, which passed its test on this index, ranks it {rank} of 100 among the setups it learned from here.</p>")
            : "<p class=\"card-rank degraded\" data-rank=\"none\">The learned score has not passed its test on this index yet, so it ranks nothing here.</p>");
        body.Append(Invariant, $"<p class=\"card-caution\">These are setups like this one under the same setup on the {Escaped(named)}, not this stock's chance.</p>");

        return body.Append("</section>").ToString();
    }

    // What a card says in place of the record of a rule standing at a change an approval set.
    public static string ApprovedRecord(string approved) =>
        $"Not this setup's record: it stands at a change approved on the Loop page, {approved}, and the record stored for it replays its settings before the change, so none is drawn as this setup's.";

    // How this setup has done, under the setup's own heading and never the stock's: its record over every stock it bought
    // as a sentence and tiles, from its replay over the pulled history, or the trades against the minimum a record is read
    // from before it, and a dashed outline saying why where the rule has not been replayed.
    // see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
    static string RuleRecord(DecisionCardView card)
    {
        if (card.Record is null && card.Approved is { } approved)
        {
            return $"<section class=\"card-record outline\" data-trades=\"none\" data-approved=\"{Escaped(approved)}\"><h5>How this setup has done</h5><p class=\"degraded\">{Escaped(ReaderWords.Plain(ApprovedRecord(approved)))}</p></section>";
        }

        if (card.Record is not { } record)
        {
            return "<section class=\"card-record outline\" data-trades=\"none\"><h5>How this setup has done</h5><p class=\"degraded\">No record yet: the setup's record is drawn once its trades over the past years have been replayed.</p></section>";
        }

        var body = new StringBuilder();
        var minimum = EquityBrief.Core.Returns.ReasonVerdict.MinimumResolved;
        var percent = record.Unit == "percent";

        body.Append(Invariant, $"<section class=\"card-record{(record.Trades < minimum ? " outline" : string.Empty)}\" data-trades=\"{record.Trades}\" data-unit=\"{Escaped(record.Unit)}\">");
        body.Append("<h5>How this setup has done</h5>");

        if (record.Trades < minimum)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-against=\"{minimum}\">{record.Trades} trades replayed of the {minimum} a record is read from, so no figure is drawn yet.</p>");
            body.Append(Invariant, $"<p class=\"card-caution\">{Escaped(RecordIsTheRules)}</p></section>");

            return body.ToString();
        }

        body.Append(RecordSentence(record, SetupOf(card.Family)));
        body.Append("<div class=\"plan-tiles record-tiles\">");

        var held = Formatted($" data-median=\"{(record.MedianSessions is { } sessions ? sessions.ToString(Invariant) : "none")}\" data-held=\"{record.HeldSessions?.ToString(Invariant) ?? "none"}\"");

        body.Append(record.MedianSessions is { } median
            ? PlanTile("held", "Usually over in", Formatted($"{median} trading {(median == 1 ? "day" : "days")}"), record.HeldSessions is { } by ? Formatted($"{EquityBrief.Core.Cards.CardLines.Share(record.HeldShare)} over within {by}") : null, extra: held)
            : PlanTile("held", "Usually over in", "Not read", null, extra: held));

        var lowest = Formatted($" data-worst=\"{(record.WorstClose is { } stored ? stored.ToString("R", Invariant) : "none")}\"");

        body.Append(record.WorstClose is { } worst
            ? PlanTile("worst", "Typical worst close", percent ? Formatted($"{worst.ToString("+0.0;-0.0;0.0", Invariant)}% from the buy") : worst < 0 ? Formatted($"{Math.Abs(worst) * 100:0}% of the way to the stop") : "Never below the buy", "the median of each trade's lowest close before it ended", extra: lowest)
            : PlanTile("worst", "Typical worst close", "Not read", null, extra: lowest));

        if (card.Score?.Similar.RuleMean is { } usual && !percent)
        {
            body.Append(PlanTile("against", "Beyond the same plan on any stock", Formatted($"{PerHundred(usual)} per $100"), "risked, before costs, over its finished setups"));
        }

        body.Append("</div>");
        body.Append(Invariant, $"<p class=\"card-caution\">{Escaped(RecordIsTheRules)} {Escaped(RecordReadsHigh)}</p>");

        if (record.Membership == "survivors only")
        {
            body.Append("<p class=\"card-caution\">Read over today's index members alone, which flatters the record.</p>");
        }

        body.Append(Invariant, $"<p class=\"card-rule\">The setup's settings: {Escaped(ReaderWords.Plain(record.Rule))}.</p></section>");

        return body.ToString();
    }

    // How the stock compares with its index's stocks tonight: a bar a heading of where it ranks among them, out of 100,
    // with words saying what higher means under that heading as the reader's words declare it, a heading with no stored
    // reading said to be not read; and, where the learned score passed its test on the index, each heading's push on the
    // score, folded.
    // see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
    string Compares(BehindItView view)
    {
        var body = new StringBuilder();
        var pushed = view.Centre is not null && view.Score is not null;

        body.Append(Invariant, $"<section class=\"behind-it card-compares\" data-index=\"{Escaped(view.Index)}\" data-push=\"{(pushed ? "drawn" : "none")}\"><h5>How it compares with other {Escaped(view.Index)} stocks tonight</h5>");
        body.Append("<ul class=\"behind-readings\">");

        foreach (var heading in view.Headings)
        {
            var (named, higher) = ReaderWords.Headings.FirstOrDefault(one => one.Heading == heading.Heading) is { Heading: not null } words
                ? (words.Named, words.Higher)
                : (heading.Heading, (string?)null);

            if (heading.Percentile is { } share)
            {
                var hundredths = (int)Math.Round(share * 100);

                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-percentile=\"{share.ToString("0.####", Invariant)}\"><span class=\"bh-name\">{Escaped(named)}</span>");
                body.Append(Invariant, $"<svg class=\"bh-bar\" viewBox=\"0 0 {BehindWidth} 8\" width=\"{BehindWidth}\" height=\"8\" aria-hidden=\"true\"><rect class=\"bh-track\" width=\"{BehindWidth}\" height=\"8\"></rect><rect class=\"bh-fill\" width=\"{hundredths}\" height=\"8\"></rect></svg>");
                body.Append(Invariant, $"<span class=\"bh-words\"><b class=\"bh-figure\">{hundredths}</b> of 100{(higher is null ? string.Empty : Formatted($"; higher means {Escaped(higher)}"))}</span></li>");
            }
            else
            {
                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-percentile=\"none\"><span class=\"bh-name\">{Escaped(named)}</span><span class=\"degraded\">not read</span></li>");
            }
        }

        body.Append("</ul>");
        body.Append(Invariant, $"<p class=\"card-note\">Each bar is where the stock ranks among the {Escaped(view.Index)}'s stocks tonight, out of 100. Further right means higher, which is not always better: higher volatility means a jumpier stock.</p>");

        if (view.Centre is { } centre && view.Score is { } score)
        {
            var largest = Math.Max(view.Headings.Max(heading => Math.Abs(heading.Push ?? 0)), 1e-9);

            body.Append("<details class=\"fold card-push\"><summary>How each heading moved the learned score</summary><div class=\"fold-b\">");
            body.Append("<p class=\"card-note\">The learned score passed its test on this index. Each heading's push is its weight times how far the stock's readings sit from the average of the setups it learned from, in typical days' moves.</p>");
            body.Append(Invariant, $"<ul class=\"behind-push\" data-centre=\"{centre.ToString("R", Invariant)}\" data-score=\"{score.ToString("R", Invariant)}\">");

            foreach (var heading in view.Headings)
            {
                var push = heading.Push ?? 0;
                var length = (int)Math.Round(Math.Abs(push) / largest * (BehindWidth / 2));
                var side = push >= 0 ? "gain" : "loss";
                var from = push >= 0 ? BehindWidth / 2 : (BehindWidth / 2) - length;
                var named = ReaderWords.Headings.FirstOrDefault(one => one.Heading == heading.Heading).Named ?? heading.Heading;

                body.Append(Invariant, $"<li data-heading=\"{Escaped(heading.Heading)}\" data-push=\"{push.ToString("R", Invariant)}\"><span class=\"bh-name\">{Escaped(named)}</span>");
                body.Append(Invariant, $"<svg class=\"bh-bar\" viewBox=\"0 0 {BehindWidth} 8\" width=\"{BehindWidth}\" height=\"8\" aria-hidden=\"true\"><rect class=\"bh-track\" width=\"{BehindWidth}\" height=\"8\"></rect><rect class=\"bh-{side}\" x=\"{from}\" width=\"{length}\" height=\"8\"></rect><line class=\"bh-zero\" x1=\"{BehindWidth / 2}\" x2=\"{BehindWidth / 2}\" y1=\"0\" y2=\"8\"></line></svg>");
                body.Append(Invariant, $"<b class=\"{BySign("bh-figure", push)}\">{push.ToString("+0.00;-0.00;0.00", Invariant)}</b></li>");
            }

            var together = score - centre;

            body.Append(Invariant, $"</ul><p class=\"behind-total\">An average setup scores {centre.ToString("0.00", Invariant)}; the pushes together, <b class=\"{BySign("bh-total", together)}\">{together.ToString("+0.00;-0.00;0.00", Invariant)}</b>, take it to this stock's {score.ToString("0.00", Invariant)}. It is the score's reading, not this stock's chance.</p></div></details>");
        }

        if (view.Unread is { } why)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-push=\"unread\">The learned score passed on this index, and its pushes are not drawn: its stored text could not be read: {Escaped(why)}</p>");
        }

        return body.Append("</section>").ToString();
    }

    // A rule's name inside a sentence: its first letter lowered and the index it names left as it is written.
    static string InASentence(string rule) => rule.Length == 0 ? rule : char.ToLowerInvariant(rule[0]) + rule[1..];
}
