namespace EquityBrief.Core.Loop;

// From 21.8 what orders a family's ranking variant on an index: the index's learned score where the score bucket audit's
// top fifth of the family's settled setups in the test years stands above its bottom fifth in mean edge, otherwise the
// expected value of the setups like each where its top fifth stands above its bottom fifth over the same setups, and
// nothing where neither does, the variant then not built on the index.
// see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
public enum RankingOrder
{
    None,
    Score,
    Expected,
}

// A fifth's mean edge after costs and how many setups it holds, none where it holds none.
public sealed record RankingFifths(double? Bottom, int BottomSetups, double? Top, int TopSetups)
{
    public bool Orders => Top is { } high && Bottom is { } low && high > low;

    public static RankingFifths None { get; } = new(null, 0, null, 0);
}

public static class RankingChoice
{
    // The start of a ranking proposal's name, which the Loop page reads its folds' sentence by.
    public const string Proposal = "the ranking";

    public static RankingOrder Of(RankingFifths score, RankingFifths expected) =>
        score.Orders ? RankingOrder.Score : expected.Orders ? RankingOrder.Expected : RankingOrder.None;

    // The ranking hook's value an order is registered with, none for no order.
    public static double? HookOf(RankingOrder order) => order switch
    {
        RankingOrder.Score => RuleHooks.RankingByScore,
        RankingOrder.Expected => RuleHooks.RankingByExpected,
        _ => null,
    };

    public static RankingOrder OrderOf(RuleHooks hooks) => hooks.Ranking switch
    {
        RuleHooks.RankingByScore => RankingOrder.Score,
        RuleHooks.RankingByExpected => RankingOrder.Expected,
        _ => RankingOrder.None,
    };

    // The bottom and top fifths of placed setups by their group, the bottom the first of five and the top the last.
    public static RankingFifths Fifths(IEnumerable<(int Group, double? Edge)> placed)
    {
        var bottom = placed.Where(one => one.Group == 0).Select(one => one.Edge).OfType<double>().ToArray();
        var top = placed.Where(one => one.Group == Groups - 1).Select(one => one.Edge).OfType<double>().ToArray();

        return new RankingFifths(bottom.Length == 0 ? null : bottom.Average(), bottom.Length, top.Length == 0 ? null : top.Average(), top.Length);
    }

    public const int Groups = 5;

    // Each figure's group among the figures given, a fifth of them a group from the lowest, a tie placed by its first
    // place among them in the order given.
    public static IReadOnlyList<int> GroupsOf(IReadOnlyList<double> figures)
    {
        var order = Enumerable.Range(0, figures.Count).OrderBy(at => figures[at]).ThenBy(at => at).ToArray();
        var groups = new int[figures.Count];

        for (var place = 0; place < order.Length; place++)
        {
            groups[order[place]] = Math.Min(Groups - 1, place * Groups / order.Length);
        }

        return groups;
    }

    // The order in the reader's words, which a run prints and a registration's name carries.
    public static string Words(RankingOrder order) => order switch
    {
        RankingOrder.Score => "the index's newest learned score",
        RankingOrder.Expected => "the expected value of the setups like each",
        _ => "neither the learned score nor the expected value of the setups like each",
    };
}
