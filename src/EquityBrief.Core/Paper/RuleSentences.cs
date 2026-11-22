using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Paper;

// A rule's settings as plain sentences: one template a setting, matched on the chip's own words and filled with the values
// they state, and one for a setting the running revision states and a variant leaves out. A variant is named by the sentence
// of its one change, and a chip no template reads is a fault the checks find.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public static partial class RuleSentences
{
    const string Number = @"([0-9]+(?:\.[0-9]+)?)";

    // Each setting's sentence: the chip's key, the whole of its words as a pattern and the sentence its groups fill.
    static readonly (string Key, Regex Words, string Sentence)[] Templates =
    [
        T("strengthFloor", $"stronger than {Number}%", "Needs the stock stronger than {1}% of the index over the past year"),
        T("depth", $"{Number} to {Number} moves down", "Needs a dip of {1} to {2} typical daily moves"),
        T("dryUpCeiling", "no dry-up gate", "Skips the check that the dip came on light volume"),
        T("dryUpCeiling", $"volume under {Number} times", "Needs the dip's volume under {1} times its usual"),
        T("arrivalSessions", $"turns up within {Number}", "Needs the stock to turn up within {1} days"),
        T("earningsWindowSessions", $"no report in {Number} sessions", "Skips a stock with an earnings report within {1} trading days"),
        T("rewardToRiskFloor", $"{Number} times the risk", "Needs room to rise at least {1} times the risk"),
        T("stop", $"stop {Number} to {Number} moves", "Needs its stop {1} to {2} typical daily moves under the price"),
        T("market", "market check off", "Skips the check on the whole market"),
        T("market", $"breadth at least {Number}%", "Buys only while at least {1}% of the index's stocks close above their 200-day average"),
        T("trade", "the ladder's plan", "Places its stop and take-profit by the ladder of price bands"),
        T("trade", "the swing plan", "Places its stop and take-profit at the nearest price bands, the swing plan"),
        T("trade", "the plan clear of the noise", "Places its stop and take-profit clear of the day-to-day noise"),
        T("skipDeteriorating", "no business deteriorating in its SEC filings", "Skips a stock whose business is deteriorating in its SEC filings"),
        T("skipDeteriorating", "no deteriorating business", "Skips a stock whose business is deteriorating"),
        T("leaders", $"top {Number} sectors, top 1 in {Number}", "Buys only in the {1} strongest sectors, the top 1 in {2} of each"),
        T("bestOf", $"best {Number} a night", "Takes at most {1} picks a night"),
        T("raisedEstimates", "estimates raised", "Needs the analysts' estimates raised"),

        T("highSessions", $"{Number}-session high", "Needs a close at a {1}-day high"),
        T("volumeMultiple", $"volume {Number} times", "Needs volume {1} times its usual"),
        T("rangeCeiling", "any range", "Takes any range of prices before the breakout"),
        T("rangeCeiling", $"ranges at {Number}", "Needs the days before the breakout to trade in ranges no wider than {1} of usual"),
        T("stopMoves", $"stop {Number} moves?", "Places its stop {1} typical daily moves under the price"),
        T("nearness", $"within {Number} of the year's high", "Needs the price within {1} of the year's high"),
        T("recency", "high within 63 sessions", "Needs the old high set within the last 63 trading days"),
        T("recency", "high older than 63 sessions", "Needs the old high set more than 63 trading days ago"),
        T("highVolume", $"volume at least {Number} times", "Needs volume at least {1} times its usual"),

        T("windowSessions", $"within {Number} sessions", "Buys within {1} trading days of its earnings report"),
        T("reactionMoves", $"up {Number} moves?", "Needs the report day up {1} typical daily moves"),
        T("target", $"target the nearer of the next price band about two typical moves up, or {Number} times the risk", "Takes profit at the nearer of the next price band above or {1} times the risk"),
        T("stopFloor", $"stop at least {Number} moves?", "Keeps its stop at least {1} typical daily moves under the price"),
        T("wideWindow", $"window {Number} sessions", "Looks back {1} trading days for the report"),
        T("peers", "peers beat first", "Needs its industry's peers to have beaten their estimates first"),
        T("entry", "bought on its first lower close", "Buys on its first lower close after the report"),
        T("revenueGrew", "revenue above a year before", "Needs the quarter's revenue above the same quarter a year before"),
        T("readingVersion", "release better, outlook not cut", "Needs its results release to read better than expected, its outlook not cut"),
        T("indexAverageSessions", $"index above its {Number}-day", "Buys only while the index is above its {1}-day average"),
        T("vixLookbackSessions", $"VIX falling over {Number}", "Buys only while the VIX is falling over {1} days"),

        T("design", "design \\(b\\)", "Picks by industry rather than by sector"),
        T("industries", "every leading industry", "Buys in every leading industry"),
        T("industries", $"{Number} strongest industries", "Buys in the {1} strongest industries"),
        T("window", $"over {Number} sessions", "Measures strength over {1} trading days"),
        T("members", $"{Number} members? an industry", "Holds {1} of each industry's members"),
        T("largest", "every company", "Picks from every company in a sector"),
        T("largest", $"{Number} largest", "Picks from each sector's {1} largest companies"),
        T("lookBack", "twelve months less the latest", "Measures strength over twelve months, leaving out the latest"),
        T("lookBack", $"{Number}-session look-back", "Measures strength over {1} trading days"),
        T("leaders", $"{Number} leaders?", "Holds the {1} strongest"),
        T("fundReturn", "against the fund", "Measures strength against its sector's fund"),
        T("fundReturn", "against the members", "Measures strength against its sector's other members"),
        T("highBeta", "beta at least 1", "Needs a beta of at least 1"),
        T("weekly", "weekly", "Reshuffles every week"),
        T("exit", "sold on either exit", "Sells when it loses the lead or closes under its 200-day average"),
        T("exit", "sold on losing the lead", "Sells when it loses the lead"),
        T("exit", "sold under the 200-day", "Sells when it closes under its 200-day average"),

        T("floor", $"a score of {Number} in 9", "Needs a quality score of {1} out of 9"),
        T("valuation", "its price not read", "Buys whatever its price"),
        T("valuation", "at or below its own usual price", "Buys only at or below its own usual valuation"),
        T("valuation", "in the cheapest third of its own range", "Buys only in the cheapest third of its own valuation range"),
        T("trend", "above its 200-day average", "Needs the price above its 200-day average"),
        T("book", $"{Number} holdings", "Holds {1} stocks"),
        T("sale", "also sold on a filing", "Also sells when a filing drops its score"),
        T("sale", "sold at reshuffles", "Sells only at reshuffles"),

        T("buy", $"buy at {Number} in 9", "Buys at a score of {1} out of 9"),
        T("flag", "a flag the night before", "Needs a flag the night before"),
        T("rise", "above its previous filing's score", "Needs a score above its previous filing's"),
        T("faster", "sales speeding up", "Needs its sales growth speeding up"),
        T("hold", $"held at {Number} in 9", "Holds while its score stays at {1} out of 9 or more"),
        T("limit", $"{Number} sessions at most", "Sells after {1} trading days at most"),

        T("strength", $"strength {Number}", "Needs a strength of {1} against the index"),
        T("dryUp", "no dry-up gate", "Skips the check that the dip came on light volume"),
        T("dryUp", $"volume under {Number} times", "Needs the dip's volume under {1} times its usual"),
        T("freshness", $"fresh within {Number}", "Needs the dip to have started within {1} days"),
        T("rewardToRisk", $"{Number} times the risk", "Needs room to rise at least {1} times the risk"),
        T("band", $"band strength {Number}", "Needs a support band of strength {1} or more"),

        T("floors", $"dollar floor {Number} times", "Needs daily dollar volume of at least {1} times the index's floor"),
        T("lowestClose", $"close at least \\${Number}", "Needs a close of at least ${1}"),
        T("quality", "no quality gate", "Skips the profit check"),
        T("quality", "profit and cover", "Needs a profit and its interest covered"),
        T("quality", "the profit gate", "Needs a profit"),
        T("hold", $"held {Number} sessions", "Holds at most {1} trading days"),
        T("industryFall", $"industry not falling over {Number}", "Needs its industry not falling over {1} days"),
        T("fundOverSpy", $"fund leading SPY over {Number}", "Needs its sector's fund ahead of SPY over {1} days"),
        T("creditAverage", "HYG above its 50-day", "Needs the high-yield bond fund above its 50-day average"),
        T("creditChange", "HYG up over 63 sessions", "Needs the high-yield bond fund up over 63 trading days"),
        T("largeBreadth", "market check off", "Skips the check on the whole market"),
        T("largeBreadth", "market check on the S&P 500's breadth(.*)", "Buys only while enough of the S&P 500's stocks close above their 200-day average{1}"),
        T("largeBreadth", "market check on its own breadth(.*)", "Buys only while enough of its own index's stocks close above their 200-day average{1}"),

        T("dollars", "dollar floor 1 times", "Needs daily dollar volume above the index's floor"),
        T("close", $"close at least \\${Number}", "Needs a close of at least ${1}"),
        T("trend", $"above its {Number}-session average", "Needs the price above its {1}-day average"),
        T("fifty", $"its {Number}-session above it", "Needs its {1}-day average above that average too"),
        T("drop", $"down {Number} moves in {Number} sessions", "Needs a fall of {1} typical daily moves in {2} days"),
        T("drop", $"{Number}-session RSI under {Number}", "Needs its {1}-day RSI under {2}"),
        T("volume", $"drop volume under {Number} times its {Number}-session average", "Needs the fall's volume under {1} times its {2}-day average"),
        T("report", $"no report in {Number} sessions or ahead", "Skips a stock with an earnings report in the last {1} days or one due"),
        T("stop", $"stop {Number} moves", "Places its stop {1} typical daily moves under the price"),
        T("exit", $"sold when its {Number}-session RSI closes over {Number}", "Sells when its {1}-day RSI closes over {2}"),
        T("exit", "sold on its first up close", "Sells on its first up close"),
        T("exit", $"sold over its {Number}-session average", "Sells on a close over its {1}-day average"),
        T("market", "market setting off", "Skips the check on the whole market"),
        T("market", "market setting (.+)", "Buys only while {1}"),
    ];

    // The sentence a setting is named by, none where no template reads its words.
    public static string? Of(RuleChip chip) =>
        chip.Key == "hooks" ? Hooks(chip.Words) : Filled(chip.Key, chip.Words, Templates);

    // The sentence for a setting the running revision states and a variant leaves out, none where none reads it.
    public static string? Without(RuleChip chip) =>
        chip.Key == "hooks" ? HooksLeftOut(chip.Words)
        : Of(chip) is { } stated ? "Leaves out one of its checks: " + char.ToLowerInvariant(stated[0]) + stated[1..]
        : null;

    // Each setting a variant states differently from its running revision, as a plain sentence: what the variant states
    // where the two differ or the revision states none, and what the variant leaves out of the revision's.
    public static IReadOnlyList<ChangeLine> Between(IReadOnlyList<RuleChip> running, IReadOnlyList<RuleChip> variant)
    {
        var runningBy = running.ToDictionary(chip => chip.Key, StringComparer.Ordinal);
        var variantKeys = variant.Select(chip => chip.Key).ToHashSet(StringComparer.Ordinal);

        // The heavyweights' design (b) is one change, picking by industry, its industries, window and members its own
        // settings and the sector design's settings left with it: read as one sentence, beside any other change.
        if (variant.FirstOrDefault(chip => chip.Key == "design") is { } design && !runningBy.ContainsKey("design"))
        {
            string[] designB = ["design", "industries", "window", "members"];
            string[] designA = ["largest", "lookBack", "leaders", "fundReturn", "highBeta", "weekly", "exit"];
            string[] parts = [.. variant.Where(chip => chip.Key is "industries" or "window" or "members").Select(chip => Of(chip) ?? chip.Words)];
            var sentence = (Of(design) ?? design.Words) + (parts.Length > 0 ? ": " + string.Join(", ", parts.Select(part => char.ToLowerInvariant(part[0]) + part[1..])) : string.Empty);

            return
            [
                new ChangeLine("design", string.Join(" · ", variant.Where(chip => designB.Contains(chip.Key)).Select(chip => chip.Words)), sentence),
                .. Between([.. running.Where(chip => !designA.Contains(chip.Key))], [.. variant.Where(chip => !designB.Contains(chip.Key))]),
            ];
        }

        // A variant on the sector design beside a running revision picking by industry is the same one change the other way.
        if (runningBy.ContainsKey("design") && !variantKeys.Contains("design"))
        {
            string[] designB = ["design", "industries", "window", "members"];
            string[] designA = ["largest", "lookBack", "leaders", "fundReturn", "highBeta", "weekly", "exit"];
            string[] parts = [.. variant.Where(chip => designA.Contains(chip.Key)).Select(chip => Of(chip) ?? chip.Words)];
            var sentence = "Picks by sector rather than by industry" + (parts.Length > 0 ? ": " + string.Join(", ", parts.Select(part => char.ToLowerInvariant(part[0]) + part[1..])) : string.Empty);

            return
            [
                new ChangeLine("design", string.Join(" · ", variant.Where(chip => designA.Contains(chip.Key)).Select(chip => chip.Words)), sentence),
                .. Between([.. running.Where(chip => !designB.Contains(chip.Key))], [.. variant.Where(chip => !designA.Contains(chip.Key))]),
            ];
        }

        return
        [
            .. variant
                .Where(chip => !runningBy.TryGetValue(chip.Key, out var same) || same.Words != chip.Words)
                .Select(chip => new ChangeLine(chip.Key, chip.Words, Of(chip) ?? Unread(chip))),
            .. running
                .Where(chip => !variantKeys.Contains(chip.Key))
                .Select(chip => new ChangeLine(chip.Key, "without " + chip.Words, Without(chip) ?? Unread(chip))),
        ];
    }

    // Settings the pages draw no chip for, by the parameter a registration states them under, in plain words.
    static readonly IReadOnlyDictionary<string, string> Unchipped = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["breakoutVolumeMultiple"] = "the breakout's volume check",
        ["tightnessCeiling"] = "the check on how tightly the price traded",
        ["leaderSectors"] = "the check on the leading sectors",
        ["leaderShareOf"] = "the share of each leading sector it buys",
        ["skipDeteriorating"] = "the check on a deteriorating business",
        ["bestOf"] = "the cap on picks a night",
        ["raisedEstimates"] = "the check on the analysts' estimates",
        ["filedState"] = "reading the business from its SEC filings",
        ["indexAverageSessions"] = "the check on the index's average",
        ["vixLookbackSessions"] = "the check on the VIX",
        ["exit"] = "its exit",
        ["sell"] = "its exit",
    };

    // What a revision changed where no chip differs: each parameter it states that the one before stated otherwise, or did
    // not state, as a sentence; a setting named at nought where the one before named none, which trades as it did.
    public static IReadOnlyList<string> Parameters(IReadOnlyDictionary<string, double> before, IReadOnlyDictionary<string, double> after)
    {
        var sentences = new List<string>();
        var renamedExit = before.ContainsKey("exit") && !after.ContainsKey("exit") && after.ContainsKey("sell") && before["exit"] == after["sell"];

        if (renamedExit)
        {
            sentences.Add("Names its exit under a new setting, at the same level, so its trades sell as before");
        }

        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (renamedExit && key is "exit" or "sell")
            {
                continue;
            }

            var had = before.TryGetValue(key, out var old);
            var has = after.TryGetValue(key, out var now);
            var plain = Unchipped.GetValueOrDefault(key) ?? "its setting " + key;

            if (had && has && old == now)
            {
                continue;
            }

            sentences.Add(!had && now == 0 ? "Names " + plain + " as off, so its trades run as before"
                : !has ? "No longer names " + plain
                : !had ? FormattableString.Invariant($"Adds {plain} at {now:0.##}")
                : FormattableString.Invariant($"Moves {plain} from {old:0.##} to {now:0.##}"));
        }

        return sentences;
    }

    // Several sentences as one: the first as it stands and each after it joined in, so a revision's change reads as one
    // plain sentence.
    public static string Joined(IReadOnlyList<string> sentences) =>
        sentences.Count == 0 ? string.Empty
        : sentences[0] + string.Concat(sentences.Skip(1).Select(one => "; and " + (one.Length > 1 && char.IsUpper(one[1]) ? one : char.ToLowerInvariant(one[0]) + one[1..])));

    // The words a page draws for a setting no template reads, which the checks find.
    public const string NoSentence = "A setting with no sentence written for it";

    static string Unread(RuleChip chip) => NoSentence + ": " + chip.Words;

    // A plan in one plain sentence: how it buys, where it sells at a loss and how it takes profit.
    public static string PlanSentence(PlanCombination plan)
    {
        var buys = plan.Entry == PlanPoints.NextOpen ? "buys at the next open"
            : plan.Entry == PlanPoints.FirstPullback ? FormattableString.Invariant($"buys on its first lower close within {plan.Window} days")
            : plan.Entry == PlanPoints.Average ? FormattableString.Invariant($"waits up to {plan.Window} days for the price to fall to its 20-day average")
            : PlanPoints.DistanceOf(plan.Entry) is { } days ? FormattableString.Invariant($"waits up to {plan.Window} days for the price to fall {days:0.##} typical daily moves under its close")
            : FormattableString.Invariant($"waits up to {plan.Window} days for the price to fall into {plan.Entry.Replace("the ", "the top of the ", StringComparison.Ordinal)} of prices where it bounced before");
        var loss = plan.Stop == PlanPoints.RuleStop ? "sells at a loss under its setup's own stop"
            : plan.Stop == PlanPoints.ZoneStop ? "sells at a loss under the bottom of the zone it bought in"
            : "sells at a loss " + plan.Stop.Replace("typical days under the buy", "typical daily moves under the price it paid", StringComparison.Ordinal).Replace("typical day under the buy", "typical daily move under the price it paid", StringComparison.Ordinal);

        return buys + ", " + loss + " and " + ProfitClause(plan);
    }

    // How a plan takes profit, in one clause.
    public static string ProfitClause(PlanCombination plan)
    {
        var sale = plan.Exit == PlanPoints.RuleSale ? "its setup's own sale"
            : plan.Exit == PlanPoints.FirstBand ? "the first price band above where the stock turned down before, read on the night it was picked"
            : plan.Exit == PlanPoints.StagedSales ? "a part at each price band above"
            : null;

        if (plan.TakeProfit is not { } multiple)
        {
            return sale is null ? "otherwise sells on its time limit" : "takes profit at " + sale;
        }

        var times = FormattableString.Invariant($"{multiple:0.##} times the risk");

        if (plan.Half)
        {
            return "takes profit on half at " + times + ", the rest " + (sale is null ? "on its time limit" : "at " + sale);
        }

        var placed = plan.Placing switch
        {
            TakeProfitPlacing.Risk => times,
            TakeProfitPlacing.Beyond => "the first price it turned down at " + times + " or more above the price it paid, or " + times + " where none stands",
            _ => "the nearest price above it where it turned down before, or " + times + " where none stands",
        };

        return sale is null || plan.Exit == PlanPoints.TimeLimit
            ? "takes profit at " + placed
            : "takes profit at " + sale + ", or at " + placed + " where that night read none";
    }

    // The approval's hooks, each part as a sentence: another exit, the plan its plan test chose, a condition it also needs,
    // the order it ranks by and how many it buys as the market stands.
    static string? Hooks(string words)
    {
        var parts = new List<string>();

        foreach (var part in words.Split("; ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("plan: ", StringComparison.Ordinal))
            {
                if (PlanPrices.Named(part["plan: ".Length..]) is not { } plan)
                {
                    return null;
                }

                parts.Add("Buys and sells by the plan the test of its buy and sell points chose: it " + PlanSentence(plan));
            }
            else if (part.StartsWith("exit: ", StringComparison.Ordinal))
            {
                parts.Add("Sells by another exit: " + part["exit: ".Length..]);
            }
            else if (part.StartsWith("also requires ", StringComparison.Ordinal))
            {
                parts.Add("Also needs " + part["also requires ".Length..]);
            }
            else if (part.StartsWith("ordered by", StringComparison.Ordinal) || part.StartsWith("ranked over", StringComparison.Ordinal))
            {
                parts.Add("Takes its picks " + part);
            }
            else if (part.StartsWith("exposure: ", StringComparison.Ordinal))
            {
                parts.Add("Buys as many as the market allows: " + part["exposure: ".Length..]);
            }
            else
            {
                return null;
            }
        }

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    // The hooks the running revision states and a variant leaves out: the plan left out buys at the next open at the setup's
    // own stop and sale, as before the plan.
    static string? HooksLeftOut(string words)
    {
        if (Hooks(words) is null)
        {
            return null;
        }

        string[] said =
        [
            .. words.Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(part =>
                part.StartsWith("plan: ", StringComparison.Ordinal) ? "buys at the next open and sells at its setup's own stop and sale, without the running revision's plan"
                : part.StartsWith("exit: ", StringComparison.Ordinal) ? "sells by its setup's own sale, without the running revision's exit, " + part["exit: ".Length..]
                : part.StartsWith("also requires ", StringComparison.Ordinal) ? "does not also need " + part["also requires ".Length..]
                : "leaves out the running revision's " + part),
        ];

        return char.ToUpperInvariant(said[0][0]) + string.Join("; and ", said)[1..];
    }

    static string? Filled(string key, string words, IEnumerable<(string Key, Regex Words, string Sentence)> templates)
    {
        foreach (var (named, pattern, sentence) in templates)
        {
            if (named != key || pattern.Match(words) is not { Success: true } match)
            {
                continue;
            }

            var filled = sentence;

            for (var group = match.Groups.Count - 1; group >= 1; group--)
            {
                filled = filled.Replace("{" + group.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", match.Groups[group].Value, StringComparison.Ordinal);
            }

            return Singular(filled);
        }

        return null;
    }

    // A count of one read in the singular.
    static string Singular(string sentence) =>
        Regex.Replace(sentence, @"\b1 (trading days|days|typical daily moves|stocks|members)\b", one => "1 " + one.Groups[1].Value switch
        {
            "trading days" => "trading day",
            "days" => "day",
            "typical daily moves" => "typical daily move",
            "stocks" => "stock",
            _ => "member",
        });

    static (string, Regex, string) T(string key, string words, string sentence) =>
        (key, new Regex("^" + words + "$", RegexOptions.CultureInvariant), sentence);
}
