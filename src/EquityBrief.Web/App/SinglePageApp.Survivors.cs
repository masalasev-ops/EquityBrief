using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// One survivor as the page draws it: its number, its words and line, the night it passed, its newest state and why, the name
// it registered under, the results of the picks it was walked to while it queued, the session it registered on, the nights
// it has been walked, and the results of the trades its variant made after it registered.
public sealed record SurvivorRow(
    long Id,
    string Words,
    string Line,
    DateOnly Passed,
    string State,
    string? Candidate,
    string Why,
    IReadOnlyList<double?> Queued,
    DateOnly? RegisteredOn,
    int Nights)
{
    public int RegisteredClosed { get; init; }

    public double? RegisteredPerDollar { get; init; }
}

// The survivors on the Improvement loop page for the index it reads: those running in the background as their setup's
// variants, those waiting for a slot with why, in the order they passed, and those retired to make room, each with its one
// record carried across its registration.
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
public sealed partial class SinglePageApp
{
    public const string SurvivorsLede = "From the night it passes, a survivor is walked every night by itself, as a variant of its setup's running revision where it has a slot, like a paper account no one trades. No survivor sets Tonight's buys.";

    public const string QueueLine = "A setup runs at most nine rules. Where it is full, the variant with the lowest result per $1 risked over the same nights makes room, never the running revision and never one that has run under 63 nights. Where none can go yet, or where its setup cannot yet run a part of its line, the survivor waits here. It is still walked every night from the night it passed, its picks stored and shown among no buys, and it keeps that record when it gets its slot. The one that passed first goes first.";

    public string SurvivorsRegion(UniverseChoice chosen, IReadOnlyList<SurvivorRow> survivors)
    {
        if (survivors.Count == 0)
        {
            return Cards.Computed("Survivors and the queue", $"<p class=\"empty\" data-survivors=\"none\" data-summary>No idea has passed the tester on the {Escaped(chosen.Name)} yet, so none runs by itself.</p>", title: "Ideas that passed, running by themselves", lede: Escaped(SurvivorsLede), region: "loop-survivors");
        }

        var running = survivors.Where(one => one.State == Survivors.Registered).ToArray();
        var waiting = survivors.Where(one => one.State == Survivors.Queued).ToArray();
        var retired = survivors.Where(one => one.State == Survivors.Retired).ToArray();
        var html = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"<p data-summary data-survivors=\"count\">On the {Escaped(chosen.Name)}: {running.Length} running in the background, {waiting.Length} waiting for a slot, {retired.Length} retired to make room.</p>"));

        html.Append("<p class=\"lm-h\">Running in the background</p>");
        html.Append(running.Length == 0 ? "<p class=\"empty\">None yet.</p>" : Table(["Variant", "Passed", "Nights walked", $"Closed trades, of the {PromotionRule.Trades} a case needs", "Per $1 risked"], running.Select(one =>
        {
            var (closed, made) = Survivors.Record(one.Queued, one.RegisteredClosed, one.RegisteredPerDollar);

            return (one.State, new[] { $"{Escaped(one.Candidate ?? string.Empty)}<code class=\"idea-line\">{Escaped(one.Line)}</code>", PassedOn(one.Passed), Number(one.Nights), string.Create(CultureInfo.InvariantCulture, $"{closed} of {PromotionRule.Trades}"), Made(made) });
        })));

        html.Append("<p class=\"lm-h\">Waiting for a slot</p>");
        html.Append($"<p class=\"lm-p\">{Escaped(QueueLine)}</p>");
        html.Append(waiting.Length == 0 ? "<p class=\"empty\">None waiting.</p>" : Table(["#", "Survivor", "Passed", "Nights walked", "Its record so far", "Why it waits"], waiting.Select((one, at) =>
        {
            var (closed, made) = Survivors.Record(one.Queued, 0, null);

            return (one.State, new[] { Number(at + 1), $"{Escaped(one.Words)}<code class=\"idea-line\">{Escaped(one.Line)}</code>", PassedOn(one.Passed), Number(one.Nights), RecordWords(closed, made), Escaped(one.Why) });
        })));

        html.Append("<p class=\"lm-h\">Retired to make room</p>");
        html.Append(retired.Length == 0 ? "<p class=\"empty\">None retired.</p>" : Table(["Variant", "Passed", "Its record", "Why"], retired.Select(one =>
        {
            var (closed, made) = Survivors.Record(one.Queued, one.RegisteredClosed, one.RegisteredPerDollar);

            return (one.State, new[] { $"{Escaped(one.Candidate ?? one.Words)}<code class=\"idea-line\">{Escaped(one.Line)}</code>", PassedOn(one.Passed), RecordWords(closed, made), Escaped(one.Why) });
        })));

        return Cards.Computed("Survivors and the queue", html.ToString(), title: "Ideas that passed, running by themselves", lede: Escaped(SurvivorsLede), region: "loop-survivors");
    }

    public static string RecordWords(int closed, double? made) =>
        closed == 0 ? "none closed" : string.Create(CultureInfo.InvariantCulture, $"{closed} closed, {Made(made)} per $1");

    static string Made(double? made) => made is { } value ? (value < 0 ? "-$" : "+$") + Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture) : "none closed";

    static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    static string PassedOn(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    static string Table(IReadOnlyList<string> headings, IEnumerable<(string State, string[] Cells)> rows)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"lm-table survivors-table\"><thead><tr>");

        html.Append(string.Concat(headings.Select(heading => $"<th>{Escaped(heading)}</th>")));
        html.Append("</tr></thead><tbody>");

        foreach (var (state, cells) in rows)
        {
            html.Append($"<tr data-survivor=\"{Escaped(state)}\">");
            html.Append(string.Concat(cells.Select((cell, at) => $"<td data-h=\"{Escaped(headings[at])}\">{cell}</td>")));
            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }
}
