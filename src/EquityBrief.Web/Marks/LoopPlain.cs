using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// The Monthly check-up's own words: what each strategy does in a sentence, a stored chance read as one in so many, and
// each change tried named for a reader new to investing. Every number is the tester's, restated and never worked out here
// but for the one in so many.
// see: The Monthly check-up opens on what needs the reader and draws each setup's changes on a page of its own
public static class LoopPlain
{
    public const string PageName = "Monthly check-up";

    public const string ScoreGroup = "A score learned from past results";

    public const string IdeaGroup = "Ideas from the AI model";

    public const string WinnersGroup = "What the winning picks had in common";

    public const string ExitGroup = "Different selling rules";

    public const string SettingsGroup = "Different settings or market checks";

    // The kinds of change, in the order a strategy's page draws them.
    public static IReadOnlyList<string> Groups { get; } = [ScoreGroup, IdeaGroup, WinnersGroup, ExitGroup, SettingsGroup];

    public static string GroupOf(string proposal) =>
        proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal)
            || proposal.StartsWith(TreeScore.Proposal, StringComparison.Ordinal)
            || proposal.StartsWith(RankingChoice.Proposal, StringComparison.Ordinal)
            ? ScoreGroup
            : proposal.StartsWith("the idea generator", StringComparison.Ordinal)
                ? IdeaGroup
                : proposal.StartsWith("winners against losers", StringComparison.Ordinal)
                    ? WinnersGroup
                    : proposal.StartsWith("the autopsy's exit", StringComparison.Ordinal)
                        ? ExitGroup
                        : SettingsGroup;

    // A stored chance as one in so many: "Likely luck" at one in two or worse, "Not measured" where the run read none.
    public static string Luck(double? adjusted)
    {
        if (adjusted is not { } chance)
        {
            return "Not measured";
        }

        if (chance >= 0.5)
        {
            return "Likely luck";
        }

        var odds = 1 / Math.Max(chance, 0.00001);
        var step = odds < 100 ? 1 : Math.Pow(10, Math.Floor(Math.Log10(odds)) - 1);
        var shown = Math.Round(odds / step) * step;

        return "About 1 in " + shown.ToString("#,0", CultureInfo.InvariantCulture);
    }

    public static string Does(string family) => family switch
    {
        "pullback" => "Buys a strong stock after a short dip on light trading.",
        "breakout" => "Buys a stock pushing to a new high on heavy trading.",
        "drift" => "Buys a stock soon after a good earnings report lifted it.",
        ImprovingRule.Name => "Holds companies whose business scorecard is strong and improving.",
        DipRule.Name => "Buys a rising stock after a sharp fall, for a quick bounce.",
        "heavyweight" => "Holds the large companies leading their sectors.",
        QualityRule.Name => "Holds top-rated companies when they are not expensive for them.",
        _ => string.Empty,
    };

    // What a change tried, in a short phrase for its row.
    public static string Tried(LoopProposalRow proposal) => Phrase(proposal.Proposal, proposal.Words);

    // What a change does, in a sentence for the page that asks the operator about it.
    public static string Explained(LoopProposalRow proposal)
    {
        var phrase = Phrase(proposal.Proposal, proposal.Words);

        if (!IsScore(proposal.Proposal))
        {
            return phrase + ".";
        }

        var skips = proposal.Proposal.Contains("lowest two fifths", StringComparison.Ordinal) ? " The lowest-scoring two fifths are skipped."
            : proposal.Proposal.Contains("lowest fifth", StringComparison.Ordinal) ? " The lowest-scoring fifth are skipped."
            : string.Empty;

        return $"The same stocks still qualify. The only difference is which ones get bought first: {ScoreKind(proposal.Proposal)} now sets the order.{skips}";
    }

    static bool IsScore(string proposal) =>
        proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal) || proposal.StartsWith(TreeScore.Proposal, StringComparison.Ordinal);

    static string ScoreKind(string proposal) =>
        proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal) ? "a score learned from past results" : "a score from many learned decision trees";

    static string Phrase(string proposal, string? stored)
    {
        if (IsScore(proposal))
        {
            var kind = ScoreKind(proposal);

            return proposal.Contains("lowest two fifths", StringComparison.Ordinal)
                ? $"Let {kind} set the buying order and skip the lowest-scoring two fifths"
                : proposal.Contains("lowest fifth", StringComparison.Ordinal)
                    ? $"Let {kind} set the buying order and skip the lowest-scoring fifth"
                    : $"Let {kind} set the buying order";
        }

        if (proposal.StartsWith(RankingChoice.Proposal, StringComparison.Ordinal))
        {
            return "Loosen the strategy's other steps, then take the top stocks by the learned score";
        }

        if (proposal == ExposureVariant.Proposal)
        {
            return "Take more or fewer picks depending on two market checks, in place of today's one";
        }

        if (stored is null)
        {
            return "Looked for something better and found nothing to change";
        }

        var words = ReaderText.Words(stored);

        if (words.StartsWith(AlsoRequires, StringComparison.Ordinal))
        {
            return "Also needs " + string.Join(", and ", Regex.Split(words[AlsoRequires.Length..], @"(?<=\d) and ", RegexOptions.CultureInvariant).Select(Condition));
        }

        return words.StartsWith(ExitOpening, StringComparison.Ordinal)
            ? Exit(words[ExitOpening.Length..])
            : words.Contains('=', StringComparison.Ordinal)
                ? "Try a different combination of this strategy's settings"
                : Capitalised(words);
    }

    const string AlsoRequires = "also requires ";

    const string ExitOpening = "exit: ";

    static string Capitalised(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    // A selling rule in the reader's words, a shape this does not know standing as the tester worded it.
    static string Exit(string exit)
    {
        var target = Regex.Match(exit, @"^a target at \$(?<up>[\d.]+) per \$1 risked$", RegexOptions.CultureInvariant);

        if (target.Success)
        {
            return $"Sell at a fixed target, up ${target.Groups["up"].Value} for every $1 risked";
        }

        var rising = Regex.Match(exit, @"^a rising stop (?<under>[\d.]+) typical days' moves under the highest close once a close is (?<up>[\d.]+) times its risk up, with no target$", RegexOptions.CultureInvariant);

        return rising.Success
            ? $"No fixed target. Once up {rising.Groups["up"].Value} times the risk, sell on a fall of {rising.Groups["under"].Value} typical days' moves from the best close"
            : exit.StartsWith("a holding also sold ", StringComparison.Ordinal)
                ? "Also sell a holding " + exit["a holding also sold ".Length..]
                : Capitalised(exit);
    }

    // One condition of "also requires", read for what its number measures, the units read from the ledger's catalogue.
    static string Condition(string clause)
    {
        var read = Regex.Match(clause, @"^(?<reading>.+?) at or (?<side>above|under|below) (?<value>-?\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant);

        if (!read.Success)
        {
            return clause;
        }

        var reading = read.Groups["reading"].Value;
        var above = read.Groups["side"].Value == "above";
        var value = double.Parse(read.Groups["value"].Value, CultureInfo.InvariantCulture);
        var limit = above ? "at least" : "at most";

        string Shown(double figure, string format) => figure.ToString(format, CultureInfo.InvariantCulture);

        string Return(string span) => value < 0 && above
            ? $"the stock not to be down more than {Shown(-value, "0.#")}% {span}"
            : $"a {(value < 0 ? "fall" : "rise")} of {limit} {Shown(Math.Abs(value), "0.#")}% {span}";

        return reading switch
        {
            "the size of its last surprise" => $"its last earnings report to beat forecasts by {limit} {Shown(value, "0.#")}%",
            "the days since its last surprise" => $"{limit} {Shown(value, "0")} trading days since its last earnings report",
            "demand in the results release" when above && value >= 1 => "its latest results report to show stronger demand",
            "the return over 63 days" => Return("over the last 63 trading days"),
            "the return over 126 days" => Return("over the last 126 trading days"),
            "the return over the year to a month ago" => Return("over the year to a month ago"),
            "beta" => $"a stock that moves {limit} {Shown(value, "0.##")} times as much as its index",
            "liquidity" => above ? "a stock with heavy daily dollar trading" : "a stock with light daily dollar trading",
            "how much its daily returns varied apart from the index's" => $"day-to-day swings, apart from the market's, of {limit} {Shown(value * 100, "0.##")}%",
            "the close against its 50-day average" => $"a close {limit} {Shown((value - 1) * 100, "0.#")}% above its 50-day average",
            "the close against its 200-day average" => $"a close {limit} {Shown((value - 1) * 100, "0.#")}% above its 200-day average",
            "the volume on the way down against its average" => $"volume on the way down of {limit} {Shown(value, "0.##")} times usual",
            "the day's volume against its 50-day average" => $"the day's volume to be {limit} {Shown(value, "0.##")} times its 50-day average",
            "the company's value" => above ? "a larger company" : "a smaller company",
            "the 2-day relative strength" => $"a 2-day strength gauge of {limit} {Shown(value, "0.#")} out of 100",
            "tightness" => $"recent daily price swings {limit} {Shown(value, "0.##")} times their usual size",
            "mid-sized companies' return against large ones'" => above ? "mid-sized companies keeping up with large ones" : "mid-sized companies lagging large ones",
            "cash from operations against net income" => $"yearly cash from operations of {limit} {Shown(value, "0.##")} times net income",
            _ => $"{reading} of {limit} {Shown(value, "0.##")}",
        };
    }
}
