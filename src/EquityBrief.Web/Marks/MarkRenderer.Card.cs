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

// A pick's card on a night: the index, the night, the family that listed it and the stock, the rule the card names, the
// plan's prices, the checklist's lines and the rule's record, none where the rule has not been replayed.
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
    CardRecordView? Record)
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
        body.Append(RuleRecord(card));
        body.Append("</div>");

        return body.ToString();
    }

    // The rule's record under its own heading: its figures from the rules' minimum on, its trades against the minimum
    // before it, and a dashed outline saying why where the rule has not been replayed.
    // see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
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
