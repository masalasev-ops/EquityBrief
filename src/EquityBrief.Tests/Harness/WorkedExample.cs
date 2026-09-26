using System.Globalization;
using System.Net;
using System.Text;

namespace EquityBrief.Tests.Harness;

// One name's night as figure 11.2 draws it: its closes to the night, the setup band, the recent high and
// the pullback from it, the plan the trade is read from, and each gate's answer.
internal sealed record NameFigure(
    string Ticker,
    DateOnly Night,
    IReadOnlyList<(DateOnly Session, decimal Close)> Closes,
    decimal BandLow,
    decimal BandHigh,
    decimal RecentHigh,
    double Depth,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal RewardToRisk,
    IReadOnlyList<(string Gate, bool Passed)> Gates);

// A setup resolving as figure 11.3 draws it: the session it was read on, the closes from its entry on,
// and the plan it resolved against.
internal sealed record ResolutionFigure(
    string Ticker,
    DateOnly Session,
    IReadOnlyList<(DateOnly Session, decimal Close)> Closes,
    decimal Entry,
    decimal Stop,
    decimal Target,
    string Outcome,
    DateOnly ResolvedOn);

// Section 11's worked example, drawn. The first three figures are drawn from numbers the code computes
// over the committed fixture, which a check regenerates and holds the document to; the last two are
// illustrative and say so. Every figure is drawn in the document's own colour variables, support green
// and resistance orange alone beside the neutral ink.
internal static class WorkedExample
{
    const string Font = "font-family=\"Segoe UI, Arial, sans-serif\"";
    const string Support = "var(--compute)";
    const string Resistance = "var(--src)";

    static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    static string N(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    static string D(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string E(string text) => WebUtility.HtmlEncode(text);

    static string Figure(int width, int height, string title, string description, string body, string caption, string key, string take) =>
        $"<figure class=\"fig svgfig\">\n<svg width=\"100%\" viewBox=\"0 0 {width} {height}\" role=\"img\" xmlns=\"http://www.w3.org/2000/svg\">\n" +
        $"<title>{E(title)}</title>\n<desc>{E(description)}</desc>\n{body}</svg>\n<figcaption>{E(caption)}</figcaption>\n</figure>\n" +
        $"<div class=\"key\">\n  <p><b>Key.</b> {E(key)}</p>\n  <p>{E(take)}</p>\n</div>\n";

    static string Text(double x, double y, string text, string fill = "var(--ink)", string size = "12.5", string anchor = "start", string weight = "400") =>
        $"<text x=\"{N(x, "0.#")}\" y=\"{N(y, "0.#")}\" text-anchor=\"{anchor}\" fill=\"{fill}\" {Font} font-size=\"{size}\" font-weight=\"{weight}\">{E(text)}</text>\n";

    // Figure 11.1: one night through the gates in order, each bar the members passing that gate and every
    // gate before it.
    internal static string Funnel(DateOnly night, int members, IReadOnlyList<(string Gate, int Passed)> steps)
    {
        var body = new StringBuilder();
        var rows = new List<(string Label, int Count)> { ("Members of the index", members) };

        rows.AddRange(steps.Select(step => (char.ToUpperInvariant(step.Gate[0]) + step.Gate[1..], step.Passed)));

        const double Left = 200, Width = 420, Top = 24, Step = 38;
        var before = members;

        for (var at = 0; at < rows.Count; at++)
        {
            var (label, count) = rows[at];
            var y = Top + (at * Step);
            var bar = members == 0 ? 0 : Width * count / members;

            body.Append(Text(Left - 12, y + 17, label, anchor: "end"));
            body.Append(count == 0
                ? $"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(y + 4, "0.#")}\" x2=\"{N(Left, "0.#")}\" y2=\"{N(y + 24, "0.#")}\" stroke=\"{Support}\" stroke-width=\"2\"/>\n"
                : $"<rect x=\"{N(Left, "0.#")}\" y=\"{N(y + 4, "0.#")}\" width=\"{N(bar, "0.#")}\" height=\"20\" rx=\"3\" fill=\"{Support}\" fill-opacity=\"0.8\"/>\n");
            body.Append(Text(Left + Math.Max(bar, 2) + 8, y + 18, count.ToString(CultureInfo.InvariantCulture), weight: "600"));

            if (at > 0 && before - count > 0)
            {
                body.Append(Text(680, y + 18, $"{before - count} removed", "var(--muted)", "12", "end"));
            }

            before = count;
        }

        var height = (int)(Top + (rows.Count * Step) + 12);

        return Figure(
            700,
            height,
            $"One night through the swing filter's gates, {D(night)}",
            $"The committed fixture's {members} members on {D(night)}, counted through the market, trend and strength, setup, trigger and trade gates in order and the exclusions after them.",
            body.ToString(),
            $"Figure 11.1. One night through the swing filter's gates, the committed fixture's {D(night)}.",
            $"Computed by the swing filter over the committed fixture's {members} members on {D(night)}, at section 17's proposed values with no filter version open. Each bar is how many members passed that gate and every gate before it, and the figure at the right is how many that gate removed; the last bar is the names no exclusion removed, which is the list.",
            "A night is read from the top down: where the bars drop is where the night's names stopped, and an empty bar says no name got further, so the list is empty for a reason the figure names.");
    }

    // Figure 11.2: one name's chart and plan with each gate's answer.
    internal static string Name(NameFigure name)
    {
        var body = new StringBuilder();
        const double Left = 64, Right = 440, Top = 30, Bottom = 290;

        var prices = name.Closes.Select(close => close.Close).Concat([name.BandLow, name.BandHigh, name.RecentHigh, name.Target, name.Stop]).ToArray();
        var high = prices.Max();
        var low = prices.Min();
        var pad = (high - low) * 0.06m;

        high += pad;
        low -= pad;

        double Y(decimal price) => Top + ((double)((high - price) / (high - low)) * (Bottom - Top));
        double X(int at) => Left + ((Right - Left) * at / Math.Max(1, name.Closes.Count - 1));

        // The support band, the stop at its low edge, the target at the resistance above and the recent high.
        body.Append($"<rect x=\"{N(Left, "0.#")}\" y=\"{N(Y(name.BandHigh), "0.#")}\" width=\"{N(Right - Left, "0.#")}\" height=\"{N(Y(name.BandLow) - Y(name.BandHigh), "0.#")}\" fill=\"{Support}\" fill-opacity=\"0.14\" stroke=\"{Support}\" stroke-width=\"1\"/>\n");
        body.Append($"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(Y(name.Target), "0.#")}\" x2=\"{N(Right, "0.#")}\" y2=\"{N(Y(name.Target), "0.#")}\" stroke=\"{Resistance}\" stroke-width=\"1.6\"/>\n");
        body.Append($"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(Y(name.Stop), "0.#")}\" x2=\"{N(Right, "0.#")}\" y2=\"{N(Y(name.Stop), "0.#")}\" stroke=\"{Support}\" stroke-width=\"1.6\" stroke-dasharray=\"5 4\"/>\n");
        body.Append($"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(Y(name.RecentHigh), "0.#")}\" x2=\"{N(Right, "0.#")}\" y2=\"{N(Y(name.RecentHigh), "0.#")}\" stroke=\"var(--muted)\" stroke-width=\"1\" stroke-dasharray=\"2 4\"/>\n");

        var line = string.Join(" ", name.Closes.Select((close, at) => $"{N(X(at), "0.#")},{N(Y(close.Close), "0.#")}"));

        body.Append($"<polyline points=\"{line}\" fill=\"none\" stroke=\"var(--ink)\" stroke-width=\"1.4\"/>\n");
        body.Append($"<circle cx=\"{N(X(name.Closes.Count - 1), "0.#")}\" cy=\"{N(Y(name.Entry), "0.#")}\" r=\"4\" fill=\"var(--ink)\"/>\n");

        body.Append(Text(Right + 8, Y(name.Target) + 4, $"target {N(name.Target)}", Resistance, "12"));
        body.Append(Text(Left + 6, Y(name.RecentHigh) - 6, $"20-session high {N(name.RecentHigh)}", "var(--muted)", "12"));
        body.Append(Text(Right + 8, Y(name.Entry) + 4, $"close {N(name.Entry)}", "var(--ink)", "12"));
        body.Append(Text(Right + 8, Y(name.Stop) + 14, $"stop {N(name.Stop)}", Support, "12"));
        body.Append(Text(Left, Top - 10, $"{name.Ticker}, {name.Closes.Count} sessions to {D(name.Night)}", "var(--muted)", "12"));

        // Each gate's answer, in order.
        for (var at = 0; at < name.Gates.Count; at++)
        {
            var (gate, passed) = name.Gates[at];

            body.Append(Text(566, 60 + (at * 24), $"{gate}: {(passed ? "passed" : "failed")}", passed ? "var(--ink)" : "var(--muted)", "12.5", weight: passed ? "600" : "400"));
        }

        var stopped = name.Gates.FirstOrDefault(gate => !gate.Passed).Gate;

        body.Append(Text(566, 60 + (name.Gates.Count * 24) + 6, stopped is null ? "every gate passed" : $"stopped at {stopped}", "var(--ink)", "12.5", weight: "600"));

        var reward = name.Target - name.Entry;
        var risk = name.Entry - name.Stop;

        body.Append(Text(Left, Bottom + 34, $"Pullback: {N(name.Depth, "0.0")} typical days below the 20-session high of {N(name.RecentHigh)}.", "var(--ink)", "12.5"));
        body.Append(Text(Left, Bottom + 56, $"Plan: entry {N(name.Entry)}, stop {N(name.Stop)}, target {N(name.Target)}: reward {N(reward)} against risk {N(risk)}, a reward to risk of {N(name.RewardToRisk)}.", "var(--ink)", "12.5"));

        return Figure(
            700,
            (int)(Bottom + 76),
            $"{name.Ticker} through every gate on {D(name.Night)}",
            $"{name.Ticker}'s closes to {D(name.Night)} with its support band, the stop at the band's low edge, the target at the nearest resistance above, the recent high, and each gate's answer.",
            body.ToString(),
            $"Figure 11.2. One name through every gate, {name.Ticker} on the committed fixture's {D(name.Night)}.",
            $"Computed over the committed fixture on {D(name.Night)} at section 17's proposed values. No member passed every gate that night, so the figure draws the one that passed the most, {name.Ticker}, and names the first gate that stopped it. The green band is the anchored support band the setup read, the dashed green line the stop at its low edge, the orange line the target at the nearest resistance band above the close, and the dotted line the highest high of the last 20 sessions the pullback is measured from. The plan is the swing trade's own: entered at the night's close, stopped below the band, won at the target.",
            "Every figure on the plan is a fact about the chart: where support sits, how far the price came down to it, and what the trade stands to gain against what it risks. None of it is a forecast, and a name that fails a gate is not on the list however good its plan looks.");
    }

    // Figure 11.3: a setup read on an earlier session, resolving on the closes after it.
    internal static string Resolution(ResolutionFigure setup)
    {
        var body = new StringBuilder();
        const double Left = 64, Right = 560, Top = 30, Bottom = 250;

        var prices = setup.Closes.Select(close => close.Close).Concat([setup.Stop, setup.Target]).ToArray();
        var high = prices.Max();
        var low = prices.Min();
        var pad = (high - low) * 0.08m;

        high += pad;
        low -= pad;

        double Y(decimal price) => Top + ((double)((high - price) / (high - low)) * (Bottom - Top));
        double X(int at) => Left + ((Right - Left) * at / Math.Max(1, setup.Closes.Count - 1));

        body.Append($"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(Y(setup.Target), "0.#")}\" x2=\"{N(Right, "0.#")}\" y2=\"{N(Y(setup.Target), "0.#")}\" stroke=\"{Resistance}\" stroke-width=\"1.6\"/>\n");
        body.Append($"<line x1=\"{N(Left, "0.#")}\" y1=\"{N(Y(setup.Stop), "0.#")}\" x2=\"{N(Right, "0.#")}\" y2=\"{N(Y(setup.Stop), "0.#")}\" stroke=\"{Support}\" stroke-width=\"1.6\" stroke-dasharray=\"5 4\"/>\n");

        var line = string.Join(" ", setup.Closes.Select((close, at) => $"{N(X(at), "0.#")},{N(Y(close.Close), "0.#")}"));

        body.Append($"<polyline points=\"{line}\" fill=\"none\" stroke=\"var(--ink)\" stroke-width=\"1.4\"/>\n");
        body.Append($"<circle cx=\"{N(X(0), "0.#")}\" cy=\"{N(Y(setup.Entry), "0.#")}\" r=\"4\" fill=\"var(--ink)\"/>\n");

        var resolvedAt = setup.Closes.Select((close, at) => (close.Session, at)).First(pair => pair.Session == setup.ResolvedOn).at;

        body.Append($"<circle cx=\"{N(X(resolvedAt), "0.#")}\" cy=\"{N(Y(setup.Closes[resolvedAt].Close), "0.#")}\" r=\"5\" fill=\"none\" stroke=\"{(setup.Outcome == "win" ? Resistance : Support)}\" stroke-width=\"2\"/>\n");
        body.Append(Text(Right + 8, Y(setup.Target) + 4, $"target {N(setup.Target)}", Resistance, "12"));
        body.Append(Text(Right + 8, Y(setup.Stop) + 4, $"stop {N(setup.Stop)}", Support, "12"));
        body.Append(Text(Left, Top - 10, $"{setup.Ticker}, entered at {N(setup.Entry)} on {D(setup.Session)}", "var(--muted)", "12"));
        body.Append(Text(Left, Bottom + 30, $"{(setup.Outcome == "win" ? "Won" : "Lost")} on {D(setup.ResolvedOn)}, {resolvedAt} session(s) after the entry, at a close of {N(setup.Closes[resolvedAt].Close)}.", "var(--ink)", "12.5", weight: "600"));

        return Figure(
            700,
            (int)(Bottom + 50),
            $"A setup resolving, {setup.Ticker} from {D(setup.Session)}",
            $"{setup.Ticker}'s swing plan read on {D(setup.Session)}, its closes from the entry to the session it resolved on, with the stop and the target it resolved against.",
            body.ToString(),
            $"Figure 11.3. A setup resolving, {setup.Ticker} replayed on the committed fixture's {D(setup.Session)}.",
            $"Replayed by the code over the committed fixture as of {D(setup.Session)}, an earlier session than the fixture's nights, so the closes after it are stored and the setup can resolve: the latest session at least twenty sessions before the fixture's night at which a member passed the setup gate and its swing plan resolved, at section 17's proposed values. The dot on the left is the entry at that session's close, the dashed green line the stop and the orange line the target; the ringed close is the one that decided it, the first close past either line.",
            "A setup is scored on closes and on nothing else: a close through the stop is a loss and a close at the target is a win, whichever comes first, and one that reaches neither inside 63 sessions is unresolved and never a win.");
    }

    // Figure 11.4: the two clocks on one timeline, illustrative.
    internal static string Clocks()
    {
        var body = new StringBuilder();

        body.Append(Text(20, 26, "Illustrative: the order things happen in, not a measurement.", "var(--muted)", "12"));
        body.Append("<line x1=\"40\" y1=\"96\" x2=\"660\" y2=\"96\" stroke=\"var(--faint)\" stroke-width=\"1\"/>\n");
        body.Append("<line x1=\"40\" y1=\"196\" x2=\"660\" y2=\"196\" stroke=\"var(--faint)\" stroke-width=\"1\"/>\n");
        body.Append(Text(40, 70, "Shape clock: how many names pass", "var(--ink)", "13", weight: "600"));
        body.Append(Text(40, 170, "Edge clock: whether passing names win", "var(--ink)", "13", weight: "600"));

        foreach (var (x, label, lane) in new (double, string, double)[]
        {
            (60, "version opened", 96), (230, "ordinary nights counted", 96), (400, "a proposal, your ruling", 96), (590, "shape frozen", 96),
            (60, "family registered", 196), (330, "first look: retire or leave", 196), (590, "earliest promotion", 196),
        })
        {
            body.Append($"<circle cx=\"{N(x, "0.#")}\" cy=\"{N(lane, "0.#")}\" r=\"5\" fill=\"{(lane < 150 ? Support : Resistance)}\"/>\n");
            body.Append(Text(x, lane + 24, label, "var(--ink)", "12", "middle"));
        }

        body.Append(Text(330, 244, "about two years", "var(--muted)", "12", "middle"));
        body.Append(Text(590, 244, "about three years", "var(--muted)", "12", "middle"));

        return Figure(
            700,
            262,
            "The swing filter's two clocks on one timeline, illustrative",
            "Illustrative. The shape clock tunes how many names pass and moves thresholds only through your rulings; the edge clock gathers evidence and changes nothing until it can retire a variant or promote one.",
            body.ToString(),
            "Figure 11.4. The two clocks on one timeline, illustrative.",
            "Illustrative: the positions show the order the events come in and not when they fall. The upper lane is the shape clock, which counts ordinary nights under one filter version and proposes a setting for your ruling, and is frozen after the one further acceptance the bound allows. The lower lane is the edge clock, which reads each candidate's blocks and can first retire one at its first look and first promote one at the look after.",
            "Nothing on the edge clock moves a threshold before its looks, and nothing on the shape clock reads an outcome, which is why the list can be tuned early and judged only late.");
    }

    // Figure 11.5: near-miss attribution, illustrative.
    internal static string NearMisses()
    {
        var body = new StringBuilder();

        body.Append(Text(20, 26, "Illustrative: invented shares, to show how the region reads.", "var(--muted)", "12"));

        var rows = new (string Label, int Share, int BreakEven, bool Withheld)[]
        {
            ("admitted by the filter", 46, 38, false),
            ("rejected by trigger alone", 41, 38, false),
            ("rejected by trade alone", 29, 40, false),
            ("removed by suspect series alone", 0, 0, true),
        };

        for (var at = 0; at < rows.Length; at++)
        {
            var (label, share, breakEven, withheld) = rows[at];
            var y = 52 + (at * 40);

            body.Append(Text(230, y + 16, label, anchor: "end"));

            if (withheld)
            {
                body.Append(Text(244, y + 16, "withheld below the block floor", "var(--muted)", "12"));

                continue;
            }

            body.Append($"<rect x=\"244\" y=\"{y + 4}\" width=\"{share * 4}\" height=\"18\" rx=\"3\" fill=\"{Support}\" fill-opacity=\"0.8\"/>\n");
            body.Append($"<line x1=\"{244 + (breakEven * 4)}\" y1=\"{y}\" x2=\"{244 + (breakEven * 4)}\" y2=\"{y + 26}\" stroke=\"{Resistance}\" stroke-width=\"2\"/>\n");
            body.Append(Text(244 + (share * 4) + 8, y + 17, $"{share}%", weight: "600"));
        }

        return Figure(
            700,
            52 + (rows.Length * 40) + 10,
            "Near-miss attribution, illustrative",
            "Illustrative. Beside the setups the filter admitted, the setups each gate or exclusion alone rejected, each group's share reaching target before stop against the break-even its own plans demanded.",
            body.ToString(),
            "Figure 11.5. Near-miss attribution, illustrative.",
            "Illustrative, with invented shares. Each bar is one group's share of setups that reached target before stop, and the orange mark is the break-even its own plans demanded. The first group is what the filter admitted; each other is what one gate or one exclusion alone kept out while everything else passed; a group below the block floor draws no bar.",
            "A gate is earning its place where the setups it alone rejects fall short of their own break-even, and costing setups where they clear it. Each group is a population and not a test, so it says where to look and never moves a threshold by itself.");
    }
}
