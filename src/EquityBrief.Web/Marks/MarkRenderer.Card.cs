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

// The rule's own picks listed on the nights the operator took one, each followed from the plan's buy: how many nights,
// how many picks, won, lost and ended, and the average once enough have ended.
public sealed record CardSameNightsView(int Nights, int Listed, int Won, int Lost, int Ended, double? Average);

// The learned score's part of a pick's card as the night stored it: the pick's rank among the setups the score learned on
// on the card's index, none until the score passed the tester there, and the setups like the pick under its rule.
public sealed record CardScoreView(int? Rank, EquityBrief.Core.Cards.CardSimilar Similar);

// A pick's card on a night: the index, the night, the family that listed it and the stock, the rule the card names, the
// plan's prices, the checklist's lines and the rule's record, none where the rule has not been replayed; and, where the
// page draws them, the plan in the operator's money or that the account is not set, the rule's management of the trade,
// the trades taken from it and whether its presses are drawn, which an export never does; and for a family a learned
// score reaches, the score's part.
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
    CardScoreView? Score = null)
{
    // The id the card's row and the control opening it share.
    public string Id => $"card-{Index}-{Family}-{Ticker}".ToLowerInvariant().Replace('.', '-').Replace(' ', '-');
}

// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: A rule's record is drawn under the rule's own heading, beside its reason or on a pick's card and never as the stock's own
public sealed partial class MarkRenderer
{
    // The sentences the rule's record is drawn under, which a reader of the card reads first.
    public const string RecordIsTheRules = "This is the rule's record over every stock it bought, not this stock's chance.";

    public const string RecordReadsHigh = "The rule's settings were chosen on this same history, so these figures read high.";

    // The control on a pick's row that opens its card in place beneath the row.
    public static string CardToggle(DecisionCardView card) =>
        $" <button type=\"button\" class=\"card-toggle\" aria-expanded=\"false\" aria-controls=\"{Escaped(card.Id)}\" title=\"open this pick's card\">card</button>";

    // A pick's card in a row of its own beneath the pick's, hidden until its control opens it.
    public string CardRow(DecisionCardView card, int columns) =>
        Formatted($"<tr class=\"card-row\" id=\"{Escaped(card.Id)}\" hidden><td colspan=\"{columns}\">{DecisionCard(card)}</td></tr>");

    // A pick's card: the checklist's lines, each with its mark and its reason, and the rule's record under the rule's own
    // heading, naming the rule and its index and never the stock.
    public string DecisionCard(DecisionCardView card)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"decision-card\" data-index=\"{Escaped(card.Index)}\" data-family=\"{Escaped(card.Family)}\" data-ticker=\"{Escaped(card.Ticker)}\" data-night=\"{DayOf(card.Night)}\">");
        body.Append(Invariant, $"<p class=\"card-head\">The checklist for {Escaped(card.Ticker)} on {DayOf(card.Night)}, as {Escaped(InASentence(card.Rule))} listed it. It advises and removes no pick.</p>");
        body.Append("<ol class=\"card-lines\">");

        foreach (var line in card.Lines)
        {
            body.Append(Invariant, $"<li class=\"card-line\" data-line=\"{line.Number}\" data-verdict=\"{Escaped(line.Verdict)}\">");
            body.Append(Invariant, $"<span class=\"card-mark\" data-verdict=\"{Escaped(line.Verdict)}\">{Escaped(line.Verdict)}</span> ");
            body.Append(Invariant, $"<b class=\"card-name\">{Escaped(line.Name)}</b> ");
            body.Append(Invariant, $"<span class=\"card-words\">{Escaped(line.Words)}</span></li>");
        }

        body.Append("</ol>");
        body.Append(PlanInMoney(card));
        body.Append(Hits(card));
        body.Append(RuleRecord(card));
        body.Append(ScorePart(card));
        body.Append(OperatorRecord(card));
        body.Append(Taken(card));
        body.Append("</div>");

        return body.ToString();
    }

    // The route of the settings page the plan links to where the account is not set.
    public const string AccountRoute = "#/account";

    // The plan in the operator's money where the account is set, the line linking the settings page where it is not,
    // and the rule's management of the trade, each stop a close below it.
    // see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
    static string PlanInMoney(DecisionCardView card)
    {
        if (card.Money is null && !card.AccountUnset && card.Management is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder("<section class=\"card-plan\"><h5>Your plan</h5>");

        if (card.Money is { } money)
        {
            body.Append(Invariant, $"<p class=\"card-money\" data-shares=\"{money.Shares}\" data-at-risk=\"{money.AtRisk.ToString("0.00", Invariant)}\">");
            body.Append(Invariant, $"Buy {money.Shares:N0} shares at {card.Entry?.ToString("0.00", Invariant)}, a position of {money.Value.ToString("N2", Invariant)}, {money.ShareOfAccount:0.0%} of the account");
            body.Append(money.WholeAtRisk
                ? ". The whole position is at risk: nothing sells it at a price set in advance."
                : Formatted($", with {money.AtRisk.ToString("N2", Invariant)} at risk to the stop at {card.Stop?.ToString("0.00", Invariant)}, the {money.RiskPercent.ToString("0.##", Invariant)}% a trade your settings give."));

            if (money.Capped)
            {
                body.Append(" Fewer shares than the risk allows: the position cap limits it.");
            }

            if (money.RoundTrip is { } trip)
            {
                body.Append(Invariant, $" The round trip at the published table is about {trip.ToString("N2", Invariant)}.");
            }

            body.Append("</p>");
        }
        else if (card.AccountUnset)
        {
            body.Append(Invariant, $"<p class=\"card-money degraded\" data-account=\"unset\">Your account is not set, so the plan is drawn in prices and risks: <a href=\"{AccountRoute}\">set your account's size and risk</a> to see it in shares and dollars.</p>");
        }

        if (card.Management is { Count: > 0 } steps)
        {
            body.Append("<ul class=\"card-management\">");

            foreach (var step in steps)
            {
                body.Append(Invariant, $"<li>{Escaped(step)}</li>");
            }

            body.Append("</ul>");
        }

        return body.Append("</section>").ToString();
    }

    // What the control taking a trade records, said above it, since a fill box and a date box alone do not say that the
    // trade is the operator's own, that each night follows it, or what an empty box means.
    public const string TakenSays =
        "Bought this stock? Record it here, and each night follows your trade by the rule's own exits above and adds it to " +
        "your record. Leave Fill empty to use the next session's opening price once it is stored, and the date empty for the " +
        "session after this pick's night.";

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

        var body = new StringBuilder("<section class=\"card-taken\"><h5>Taken</h5>");

        foreach (var trade in taken)
        {
            body.Append(Invariant, $"<p class=\"card-taken-line\" data-taken-at=\"{Escaped(trade.TakenAt)}\" data-provisional=\"{(trade.Provisional ? "yes" : "no")}\">");
            body.Append(Invariant, $"Taken at {trade.Fill.ToString("0.00", Invariant)} for {DayOf(trade.FillDate)}");
            body.Append(trade.Provisional ? ", provisional: the plan's buy until the next session's open is stored" : string.Empty);
            body.Append(trade.ExitPrice is { } exit && trade.ExitDate is { } on
                ? Formatted($"; exited at {exit.ToString("0.00", Invariant)} on {DayOf(on)}.")
                : trade.EndedOn is { } ended && trade.EndPrice is { } sold
                    ? Formatted($"; ended by its rule's {Escaped(trade.EndReason ?? "end")} on {DayOf(ended)} at {sold.ToString("0.00", Invariant)}.")
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
            body.Append("<label>Fill <input name=\"price\" inputmode=\"decimal\" placeholder=\"the next open\"></label> <label>on <input name=\"date\" type=\"date\"></label> <button type=\"submit\">Taken</button></form>");
        }

        return body.Append("<p class=\"card-said\" aria-live=\"polite\"></p></section>").ToString();
    }

    // The rule's record under its own heading: its figures from the rules' minimum on, its trades against the minimum
    // before it, and a dashed outline saying why where the rule has not been replayed.
    // see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
    // What could hit the trade before its hold ends, each as the night stored it: the stock's reactions, the next
    // ex-dividend date declared or estimated, and each market event inside the hold, with a kind whose table ends first
    // said to end.
    // see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
    static string Hits(DecisionCardView card)
    {
        if (card.Hits is not { } hits)
        {
            return string.Empty;
        }

        var body = new StringBuilder(Formatted($"<section class=\"card-hits\" data-through=\"{DayOf(hits.Through)}\"><h5>What could hit it before {DayOf(hits.Through)}</h5><ul>"));

        body.Append(hits.Reactions == 0
            ? "<li data-hit=\"reactions\">No earnings reaction of the stock's is stored.</li>"
            : Formatted($"<li data-hit=\"reactions\">Its {hits.Reactions} stored earnings {(hits.Reactions == 1 ? "reaction" : "reactions")} moved a median {Figure(hits.MedianTypical)} typical moves, {Figure(hits.MedianRisks)} of this plan's risk; {hits.PastTheStop} moved further than the stop's distance.</li>"));

        body.Append(hits.DividendDate is { } paid
            ? Formatted($"<li data-hit=\"dividend\" data-declared=\"{(hits.DividendDeclared ? "yes" : "no")}\">Ex-dividend {DayOf(paid)}, {(hits.DividendDeclared ? "declared" : "estimated from the company's last declared date and its usual interval")}{Paid(hits)}.</li>")
            : hits.DividendUnread
                ? "<li data-hit=\"dividend\" data-declared=\"unread\">No ex-dividend date is declared in the sessions the calendar is asked for, and no dividend of the company's is stored yet to estimate a later one from, so one later in the hold is not ruled out.</li>"
                : "<li data-hit=\"dividend\">No ex-dividend date inside the hold, declared or estimated.</li>");

        foreach (var (date, name) in hits.Events)
        {
            body.Append(Formatted($"<li data-hit=\"event\">{Escaped(name)} on {DayOf(date)}.</li>"));
        }

        if (hits.Events.Count == 0)
        {
            body.Append("<li data-hit=\"event\">No FOMC decision or CPI release inside the hold.</li>");
        }

        foreach (var kind in hits.PastTheTable)
        {
            body.Append(Formatted($"<li data-hit=\"past\">The hold runs past the last {Escaped(kind)} the table holds, so one after it is not ruled out.</li>"));
        }

        return body.Append("</ul></section>").ToString();

        static string Figure(double? value) => value is { } held ? held.ToString("0.0#", Invariant) : "not read";

        static string Paid(CardHitsView hits) =>
            hits.DividendAmount is not { } amount ? string.Empty
            : hits.DividendRisks is { } risks ? Formatted($", {amount.ToString("0.00##", Invariant)} a share, {risks.ToString("0.00", Invariant)} of the risk")
            : hits.DividendPercent is { } percent ? Formatted($", {amount.ToString("0.00##", Invariant)} a share, {percent.ToString("0.00", Invariant)}% of the buy")
            : Formatted($", {amount.ToString("0.00##", Invariant)} a share");
    }

    // The operator's own record of the card's family on its index, under the rule's heading and never the stock's:
    // won, lost and open, or ended for a rule setting no target, and the average result once enough have ended, a dashed
    // outline saying how many have before. Beneath it the rule's own picks listed on the same nights, bought at the plan's
    // buy and counted the same way, so what differs between the two is the operator's choices and fills.
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

        return Formatted($"<section class=\"card-mine{(mine.Average is null ? " outline" : string.Empty)}\"><h5>Your trades of {Escaped(InASentence(card.Rule))}</h5><p>{counts}.</p>{average}{SameNights(mine)}</section>");
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

        return Formatted($"<p class=\"card-same-nights\" data-nights=\"{rule.Nights}\" data-listed=\"{rule.Listed}\" data-average=\"{(rule.Average is { } shown ? shown.ToString("0.00", Invariant) : "none")}\">The rule's own {rule.Listed} {picks} on the same {rule.Nights} {nights}, each bought at the plan's buy: {counts}.{average}</p>");
    }

    // The learned score's part beneath the rule's record, under the rule's heading and never as the stock's own: the pick's
    // rank where the score passed the walk-forward tester on the card's index and the words saying it has not where it has
    // not; and the setups like the pick under the rule with their mean edge, its interval and the rule's own mean, saying
    // where the interval holds the rule's mean that the part is not told from the rule's record, or why none were matched.
    // see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
    static string ScorePart(DecisionCardView card)
    {
        if (card.Score is not { } score)
        {
            return string.Empty;
        }

        var similar = score.Similar;
        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"card-score\" data-rank=\"{(score.Rank is { } held ? held.ToString(Invariant) : "none")}\">");
        body.Append("<h5>Setups like this one</h5>");
        body.Append(Invariant, $"<p class=\"card-caution\">{Escaped(EquityBrief.Core.Cards.CardSimilar.Heading(new IndexMembers(card.Index, 0).Named))}</p>");
        body.Append(score.Rank is { } rank
            ? Formatted($"<p class=\"card-rank\" data-rank=\"{rank}\">The learned score, which passed the walk-forward tester on this index, places it at hundredth {rank} of the setups it learned on here.</p>")
            : $"<p class=\"card-rank degraded\" data-rank=\"none\">{Escaped(EquityBrief.Core.Cards.CardSimilar.NotValidated)}.</p>");

        if (similar.Unmatched is { } why || similar.Count is not { } count || similar.Mean is not { } mean || similar.Low is not { } low || similar.High is not { } high || similar.RuleMean is not { } ruleMean)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-similar=\"none\">No setups matched: {Escaped(similar.Unmatched ?? "the part holds no figure")}.</p></section>");

            return body.ToString();
        }

        body.Append("<dl class=\"card-figures\">");
        body.Append(Formatted($"<dt>Matched</dt><dd data-similar=\"{count}\" data-distance=\"{(similar.Distance ?? 0).ToString("R", Invariant)}\">{count:N0} of the rule's {similar.RuleSetups ?? 0:N0} finished setups on this index, the nearest on {Escaped(string.Join(", ", similar.Readings ?? []))} by their places among them, a median distance of {(similar.Distance ?? 0).ToString("0.000", Invariant)}</dd>"));
        body.Append(Formatted($"<dt>Their edge</dt><dd data-mean=\"{mean.ToString("R", Invariant)}\" data-low=\"{low.ToString("R", Invariant)}\" data-high=\"{high.ToString("R", Invariant)}\">a mean {mean.ToString("+0.00;-0.00;0.00", Invariant)} of the risk against the same plan on every member that session, before costs, ninety per cent between {low.ToString("+0.00;-0.00;0.00", Invariant)} and {high.ToString("+0.00;-0.00;0.00", Invariant)}</dd>"));
        body.Append(Formatted($"<dt>The rule's</dt><dd data-rule-mean=\"{ruleMean.ToString("R", Invariant)}\">a mean {ruleMean.ToString("+0.00;-0.00;0.00", Invariant)} over every one of them</dd>"));
        body.Append("</dl>");

        if (!similar.Distinguishable)
        {
            body.Append(Invariant, $"<p class=\"card-verdict\" data-distinguishable=\"false\">{Escaped(EquityBrief.Core.Cards.CardSimilar.NotDistinguishable)}</p>");
        }

        body.Append("</section>");

        return body.ToString();
    }

    static string RuleRecord(DecisionCardView card)
    {
        if (card.Record is not { } record)
        {
            return $"<section class=\"card-record outline\" data-trades=\"none\"><h5>The record of {Escaped(InASentence(card.Rule))}</h5><p class=\"degraded\">Not replayed yet: the rule's record is read once its replay over the pulled history has run, by the command a freeze's remedy runs.</p></section>";
        }

        var body = new StringBuilder();
        var minimum = EquityBrief.Core.Returns.ReasonVerdict.MinimumResolved;
        var percent = record.Unit == "percent";

        body.Append(Invariant, $"<section class=\"card-record{(record.Trades < minimum ? " outline" : string.Empty)}\" data-trades=\"{record.Trades}\" data-unit=\"{Escaped(record.Unit)}\">");
        body.Append(Invariant, $"<h5>The record of {Escaped(InASentence(card.Rule))}</h5>");
        body.Append(Invariant, $"<p class=\"card-caution\">{Escaped(RecordIsTheRules)}</p>");

        if (record.Trades < minimum)
        {
            body.Append(Invariant, $"<p class=\"degraded\" data-against=\"{minimum}\">{record.Trades} trades replayed of the {minimum} a record is read from, so no figure is drawn.</p></section>");

            return body.ToString();
        }

        body.Append("<dl class=\"card-figures\">");
        body.Append(Invariant, $"<dt>Trades</dt><dd data-trades=\"{record.Trades}\">{record.Trades:N0}, {DayOf(record.From)} to {DayOf(record.Through)}, {Escaped(record.Membership)}</dd>");
        body.Append(record.Won is { } won
            ? Formatted($"<dt>Won</dt><dd data-won=\"{won.ToString("R", Invariant)}\">{won:0%} ended at their target</dd>")
            : "<dt>Won</dt><dd data-won=\"none\">none read: the rule sets no target, so a trade has no win</dd>");
        body.Append(record.Average is { } average
            ? Formatted($"<dt>Average result</dt><dd data-average=\"{average.ToString("R", Invariant)}\">{(percent ? average.ToString("0.00", Invariant) + "%" : average.ToString("0.00", Invariant) + " of the risk")} after each trade's cost</dd>")
            : "<dt>Average result</dt><dd data-average=\"none\">none</dd>");
        body.Append(record.MedianSessions is { } median
            ? Formatted($"<dt>Held</dt><dd data-median=\"{median}\" data-held=\"{record.HeldSessions?.ToString(Invariant) ?? "none"}\">a median {median} sessions, {EquityBrief.Core.Cards.CardLines.Share(record.HeldShare)} ended by {record.HeldSessions?.ToString(Invariant) ?? "none"}</dd>")
            : "<dt>Held</dt><dd data-median=\"none\">none</dd>");
        body.Append(record.WorstClose is { } worst
            ? Formatted($"<dt>Worst close</dt><dd data-worst=\"{worst.ToString("R", Invariant)}\">a median {(percent ? worst.ToString("0.00", Invariant) + "%" : worst.ToString("0.00", Invariant) + " of the risk")} from the buy before the end</dd>")
            : "<dt>Worst close</dt><dd data-worst=\"none\">none</dd>");
        body.Append("</dl>");
        body.Append(Invariant, $"<p class=\"card-caution\">{Escaped(RecordReadsHigh)}</p>");

        if (record.Membership == "survivors only")
        {
            body.Append("<p class=\"card-caution\">Survivors only: read over today's members alone on every session, which flatters the record.</p>");
        }

        body.Append(Invariant, $"<p class=\"card-rule\">{Escaped(record.Rule)}</p></section>");

        return body.ToString();
    }

    // A rule's name inside a sentence: its first letter lowered and the index it names left as it is written.
    static string InASentence(string rule) => rule.Length == 0 ? rule : char.ToLowerInvariant(rule[0]) + rule[1..];
}
