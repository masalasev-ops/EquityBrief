using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The proof that every page's count is the one function's for the group it names: each count read back off the words its
// page draws and held to `TradeGroups`, an index at a time, as a restart leaves them or any other night does. A page that
// draws no such count is unexamined rather than passed.
// see: Every count of trades names its group and one function works the four groups
public static partial class CountsProof
{
    public const string Route = "/proof/counts";

    const string Pass = "pass";
    const string Fail = "fail";
    const string Unexamined = "unexamined";

    public static IReadOnlyList<ProofVerdict> Check(TradeGroupsView groups, string home, string tonight, string paper)
    {
        var (onHome, onTonight, onPaper) = (Text(home), Text(tonight), Text(paper));

        return
        [
            Same("Home's ideas bought and sold are the system's closed", Read(onHome, @"All versions since [^:]+: (\d+) ideas? bought and sold"), groups.System.Closed),
            Same("Home's ideas that made money are the system's", Read(onHome, @"bought and sold on paper, (\d+) made money"), groups.System.MadeMoney),
            Same("Home's stocks to buy are the new picks", Read(onHome, @"Tonight's ideas: (\d+) stocks?"), groups.NewPicks),
            Same("Tonight's open trades are the system's", Read(onTonight, @"Of the system's trades, (\d+)"), groups.System.Open),
            Same("Tonight's open trades of this version are this version's", Read(onTonight, @"(\d+) of them this version's"), groups.ThisVersion.Open),
            Same("Tonight's open trades selling at tomorrow's open are the system's", Read(onTonight, @"(\d+) of them sells? at tomorrow's open", "Of the system's trades"), groups.System.Selling),
            Same("Paper trading's open trades selling at tomorrow's open are the system's", Read(onPaper, @"The system has bought [^:]*: \d+ open, (\d+) of them sells? at tomorrow's open", "The system has bought"), groups.System.Selling),
            Same("Tonight's new picks are those to buy less the reshuffle", Read(onTonight, @"(\d+) new picks? to buy", "No new picks to buy"), groups.NewPicks),
            Same("Tomorrow's buys count the same new picks", Read(onTonight, @"(\d+) stocks? to buy at [^.,:]+ open, at about tonight's close", "No stock to buy at"), groups.NewPicks),
            Same("Paper trading's bought are the system's", Read(onPaper, @"The system has bought (\d+)"), groups.System.Bought),
            Same("Paper trading's open are the system's", Read(onPaper, @"The system has bought [^:]*: (\d+) open"), groups.System.Open),
            Same("Paper trading's reached target are the system's", Read(onPaper, @"(\d+) reached their target"), groups.System.AtTarget),
            Same("Paper trading's hit their stop are the system's", Read(onPaper, @"(\d+) hit their stop"), groups.System.AtStop),
            Same("Paper trading's open of this version are this version's", Read(onPaper, @"This version's trades: (\d+) open", "No live setup has bought a trade under its current version"), groups.ThisVersion.Open),
            Same("Paper trading's fold counts the earlier versions'", Read(onPaper, @"Earlier versions: (\d+) trades?"), groups.EarlierVersions.Bought),
            Same("Tonight's open trades of the same before this version are that group's", Read(onTonight, @"(\d+) the same trades? before this version", "Of the system's trades"), groups.SameTrades.Open),
            Same("Paper trading's same trades before this version are that group's", Read(onPaper, @"(\d+) of them (?:is|are) the same trades? before this version", "The system has bought"), groups.SameTrades.Bought),
            new("The system's trades are this version's, the same before it and the earlier versions'", groups.System.Bought == groups.ThisVersion.Bought + groups.SameTrades.Bought + groups.EarlierVersions.Bought && groups.System.Open == groups.ThisVersion.Open + groups.SameTrades.Open + groups.EarlierVersions.Open ? Pass : Fail),
        ];
    }

    // Past picks' first table held to the system's trades of the setups it lists; an index's table draws no count of targets
    // and stops, its rows stating each trade's result.
    public static IReadOnlyList<ProofVerdict> CheckPicks(TradeCountView listed, int toBuy, string picks)
    {
        var text = Text(picks);
        var open = Read(text, @"(\d+) in this table") ?? Read(text, @"Still open (\d+)");
        var buying = Read(text, @"To buy at tomorrow's open (\d+)") ?? Read(text, @"trades?, \d+ open(?:, \d+ of them sells? at tomorrow's open)?, (\d+) to buy at tomorrow's open", @"trades?, \d+ open(?:, \d+ of them sells? at tomorrow's open)?\.");

        return
        [
            Same("Past picks' open are the system's of its setups", open, listed.Open),
            Same("Past picks' stocks to buy are those waiting on the open", buying, toBuy),
            .. Read(text, @"Reached target (\d+)") is { } target ? [Same("Past picks' reached target are the system's of its setups", target, listed.AtTarget)] : Array.Empty<ProofVerdict>(),
            .. Read(text, @"Stopped out (\d+)") is { } stopped ? [Same("Past picks' stopped out are the system's of its setups", stopped, listed.AtStop)] : Array.Empty<ProofVerdict>(),
        ];
    }

    // The Run page's line on the pullback held to the system's trades of the pullback.
    public static IReadOnlyList<ProofVerdict> CheckRun(TradeGroupsView pullback, string run)
    {
        var text = Text(run);

        return
        [
            Same("The Run page's pullback bought are the system's of the pullback", Read(text, @"part of the system's trades: (\d+) bought"), pullback.System.Bought),
            Same("The Run page's pullback open are the system's of the pullback", Read(text, @"part of the system's trades: \d+ bought, (\d+) still open"), pullback.System.Open),
        ];
    }

    static ProofVerdict Same(string rule, int? drawn, int counted) =>
        drawn is not { } read
            ? new ProofVerdict(rule, Unexamined, "the page draws no such count")
            : read == counted
                ? new ProofVerdict(rule, Pass)
                : new ProofVerdict(rule, Fail, FormattableString.Invariant($"the page draws {read}, and the function counts {counted}"));

    // The first count the words hold, nothing where they hold none, and none at all where the page says in words it has none.
    static int? Read(string text, string pattern, string? none = null) =>
        Regex.Match(text, pattern) is { Success: true } found
            ? int.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture)
            : none is not null && Regex.IsMatch(text, none) ? 0 : null;

    // A page's words as a reader reads them.
    static string Text(string page) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(Scripts().Replace(page, " "), " ")), " ");

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Singleline)]
    private static partial Regex Scripts();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // The proof as a page: an index's verdicts in a row each, failures first.
    public static string Page(IReadOnlyList<(string Index, IReadOnlyList<ProofVerdict> Verdicts)> indexes)
    {
        var page = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>Counts proof</title></head><body><h1>Counts proof</h1>");

        foreach (var (index, verdicts) in indexes)
        {
            page.Append(CultureInfo.InvariantCulture, $"<h2 data-failed=\"{verdicts.Count(one => one.Verdict != Pass)}\">{WebUtility.HtmlEncode(index)}: {verdicts.Count(one => one.Verdict == Pass)} of {verdicts.Count} pass</h2><ul>");

            foreach (var verdict in verdicts.OrderBy(one => one.Verdict == Pass))
            {
                page.Append(CultureInfo.InvariantCulture, $"<li data-verdict=\"{verdict.Verdict}\">{verdict.Verdict}: {WebUtility.HtmlEncode(verdict.Rule)}{(verdict.Why is { } why ? ", " + WebUtility.HtmlEncode(why) : string.Empty)}</li>");
            }

            page.Append("</ul>");
        }

        return page.Append("</body></html>").ToString();
    }
}
