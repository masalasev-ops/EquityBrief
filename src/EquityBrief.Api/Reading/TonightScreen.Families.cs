using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from the page's stored list to the cards a night the families drew is read as. It
// evaluates no gate and draws no list: the lister drew it and stored it, and this reads it back, family by
// family in the page's order, each pick with the trade and the words its own family's stored answer holds.
// see: A screen reads and renders, and computes nothing
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public static partial class TonightScreen
{
    // The stocks the page lists on the night, in the page's order, each drawn as a row of the list draws a
    // name, which is what a selected row and the walk from name to name are read from.
    public static IReadOnlyList<ListingCell> ListedByFamilies(
        DateOnly night,
        IReadOnlyList<FamilyPickRow> picks,
        IReadOnlyList<ListingRow> listings,
        IReadOnlyDictionary<string, UniverseCell> cellByTicker,
        IReadOnlyList<CloseRow> closesToTheNight,
        IReadOnlyList<SuspectSeriesRow>? suspects = null,
        IReadOnlyList<ResearchedRow>? researched = null,
        IReadOnlyList<GateResultRow>? gates = null,
        IReadOnlyList<FundamentalReadingRow>? readings = null,
        IReadOnlyDictionary<string, (int Positive, int Negative)>? news = null)
    {
        var (drawn, _) = Drawer(night, cellByTicker, closesToTheNight, suspects, researched, readings);
        var byTicker = listings.ToDictionary(listing => listing.Ticker, StringComparer.Ordinal);
        var gateByTicker = (gates ?? []).ToDictionary(gate => gate.Ticker, StringComparer.Ordinal);

        return
        [
            .. picks
                .Where(pick => pick.State == FamilyList.Listed && byTicker.ContainsKey(pick.Ticker))
                .OrderBy(pick => pick.Place ?? int.MaxValue)
                .ThenBy(pick => pick.Ticker, StringComparer.Ordinal)
                .Select(pick =>
                {
                    var cell = drawn(byTicker[pick.Ticker]);

                    if (gateByTicker.TryGetValue(pick.Ticker, out var gate))
                    {
                        var filter = FilterRowOf(gate);

                        cell = cell with
                        {
                            Filter = filter,
                            RewardToRisk = decimal.TryParse(filter.RewardToRisk, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ? ratio : null,
                            NoRewardToRisk = null,
                        };
                    }

                    return news is not null && news.TryGetValue(pick.Ticker, out var counts)
                        ? cell with { NewsPositive = counts.Positive, NewsNegative = counts.Negative }
                        : cell;
                }),
        ];
    }

    // One card a family, in the page's order: its picks as the lister placed them, each with its trade and
    // why its family lists it, the notes on the stocks it passed and the page holds back, and why it lists
    // none on a night it lists none. The rows are the ones `ListedByFamilies` drew, carrying what the queue
    // holds for each.
    public static IReadOnlyList<FamilyCardView> Families(
        DateOnly night,
        IReadOnlyList<string> onThePage,
        IReadOnlyList<FamilyPickRow> picks,
        IReadOnlyList<ListingCell> rows,
        IReadOnlyList<GateResultRow> gates,
        IReadOnlyList<CandidateRow> register,
        ListRuleView rule)
    {
        var rowByTicker = rows.ToDictionary(row => row.Ticker, StringComparer.Ordinal);
        var gateByTicker = gates.ToDictionary(gate => gate.Ticker, StringComparer.Ordinal);
        var listedUnder = picks
            .Where(pick => pick.State == FamilyList.Listed)
            .ToDictionary(pick => pick.Ticker, pick => pick.Family, StringComparer.Ordinal);

        // The families the night's page held, in the order it held them, which an earlier night's page is
        // drawn with whatever families have joined the page since.
        var families = onThePage.Select(SetupFamilies.Named).OfType<SetupFamily>().ToArray();
        var cards = new List<FamilyCardView>();

        foreach (var (family, at) in families.Select((family, at) => (family, at)))
        {
            var own = picks.Where(pick => pick.Family == family.Name).ToArray();

            var listed = own
                .Where(pick => pick.State == FamilyList.Listed && rowByTicker.ContainsKey(pick.Ticker))
                .OrderBy(pick => pick.Place ?? int.MaxValue)
                .Select(pick => PickOf(family, pick, rowByTicker[pick.Ticker], gateByTicker.GetValueOrDefault(pick.Ticker)))
                .ToArray();

            var (liveSince, variants) = Standing(family, register, night);

            cards.Add(new FamilyCardView(
                family.Name,
                family.Label,
                family.Heading,
                family.Eyebrow,
                family.Rule,
                at + 1,
                families.Length,
                liveSince,
                variants,
                listed,
                Notes(family, own, listedUnder),
                listed.Length > 0 ? null : Empty(family, own, rule)));
        }

        return cards;
    }

    // A pick as its family's card draws it. A pullback's trade is the plan its night's trade gate read, off
    // its stored gate result, and its words are the figures its setup and trigger gates stored.
    static FamilyPickCell PickOf(SetupFamily family, FamilyPickRow pick, ListingCell row, GateResultRow? gate)
    {
        var also = pick.Also.Select(name => SetupFamilies.Named(name)?.Label ?? name).ToArray();

        if (family.Name != SetupFamilies.Pullback || gate is null || row.Filter is not { } filter)
        {
            return new FamilyPickCell(row, pick.Place ?? 0, null, null, null, false, null, "no stored answer of this setup is held for the night", also);
        }

        var (stop, target) = filter.Input == FilterSettings.ClearWord
            ? (gate.ClearStop, gate.ClearTarget)
            : filter.Input == FilterSettings.SwingWord
                ? (gate.SwingStop, gate.SwingTarget)
                : (null, null);

        return new FamilyPickCell(row, pick.Place ?? 0, gate.SwingEntry, stop, target, false, row.RewardToRisk, PullbackWhy(gate), also);
    }

    // Why the pullback lists a stock tonight, in the figures its setup and trigger gates stored: how far it
    // came down, the band it came down into, how its volume ran on the way, and the session it turned up on.
    public static string PullbackWhy(GateResultRow gate)
    {
        using var document = JsonDocument.Parse(gate.Gates);

        string? Value(string name, string key) =>
            document.RootElement.GetProperty("gates").EnumerateArray()
                .Where(one => one.GetProperty("gate").GetString() == name)
                .Select(one => one.GetProperty("values").TryGetProperty(key, out var value) ? value.GetString() : null)
                .FirstOrDefault();

        double? Figure(string name, string key) =>
            double.TryParse(Value(name, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var figure) ? figure : null;

        var depth = Figure(SwingGates.Setup, "depth");
        var dryUp = Figure(SwingGates.Setup, "dry-up");
        var low = Value(SwingGates.Setup, "band low");
        var high = Value(SwingGates.Setup, "band high");
        var arrived = Value(SwingGates.Trigger, SwingGates.ArrivedValue);

        var fell = depth is { } moves
            ? FormattableString.Invariant($"Fell {moves:0.0} typical moves")
            : "Fell";
        var band = low is { Length: > 0 } and not "none" && high is { Length: > 0 } and not "none"
            ? FormattableString.Invariant($" into its support band at {Price(low)} to {Price(high)}")
            : " into a support band";
        var volume = dryUp is { } ratio
            ? FormattableString.Invariant($" on volume {ratio:0.00} times its average")
            : string.Empty;
        var turned = arrived switch
        {
            null or "none" => string.Empty,
            "tonight" => ", and turned up tonight",
            var on when DateOnly.TryParseExact(on, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => $", and turned up on {on}",
            var worded => $", and turned up on {worded}",
        };

        return fell + band + volume + turned + ".";
    }

    static string Price(string stored) =>
        decimal.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ? Figures.Price(price) : stored;

    // The day a family's rule went live and the variants scored beside it, read off the register as it
    // stood at the night's end: the pullback's live candidate and the other candidates its evaluator runs.
    // A family no registration stands for runs on provisional settings and reads no day.
    // see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    static (DateOnly? LiveSince, int Variants) Standing(SetupFamily family, IReadOnlyList<CandidateRow> register, DateOnly night)
    {
        if (family.Name != SetupFamilies.Pullback)
        {
            return (null, 0);
        }

        var at = new DateTimeOffset(night.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var standing = CandidateFamily.Standing(
                [
                    .. register.Select(row => new RegisterRow(
                        row.Id, row.Candidate, string.Empty, string.Empty, row.Evaluator,
                        row.Parameters, string.Empty, row.Event, row.Retires, row.RegisteredAt, row.Evidence)),
                ],
                at)
            .Where(row => row.Evaluator == SwingFilterRule.EvaluatorName)
            .ToArray();

        var live = standing.LastOrDefault(row => SwingFamily.IsLive(row.Candidate));

        return (
            live is null ? null : DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime),
            standing.Count(row => !SwingFamily.IsLive(row.Candidate)));
    }

    // The notes beneath a family's picks: each stock it passed that a trade still open holds back, each one
    // the page lists under an earlier family, and how many it passed beyond the five it lists.
    // see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
    static IReadOnlyList<string> Notes(SetupFamily family, IReadOnlyList<FamilyPickRow> own, IReadOnlyDictionary<string, string> listedUnder)
    {
        var notes = new List<string>();

        foreach (var held in own.Where(pick => pick.State == FamilyList.OpenTrade).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var from = held.HeldNight is { } listed ? listed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "an earlier night";
            var whose = held.HeldFamily is { } holder && holder != family.Name
                ? $"its {(SetupFamilies.Named(holder)?.Label ?? holder).ToLowerInvariant()} trade from {from}"
                : $"its trade from {from}";

            notes.Add($"{held.Ticker} qualified again tonight but {whose} is still open, so it is not listed. One stock, one trade.");
        }

        foreach (var shared in own.Where(pick => pick.State == FamilyList.UnderAnother).OrderBy(pick => pick.Ticker, StringComparer.Ordinal))
        {
            var under = listedUnder.TryGetValue(shared.Ticker, out var first) ? SetupFamilies.Named(first)?.Heading ?? first : "another setup";

            notes.Add($"{shared.Ticker} also qualified here tonight and is shown once, under {under}, with both labels. One stock is one trade.");
        }

        var past = own.Where(pick => pick.State == FamilyList.PastFive).Select(pick => pick.Ticker).Order(StringComparer.Ordinal).ToArray();

        if (past.Length > 0)
        {
            notes.Add(FormattableString.Invariant($"{past.Length} more qualified tonight and {(past.Length == 1 ? "is" : "are")} not listed here, a setup listing at most {SetupFamilies.ListedANight} a night: {string.Join(", ", past)}."));
        }

        return notes;
    }

    // Why a family lists nothing on a night it lists none: the market check closed every list, no stock
    // passed its gates, or every stock it passed is held back, which its notes say.
    // see: The market check closes every family's list together
    static string Empty(SetupFamily family, IReadOnlyList<FamilyPickRow> own, ListRuleView rule)
    {
        if (own.Count > 0)
        {
            return "Every stock this setup passed tonight is held back, as the notes beneath say.";
        }

        if (!rule.MarketOpen)
        {
            return rule.Breadth is { } breadth && rule.Floor is { } floor
                ? FormattableString.Invariant($"The market check closed every list tonight: {breadth * 100:0.0}% of the members closed above their 200-day average, below its floor of {floor * 100:0.#}%.")
                : "The market check closed every list tonight: the night's breadth is not available.";
        }

        return family.Name == SetupFamilies.Pullback && rule.Reached.Count == 4
            ? FormattableString.Invariant($"No stock passed this setup tonight: {rule.Reached[0]} passed the trend and strength gate, {rule.Reached[1]} the setup, {rule.Reached[2]} the trigger and {rule.Reached[3]} the trade, and none of those past the exclusions.")
            : "No stock passed this setup tonight.";
    }
}
