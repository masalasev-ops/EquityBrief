using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The short-term dip's card and its place on an index's Tonight, read from its own rows: its rule in a sentence and the
// provisional marker, its picks with the buy at the next open and the stop, the notes on what it passed and held back,
// why it lists none on a night it lists none, its own market check, and its label on an earlier family's row where it
// gave way to that family. It evaluates no gate: the dip's step stored every answer, and this reads them back.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
public static partial class TonightScreen
{
    // The dip's market setting in words, which its own check and the market line read.
    public static string DipSettingWords(DipNightRow night) =>
        night.Market == DipRule.MarketOff
            ? "the short-term dip reads its own market setting, none while it is provisional, and its list is open"
            : $"the short-term dip reads its own market setting, {night.Market}, which {(night.MarketOpen ? "left its list open" : "closed its list")}";

    // The dip's own market check as the page's top reads each family's, so its tile and the line say its own and never
    // the swing families' check.
    public static MarketCheckView DipCheck(DipNightRow night) =>
        new(DipRule.Name, SetupFamilies.Dips.Label, night.MarketOpen, DipSettingWords(night));

    // The dip's card on an index's night, none where its step did not read the night.
    public static FamilyCardView? DipCard(
        UniverseChoice universe,
        DipNightRow? night,
        IReadOnlyList<DipPickRow> picks,
        IReadOnlyList<IndexResultRow> results,
        IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members,
        IReadOnlyList<ResearchedRow> researched,
        IReadOnlyDictionary<string, QueueState> queue)
    {
        if (night is null)
        {
            return null;
        }

        var family = SetupFamilies.Dips;
        var memberBy = members.ToDictionary(member => member.Ticker, StringComparer.Ordinal);
        var writtenOn = researched.ToDictionary(row => row.Ticker, row => row.Written, StringComparer.Ordinal);
        var passedBy = results.Where(result => result.Passed).ToDictionary(result => result.Ticker, StringComparer.Ordinal);

        FamilyPickCell[] listed =
        [
            .. picks
                .Where(pick => pick.State == FamilyList.Listed && passedBy.ContainsKey(pick.Ticker))
                .OrderBy(pick => pick.Place ?? int.MaxValue)
                .Select(pick =>
                {
                    var answer = passedBy[pick.Ticker];
                    var member = memberBy.GetValueOrDefault(pick.Ticker);
                    var row = new ListingCell(
                        pick.Ticker,
                        night.Session,
                        0,
                        0,
                        member.Close,
                        [],
                        Distance: new UniverseCell(pick.Ticker, string.Empty, member.Close, null, null, null, null, null, null, Name: member.Company),
                        ResearchedOn: writtenOn.TryGetValue(pick.Ticker, out var written) ? written : null,
                        Queue: queue.GetValueOrDefault(pick.Ticker));

                    return new FamilyPickCell(row, pick.Place ?? 0, answer.Entry, answer.Stop, null, false, null, DipWhy(answer, universe.Name), []);
                }),
        ];

        return new FamilyCardView(
            family.Name,
            family.Label,
            family.Heading,
            family.Eyebrow,
            family.Rule,
            0,
            0,
            null,
            0,
            listed,
            DipNotes(picks),
            listed.Length > 0 ? null : DipEmpty(universe, night, picks, results),
            HeldBack: picks.Count(pick => pick.State == FamilyList.OpenTrade),
            Passes: [.. picks.OrderBy(pick => pick.Place ?? int.MaxValue).ThenBy(pick => pick.Ticker, StringComparer.Ordinal).Select(pick => pick.Ticker)]);
    }

    // Why the dip lists a stock tonight: its 2-session RSI as the night stored it and the rule that passed it.
    static string DipWhy(IndexResultRow answer, string index) =>
        answer.OrderBy is { } rsi
            ? string.Create(CultureInfo.InvariantCulture, $"Its 2-session RSI closed at {rsi:0.0} after a sharp drop, above its 200-session average with no report in the drop or before its time limit, every gate of the short-term dip passing on the {index}'s provisional setting.")
            : $"Every gate of the short-term dip passed on the {index}'s provisional setting tonight.";

    // The notes beneath the dip's picks: each stock it passed that a trade still open holds back, each one another family
    // listed tonight, which that family's row draws with the dip's label, and how many it passed beyond its five.
    static IReadOnlyList<string> DipNotes(IReadOnlyList<DipPickRow> picks)
    {
        var notes = new List<string>();

        foreach (var held in picks.Where(pick => pick.State == FamilyList.OpenTrade).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var from = held.HeldNight is { } listed ? listed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "an earlier night";
            var where = held.HeldIndex is { } code ? $" on the {Universes.ByCode(code)?.Possessive ?? code} list" : string.Empty;
            var whose = held.HeldFamily is { } holder && holder != DipRule.Name
                ? $"its {(SetupFamilies.Named(holder)?.Label ?? holder).ToLowerInvariant()} trade from {from}{where}"
                : $"its trade from {from}{where}";

            notes.Add($"{held.Ticker} qualified tonight but {whose} is still open, so it is not listed. One stock, one trade.");
        }

        foreach (var shared in picks.Where(pick => pick.State == FamilyList.UnderAnother).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var under = shared.ListedUnder is { } first ? SetupFamilies.Named(first)?.Heading ?? first : "another setup";

            notes.Add($"{shared.Ticker} also qualified here tonight and is shown once, under {under}, with both labels. One stock is one trade.");
        }

        var past = picks.Where(pick => pick.State == FamilyList.PastFive).Select(pick => pick.Ticker).Order(StringComparer.Ordinal).ToArray();

        if (past.Length > 0)
        {
            notes.Add(FormattableString.Invariant($"{past.Length} more qualified tonight and {(past.Length == 1 ? "is" : "are")} not listed here, a setup listing at most {SetupFamilies.ListedANight} a night: {string.Join(", ", past)}."));
        }

        return notes;
    }

    // Why the dip lists nothing on a night it lists none: its part of the night failed, its own market setting closed its
    // list, every stock it passed is held back, or none passed, with how many members stopped at each gate.
    static string DipEmpty(UniverseChoice universe, DipNightRow night, IReadOnlyList<DipPickRow> picks, IReadOnlyList<IndexResultRow> results)
    {
        if (night.Fault is not null)
        {
            return $"Not computed tonight: the dip's part of the {universe.Possessive} night failed, and the Run page names its cause.";
        }

        if (picks.Count > 0)
        {
            return "Every stock this setup passed tonight is held back, as the notes beneath say.";
        }

        if (!night.MarketOpen)
        {
            return $"Its own market setting closed this setup's list tonight: {DipSettingWords(night)}.";
        }

        if (results.Count == 0)
        {
            return "No answer of this setup is stored for the night.";
        }

        var stopped = results
            .Where(result => !result.Passed)
            .GroupBy(result => result.Reason ?? "no reason stored", StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => FormattableString.Invariant($"{group.Count()} {group.Key}"));

        return $"No stock passed this setup tonight: of the {universe.Possessive} {results.Count} members read, {string.Join(", ", stopped)}.";
    }

    // The cards with the dip's card after every swing family's, each card's place and count of setups read again so the
    // headers number the dip among them; the cards as they were where the dip has no card for the night.
    public static IReadOnlyList<FamilyCardView> WithDip(IReadOnlyList<FamilyCardView> cards, FamilyCardView? dip)
    {
        if (dip is null)
        {
            return cards;
        }

        IReadOnlyList<FamilyCardView> all = [.. cards, dip];

        return [.. all.Select((card, at) => card with { Place = at + 1, Of = all.Count })];
    }

    // The cards with the dip's label on each earlier family's row of a stock the dip passed tonight and gave way to that
    // family for, as a stock qualifying under two families carries both labels on the first's row.
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    public static IReadOnlyList<FamilyCardView> WithDipLabels(IReadOnlyList<FamilyCardView> cards, IReadOnlyList<DipPickRow> picks)
    {
        var under = picks
            .Where(pick => pick.State == FamilyList.UnderAnother && pick.ListedUnder is not null)
            .ToDictionary(pick => (pick.ListedUnder!, pick.Ticker));

        if (under.Count == 0)
        {
            return cards;
        }

        return
        [
            .. cards.Select(card => card with
            {
                Picks =
                [
                    .. card.Picks.Select(pick => under.ContainsKey((card.Family, pick.Row.Ticker)) && !pick.Also.Contains(SetupFamilies.Dips.Label)
                        ? pick with { Also = [.. pick.Also, SetupFamilies.Dips.Label] }
                        : pick),
                ],
            }),
        ];
    }

    // The stocks one gate short of the dip's buy point: each member whose stored answer failed exactly one gate, the
    // history of its average read, by ticker, with that gate and its words; none where the night stored no answer.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyList<CloseToBuyCell> DipCloseAcross(IReadOnlyList<IndexResultRow> results, IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members)
    {
        var companies = members.ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal);
        var cells = new List<CloseToBuyCell>();

        foreach (var result in results.Where(result => !result.Passed && result.Gates is not null).OrderBy(result => result.Ticker, StringComparer.Ordinal))
        {
            var gates = FamilyRule.GatesOf(result.Gates!);

            if (gates.Any(gate => gate.Name == DipRule.HistoryGate && gate.Passed) && gates.Count(gate => !gate.Passed) == 1)
            {
                var missed = gates.First(gate => !gate.Passed);

                cells.Add(new CloseToBuyCell(result.Ticker, companies.GetValueOrDefault(result.Ticker), DipRule.Name, SetupFamilies.Dips.Label, missed.Name, missed.Reason));
            }
        }

        return cells;
    }
}
