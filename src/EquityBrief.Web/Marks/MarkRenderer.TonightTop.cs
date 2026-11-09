namespace EquityBrief.Web.Marks;

// One setup's tile at the top of tonight's page: the family's word and label, tonight's count, what that count is of,
// and its state in words, in the family's tint and edge.
public sealed record SetupTileView(string Family, string Label, int Count, string Counted, string State, string? Quiet = null);

// One of tonight's buy points as the table across every setup draws it: the setup's word and label and its pick.
public sealed record BuyPointView(string Family, string Label, FamilyPickCell Pick);

// A buy that no plan with a stop and a target sells, the Fundamentals family's and a monthly book's, as the table across
// every setup draws it: the setup's word and label, the stock and its company, why it qualified and its business in the
// reader's words, tonight's close it is bought at about, how it sells, the link to its row on its own card, and its entry
// check's reading.
// see: Tonight's buys name every buy on the index, a buy no plan sells drawn with how it sells in its stop's and target's place
public sealed record PlanlessBuyView(string Family, string Label, string Ticker, string? Company, string Why, string Business, decimal? Close, string Sells, string Href, EquityBrief.Core.EntryCheck.EntryReading? EntryCheck = null, SetupLongRecord? LongRecord = null);

// The parts tomorrow's buys shares with a setup's card: each row's fold and the control opening it, and the reward
// against the risk as the plan states it.
// see: A pick's page heads its rule's plan as traded and draws every other way in open beneath it, headed as not traded
// see: Each setup family has a hue of its own drawn as a mark and a tint beside its word, the Fundamentals family's among them
public sealed partial class MarkRenderer
{
    // The id a buy point's fold and the control opening it share, the setup in it so a stock under two setups folds twice.
    public static string FoldId(string family, string ticker) =>
        $"fold-{family}-{ticker}".ToLowerInvariant().Replace('.', '-').Replace(' ', '-');

    // The control in a buy point's name cell opening and closing its fold, read by the page's script as a card's control is.
    static string FoldToggle(string fold, bool opened) =>
        Formatted($" <button type=\"button\" class=\"card-toggle fold-toggle\" aria-expanded=\"{(opened ? "true" : "false")}\" aria-controls=\"{Escaped(fold)}\" title=\"open this pick's plan and checks\">Open plan</button>");

    // A pick's reward to risk as its plan states it, and where its setup sets no target why: its stop rising with the
    // price, or its exit in words.
    static string RewardCell(FamilyPickCell pick) =>
        pick.RewardToRisk is { } ratio
            ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">${Figures.Ratio(ratio)}</td>")
            : $"<td class=\"r num\" data-reward-to-risk=\"none\">{(pick.Trailing ? NoTargetRising : pick.Exit is not null ? NoTargetExit : pick.Stop is not null && pick.Target is not null ? "<span class=\"degraded\">not stored</span>" : "<span class=\"degraded\">none</span>")}</td>";

    // What a pick's risk line says where its setup sets no target.
    const string NoTargetRising = "No target: it sells on a rising stop";

    const string NoTargetExit = "No target: it sells on its exit signal";
}
