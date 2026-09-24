using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Moves;

// A member of the index on a session, with the sector and the industry its membership row names.
public sealed record GroupMember(string Ticker, string? Sector, string? Industry);

// A name's group: its industry or its sector, named, and the other members in it. The name itself
// is never one of them, and a group can hold nobody, which a surface says rather than drawing a
// figure over none.
public sealed record Group(string Kind, string? Name, IReadOnlyList<string> Members)
{
    public const string Industry = "industry";
    public const string Sector = "sector";
}

// A group's median move over a span, and how many members it was taken over and left out.
public sealed record GroupMedian(double? Median, int Counted, int Missing);

// The one rule a name's group is read by, declared once for every surface that uses it, so the
// move table and the peers table can never disagree about who a name's group is.
// see: A name's group is its industry where at least five other members share it on the session, and its sector otherwise, and every surface that uses it says which and how many
public static class Groups
{
    // How many other members an industry needs on the session before it is a name's group. An
    // industry of fewer makes a median of a name or two, which reads as a comparison without
    // being one, so a smaller industry gives way to its sector.
    public const int Floor = 5;

    public static Group Of(string ticker, IReadOnlyList<GroupMember> members)
    {
        var self = members.FirstOrDefault(member => string.Equals(member.Ticker, ticker, StringComparison.Ordinal));

        string[] Sharing(Func<GroupMember, string?> of, string? value) =>
        [
            .. members
                .Where(member => value is not null
                    && !string.Equals(member.Ticker, ticker, StringComparison.Ordinal)
                    && string.Equals(of(member), value, StringComparison.Ordinal))
                .Select(member => member.Ticker)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        var industry = Sharing(member => member.Industry, self?.Industry);

        return industry.Length >= Floor
            ? new Group(Group.Industry, self!.Industry, industry)
            : new Group(Group.Sector, self?.Sector, Sharing(member => member.Sector, self?.Sector));
    }

    // The median of the members' moves from the close before a span's first session to the close
    // of its last, each in per cent as a name's own move is. A member missing either close is left
    // out and counted, and a group with none left says so rather than giving a median of nothing.
    // An even count takes the mean of the two middle moves, which is the median's own rule.
    // see: A large move is shown beside its group's median move over the same sessions
    public static GroupMedian MedianMove(IReadOnlyList<(decimal? From, decimal? To)> closes)
    {
        var moves = closes
            .Where(close => close.From is > 0 && close.To is not null)
            .Select(close => Statistic.FromRatio((close.To!.Value - close.From!.Value) / close.From.Value) * 100)
            .Order()
            .ToArray();

        var missing = closes.Count - moves.Length;

        if (moves.Length == 0)
        {
            return new GroupMedian(null, 0, missing);
        }

        var middle = moves.Length / 2;

        return new GroupMedian(
            moves.Length % 2 == 1 ? moves[middle] : (moves[middle - 1] + moves[middle]) / 2,
            moves.Length,
            missing);
    }
}
