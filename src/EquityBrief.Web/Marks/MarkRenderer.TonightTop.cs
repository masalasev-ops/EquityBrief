namespace EquityBrief.Web.Marks;

// One setup's tile at the top of tonight's page: the family's word and label, tonight's count, what that count is of,
// and its state in words, in the family's tint and edge.
public sealed record SetupTileView(string Family, string Label, int Count, string Counted, string State, string? Quiet = null);

// One of tonight's buy points as the table across every setup draws it: the setup's word and label and its pick.
public sealed record BuyPointView(string Family, string Label, FamilyPickCell Pick);

// The parts tomorrow's buys shares with a setup's card: each row's fold and the control opening it, and the reward
// against the risk as the plan states it.
// see: A buy point's fold draws the stock's entry and exit plan beneath its setup's own plan, as the full page draws it
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
