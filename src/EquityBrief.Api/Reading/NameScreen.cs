using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Research;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Spending;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what a mark takes.
//
// It sits between the read surface and the app because both of those have a
// rule about them: the read surface hands back stored values unchanged, and the
// app renders and computes nothing. Turning a row into a mark's input is
// neither, so it is named here rather than smuggled into one of them.
//
// Nothing in it derives a figure. Every value is the stored column, and the one
// thing that is worked out is which of the four momentum readings exist, which
// is a lookup in a list the indicator arithmetic already carries.
// see: A screen reads and renders, and computes nothing
public static class NameScreen
{
    // The three averages drawn on a price axis. The momentum readings are not
    // among them for the reason the level builder gives: an RSI is not a price.
    static readonly string[] Averages = [IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    // The plan column's rows, from the ladder's stored plan. Nothing here
    // derives a figure: each row is a price the ladder wrote and a sentence
    // saying what it is, and the sentences are assembled from stored values
    // rather than computed from them.
    public static IReadOnlyList<PlanRow> PlanRows(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return [];
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var root = plan.RootElement;
        var rows = new List<PlanRow>();

        foreach (var tranche in root.GetProperty("tranches").EnumerateArray())
        {
            var low = Price(tranche, "lowEdge");
            var high = Price(tranche, "highEdge");
            decimal? stop = tranche.GetProperty("stop").GetString() is { } stored
                ? decimal.Parse(stored, CultureInfo.InvariantCulture)
                : null;
            var condition = tranche.GetProperty("condition").GetString();

            rows.Add(new PlanRow(
                low,
                high,
                PlanKind.Tranche,
                stop is { } below
                    ? $"buy on {Words(condition)}, stop on a daily close below {Figures.Price(below)}"
                    : $"buy on {Words(condition)}, no stop beneath",
                Traded: true));

            // The stop is its own row, because it is a price a close is measured
            // against rather than a zone anything is bought in.
            if (stop is { } price)
            {
                rows.Add(new PlanRow(price, price, PlanKind.Stop, $"stop for the {Figures.Price(low)} zone", Traded: true));
            }
        }

        foreach (var exit in root.GetProperty("exits").EnumerateArray())
        {
            var traded = exit.GetProperty("traded").GetBoolean();
            var trailing = exit.GetProperty("trailing").GetBoolean();
            var fraction = exit.GetProperty("fraction").GetString();

            rows.Add(new PlanRow(
                Price(exit, "lowEdge"),
                Price(exit, "highEdge"),
                PlanKind.Exit,
                traded
                    ? trailing
                        ? $"sell {fraction} and trail the rest"
                        : $"sell {fraction}"
                    : exit.GetProperty("reason").GetString() ?? "listed and not traded",
                traded));
        }

        // The invalidation is the lowest stop rather than a rule beside it, which
        // is what section 15.5 says the figure shows: stops are horizontal rules
        // and the invalidation is the lowest one. So the stop at that price is
        // relabelled rather than a second rule being drawn on top of it, and a
        // row is added only where no stop sits there, which is a structure whose
        // lowest tranche has no band beneath it.
        if (root.GetProperty("invalidation").GetString() is { } invalidation)
        {
            var price = decimal.Parse(invalidation, CultureInfo.InvariantCulture);
            var at = rows.FindIndex(row => row.Kind == PlanKind.Stop && row.LowEdge == price);

            var detail = "the whole position is wrong below this";

            if (at >= 0)
            {
                rows[at] = rows[at] with
                {
                    Kind = PlanKind.Invalidation,
                    Detail = $"{rows[at].Detail}, and {detail}",
                };
            }
            else
            {
                rows.Add(new PlanRow(price, price, PlanKind.Invalidation, detail, Traded: true));
            }
        }

        return rows;
    }

    // The second book, as the page shows it. Every setup says it is a proposal
    // and names what will calibrate it, because a figure on a screen gets acted
    // on and one that looks measured and is not is the failure this states its
    // way out of.
    // see: The event setups' triggers are proposals until resolved setups can score them
    public static string EventBook(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return string.Empty;
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var setups = plan.RootElement.GetProperty("events").EnumerateArray().ToArray();

        var html = new System.Text.StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"event-book\" data-ticker=\"{ladder.Ticker}\" data-setups=\"{setups.Length}\">");

        if (setups.Length == 0)
        {
            html.Append("<p class=\"degraded\">no dated event is on file, so there are no setups</p></section>");

            return html.ToString();
        }

        html.Append(
            "<p class=\"proposal-note\" data-proposal=\"true\">Every figure in these setups is a " +
            "proposal and none has been tested. They are calibrated against resolved setups on the " +
            "run page, not tuned in advance.</p>");

        html.Append("<div class=\"tbl-wrap\">");
        html.Append("<table class=\"event-table\"><tr><th>Setup</th><th>Trigger</th><th>Entry, stop and target</th></tr>");

        foreach (var setup in setups)
        {
            var entry = setup.GetProperty("entry").GetString();
            var stop = setup.GetProperty("stop").GetString();
            var target = setup.GetProperty("target").GetString();

            html.Append(CultureInfo.InvariantCulture, $"<tr data-setup=\"{setup.GetProperty("name").GetString()}\" data-proposal=\"true\" data-entry=\"{entry}\" data-stop=\"{stop}\" data-target=\"{target}\">");
            html.Append(CultureInfo.InvariantCulture, $"<td>{setup.GetProperty("name").GetString()} on {setup.GetProperty("eventDate").GetString()}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>{Figures.InSentence(setup.GetProperty("trigger").GetString() ?? string.Empty)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td>enter {Drawn(entry, Figures.Price)}, stop {Drawn(stop, Figures.Price)}, target {Drawn(target, Figures.Price)}</td></tr>");
        }

        html.Append("</table></div></section>");

        return html.ToString();
    }

    // Section 15.9's fact strip, which is the region that states a name's summary
    // figures in one line.
    //
    // Seven parts, which are the seven the row enumerates, and every one is on its
    // own attribute so a strip that drew six of them fails rather than passing on
    // the row. Two of the seven are the reason the row is owed at 6.1 and not
    // earlier: market capitalisation and the multiples come from the fundamentals,
    // and no computed table holds either.
    //
    // Every figure is stored. The multiples were copied from the provider with the
    // earnings basis they were struck on, the market value likewise, the close and
    // the extremes are bars, and the averages, the momentum readings and the
    // typical daily move are the indicator engine's own rows.
    // see: A screen reads and renders, and computes nothing
    public static string FactStrip(
        string ticker,
        decimal? close,
        DateOnly? nextEvent,
        MoveExtremes? extremes,
        IReadOnlyList<FilingRow> filings,
        IReadOnlyList<IndicatorRow> indicators)
    {
        var latest = indicators
            .GroupBy(row => row.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.SessionDate).Last().Value,
                StringComparer.Ordinal);

        var newest = filings.Count > 0 ? JsonDocument.Parse(filings[0].Payload) : null;

        var html = new System.Text.StringBuilder();

        // The strip as a grid a reader scans, each figure to the places it is read at, above the
        // sentence that states every value as the store holds it.
        // see: Every region is a card that states where its figures came from and how to read them
        void Fact(string label, string value, string? note = null) =>
            html.Append(CultureInfo.InvariantCulture, $"<div><dt>{label}</dt><dd>{value}{(note is null ? string.Empty : $" <small>{note}</small>")}</dd></div>");

        var multiples = newest is not null
            && newest.RootElement.TryGetProperty("valuation", out var stated)
            && stated.ValueKind == JsonValueKind.Object
                ? stated
                : default;

        html.Append("<dl class=\"facts\">");
        Fact("Close", close is { } shownClose ? Read(shownClose) : NotOnFile);
        Fact("Market value", Scaled(newest is null ? null : Text(newest.RootElement, "marketCapitalisation")));
        Fact("High of the move", extremes is { } highs ? Read(highs.High) : NotOnFile, extremes is { } over ? Invariant($"over {over.Sessions} session(s)") : null);
        Fact("Low of the move", extremes is { } lows ? Read(lows.Low) : NotOnFile);
        Fact("Next dated event", nextEvent is { } coming ? Day(coming) : NotOnFile);
        Fact("Price to earnings", Tenths(Text(multiples, "trailingPe")), Text(multiples, "forwardPe") is { } forward ? $"forward {Tenths(forward)}" : null);
        Fact("20-day average", Hundredths(latest, IndicatorSeries.Sma20));
        Fact("50-day average", Hundredths(latest, IndicatorSeries.Sma50));
        Fact("200-day average", Hundredths(latest, IndicatorSeries.Sma200));
        Fact("Relative strength", latest.TryGetValue(IndicatorSeries.Rsi14, out var strength) && strength is { } rsi ? rsi.ToString("0.0", CultureInfo.InvariantCulture) : NotOnFile);
        Fact("Trend momentum", latest.TryGetValue(IndicatorSeries.MacdHist, out var momentum) && momentum is { } gap ? gap.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : NotOnFile);
        Fact("Typical daily move", Hundredths(latest, IndicatorSeries.Atr14));
        html.Append("</dl>");

        html.Append(CultureInfo.InvariantCulture, $"<p class=\"fact-strip\" data-ticker=\"{ticker}\"");

        // close
        html.Append(close is { } last
            ? FormattableString.Invariant($" data-close=\"{last}\"")
            : " data-close=\"none\"");

        // market capitalisation, and the multiples, which are the two parts no
        // computed table holds
        var capitalisation = newest is null ? null : Text(newest.RootElement, "marketCapitalisation");

        html.Append(CultureInfo.InvariantCulture, $" data-market-capitalisation=\"{capitalisation ?? "none"}\"");

        var valuation = newest is not null
            && newest.RootElement.TryGetProperty("valuation", out var ratios)
            && ratios.ValueKind == JsonValueKind.Object
                ? ratios
                : default;

        html.Append(CultureInfo.InvariantCulture, $" data-trailing-multiple=\"{Text(valuation, "trailingPe") ?? "none"}\"");
        html.Append(CultureInfo.InvariantCulture, $" data-forward-multiple=\"{Text(valuation, "forwardPe") ?? "none"}\"");

        // the high and low of the move, over the sessions that move spans
        html.Append(extremes is { } move
            ? FormattableString.Invariant($" data-move-high=\"{move.High}\" data-move-low=\"{move.Low}\" data-move-sessions=\"{move.Sessions}\"")
            : " data-move-high=\"none\" data-move-low=\"none\"");

        // next earnings date
        html.Append(nextEvent is { } dated
            ? $" data-next-event=\"{Day(dated)}\""
            : " data-next-event=\"none\"");

        // the averages, then momentum and the typical daily move, which the row
        // states as one part and which are drawn as the readings they are
        foreach (var name in Averages)
        {
            html.Append(CultureInfo.InvariantCulture, $" data-{name}=\"{Reading(latest, name)}\"");
        }

        foreach (var name in IndicatorSeries.Momentum)
        {
            html.Append(CultureInfo.InvariantCulture, $" data-{name}=\"{Reading(latest, name)}\"");
        }

        html.Append(CultureInfo.InvariantCulture, $" data-{IndicatorSeries.Atr14}=\"{Reading(latest, IndicatorSeries.Atr14)}\">");

        // The sentence a person reads, which states the same figures in the same
        // order the row lists them, at the places the grid reads them at. A strip
        // whose attributes and words disagree is two statements, so both come from
        // the values above.
        html.Append(CultureInfo.InvariantCulture, $"close {(close is { } closed ? Read(closed) : NotOnFile)}, market capitalisation {Scaled(capitalisation)}, ");
        html.Append(extremes is { } drawn
            ? FormattableString.Invariant($"the move's high {Read(drawn.High)} and low {Read(drawn.Low)} over {drawn.Sessions} session(s), ")
            : "the move's high and low not on file, ");
        html.Append(CultureInfo.InvariantCulture, $"next dated event: {(nextEvent is { } on ? Day(on) : "not on file")}, ");
        html.Append(CultureInfo.InvariantCulture, $"trailing multiple {Tenths(Text(valuation, "trailingPe"))}, ");
        html.Append(CultureInfo.InvariantCulture, $"forward multiple {Tenths(Text(valuation, "forwardPe"))}");
        html.Append("</p>");

        newest?.Dispose();

        return html.ToString();
    }

    // One indicator reading as the store holds it, or the absence said in a word.
    // A reading the engine could not compute over the stored year is not a zero.
    static string Reading(IReadOnlyDictionary<string, double?> latest, string name) =>
        latest.TryGetValue(name, out var value) && value is { } reading
            ? reading.ToString("0.######", CultureInfo.InvariantCulture)
            : "none";

    const string NotOnFile = "not on file";

    // A figure as the fact grid reads it: a price to two places, a money figure in its scale, a
    // multiple to one place. Each is the stored value rounded to be read, and the strip's own
    // attributes carry it whole.
    // see: A figure is drawn at the places it is read at, and its element carries the stored value whole
    static string Read(decimal price) => Figures.Price(price);

    static string Hundredths(IReadOnlyDictionary<string, double?> latest, string name) =>
        latest.TryGetValue(name, out var value) && value is { } reading
            ? reading.ToString("#,##0.00", CultureInfo.InvariantCulture)
            : NotOnFile;

    static string Tenths(string? stored) => Drawn(stored, Figures.Multiple);

    static string Scaled(string? stored) => Drawn(stored, value => Figures.Money(value));

    // A stored figure as it is read, the stored text where it is not a number, and the absence
    // said in words.
    static string Drawn(string? stored, Func<decimal, string> read) =>
        stored is null
            ? NotOnFile
            : decimal.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? read(value)
                : stored;

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // How many reported quarters the numbers section draws, which is section 4's
    // own figure. The store holds twelve and this is what is shown, and the two
    // being different numbers is the point of the ruling that set them
    // (see: Twelve filings are stored and five are shown).
    public const int QuartersShown = 5;

    // Section 4's numbers, as the page shows them.
    //
    // Every value is a stored one, drawn at the places it is read at with the
    // stored value on its cell. Nothing here works a figure out: the margin was
    // computed by the fetcher from the two figures in its own filing, and the
    // valuation was copied from the provider with the earnings basis beside it.
    // see: A screen reads and renders, and computes nothing
    // see: A figure is drawn at the places it is read at, and its element carries the stored value whole
    //
    // Each figure carries the filing date it came from, which is what the whole
    // table is keyed on (see: Fundamentals are stored with the filing date they
    // came from).
    //
    // Every absence is stated rather than left blank, because a blank cell reads as
    // a zero, and each says which of the three reasons it was: a part the company
    // financials endpoint files for nobody, a part the archive answered about and
    // did not serve, and a part the archive was not read for at all. The three are
    // different mornings and the source column beside the payload carries which.
    //
    // The guided quarter is management's own passage rather than a figure, and the
    // consensus estimate is drawn beside it and named for what it is: what analysts
    // expect rather than what management said.
    // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
    public static string Numbers(IReadOnlyList<FilingRow> filings)
    {
        var html = new System.Text.StringBuilder();

        if (filings.Count == 0)
        {
            // Not an error and not a blank. A name nobody has opened holds no
            // filing, and the computed sections render from the nightly store
            // whatever this one says.
            return "<section class=\"numbers\" data-filings-held=\"0\">"
                + "<p class=\"degraded\" data-absent=\"fundamentals\">No filing is stored for this name "
                + "yet, so the numbers section is empty. The computed sections above are tonight's.</p>"
                + "</section>";
        }

        using var newest = JsonDocument.Parse(filings[0].Payload);

        var currency = Text(newest.RootElement, "currency") ?? string.Empty;
        var shown = Math.Min(QuartersShown, filings.Count);

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"numbers\" data-ticker=\"{filings[0].Ticker}\"");
        html.Append(CultureInfo.InvariantCulture, $" data-filings-held=\"{filings.Count}\" data-quarters-shown=\"{shown}\"");
        html.Append(CultureInfo.InvariantCulture, $" data-currency=\"{currency}\">");

        // The count the reading was made over, stated on the surface rather than
        // inferred from the number of rows drawn, for the reason an indicator row
        // carries its bar count.
        if (filings.Count < QuartersShown)
        {
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"degraded\" data-short-window=\"{filings.Count}\">This name holds " +
                $"{filings.Count} filing(s), fewer than the {QuartersShown} quarters this section states.</p>");
        }

        html.Append("<div class=\"tbl-wrap\">");
        html.Append("<table class=\"numbers-quarters\"><tr><th>Quarter</th><th>Revenue</th>");
        html.Append("<th>Gross margin</th><th>Net margin</th><th>Net income</th><th>Filed</th></tr>");

        foreach (var filing in filings.Take(shown))
        {
            using var payload = JsonDocument.Parse(filing.Payload);

            var quarter = payload.RootElement.TryGetProperty("quarter", out var figures) ? figures : default;
            var period = Text(payload.RootElement, "periodEnd") ?? string.Empty;

            html.Append(CultureInfo.InvariantCulture, $"<tr data-period-end=\"{period}\" data-filed=\"{Day(filing.FilingDate)}\">");
            html.Append(CultureInfo.InvariantCulture, $"<td>{period}</td>");
            html.Append(Cell(quarter, "revenue", value => Figures.Money(value, currency)));
            html.Append(Cell(quarter, "grossMargin", Figures.Percent));
            html.Append(Cell(quarter, "netMargin", Figures.Percent));
            html.Append(Cell(quarter, "netIncome", value => Figures.Money(value, currency)));
            html.Append(CultureInfo.InvariantCulture, $"<td data-filed=\"{Day(filing.FilingDate)}\">{Day(filing.FilingDate)}</td></tr>");
        }

        html.Append("</table></div>");

        html.Append(Guidance(newest.RootElement, Attribution(filings[0].Source)));

        if (newest.RootElement.TryGetProperty("estimated", out var estimated)
            && estimated.ValueKind == JsonValueKind.Object)
        {
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"estimate\" data-estimated-quarter=\"{Text(estimated, "periodEnd")}\"" +
                $" data-eps-estimate=\"{Text(estimated, "epsEstimate")}\">The next quarter ends " +
                $"{Text(estimated, "periodEnd")} and is expected on {Text(estimated, "reportDate")}, " +
                $"with a consensus estimate of {Drawn(Text(estimated, "epsEstimate"), Figures.PerShare)} per share.</p>");
        }
        else
        {
            html.Append(
                "<p class=\"degraded\" data-absent=\"estimate\">No estimate is filed for this name's " +
                "next quarter, so no expected figure is shown.</p>");
        }

        html.Append(Sheet(newest.RootElement, filings[0].FilingDate, currency));
        html.Append(Valuation(newest.RootElement));
        html.Append(Segments(newest.RootElement, Attribution(filings[0].Source), currency));

        html.Append("</section>");

        return html.ToString();
    }

    // Which provider each part of the row came from, read off the stored source
    // column. A screen that guessed would be a screen deciding what an absence
    // meant, and the three reasons a part can be absent are exactly what this
    // column exists to tell apart.
    // see: A screen reads and renders, and computes nothing
    static IReadOnlyDictionary<string, string> Attribution(string source)
    {
        try
        {
            using var document = JsonDocument.Parse(source);

            return document.RootElement.EnumerateObject()
                .ToDictionary(part => part.Name, part => part.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A row written before the column held an object. Nothing is drawn from
            // it rather than the section refusing, because the figures beside it are
            // still the store's.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    // Management's own forecast, as filed, with the exhibit and the date it came
    // from. Never a figure: the release states it in a sentence and a range pulled
    // out of prose by pattern is neither computed from stored data nor copied from a
    // payload with its filing date.
    // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
    static string Guidance(JsonElement payload, IReadOnlyDictionary<string, string> source)
    {
        if (!payload.TryGetProperty("guidance", out var guidance) || guidance.ValueKind != JsonValueKind.Object)
        {
            return Unavailable("guidance", source, "the guided quarter");
        }

        var document = Text(guidance, "document") ?? string.Empty;
        var filedOn = Text(guidance, "filedOn") ?? string.Empty;

        if (!guidance.TryGetProperty("located", out var located) || !located.GetBoolean())
        {
            // Not the same statement as a company that guided nothing, and the
            // exhibit named beside it is the evidence for which of the two it was.
            // A heading locates the passage for five of twelve filers measured.
            return "<p class=\"degraded\" data-absent=\"guidance\" data-guidance=\"not located\""
                + FormattableString.Invariant($" data-exhibit=\"{document}\" data-filed=\"{filedOn}\">")
                + "The earnings release filed on " + filedOn + " is stored and no heading in it locates a "
                + "guidance passage, which is not the same as this company stating none. The exhibit is "
                + document + ".</p>";
        }

        var heading = Text(guidance, "heading") ?? string.Empty;
        var passage = Text(guidance, "passage") ?? string.Empty;

        return "<blockquote class=\"guidance\" data-guidance=\"located\""
            + FormattableString.Invariant($" data-exhibit=\"{document}\" data-filed=\"{filedOn}\"")
            + FormattableString.Invariant($" data-heading=\"{heading}\">")
            + "<p class=\"guidance-heading\">" + heading + ", as management filed it on " + filedOn + "</p>"
            + "<p class=\"guidance-passage\">" + Escaped(passage) + "</p>"
            + "<p class=\"guidance-source\">From " + document + ", the exhibit to that day's results "
            + "announcement. Management's own words, not a figure this report computed.</p></blockquote>";
    }

    // The segment table, as the archive rendered it, for the newest period the
    // table states.
    //
    // Every group the table carries, in the order it carries them, because two
    // groups of one captured table share a member and two labels repeat: a screen
    // that keyed on the label would draw one of them and drop the other.
    static string Segments(JsonElement payload, IReadOnlyDictionary<string, string> source, string currency)
    {
        if (!payload.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Object)
        {
            return Unavailable("segments", source, "the segment table");
        }

        var report = Text(segments, "report") ?? string.Empty;
        var periods = segments.GetProperty("periods").EnumerateArray().ToArray();

        // The shortest period the table states, which is the quarter. A table
        // carrying three months and nine months under the same end date would
        // otherwise show three quarters of a year as one.
        var shortest = periods.Length == 0
            ? 0
            : periods.Min(period => period.GetProperty("months").GetInt32());

        var ended = periods
            .Where(period => period.GetProperty("months").GetInt32() == shortest)
            .Select(period => period.GetProperty("ended").GetString() ?? string.Empty)
            .DefaultIfEmpty(string.Empty)
            .Max(StringComparer.Ordinal)!;

        var html = new System.Text.StringBuilder();

        html.Append("<div class=\"tbl-wrap\">");
        html.Append("<table class=\"numbers-segments\"")
            .Append(FormattableString.Invariant($" data-report=\"{report}\" data-months=\"{shortest}\""))
            .Append(FormattableString.Invariant($" data-period-end=\"{ended}\">"));

        html.Append("<tr><th>Segment</th><th>Line</th><th>Figure</th></tr>");

        var rows = 0;

        foreach (var (label, figures) in Grouped(segments))
        {
            foreach (var figure in figures)
            {
                if (figure.GetProperty("months").GetInt32() != shortest
                    || (figure.GetProperty("ended").GetString() ?? string.Empty) != ended)
                {
                    continue;
                }

                var value = figure.GetProperty("value");

                html.Append(FormattableString.Invariant($"<tr data-segment=\"{label}\">"))
                    .Append("<td>").Append(label).Append("</td>")
                    .Append("<td>").Append(figure.GetProperty("lineItem").GetString()).Append("</td>")
                    .Append(value.ValueKind == JsonValueKind.String
                        ? "<td class=\"num\" data-segment-figure=\"" + value.GetString() + "\">" + SegmentFigure(value.GetString()!, figure, currency) + "</td>"
                        : "<td class=\"degraded\" data-segment-figure=\"absent\">not filed</td>")
                    .Append("</tr>");

                rows++;
            }
        }

        html.Append("</table></div>");
        html.Append("<p class=\"segments-source\">")
            .Append(FormattableString.Invariant(
                $"The {shortest} month(s) to {ended}, from {report} of that filing, {rows} line(s)."))
            .Append("</p>");

        return html.ToString();
    }

    // The consolidated rows first, labelled for what they are, then every group in
    // the order the table states them.
    static IEnumerable<(string Label, IReadOnlyList<JsonElement> Figures)> Grouped(JsonElement segments)
    {
        yield return ("The company", [.. segments.GetProperty("consolidated").EnumerateArray()]);

        foreach (var group in segments.GetProperty("groups").EnumerateArray())
        {
            yield return (
                group.GetProperty("label").GetString() ?? string.Empty,
                [.. group.GetProperty("figures").EnumerateArray()]);
        }
    }

    // A figure in the table's currency in its scale, and one that is not carrying the unit it
    // is in, as filed, which is how a count of two segments stops reading as two million of them.
    static string SegmentFigure(string stored, JsonElement figure, string currency) =>
        figure.GetProperty("unit").ValueKind == JsonValueKind.String
            ? stored + " " + figure.GetProperty("unit").GetString()
            : Drawn(stored, value => Figures.Money(value, currency));

    // A part the archive supplies and this row does not carry, with the reason read
    // off the source column rather than assumed. A read that did not happen and a
    // provider that served nothing are different mornings, and neither is a blank.
    static string Unavailable(string part, IReadOnlyDictionary<string, string> source, string what)
    {
        var reason = source.TryGetValue(part, out var stated) ? stated : string.Empty;

        return "<p class=\"degraded\" data-absent=\"" + part + "\""
            + FormattableString.Invariant($" data-reason=\"{reason}\">")
            + Capitalised(what) + " is absent rather than empty, and the row says why: "
            + (reason.Length > 0 ? reason : "no source is recorded for this part") + ".</p>";
    }

    static string Capitalised(string what) =>
        what.Length == 0 ? what : char.ToUpperInvariant(what[0]) + what[1..];

    // The archive's own text, drawn as text. A release exhibit is a filer's prose
    // and this report quotes it, so the markup it might carry is shown rather than
    // rendered.
    static string Escaped(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    // The balance sheet, from the newest filing and labelled with its date. The
    // figures on this block are as of a filing rather than as of the fetch, which is
    // why the date is drawn beside them.
    static string Sheet(JsonElement payload, DateOnly filed, string currency)
    {
        if (!payload.TryGetProperty("balanceSheet", out var sheet) || sheet.ValueKind != JsonValueKind.Object)
        {
            return "<p class=\"degraded\" data-absent=\"balanceSheet\">No balance sheet is stored for " +
                "this filing.</p>";
        }

        var html = new System.Text.StringBuilder();

        html.Append("<div class=\"tbl-wrap\">");
        html.Append(CultureInfo.InvariantCulture, $"<table class=\"numbers-balance-sheet\" data-filed=\"{Day(filed)}\">");
        html.Append("<tr><th>Total assets</th><th>Total liabilities</th><th>Equity</th><th>Cash</th><th>Net debt</th></tr><tr>");
        foreach (var name in new[] { "totalAssets", "totalLiabilities", "equity", "cash", "netDebt" })
        {
            html.Append(Cell(sheet, name, value => Figures.Money(value, currency)));
        }
        html.Append("</tr></table></div>");

        return html.ToString();
    }

    // The valuation on each earnings basis, with the basis beside the ratio. Both
    // are stored values, and the basis is drawn because a multiple without the
    // earnings figure it was struck on is a number nobody can check.
    static string Valuation(JsonElement payload)
    {
        if (!payload.TryGetProperty("valuation", out var valuation) || valuation.ValueKind != JsonValueKind.Object)
        {
            return "<p class=\"degraded\" data-absent=\"valuation\">No valuation is stored for this " +
                "name's newest filing.</p>";
        }

        var bases = payload.TryGetProperty("epsBases", out var held) && held.ValueKind == JsonValueKind.Object
            ? held
            : default;

        var html = new System.Text.StringBuilder();

        html.Append("<div class=\"tbl-wrap\">");
        html.Append("<table class=\"numbers-valuation\"><tr><th>Basis</th><th>Earnings per share</th><th>Price to earnings</th></tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr data-basis=\"trailing\"><td>trailing</td>{Cell(bases, "trailing", Figures.PerShare)}{Cell(valuation, "trailingPe", Figures.Multiple)}</tr>");
        html.Append(CultureInfo.InvariantCulture, $"<tr data-basis=\"forward\"><td>next year</td>{Cell(bases, "nextYear", Figures.PerShare)}{Cell(valuation, "forwardPe", Figures.Multiple)}</tr>");
        html.Append("</table></div>");

        return html.ToString();
    }

    // One figure, drawn at the places it is read at, with the stored text on the
    // cell as well so a test reads back what was written rather than what was
    // rendered. A figure the filing does not carry says so in words rather than as
    // an empty cell.
    static string Cell(JsonElement holder, string name, Func<decimal, string> read)
    {
        var value = Text(holder, name);

        return value is null
            ? $"<td class=\"degraded\" data-{name}=\"absent\">not filed</td>"
            : $"<td class=\"num\" data-{name}=\"{value}\">{Drawn(value, read)}</td>";
    }

    // A date as the store holds it. Formatted here rather than in an
    // interpolation hole, because a hole carrying a date format is a form
    // `clock-usage` reads off the literal and the pinned forms it accepts do not
    // include a StringBuilder handed a provider. The date reaches the hole as text
    // that was already formatted invariantly, which is the same guarantee by a
    // route the reader can see.
    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string? Text(JsonElement holder, string name) =>
        holder.ValueKind == JsonValueKind.Object
        && holder.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The sizing arithmetic and the earnings rule, as the plan section states
    // them. Every figure is read off the ladder row, which derived them from its
    // own prices, so nothing here computes and nothing can drift.
    // see: A screen reads and renders, and computes nothing
    public static string Arithmetic(LadderRow? ladder)
    {
        if (ladder is null)
        {
            return string.Empty;
        }

        using var plan = JsonDocument.Parse(ladder.Plan);
        var figures = plan.RootElement.GetProperty("arithmetic");
        var prints = plan.RootElement.GetProperty("earningsRule").EnumerateArray().ToArray();

        var html = new System.Text.StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"plan-arithmetic\" data-ticker=\"{ladder.Ticker}\">");

        if (figures.GetProperty("absent").GetString() is { } absent)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"degraded\" data-absent=\"true\">{absent}</p>");
        }
        else
        {
            html.Append("<div class=\"tbl-wrap\">");
            html.Append("<table class=\"arithmetic-table\">");
            html.Append("<tr><th>From</th><th>Entry</th><th>Risk</th><th>Reward</th><th>Reward to risk</th></tr>");

            html.Append(CultureInfo.InvariantCulture,
                $"<tr data-from=\"first\"><td>the first tranche</td>{Figure(figures, "firstEntry", Figures.Price)}" +
                $"{Figure(figures, "firstRisk", Figures.Price)}{Figure(figures, "firstReward", Figures.Price)}" +
                $"{Figure(figures, "firstRewardToRisk", Figures.Ratio)}</tr>");

            if (figures.GetProperty("blendedEntry").GetString() is not null)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<tr data-from=\"blended\"><td>the blended first two</td>{Figure(figures, "blendedEntry", Figures.Price)}" +
                    $"{Figure(figures, "blendedRisk", Figures.Price)}{Figure(figures, "blendedReward", Figures.Price)}" +
                    $"{Figure(figures, "blendedRewardToRisk", Figures.Ratio)}</tr>");
            }

            html.Append("</table></div>");

            // The break-even, which is what the plan demands of itself rather
            // than a benchmark borrowed from elsewhere.
            var breakEven = figures.GetProperty("breakEven").GetString();

            html.Append(CultureInfo.InvariantCulture, $"<p class=\"break-even\" data-break-even=\"{breakEven}\">");
            html.Append(breakEven is null
                ? "This plan states no break-even, because its first tranche has no reward to set against its risk.</p>"
                : $"This plan is worth taking if its first tranche reaches the target before the stop more than {Drawn(breakEven, Figures.Percent)} of the time.</p>");

            // The worked sizing example, from a risk budget the reader chooses.
            // The plan places a position and never sizes one, so the budget is a
            // sentence rather than a figure this system holds.
            // see: The plan places a position and never sizes one
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"sizing\" data-risk=\"{figures.GetProperty("firstRisk").GetString()}\">" +
                $"Sizing is yours: a risk budget divided by {Drawn(figures.GetProperty("firstRisk").GetString(), Figures.Price)} " +
                $"is the number of shares the first tranche takes. This tool places the position and " +
                $"never sizes it.</p>");
        }

        if (prints.Length > 0)
        {
            html.Append("<div class=\"tbl-wrap\">");
            html.Append("<table class=\"earnings-rule\"><tr><th>Print</th><th>Session</th><th>One-day move</th><th>Against the stop</th></tr>");

            foreach (var print in prints)
            {
                var share = print.GetProperty("shareOfStop").GetString();

                html.Append(CultureInfo.InvariantCulture,
                    $"<tr data-print=\"{print.GetProperty("eventDate").GetString()}\">" +
                    $"<td>{print.GetProperty("eventDate").GetString()}</td>" +
                    $"<td>{print.GetProperty("session").GetString()}</td>" +
                    $"{Figure(print, "move", Figures.Price)}");
                html.Append(share is null
                    ? "<td>no stop to measure against</td></tr>"
                    : $"<td class=\"num\" data-share-of-stop=\"{share}\">{Drawn(share, Figures.Percent)} of the stop distance</td></tr>");
            }

            html.Append("</table></div>");
        }

        html.Append("</section>");

        return html.ToString();
    }

    static decimal Price(JsonElement row, string name) =>
        decimal.Parse(row.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    // One of the plan's figures in its cell, drawn at the places it is read at with the stored
    // value on the cell, and a figure the plan does not state said in a word.
    static string Figure(JsonElement holder, string name, Func<decimal, string> read) =>
        holder.GetProperty(name).GetString() is { } stored
            ? $"<td class=\"num\" data-{Attribute(name)}=\"{stored}\">{Drawn(stored, read)}</td>"
            : $"<td data-{Attribute(name)}=\"none\">none</td>";

    // A stored field's name as an attribute's, so firstRewardToRisk is read back off
    // data-first-reward-to-risk.
    static string Attribute(string name) =>
        string.Concat(name.Select(letter => char.IsUpper(letter) ? "-" + char.ToLowerInvariant(letter) : letter.ToString()));

    // The condition in words. The enum's names are what the store holds and are
    // not what a reader reads, and this is the one place the two are paired.
    //
    // It ended in a catch-all arm until 5.4, so a sixth condition, a typo or an
    // unset value rendered as the sentence for reaching the zone with nothing
    // failing, and no test in the suite asserted any plan sentence at all. The
    // phase 4 sign-off found it and 5.4 owes it.
    //
    // Every condition the enum carries has its own arm and anything else throws.
    // A sentence a reader acts on that was produced by a value nobody wrote is
    // exactly the failure a catch-all makes invisible: it looks like a plan and
    // it is a default.
    // see: Every figure carries a plain-language key
    static string Words(string? condition) => condition switch
    {
        nameof(TrancheCondition.AvailableNow) => "this price now",
        nameof(TrancheCondition.FailedBreakdown) => "a failed breakdown back into the zone",
        nameof(TrancheCondition.FirstCloseBackAbove) => "the first close back above the zone after a dip",
        nameof(TrancheCondition.SecondDayAfterAShock) => "the second day after a shock, once the first day's low has held",
        nameof(TrancheCondition.ReachesTheZone) => "the price reaching the zone",
        _ => throw new InvalidOperationException(
            $"The stored plan carries the condition '{condition ?? "null"}', which this mapping has no words for. " +
            "A sentence a reader acts on that was produced by a value nobody wrote is what a catch-all arm " +
            "makes invisible, so the page fails rather than rendering a default."),
    };

    public static string Region(
        SinglePageApp page,
        MarkRenderer marks,
        string ticker,
        IReadOnlyList<BarRow> bars,
        IReadOnlyList<IndicatorRow> indicators,
        IReadOnlyList<LevelRow> levels,
        IReadOnlyList<ProfileRow> profile,
        LadderRow? ladder,
        CalendarRow? nextEvent,
        IReadOnlyList<MoveRow> moves,
        IReadOnlyList<FilingRow>? fundamentals = null,
        MoveExtremes? extremes = null,
        ListingRow? listing = null,
        string? previousOnTheList = null,
        string? nextOnTheList = null,
        IReadOnlyList<SectionStateRow>? sections = null,
        StalenessVerdict? staleness = null,
        IReadOnlyList<WrittenSectionRow>? written = null,
        string? pass = null,
        SpendVerdict? spend = null,
        IReadOnlyList<CitedDocumentRow>? cited = null,
        IReadOnlyList<CalendarRow>? events = null,
        IReadOnlyList<(string RunId, decimal Spend)>? priced = null,
        DateOnly? today = null,
        SuspectSeriesRow? suspect = null,
        NoYearRow? noYear = null,
        UniverseRow? member = null,
        IReadOnlyList<ListingRow>? history = null,
        IReadOnlyList<ForwardReturnRow>? outcomes = null,
        DateOnly? night = null)
    {
        var accepted = written ?? [];
        var leftOut = LeftOut(sections ?? []);
        var (cells, causes) = Causes(moves, accepted);
        var newest = Pass(pass);
        var notWritten = NotWritten(pass, accepted, leftOut);

        var drawn = bars
            .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
            .ToArray();

        // The averages, aligned to the bars session by session rather than by
        // position. The two queries are ordered the same way and over the same
        // window, so the sequences agree today; aligning on the date is what
        // keeps them agreeing when one of them does not, and a line drawn one
        // slot out would look entirely plausible.
        var bySession = indicators
            .GroupBy(row => row.Name)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(row => row.SessionDate, row => row.Value));

        var lines = Averages
            .Where(bySession.ContainsKey)
            .Select(name => new ChartAverage(
                name,
                [.. drawn.Select(bar => bySession[name].GetValueOrDefault(bar.SessionDate))]))
            .ToArray();

        var filings = fundamentals ?? [];

        var readings = IndicatorSeries.Momentum
            .Select(name =>
            {
                var bounds = IndicatorSeries.BoundsOf(name);

                return new MomentumReading(
                    name,
                    [.. indicators.Where(row => row.Name == name).OrderBy(row => row.SessionDate).Select(row => row.Value)],
                    IndicatorSeries.NeutralOf(name),
                    bounds?.Floor,
                    bounds?.Ceiling);
            })
            .ToArray();

        // An average that has no value on any stored session anchors no band, and
        // section 18 says the absence is stated with the bar count rather than
        // left out of the table.
        var absent = Averages
            .Where(name => bySession.TryGetValue(name, out var values) && values.Values.All(value => value is null))
            .Select(name => new AbsentAverage(
                name,
                indicators.Where(row => row.Name == name).Select(row => row.BarCount).DefaultIfEmpty(0).Max()))
            .ToArray();

        return page.NameRegion(
            marks,
            ticker,
            drawn,
            lines,
            [.. levels.Select(level => new ChartBand(level.LowEdge, level.HighEdge, level.Role, level.Immediate, level.Strength))],
            [.. profile.Select(band => new ProfileBand(band.BandLow, band.BandHigh, band.Shares, band.ShareOfPeriod))],
            readings,
            [.. levels.Select(level => new SummaryBand(
                level.LowEdge,
                level.HighEdge,
                level.Role,
                level.Immediate,
                level.Strength,
                level.HasNonAverageAnchor,
                Members(level.Members)))],
            absent,
            ladder?.TrendState,
            ladder?.AsOf,
            FactStrip(
                ticker,
                bars.Count > 0 ? bars[^1].Close : null,
                nextEvent?.EventDate,
                extremes,
                filings,
                indicators),
            PlanRows(ladder),
            bars.Count > 0 ? bars[^1].Close : 0m,
            EventBook(ladder),
            Arithmetic(ladder),
            Numbers(filings),
            cells,
            TwelveMonths(bars),
            FiredReasons(listing),
            previousOnTheList,
            nextOnTheList,
            leftOut,
            staleness is null ? null : ResearchState(staleness),
            causes,
            notWritten,
            marks.ProvenanceFooter(
                ticker,
                bars.Count > 0 ? bars[^1].SessionDate : null,
                filings.Count > 0 ? filings.Max(filing => filing.FilingDate) : null,
                [.. accepted.Select(section => new WrittenPart(section.Section, section.AsOf, section.Model))]),
            spend is null ? null : Paused(spend),
            Written(accepted),
            [.. (cited ?? []).Select(document => new SourceCell(document.Id, document.Title, document.Url, document.PublishedOn))],
            [.. (events ?? []).Select(dated => new DateCell(dated.EventDate, dated.Kind, dated.Timing))],
            PassLine(newest, spend, today),
            Controls(staleness, newest, notWritten, spend, priced, today),
            spend is null || priced is null ? null : Cost(priced, spend),
            Suspect(suspect),
            listing is null ? [] : TonightScreen.WrittenBeforeTheCorrection([listing]),
            noYear is null ? null : new NoYear(noYear.Nights, noYear.Last, noYear.Next),
            new NameMast(member?.Name, member?.Sector, member?.Industry, DayChange(ticker, bars)),
            filings.Count > 0 ? filings.Max(filing => filing.FilingDate) : null,
            history is null ? null : History(history, outcomes ?? [], bars),
            night);
    }

    // A name's listing history over the evenings the store holds its listings for: whether it was
    // on the list each evening, oldest first, and each evening it was, newest first, with the
    // reasons that fired, the stored close that night and what the two session horizons came to.
    // It forms no rate for the name.
    // see: A name's listing history states what followed each evening it was listed and forms no rate for the name
    public static ListingHistoryCard History(IReadOnlyList<ListingRow> listings, IReadOnlyList<ForwardReturnRow> outcomes, IReadOnlyList<BarRow> bars)
    {
        HorizonResult Result(DateOnly evening, string horizon) =>
            outcomes.FirstOrDefault(row => row.SessionDate == evening && row.Horizon == horizon) is { } row
                ? new HorizonResult(row.Outcome, row.ReturnPct, row.BaseRate)
                : new HorizonResult(null, null, null);

        return new ListingHistoryCard(
            [.. listings.OrderBy(listing => listing.SessionDate).Select(listing => listing.FiredCount > 0)],
            [
                .. listings
                    .Where(listing => listing.FiredCount > 0)
                    .OrderByDescending(listing => listing.SessionDate)
                    .Select(listing =>
                    {
                        var fired = FiredReasons(listing);

                        return new ListingEvening(
                            listing.SessionDate,
                            [.. fired.Select(reason => reason.Name)],
                            bars.FirstOrDefault(bar => bar.SessionDate == listing.SessionDate)?.Close,
                            Result(listing.SessionDate, ForwardReturnSeries.FiveSessions),
                            Result(listing.SessionDate, ForwardReturnSeries.TwentyOneSessions));
                    }),
            ]);
    }

    // The change on the day the masthead states, off the two newest stored closes, by the
    // rule tonight's list takes a row's change by.
    static double? DayChange(string ticker, IReadOnlyList<BarRow> bars) =>
        bars.Count < 2
            ? null
            : TonightScreen.DayChange(
                bars[^1].SessionDate,
                [new CloseRow(ticker, bars[^1].SessionDate, bars[^1].Close), new CloseRow(ticker, bars[^2].SessionDate, bars[^2].Close)]);

    // A suspect row as the page states it, and nothing for a name whose series is trusted.
    // The instant and the reason are the row's, and a row carrying no reason says so.
    public static SuspectPrices? Suspect(SuspectSeriesRow? row) =>
        row is null ? null : new SuspectPrices(row.CheckedAt, row.Reason ?? NoReason);

    public const string NoReason = "no reason was recorded";

    // The reasons the local lane records a section is not written for, which are what the
    // option to have the paid model write it is offered on. The worker's own constants
    // cannot be referenced from here, so the words are stated and `read-surface` asserts
    // each agrees with the worker's.
    public const string LocalUnavailable = "the local model is unavailable";
    public const string CannotHold = "the machine cannot hold it";

    // The line the spend cap refuses a call with, which is how a not-written reason from
    // a paused pass is told from any other.
    public const string PausedLine = "research is paused:";

    // The newest pass for a name as the page reads it off the run log's detail: the stage
    // that wrote it, the session it ran on, what it came to, and its reason.
    public sealed record PassRecord(string Stage, DateOnly? AsOf, string Outcome, string? Reason, IReadOnlyList<(string Section, string Reason)> NotWritten);

    public static PassRecord? Pass(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        using var document = JsonDocument.Parse(detail);
        var root = document.RootElement;

        // A research pass's detail carries its outcome and the session it ran on; a prose
        // pass's carries neither, and is read as the local lane writing on its own.
        var research = root.TryGetProperty("outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String;

        return new PassRecord(
            research ? ReadApi.ResearchStage : ReadApi.ProseStage,
            root.TryGetProperty("asOf", out var asOf) && asOf.ValueKind == JsonValueKind.String
                ? DateOnly.ParseExact(asOf.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null,
            research ? outcome.GetString()! : string.Empty,
            root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null,
            root.TryGetProperty("notWritten", out var lines) && lines.ValueKind == JsonValueKind.Array
                ? [.. lines.EnumerateArray().Select(line => (line.GetProperty("section").GetString()!, line.GetProperty("reason").GetString()!))]
                : []);
    }

    // The stages a pass writes, in the words a reader reads them in. The worker's own
    // constants cannot be referenced from here, so they are stated and `read-surface`
    // asserts the two agree, as the prose and research stages already are.
    public static readonly IReadOnlyDictionary<string, string> PassSteps =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fundamentals"] = "reading the company's filings",
            ["facts"] = "assembling the night's facts file",
            ["changes"] = "reading what changed",
            ["staleness"] = "deciding which sections need writing",
            ["theme research"] = "writing the industry's cycle",
            [ReadApi.ProseStage] = "writing the sections the local model drafts",
            [ReadApi.PaidCallStage] = "asking the research model",
            ["claims"] = "checking every figure against the facts file",
        };

    // Where a pass the page started stands, read off its own rows.
    //
    // A pass writes a row per component as it works and its own row last, so a run with no
    // rows yet is one that has started and a run carrying its own row is one that has ended.
    // The step is the newest row that has not ended, or the newest row there is, in the
    // words above rather than in the stage's own name, and a stage this does not name is
    // stated as itself rather than dropped.
    // see: A pass the page starts is watched until it ends and the page redraws as each section lands
    public static PassProgress Progress(IReadOnlyList<PassStageRow> rows, IReadOnlyList<SectionStateRow> sections)
    {
        var written = sections.Count(section => string.Equals(section.Status, Staleness.Accepted, StringComparison.Ordinal));

        if (rows.Count == 0)
        {
            return new PassProgress("starting", "the pass has started and has written nothing yet", written);
        }

        if (rows.Any(row => string.Equals(row.Stage, ReadApi.ResearchStage, StringComparison.Ordinal)))
        {
            return new PassProgress("ended", "the pass has ended", written);
        }

        var on = rows.LastOrDefault(row => !row.Ended) ?? rows[^1];
        var named = PassSteps.FirstOrDefault(step => on.Stage.StartsWith(step.Key, StringComparison.Ordinal));

        // A paid call names the section it asked for after its own stage, which the page
        // states rather than dropping: `research call: The two cases` reads as asking the
        // research model for the two cases.
        var asked = named.Key is not null && on.Stage.Length > named.Key.Length
            ? named.Value + " for " + on.Stage[(named.Key.Length + 2)..]
            : named.Value;

        return new PassProgress("running", asked ?? on.Stage, written);
    }

    // What the newest research pass came to, in one line, where it says something the
    // research state does not. A pass that wrote what it could says when it ran; one the
    // research model did not answer says the pass did not start and the stored research is
    // shown as written; one the cap stopped short of a reached cap says so with the cap's
    // own line, which is the refusal the page's verdict cannot see because it judges with
    // no call in hand. A pause the page's own verdict states is not stated twice.
    // owes: A pass the spend cap refuses short of a reached cap stated on the name page
    public static ResearchPassLine? PassLine(PassRecord? pass, SpendVerdict? spend, DateOnly? today)
    {
        if (pass is not { Stage: ReadApi.ResearchStage, AsOf: { } ran })
        {
            return null;
        }

        var day = Day(ran);

        string? line = pass.Outcome switch
        {
            ReadApi.PassWritten => ran == today
                ? $"a research pass ran for this name on {day}, and opening it again today writes nothing"
                : $"the newest research pass for this name ran on {day}",
            ReadApi.PassUnavailable => $"the research model did not answer when a pass was asked for on {day}, so the pass did not start and the stored research is shown as written, with the sections not written offered to be written later. What the model's address said: {pass.Reason}",
            ReadApi.PassPaused when spend is { Paused: true } => null,
            ReadApi.PassPaused => $"the pass on {day} was stopped by the spend cap before a cap was reached, which the cap does where a call could take spend past it: {pass.NotWritten.Select(line => line.Reason).FirstOrDefault(reason => reason.StartsWith(PausedLine, StringComparison.Ordinal)) ?? pass.Reason}",
            // What the pass did rather than that it did not run, which it said until 8.0
            // while a pass that refreshed the industry's cycle had made a paid call inside it.
            // see: A pass for a name with no facts file refreshes its industry's theme and says so
            ReadApi.PassNoFactsFile => $"the pass asked for on {day}: {pass.Reason}",
            _ => null,
        };

        return line is null ? null : new ResearchPassLine(pass.Outcome, ran, line);
    }

    // The controls a page offers, each asking for what a press would do: write the
    // research where none stands, rewrite what went stale, and have the paid model write
    // the local lane's sections where the local model could not. None while the cap has
    // paused research, since a press would be refused, and none that write or rewrite on
    // the day a pass ran, since a second plain pass that day writes nothing. None at all
    // where the page holds no cost to state beside them.
    // see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one
    public static IReadOnlyList<ResearchControl> Controls(
        StalenessVerdict? staleness,
        PassRecord? pass,
        IReadOnlyList<LeftOutSection> notWritten,
        SpendVerdict? spend,
        IReadOnlyList<(string RunId, decimal Spend)>? priced,
        DateOnly? today)
    {
        if (spend is null || priced is null || spend.Paused)
        {
            return [];
        }

        var ranToday = pass is { Stage: ReadApi.ResearchStage, Outcome: ReadApi.PassWritten } && pass.AsOf == today;
        var controls = new List<ResearchControl>();

        if (!ranToday && staleness?.State == Core.Research.ResearchState.Missing)
        {
            controls.Add(new ResearchControl("write", "Write the researched sections", Refresh: false, PaidForLocal: false));
        }

        if (!ranToday && staleness?.State == Core.Research.ResearchState.Stale)
        {
            controls.Add(new ResearchControl("rewrite", "Rewrite the stale sections", Refresh: false, PaidForLocal: false));
        }

        if (notWritten.Any(line => line.Reason.StartsWith(LocalUnavailable, StringComparison.Ordinal) || line.Reason.StartsWith(CannotHold, StringComparison.Ordinal)))
        {
            controls.Add(new ResearchControl("paid-for-local", "Have the paid model write them", Refresh: false, PaidForLocal: true));
        }

        return controls;
    }

    // What research has cost, from the priced calls the run log carries: the passes they
    // were made in, what they came to, and the most one pass came to, beside the line the
    // spend cap states for where research stands now.
    public static ResearchCost Cost(IReadOnlyList<(string RunId, decimal Spend)> priced, SpendVerdict spend)
    {
        var passes = priced
            .GroupBy(call => call.RunId, StringComparer.Ordinal)
            .Select(pass => pass.Sum(call => call.Spend))
            .ToArray();

        return new ResearchCost(passes.Length, passes.Sum(), passes.DefaultIfEmpty(0m).Max(), spend.Line);
    }

    // The written sections a page draws in their own places. The cause of each large move
    // is drawn in the moves table, in the row of each move it names, so it is not drawn
    // again as a section.
    public static IReadOnlyList<WrittenCell> Written(IReadOnlyList<WrittenSectionRow> accepted) =>
    [
        .. accepted
            .Where(section => !string.Equals(section.Section, ClaimRules.CauseSection, StringComparison.Ordinal))
            .Select(section => new WrittenCell(section.Section, section.Prose, section.AsOf, section.Model, SourceIds(section.SourceIds))),
    ];

    // Every id the written sections cite, once each, which is what the dates-and-sources
    // region reads the documents by.
    public static IReadOnlyList<string> Cited(IReadOnlyList<WrittenSectionRow> accepted) =>
        [.. accepted.SelectMany(section => SourceIds(section.SourceIds)).Distinct(StringComparer.Ordinal)];

    static IReadOnlyList<string> SourceIds(string stored)
    {
        using var ids = JsonDocument.Parse(stored);

        return [.. ids.RootElement.EnumerateArray().Select(id => id.GetString()!)];
    }

    // The pause as the page draws it, where the spend cap has stopped research, and
    // nothing where it has not. Judged by the screen's caller with the rule the cap
    // refuses a call by, and handed here as a verdict so this states and computes nothing.
    public static ResearchPausedLine? Paused(SpendVerdict verdict) =>
        verdict.Paused ? new ResearchPausedLine(verdict.Cap!, verdict.ResumesAt!.Value, verdict.Line) : null;

    // Where research stands against the caps at an instant, from the rows a month
    // holds: the rule itself, with no call in hand, so a page states a pause from the
    // moment a cap is reached.
    public static SpendVerdict Spend(IReadOnlyList<SpentRow> rows, SpendCaps caps, DateTimeOffset now) =>
        SpendRule.Judge(new SpendLedger(rows), caps, now);

    // The moves table's rows with the cause of each, where a cause section has been
    // accepted for the name.
    //
    // A sentence belongs to the move whose session it names, read by the claim
    // checker's own reader, so the page and the checker agree about which move a
    // sentence is about: the checker accepted the section on exactly that reading,
    // holding every document the sentence cites inside that move. A move no sentence
    // names carries no cause, and the table says so in its row. Nothing is worked
    // out beyond that: the text is the stored prose, cut at its own sentences.
    // see: A cause of a move rests only on a document published inside that move
    public static (IReadOnlyList<MoveCell> Cells, CauseSource? Source) Causes(
        IReadOnlyList<MoveRow> moves,
        IReadOnlyList<WrittenSectionRow> written)
    {
        var cause = written.FirstOrDefault(section => string.Equals(section.Section, ClaimRules.CauseSection, StringComparison.Ordinal));

        if (cause is null)
        {
            return ([.. moves.Select(move => new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank))], null);
        }

        var sentences = ClaimRules.Sentences(cause.Prose)
            .Select(sentence => (sentence.Text, Dates: ClaimRules.Figures(sentence.Text).Where(figure => figure.Kind == FigureKind.Date).ToArray()))
            .ToArray();

        static bool Names(ProseFigure[] dates, DateOnly session) =>
            dates.Any(date => date.Date is { } full ? full == session : date.Month == session.Month && date.Day == session.Day);

        return (
            [
                .. moves.Select(move =>
                {
                    var naming = sentences.Where(sentence => Names(sentence.Dates, move.SessionDate)).Select(sentence => sentence.Text).ToArray();

                    return new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank, naming.Length == 0 ? null : string.Join(" ", naming));
                }),
            ],
            new CauseSource(cause.AsOf, cause.Model));
    }

    // The sections the newest pass for the name did not write, with the reason the pass
    // recorded, less any section a reader is shown some other way: one with a written
    // version is drawn as written, and one whose newest version was left out already
    // carries the line the checker stored. The skips a pass records are not read at all,
    // since each is a section with a row of its own. The pass is a prose pass the local
    // lane ran on its own or, from 6.8, a research pass, whose lines carry the local
    // lane's and the paid lane's together.
    public static IReadOnlyList<LeftOutSection> NotWritten(
        string? newestPass,
        IReadOnlyList<WrittenSectionRow> written,
        IReadOnlyList<LeftOutSection> leftOut)
    {
        if (string.IsNullOrWhiteSpace(newestPass))
        {
            return [];
        }

        using var pass = JsonDocument.Parse(newestPass);

        if (!pass.RootElement.TryGetProperty("notWritten", out var lines) || lines.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        // A section drawn is not also named as not written, except a theme's cycle drawn from
        // before the pass: that pass could not refresh it, so the page draws what is stored
        // under its own date and the one line saying why it is not newer.
        var passOn = pass.RootElement.TryGetProperty("asOf", out var on) && on.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(on.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : (DateOnly?)null;

        var shown = written
            .Where(section => !(string.Equals(section.Section, ClaimRules.CycleSection, StringComparison.Ordinal) && passOn is { } asOf && section.AsOf < asOf))
            .Select(section => section.Section)
            .Concat(leftOut.Select(section => section.Section))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. lines.EnumerateArray()
                .Select(line => (Section: line.GetProperty("section").GetString()!, Reason: line.GetProperty("reason").GetString()!))
                .Where(line => !shown.Contains(line.Section))
                .Select(line => new LeftOutSection(string.Empty, line.Section, line.Reason)),
        ];
    }

    // The verdict as the page draws it, in the words the judge writes. Nothing is
    // computed here: the state is the verdict's and the line is the verdict's.
    public static ResearchStateLine ResearchState(StalenessVerdict verdict) =>
        new(verdict.State.ToString().ToLowerInvariant(), verdict.Line);

    // The status a checker leaves a section in when it is left out. The worker's
    // own constant cannot be referenced from here, since the read surface holds no
    // reference to the worker, so the word is stated and `read-surface` asserts
    // the two agree.
    public const string Fallback = "fallback";

    // The sections whose newest version the checker left out, each with the reason
    // it stored. Nothing is computed: the reason is the row's, drawn as written.
    public static IReadOnlyList<LeftOutSection> LeftOut(IReadOnlyList<SectionStateRow> sections) =>
    [
        .. sections
            .Where(section => string.Equals(section.Status, Fallback, StringComparison.Ordinal))
            .OrderBy(section => section.Section, StringComparer.Ordinal)
            .Select(section => new LeftOutSection(string.Empty, section.Section, section.Reason ?? string.Empty)),
    ];

    // The plan region alone, which is what tonight's list shows for the selected
    // name: the plan column and the two tables it is read beside. Section 15.7
    // says the common case of checking a plan needs no navigation.
    public static string PlanRegion(
        SinglePageApp page,
        MarkRenderer marks,
        string ticker,
        LadderRow? ladder,
        decimal close,
        IReadOnlyList<LevelRow> levels)
    {
        var rows = PlanRows(ladder);

        // The level summary beside the plan column, which is the other half of
        // what section 15.7 states this region holds and which stood undrawn
        // under the row's own PASS until 5.8. It is the name screen's own table,
        // the same mark over the same stored bands, because the common case is
        // checking a plan and a plan read without the levels it rests on is a
        // list of prices.
        //
        // No absent-average row here. That is the name screen's statement about
        // a name whose long average has no value over its stored year, and it
        // needs the indicator series this region does not read; the name page is
        // where it belongs and where it is drawn.
        return $"<section class=\"selected-name\" data-ticker=\"{ticker}\" data-plan-rows=\"{rows.Count}\" data-bands=\"{levels.Count}\">"
            + "<div class=\"selwrap\"><div class=\"fig\">"
            + marks.PlanColumn(ticker, close, rows)
            + "</div><div class=\"tbl-wrap\">"
            + marks.PlanTables(ticker, rows)
            + marks.LevelSummary(
                ticker,
                [.. levels.Select(level => new SummaryBand(
                    level.LowEdge,
                    level.HighEdge,
                    level.Role,
                    level.Immediate,
                    level.Strength,
                    level.HasNonAverageAnchor,
                    Members(level.Members)))],
                [])
            + "</div></div></section>";
    }

    // The sessions of the last twelve months, which is what section 15.9's
    // how-it-got-here region draws its picture over.
    //
    // Measured back from the newest stored session rather than from the clock,
    // so a page opened on a Sunday draws the year to Friday and not a year to
    // today with two blank days at the end of it. The bar history kept is a year
    // (section 17), so on an ordinary name this is every stored bar; it is a
    // filter rather than a pass-through because that is a limit the store is
    // trusted to hold rather than one this region may assume.
    public static IReadOnlyList<ChartBar> TwelveMonths(IReadOnlyList<BarRow> bars)
    {
        if (bars.Count == 0)
        {
            return [];
        }

        var from = bars[^1].SessionDate.AddYears(-1);

        return
        [
            .. bars
                .Where(bar => bar.SessionDate > from)
                .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume)),
        ];
    }

    // The reasons that fired for this name tonight, read back from the stored
    // listing. A name with no listing row, or one that fired nothing, has none,
    // and the region says the name is not on tonight's list rather than being
    // absent.
    static IReadOnlyList<FiredReason> FiredReasons(ListingRow? listing)
    {
        if (listing is null || listing.FiredCount == 0)
        {
            return [];
        }

        using var document = JsonDocument.Parse(listing.Reasons);

        return
        [
            .. document.RootElement.EnumerateArray()
                .Where(reason => reason.GetProperty("fired").GetBoolean())
                .Select(reason => new FiredReason(
                    reason.GetProperty("name").GetString()!,
                    reason.GetProperty("values").EnumerateObject()
                        .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal))),
        ];
    }

    // The members column, as the mark needs it. SCHEMA stores each member's
    // kind, price and date as JSON with the price in the storage form, so this
    // reads back what was written and derives nothing.
    static IReadOnlyList<SummaryMember> Members(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.EnumerateArray().Select(member => new SummaryMember(
                member.GetProperty("kind").GetString()!,
                decimal.Parse(member.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
        ];
    }
}
