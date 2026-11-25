using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Paper;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// From the operator's correction of 2026-11-24, item 1: every live rule's order and trade since the rules restart, an index at
// a time, beside what Home, Tonight and Paper trading draw for it; each mismatch named, and none where the three pages and the
// store agree. Built first, kept as the check run after every night.
// see: The reconciliation holds every live rule's order and trade to what Home, Tonight and Paper trading draw, and fails on any mismatch
public static partial class PicksProof
{
    public const string Route = "/proof/picks";

    // The line the night's step reads: the count of mismatches over every index.
    public const string Tally = "picks-proof mismatches:";

    // The surface's verb that prints the proof and exits, and the exit code of a proof holding a mismatch.
    public const string Verb = "picks-proof";

    public const int Mismatched = 4;

    public sealed record PickLine(
        string Index,
        DateOnly Night,
        string Family,
        string Revision,
        string Ticker,
        string Order,
        decimal? Price,
        DateOnly? FilledOn,
        string State,
        string Home,
        string Tonight,
        string Paper,
        IReadOnlyList<string> Mismatches);

    // What the pages drew on the newest night: Home, Tonight, each night's Tonight since the restart, and each family's Paper
    // trading waiting tab, every page of it.
    public sealed record DrawnPages(string Home, string Tonight, IReadOnlyDictionary<DateOnly, string> Nights, IReadOnlyDictionary<string, string> PaperWaiting);

    // Every live rule's order and trade alive since the restart, held to the pages: an order waiting is a waiting card on Home
    // and on Tonight and a row of its family's waiting tab, one placed tonight Home's idea and Tonight's pick; a trade bought,
    // sold or an order that left unbought in the last five trading days a note under Home's waiting cards; an order of a
    // revision since replaced never waits; and nothing drawn as waiting or as tonight's idea that no live order stands for.
    public static IReadOnlyList<PickLine> Check(
        string index,
        DateOnly night,
        DateOnly since,
        IReadOnlyList<PlannedTradeRow> planned,
        IReadOnlyList<DateOnly> sessions,
        Func<string, string, DateOnly, DateOnly?> replacedOn,
        DrawnPages drawn,
        out IReadOnlyList<string> extras)
    {
        DateOnly[] recent = [.. sessions.Where(session => session <= night).OrderDescending().Take(LeftOrderView.Sessions)];
        var fromRecent = recent.Length > 0 ? recent.Min() : night;
        var homeIdeas = Pairs(drawn.Home, "<article class=\"home-idea[^\"]*\" data-family=\"([^\"]+)\" data-ticker=\"([^\"]+)\"");
        var homeWaiting = Triples(drawn.Home, "<article class=\"wo-card[^\"]*\" data-family=\"([^\"]+)\" data-ticker=\"([^\"]+)\" data-listed=\"([^\"]+)\"");
        var tonightWaiting = Triples(drawn.Tonight, "<article class=\"wo-card[^\"]*\" data-family=\"([^\"]+)\" data-ticker=\"([^\"]+)\" data-listed=\"([^\"]+)\"");
        var homeLeft = Regex.Matches(drawn.Home, "<li class=\"(bt|cx|sd)\" data-ticker=\"([^\"]+)\"").Select(match => (Kind: match.Groups[1].Value, Ticker: match.Groups[2].Value)).ToArray();
        var lines = new List<PickLine>();
        var rows = planned.Where(row => row.Live && (row.Listed >= since || row.EndedOn is null || row.EndedOn >= since) && row.Listed <= night)
            .GroupBy(row => (row.Family, row.Ticker, row.Listed)).Select(group => group.First())
            .OrderBy(row => row.Listed).ThenBy(row => row.Family, StringComparer.Ordinal).ThenBy(row => row.Place).ToArray();

        foreach (var row in rows)
        {
            var mismatches = new List<string>();
            var replaced = replacedOn(index, row.Owner, row.Listed);
            var running = replaced is null || replaced > night;
            var gone = RevisionScreen.LeftUnbought(row, night, sessions);
            var state = row.Fill is not null
                ? row.EndedOn is { } sold && sold <= night ? "sold " + Day(sold) : "open"
                : gone is { } left ? "not bought: " + left.Words
                : running ? "waiting" : "waiting, its revision replaced on " + Day(replaced!.Value);
            var paperRows = drawn.PaperWaiting.GetValueOrDefault(row.Family, string.Empty);
            var onHome = homeWaiting.Contains((row.Family, row.Ticker, Day(row.Listed))) ? "waiting card"
                : row.Listed == night && homeIdeas.Contains((row.Family, row.Ticker)) ? "idea"
                : homeLeft.Any(one => one.Ticker == row.Ticker) ? "note" : "-";
            var onTonight = tonightWaiting.Contains((row.Family, row.Ticker, Day(row.Listed))) ? "waiting card" : row.Listed == night && (Listed(drawn.Tonight, row.Family, row.Ticker) || Ordered(drawn.Tonight, row.Family, row.Ticker)) ? "pick" : "-";
            var onPaper = paperRows.Contains(FormattableString.Invariant($"<tr data-ticker=\"{row.Ticker}\" data-placed=\"{Day(row.Listed)}\""), StringComparison.Ordinal) ? "waiting" : "-";

            if (state == "waiting")
            {
                Expect(mismatches, "Home", onHome, row.Listed == night ? "idea" : "waiting card");
                Expect(mismatches, "Tonight", onTonight, row.Listed == night ? "pick" : "waiting card");

                if (PlannedTrades.BuysAtAPrice(row))
                {
                    Expect(mismatches, "Paper trading", onPaper, "waiting");
                }
            }
            else
            {
                if (state.StartsWith("waiting, its revision replaced", StringComparison.Ordinal))
                {
                    mismatches.Add("its revision was replaced on " + Day(replaced!.Value) + " and its order still waits");
                }

                if (onHome == "waiting card" || onTonight == "waiting card" || onPaper == "waiting")
                {
                    mismatches.Add(FormattableString.Invariant($"drawn as waiting while the store says {state}"));
                }

                var movedOn = row.Fill is not null
                    ? new[] { row.FilledOn, row.EndedOn }.Where(day => day is { } on && on >= fromRecent && on <= night).ToArray()
                    : gone is { } went && went.On >= fromRecent && went.On <= night ? [went.On] : [];

                if (movedOn.Length > 0 && onHome != "note")
                {
                    mismatches.Add("Home's notes name no move of it in the last five trading days");
                }
            }

            if (row.Listed >= since && drawn.Nights.TryGetValue(row.Listed, out var thatNight) && !Listed(thatNight, row.Family, row.Ticker) && !Ordered(thatNight, row.Family, row.Ticker))
            {
                mismatches.Add("Tonight of " + Day(row.Listed) + " lists it under no family");
            }

            lines.Add(new PickLine(index, row.Listed, row.Family, running ? "running" : "replaced " + Day(replaced!.Value), row.Ticker, row.Order.Words, PlannedTrades.Price(row), row.FilledOn, state, onHome, onTonight, onPaper, mismatches));
        }

        // Anything drawn as waiting, or as tonight's idea of a family whose live rule trades a plan, that no live order stands for.
        var waitingOrders = rows.Where(row => PlannedTrades.IsWaiting(row) && !row.Order.PlacesNoPrice()).Select(row => (row.Family, row.Ticker, Day(row.Listed))).ToHashSet();
        var tonightOrders = rows.Where(row => row.Listed == night).Select(row => (row.Family, row.Ticker)).ToHashSet();
        var planFamilies = rows.Select(row => row.Family).ToHashSet(StringComparer.Ordinal);
        var extra = new List<string>();

        extra.AddRange(homeWaiting.Concat(tonightWaiting).Distinct().Where(card => !waitingOrders.Contains(card)).Select(card => FormattableString.Invariant($"{card.Item1} {card.Item2} placed {card.Item3}: a waiting card no live order stands for")));
        extra.AddRange(homeIdeas.Where(idea => planFamilies.Contains(idea.Item1) && !tonightOrders.Contains(idea)).Select(idea => FormattableString.Invariant($"{idea.Item1} {idea.Item2}: Home's idea tonight with no order of its live rule")));

        foreach (var (family, rowsDrawn) in drawn.PaperWaiting)
        {
            foreach (Match match in Regex.Matches(rowsDrawn, "<tr data-ticker=\"([^\"]+)\" data-placed=\"([^\"]+)\""))
            {
                if (planFamilies.Contains(family) && !rows.Any(row => row.Family == family && row.Ticker == match.Groups[1].Value && Day(row.Listed) == match.Groups[2].Value && (PlannedTrades.IsWaiting(row) || row.Listed == night)))
                {
                    extra.Add(FormattableString.Invariant($"{family} {match.Groups[1].Value} placed {match.Groups[2].Value}: Paper trading waits on it and no live order stands for it"));
                }
            }
        }

        extras = [.. extra.Distinct(StringComparer.Ordinal)];

        return lines;
    }

    static void Expect(List<string> mismatches, string page, string shown, string wanted)
    {
        if (shown != wanted)
        {
            mismatches.Add(FormattableString.Invariant($"{page} shows {(shown == "-" ? "nothing" : shown)}, the store says {wanted}"));
        }
    }

    // Tonight lists a stock under a family where its family's table holds its row with a place.
    static bool Listed(string page, string family, string ticker) =>
        page.Contains(FormattableString.Invariant($"<tr data-ticker=\"{ticker}\" data-family=\"{family}\" data-place="), StringComparison.Ordinal);

    // A stock Tonight draws among those met again, held by another setup, whose own setup's rule ordered it that night.
    static bool Ordered(string page, string family, string ticker) =>
        Regex.IsMatch(page, FormattableString.Invariant($"<div class=\"held-card\" data-ticker=\"{Regex.Escape(ticker)}\" data-family=\"{Regex.Escape(family)}\"[^>]*data-ordered=\"true\""));

    static HashSet<(string, string)> Pairs(string page, string pattern) =>
        [.. Regex.Matches(page, pattern).Select(match => (match.Groups[1].Value, match.Groups[2].Value))];

    static HashSet<(string, string, string)> Triples(string page, string pattern) =>
        [.. Regex.Matches(page, pattern).Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value))];

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The proof as a page: each index's table of orders and trades, its mismatches first, then what was drawn with no order;
    // as text, a line a mismatch and the tally the night's step reads.
    public static string Page(IReadOnlyList<(string Index, IReadOnlyList<PickLine> Lines, IReadOnlyList<string> Extras)> indexes, bool text)
    {
        var total = indexes.Sum(one => one.Lines.Sum(line => line.Mismatches.Count) + one.Extras.Count);

        if (text)
        {
            var plain = new StringBuilder();

            foreach (var (index, lines, extras) in indexes)
            {
                plain.Append(CultureInfo.InvariantCulture, $"{index}: {lines.Count} orders and trades, {lines.Sum(line => line.Mismatches.Count) + extras.Count} mismatches\n");

                foreach (var line in lines.Where(one => one.Mismatches.Count > 0))
                {
                    foreach (var mismatch in line.Mismatches)
                    {
                        plain.Append(CultureInfo.InvariantCulture, $"  {line.Family} {line.Ticker} placed {Day(line.Night)}: {mismatch}\n");
                    }
                }

                foreach (var extra in extras)
                {
                    plain.Append(CultureInfo.InvariantCulture, $"  {extra}\n");
                }
            }

            return plain.Append(CultureInfo.InvariantCulture, $"{Tally} {total}\n").ToString();
        }

        var page = new StringBuilder(FormattableString.Invariant($"<!doctype html><html><head><meta charset=\"utf-8\"><title>Picks proof</title></head><body><h1>Picks proof</h1><p data-mismatches=\"{total}\">{Tally} {total}</p>"));

        foreach (var (index, lines, extras) in indexes)
        {
            page.Append(CultureInfo.InvariantCulture, $"<h2 data-index=\"{WebUtility.HtmlEncode(index)}\" data-mismatches=\"{lines.Sum(line => line.Mismatches.Count) + extras.Count}\">{WebUtility.HtmlEncode(index)}</h2>");
            page.Append("<table><thead><tr><th>Night</th><th>Rule</th><th>Revision</th><th>Stock</th><th>Order</th><th>Buy price</th><th>Filled</th><th>Now</th><th>Home</th><th>Tonight</th><th>Paper trading</th><th>Mismatch</th></tr></thead><tbody>");

            foreach (var line in lines.OrderBy(one => one.Mismatches.Count == 0).ThenBy(one => one.Night))
            {
                page.Append(CultureInfo.InvariantCulture, $"<tr data-mismatch=\"{(line.Mismatches.Count > 0 ? "true" : "false")}\" data-ticker=\"{WebUtility.HtmlEncode(line.Ticker)}\"><td>{Day(line.Night)}</td><td>{WebUtility.HtmlEncode(line.Family)}</td><td>{WebUtility.HtmlEncode(line.Revision)}</td><td>{WebUtility.HtmlEncode(line.Ticker)}</td><td>{WebUtility.HtmlEncode(line.Order)}</td><td>{(line.Price is { } price ? price.ToString("0.00", CultureInfo.InvariantCulture) : "-")}</td><td>{(line.FilledOn is { } filled ? Day(filled) : "-")}</td><td>{WebUtility.HtmlEncode(line.State)}</td><td>{WebUtility.HtmlEncode(line.Home)}</td><td>{WebUtility.HtmlEncode(line.Tonight)}</td><td>{WebUtility.HtmlEncode(line.Paper)}</td><td>{WebUtility.HtmlEncode(string.Join("; ", line.Mismatches))}</td></tr>");
            }

            page.Append("</tbody></table><ul>");

            foreach (var extra in extras)
            {
                page.Append(CultureInfo.InvariantCulture, $"<li data-extra>{WebUtility.HtmlEncode(extra)}</li>");
            }

            page.Append("</ul>");
        }

        return page.Append("</body></html>").ToString();
    }
}
