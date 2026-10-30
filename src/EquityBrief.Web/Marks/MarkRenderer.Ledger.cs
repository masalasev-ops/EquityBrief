using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Web.Marks;

// From 21.6 where the readings whose sources keep no history start, as the read surface reads the store: the first
// quarters fetch filing the current year's trend, the first night that asked every member's trend, the first settlement
// of FINRA's short interest held, and the stocks holding an answer from Yahoo.
public sealed record ReadingSources(DateOnly? FirstFetch, DateOnly? FirstNight, DateOnly? FirstSettlement, int AnalystStocks);

// The Ledger page's marks: a family's setups a year with the share the live rule passes and the share the night's list
// picked, the cut points between the deciles of result and edge, its newest settled setups to choose from, and the
// chosen setup's closes drawn with its plan's lines. Every figure is read off the summary the ledger's writers refresh
// or off the setup's own row; the marks draw and compute none.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: A heavyweights' setup is each member of its sector's size cut on a rebalance of the S&P 500's book, held as the rule holds a buy
public sealed partial class MarkRenderer
{
    const int LedgerWidth = 560;

    const int LedgerHeight = 200;

    const int LedgerTop = 22;

    const int LedgerRight = 92;

    // A figure in the unit its heading names once: a heavyweights' setup's in per cent of the buy and every other family's
    // in risks, signed and drawn with no unit of its own.
    static string LedgerNumber(string family, double? value) =>
        value is not { } held
            ? "<span class=\"degraded\">none</span>"
            : family == HeavyweightRule.Name
                ? SignedTenth(held * 100)
                : SignedTwo(held);

    // The unit a family's figures are read in, named once in each heading.
    static string LedgerUnit(string family) => family == HeavyweightRule.Name ? "% of the buy" : "risks";

    public string LedgerYears(string family, IReadOnlyList<LedgerYear> years)
    {
        var table = new StringBuilder();
        var unit = LedgerUnit(family);

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-years\" data-family=\"{Escaped(family)}\" data-rows=\"{years.Count}\"><thead><tr>");
        table.Append(TippedHeading("Year", "the calendar year of the setups' sessions"));
        table.Append(TippedHeading("Setups", "the member-sessions the family's loose gates passed that year", "r"));
        table.Append(TippedHeading("Live rule", "the share of them the live rule's own setting passes", "r"));
        table.Append(TippedHeading("Picked", "of the setups the night wrote, the share its list picked; the history build writes no pick", "r"));
        table.Append(TippedHeading("Settled", "the setups whose path and benchmark have both ended", "r"));
        table.Append(TippedHeading(Formatted($"Result, {unit}"), family == HeavyweightRule.Name ? "the settled setups' mean return as a share of the buy" : "the settled setups' mean result in multiples of their risk", "r"));
        table.Append(TippedHeading(Formatted($"Edge, {(family == HeavyweightRule.Name ? "points" : "risks")}"), family == HeavyweightRule.Name ? "the mean of each return less its size cut's, in points" : "the mean of each result less the same plan on every member that session, in risks", "r"));
        table.Append("</tr></thead><tbody>");

        foreach (var year in years)
        {
            var passed = year.Setups > 0 ? Formatted($"{100.0 * year.LivePasses / year.Setups:0}%") : "<span class=\"degraded\">none</span>";
            var picked = year.NightRows > 0 ? Formatted($"{100.0 * year.Picked / year.NightRows:0}% of {year.NightRows}") : "<span class=\"degraded\">none</span>";

            table.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-setups=\"{year.Setups}\" data-live-passes=\"{year.LivePasses}\" data-picked=\"{year.Picked}\" data-settled=\"{year.Settled}\" data-night-rows=\"{year.NightRows}\">");
            table.Append(Invariant, $"<td class=\"num\">{year.Year}</td><td class=\"r num\">{year.Setups}</td><td class=\"r num\">{passed}</td><td class=\"r num\">{picked}</td><td class=\"r num\">{year.Settled}</td>");
            table.Append(Invariant, $"<td class=\"r num\" data-result-mean=\"{(year.ResultMean is { } r ? r.ToString("R", Invariant) : "none")}\">{LedgerNumber(family, year.ResultMean)}</td>");
            table.Append(Invariant, $"<td class=\"r num\" data-edge-mean=\"{(year.EdgeMean is { } e ? e.ToString("R", Invariant) : "none")}\">{LedgerNumber(family, year.EdgeMean)}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        // Said once beneath the table where any year's setups were written by the history build alone.
        var unwritten = years.Count(year => year.NightRows == 0);

        if (unwritten > 0)
        {
            table.Append(Invariant, $"<p class=\"ledger-unwritten\" data-years=\"{unwritten}\">None written by a night: the {unwritten} year(s) whose Picked reads none were written by the history build alone, which writes no pick.</p>");
        }

        return LedgerEdgeBars(family, years) + table.ToString();
    }

    // The edge of each year's settled setups as a bar from one zero line, a gain in green and a loss in red, the year
    // beneath it and the figure under the pointer; a year holding none settled drawn as a gap.
    public static string LedgerEdgeBars(string family, IReadOnlyList<LedgerYear> years)
    {
        const int Column = 22;
        const int Height = 64;
        const int Axis = 30;
        var drawn = years.Where(year => year.EdgeMean is not null).ToArray();
        var top = drawn.Select(year => Math.Abs(year.EdgeMean!.Value)).DefaultIfEmpty(0).Max();
        var width = Math.Max(years.Count, 1) * Column;
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg class=\"ledger-edge-bars\" data-family=\"{Escaped(family)}\" data-years=\"{drawn.Length}\" viewBox=\"0 0 {width} {Height + 14}\" width=\"{width}\" height=\"{Height + 14}\" role=\"img\" aria-label=\"Each year's mean edge of its settled setups\">");
        svg.Append(Invariant, $"<line class=\"yb-axis\" x1=\"0\" y1=\"{Axis}\" x2=\"{width}\" y2=\"{Axis}\"/>");

        foreach (var (year, at) in years.Select((year, at) => (year, at)))
        {
            var x = (at * Column) + 3;

            if (year.EdgeMean is { } edge)
            {
                var length = top > 0 ? Math.Abs(edge) / top * (Axis - 2) : 0;
                var shown = family == HeavyweightRule.Name ? Formatted($"{edge * 100:+0.0;-0.0;0.0} points") : Formatted($"{edge:+0.00;-0.00;0.00} risks");

                svg.Append(Invariant, $"<rect class=\"{(edge >= 0 ? "gain" : "loss")}\" data-year=\"{year.Year}\" data-edge=\"{edge.ToString("R", Invariant)}\" x=\"{x}\" y=\"{(edge >= 0 ? Axis - length : Axis):0.#}\" width=\"{Column - 6}\" height=\"{length:0.#}\"><title>{year.Year}: {shown}</title></rect>");
            }

            svg.Append(Invariant, $"<text class=\"yb-year\" x=\"{x}\" y=\"{Height + 12}\">{year.Year % 100:00}</text>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The deciles of the newest year's settled setups as a strip: the edge's nine cut points on one line from its lowest to
    // its highest with nothing marked, a tick a cut point and the median drawn larger.
    public static string LedgerDecileStrip(LedgerYear? year)
    {
        if (year?.EdgeDeciles is not { Count: > 0 } edges)
        {
            return string.Empty;
        }

        const double Wide = 300;
        var low = Math.Min(edges.Min(), 0);
        var high = Math.Max(edges.Max(), 0);
        var span = high - low > 0 ? high - low : 1;

        double X(double value) => 6 + ((Wide - 12) * (value - low) / span);

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg class=\"ledger-decile-strip\" data-family=\"{Escaped(year.Family)}\" data-year=\"{year.Year}\" viewBox=\"0 0 {Wide} 26\" width=\"{Wide}\" height=\"26\" role=\"img\" aria-label=\"The edge at each tenth of {year.Year}'s settled setups\">");
        svg.Append(Invariant, $"<line class=\"ds-line\" x1=\"6\" y1=\"13\" x2=\"{Wide - 6}\" y2=\"13\"/>");
        svg.Append(Invariant, $"<line class=\"ds-zero\" x1=\"{X(0):0.#}\" y1=\"3\" x2=\"{X(0):0.#}\" y2=\"23\"/>");

        foreach (var (edge, at) in edges.Select((edge, at) => (edge, at)))
        {
            svg.Append(Invariant, $"<circle class=\"{(edge >= 0 ? "gain" : "loss")}\" data-cut=\"{at + 1}\" data-edge=\"{edge.ToString("R", Invariant)}\" cx=\"{X(edge):0.#}\" cy=\"13\" r=\"{(at == edges.Count / 2 ? 5 : 3)}\"/>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The family tabs over the newest settled setups, each in its family's tint, the chosen one marked.
    public static string LedgerTabs(IReadOnlyList<string> families, string? chosen, string universe, string route) =>
        "<nav class=\"ledger-tabs\" aria-label=\"The setup whose settled setups are drawn\">"
        + string.Concat(families.Select(word => Formatted(
            $"<a class=\"fam-tag\" data-family=\"{Escaped(word)}\" data-family-link=\"{Escaped(word)}\" href=\"{route}?universe={Uri.EscapeDataString(universe)}&amp;family={Uri.EscapeDataString(word)}\"{(word == chosen ? " aria-current=\"true\"" : string.Empty)}><span class=\"fam-swatch\" data-family=\"{Escaped(word)}\"></span>{Escaped(FamilyName(word))}</a>")))
        + "</nav>";

    // The cut points between the deciles of the newest year holding settled setups, result beside edge.
    public string LedgerDeciles(LedgerYear? year)
    {
        if (year?.ResultDeciles is not { } results || year.EdgeDeciles is not { } edges)
        {
            return "<p class=\"degraded\" data-deciles=\"none\">No setup of the family has settled yet, so no decile is drawn.</p>";
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-deciles\" data-family=\"{Escaped(year.Family)}\" data-year=\"{year.Year}\" data-settled=\"{year.Settled}\"><thead><tr>");
        table.Append(TippedHeading("Share below", "the share of the year's settled setups at or under the figure beside it"));
        table.Append(TippedHeading(Formatted($"Result, {LedgerUnit(year.Family)}"), "the result at that share", "r"));
        table.Append(TippedHeading(Formatted($"Edge, {(year.Family == HeavyweightRule.Name ? "points" : "risks")}"), "the edge at that share", "r"));
        table.Append("</tr></thead><tbody>");

        for (var cut = 0; cut < LedgerSummaries.CutPoints; cut++)
        {
            table.Append(Invariant, $"<tr data-cut=\"{cut + 1}\"><td class=\"num\">{(cut + 1) * 10}%</td><td class=\"r num\" data-result=\"{results[cut].ToString("R", Invariant)}\">{LedgerNumber(year.Family, results[cut])}</td><td class=\"r num\" data-edge=\"{edges[cut].ToString("R", Invariant)}\">{LedgerNumber(year.Family, edges[cut])}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        return LedgerDecileStrip(year) + table.ToString();
    }

    // The family's newest settled setups, each a link drawing its path on the page.
    public string LedgerSettled(IReadOnlyList<LedgerSetupRow> rows, string universe, string route)
    {
        if (rows.Count == 0)
        {
            return "<p class=\"degraded\" data-settled=\"none\">No setup of the family has settled yet.</p>";
        }

        var table = new StringBuilder();

        table.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-settled\" data-rows=\"{rows.Count}\"><thead><tr>");
        table.Append(TippedHeading("Session", "the session the setup was read on, bought at the next session's open"));
        table.Append(TippedHeading("Stock", "the member, which opens the setup's path below"));
        table.Append(TippedHeading("Live rule", "whether the live rule's own setting passed it"));
        table.Append(TippedHeading("End", "how its path ended"));
        table.Append(TippedHeading(rows.Count > 0 ? Formatted($"Result, {LedgerUnit(rows[0].Family)}") : "Result", "what its path came to", "r"));
        table.Append(TippedHeading(rows.Count > 0 && rows[0].Family == HeavyweightRule.Name ? "Edge, points" : "Edge, risks", "its result less its benchmark", "r"));
        table.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            var link = Formatted($"{route}?universe={Uri.EscapeDataString(universe)}&family={Uri.EscapeDataString(row.Family)}&setup={Uri.EscapeDataString(row.Ticker)}.{DayOf(row.Session)}");

            table.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-session=\"{DayOf(row.Session)}\" data-end=\"{Escaped(row.End)}\">");
            table.Append(Invariant, $"<td class=\"num\">{DayOf(row.Session)}</td><td><a class=\"ledger-choose\" href=\"{Escaped(link)}\">{Escaped(row.Ticker)}</a></td>");
            table.Append(Invariant, $"<td>{(row.LivePass ? "passed" : "not passed")}</td><td>{Escaped(row.End)}</td>");
            table.Append(Invariant, $"<td class=\"r num\">{LedgerNumber(row.Family, row.Result)}</td><td class=\"r num\">{LedgerNumber(row.Family, row.Edge)}</td></tr>");
        }

        table.Append("</tbody></table></div>");

        return table.ToString();
    }

    // From 21.6 the line saying where the readings whose sources keep no history start: the estimate revision from the
    // first quarters fetch and the night's first ask of every member, the rating actions from each stock's newest answer,
    // and FINRA's short interest from its first settlement held; each said not held yet where the store holds none.
    // see: A new reading joins the ledger with its history where its source keeps one, and from the night it is first stored where it keeps none
    public static string LedgerSources(ReadingSources sources)
    {
        static string On(DateOnly? day, string none) => day is { } held ? held.ToString("d MMM yyyy", Invariant) : none;

        return Formatted($"<p class=\"ledger-sources\" data-first-fetch=\"{On(sources.FirstFetch, "none")}\" data-first-night=\"{On(sources.FirstNight, "none")}\" data-first-settlement=\"{On(sources.FirstSettlement, "none")}\" data-analyst-stocks=\"{sources.AnalystStocks}\">")
            + Formatted($"The estimate revision reads the analysts' consensus, which its source states only on the day it is asked, so it holds nothing before a stock's first quarters fetch, the first on {On(sources.FirstFetch, "no day yet")}, and from {On(sources.FirstNight, "the night the night first asks it")} the night's own ask of every member. ")
            + Formatted($"The rating actions read each stock's newest answer from Yahoo, {sources.AnalystStocks:N0} stock{(sources.AnalystStocks == 1 ? string.Empty : "s")} holding one, and none on a session after its ask. ")
            + Formatted($"FINRA's short interest reads every settlement from {On(sources.FirstSettlement, "the first the store holds, none yet")}.</p>");
    }

    // Which readings go with winners for the family chosen, as the walk-forward tester stored them: the readings beyond
    // the shuffle first by their size and then every other in the catalogue's order, each with its heading on the chart of
    // what is behind a stock, the settled setups holding it, the test years counted, the mean correlation in its hue, how
    // many of those years pointed its way, the shuffle's 95th percentile and the words; or why no table is drawn. It draws
    // the stored rows and works none of them.
    // see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
    public string LedgerLinks(LinkTable? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return "<p class=\"degraded\" data-links=\"none\">No monthly run has worked which readings go with winners for this family on this index yet.</p>";
        }

        static int Beyond(ReadingLink row) => row.Words is ReadingLinks.Winners or ReadingLinks.Losers ? 0 : 1;

        var ordered = table.Rows
            .Select((row, at) => (Row: row, At: at))
            .OrderBy(pair => Beyond(pair.Row))
            .ThenByDescending(pair => Beyond(pair.Row) == 0 ? Math.Abs(pair.Row.Correlation ?? 0) : 0)
            .ThenBy(pair => pair.At)
            .Select(pair => pair.Row)
            .ToArray();
        var text = new StringBuilder();

        text.Append(Invariant, $"<p class=\"ledger-links-said\" data-run=\"{Escaped(table.RunId)}\" data-month=\"{Escaped(table.Month)}\">Worked by the monthly run for {Escaped(table.Month)}: {table.Rows.Count(row => row.Words == ReadingLinks.Winners)} reading{(table.Rows.Count(row => row.Words == ReadingLinks.Winners) == 1 ? string.Empty : "s")} going with winners and {table.Rows.Count(row => row.Words == ReadingLinks.Losers)} with losers beyond the shuffle, of {table.Rows.Count}.</p>");
        text.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-links\" data-rows=\"{table.Rows.Count}\"><thead><tr>");
        text.Append(TippedHeading("Reading", "the ledger's reading, its definition under the pointer"));
        text.Append(TippedHeading("Heading", "the heading it stands under on the chart of what is behind a stock"));
        text.Append(TippedHeading("Setups", "the settled setups holding it in the test years", "r"));
        text.Append(TippedHeading("Years", "the test years holding enough of them to count", "r"));
        text.Append(TippedHeading("Correlation", "the mean over those years of its rank correlation at entry with the setup's result", "r"));
        text.Append(TippedHeading("Same way", "how many of those years pointed the way the mean does", "r"));
        text.Append(TippedHeading("Shuffle's 95th", "the 95th percentile of the mean's size with the results shuffled within each night", "r"));
        text.Append(TippedHeading("Says", "whether its size stands above the shuffle's"));
        text.Append("</tr></thead><tbody>");

        foreach (var row in ordered)
        {
            var place = LedgerReadings.All.Select((reading, at) => (reading, at)).FirstOrDefault(pair => pair.reading.Column == row.Reading);
            var definition = place.reading?.Definition ?? string.Empty;
            var heading = place.reading is null ? string.Empty : LedgerHeadings.All[LedgerHeadings.HeadingOf(place.at)].Name;
            var correlation = row.Correlation is { } mean ? SignedFigure(mean, 3, Formatted($"{(mean > 0 ? "+" : string.Empty)}{mean:0.000}")) : "none";
            var sameWay = row.SameWay is { } share ? Formatted($"{Math.Round(share * row.Years, MidpointRounding.AwayFromZero):0} of {row.Years}") : "none";
            var shuffled = row.Shuffled is { } bar ? Formatted($"{bar:0.000}") : "none";

            text.Append(Invariant, $"<tr data-reading=\"{Escaped(row.Reading)}\" data-words=\"{Escaped(row.Words)}\" data-correlation=\"{(row.Correlation is { } value ? value.ToString("R", Invariant) : "none")}\" data-shuffled=\"{(row.Shuffled is { } held ? held.ToString("R", Invariant) : "none")}\">");
            text.Append(Invariant, $"<td><span title=\"{Escaped(definition)}\">{Escaped(row.Reading)}</span></td><td>{Escaped(heading)}</td>");
            text.Append(Invariant, $"<td class=\"r num\">{row.Setups:N0}</td><td class=\"r num\">{row.Years}</td><td class=\"r num\">{correlation}</td><td class=\"r num\">{sameWay}</td><td class=\"r num\">{shuffled}</td><td>{Escaped(row.Words)}</td></tr>");
        }

        text.Append("</tbody></table></div>");

        return text.ToString();
    }

    // From 20.8 what each answer the local model gave of the results releases came to on the family chosen, as the
    // walk-forward tester stored it: a row a question's answer in the order it wrote them, the setups holding it after the
    // model's cutoff, how many won and their mean result and edge in the family's own unit, and beside them the same before
    // the cutoff; or why no table is drawn. It draws the stored rows and works none of them.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    public string LedgerAnswers(string family, AnswerTable? table)
    {
        if (table is null || table.Rows.Count == 0)
        {
            return "<p class=\"degraded\" data-answers=\"none\">No monthly run has worked what the release readings came to for this family on this index yet: they join the ledger once their history read is complete.</p>";
        }

        var answers = table.Rows.Select(row => (row.Question, row.Answer)).Distinct().ToArray();
        var text = new StringBuilder();
        var (result, edge) = (Formatted($"Result, {LedgerUnit(family)}"), family == HeavyweightRule.Name ? "Edge, points" : "Edge, risks");

        string Side(AnswerEdge? row) =>
            row is null
                ? "<td class=\"r num\">none</td><td class=\"r num\">none</td><td class=\"r num\">none</td><td class=\"r num\">none</td>"
                : Formatted($"<td class=\"r num\">{row.Setups:N0}</td><td class=\"r num\">{row.Winners:N0}</td><td class=\"r num\" data-result-mean=\"{row.MeanResult.ToString("R", Invariant)}\">{LedgerNumber(family, row.MeanResult)}</td><td class=\"r num\" data-edge-mean=\"{row.MeanEdge.ToString("R", Invariant)}\">{LedgerNumber(family, row.MeanEdge)}</td>");

        text.Append(Invariant, $"<p class=\"ledger-answers-said\" data-run=\"{Escaped(table.RunId)}\" data-month=\"{Escaped(table.Month)}\" data-cutoff=\"{table.Cutoff:yyyy-MM-dd}\">Worked by the monthly run for {Escaped(table.Month)}, split at the local model's cutoff of {table.Cutoff:yyyy-MM-dd} by the day the SEC accepted each release.</p>");
        text.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"ledger-answers\" data-family=\"{Escaped(family)}\" data-rows=\"{answers.Length}\"><thead><tr>");
        text.Append(TippedHeading("Question", "what the local model was asked of the release"));
        text.Append(TippedHeading("Answer", "the word it answered, or not read where its reading was refused or is not held"));
        text.Append(TippedHeading("After: setups", "the settled setups whose release the SEC accepted after the cutoff", "r"));
        text.Append(TippedHeading("Won", "of those, the ones whose edge was above nothing", "r"));
        text.Append(TippedHeading(result, "their mean result", "r"));
        text.Append(TippedHeading(edge, "their mean edge over their benchmark", "r"));
        text.Append(TippedHeading("Before: setups", "the settled setups whose release the SEC accepted on or before the cutoff", "r"));
        text.Append(TippedHeading("Won", "of those, the ones whose edge was above nothing", "r"));
        text.Append(TippedHeading(result, "their mean result", "r"));
        text.Append(TippedHeading(edge, "their mean edge over their benchmark", "r"));
        text.Append("</tr></thead><tbody>");

        foreach (var (question, answer) in answers)
        {
            var after = table.Rows.FirstOrDefault(row => row.Question == question && row.Answer == answer && row.Side == ReleaseUsefulness.After);
            var before = table.Rows.FirstOrDefault(row => row.Question == question && row.Answer == answer && row.Side == ReleaseUsefulness.Before);

            text.Append(Invariant, $"<tr data-question=\"{Escaped(question)}\" data-answer=\"{Escaped(answer)}\"><td>{Escaped(question)}</td><td>{Escaped(answer)}</td>{Side(after)}{Side(before)}</tr>");
        }

        text.Append("</tbody></table></div>");

        return text.ToString();
    }

    // A chosen setup's closes from ten sessions before it to where its path ended, with its buy, its stop where it holds
    // one and its target where it names one drawn across, and the session it was bought on marked.
    public string LedgerPath(LedgerPath? path)
    {
        if (path is null)
        {
            return "<p class=\"degraded\" data-path=\"none\">Choose a settled setup above to draw its path.</p>";
        }

        var setup = path.Setup;

        if (path.Closes.Count < 2)
        {
            return Formatted($"<p class=\"degraded\" data-path=\"no-closes\" data-ticker=\"{Escaped(setup.Ticker)}\">The store holds {path.Closes.Count} close(s) of {Escaped(setup.Ticker)} over this setup's sessions: its bars sit in the pulled history, which this page does not read.</p>");
        }

        var lines = new List<(string Named, string Side, decimal Price)> { ("buy", "buy", setup.Entry) };

        if (setup.Stop > 0m)
        {
            lines.Add(("stop", "sup", setup.Stop));
        }

        if (setup.Target is { } target)
        {
            lines.Add(("target", "res", target));
        }

        var prices = path.Closes.Select(close => PlotValue(close.Close)).Concat(lines.Select(line => PlotValue(line.Price))).ToArray();
        var low = prices.Min();
        var high = prices.Max();
        var span = high - low > 0 ? high - low : 1;
        double plotWidth = LedgerWidth - LedgerRight - 4;
        var step = plotWidth / (path.Closes.Count - 1);

        double Y(double plotted) => LedgerTop + ((LedgerHeight - LedgerTop - 6) * (1 - ((plotted - low) / span)));

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {LedgerWidth} {LedgerHeight}\" width=\"{LedgerWidth}\" height=\"{LedgerHeight}\" role=\"img\" class=\"ledger-path\" data-ticker=\"{Escaped(setup.Ticker)}\" data-session=\"{DayOf(setup.Session)}\" data-closes=\"{path.Closes.Count}\">");
        svg.Append(Invariant, $"<title>{Escaped(setup.Ticker)}'s {Escaped(setup.Family)} setup of {DayOf(setup.Session)}, {path.Closes.Count} closes from {DayOf(path.Closes[0].Session)} to {DayOf(path.Closes[^1].Session)}</title>");
        svg.Append(Invariant, $"<text class=\"m-pane-h\" x=\"4\" y=\"14\">{Escaped(setup.Ticker)}, bought {DayOf(setup.Session)}, {Escaped(setup.End)}</text>");
        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"4\" y=\"{LedgerTop - 4}\" width=\"{plotWidth}\" height=\"{LedgerHeight - LedgerTop}\"/>");

        foreach (var (named, side, price) in lines)
        {
            var at = Y(PlotValue(price));

            svg.Append(Invariant, $"<line class=\"m-edge-{side}\" data-{named}=\"{price.ToString(Invariant)}\" x1=\"4\" y1=\"{Number(at)}\" x2=\"{4 + plotWidth}\" y2=\"{Number(at)}\"/>");
            svg.Append(Invariant, $"<text class=\"m-legend-t\" x=\"{8 + plotWidth}\" y=\"{Number(at + 4)}\">{named} {Price(price)}</text>");
        }

        var bought = path.Closes.Select((close, at) => (close.Session, at)).FirstOrDefault(pair => pair.Session >= setup.Session).at;

        svg.Append(Invariant, $"<line class=\"m-bought\" data-bought=\"{DayOf(setup.Session)}\" x1=\"{Number(4 + (step * bought))}\" y1=\"{LedgerTop - 4}\" x2=\"{Number(4 + (step * bought))}\" y2=\"{LedgerHeight - 2}\"/>");

        var line = new StringBuilder();

        for (var at = 0; at < path.Closes.Count; at++)
        {
            line.Append(at == 0 ? 'M' : 'L').Append(Number(4 + (step * at))).Append(' ').Append(Number(Y(PlotValue(path.Closes[at].Close)))).Append(' ');
        }

        svg.Append(Invariant, $"<path class=\"m-mom\" d=\"{line.ToString().Trim()}\"/>");
        svg.Append("</svg>");

        return svg.ToString();
    }
}
