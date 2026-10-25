using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
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
    // The dip's market setting in words, which its own check and the market line read, and on a night its part failed
    // that it lists nothing for that.
    public static string DipSettingWords(DipNightRow night) =>
        night.Fault is not null
            ? "the short-term dip's part of the night failed, so it lists nothing tonight and the Run page names the cause"
            : night.Market == DipRule.MarketOff
                ? RuleOf(night) == ProvisionalDip
                    ? "the short-term dip reads its own market setting, none while it is provisional, and its list is open"
                    : "the short-term dip reads its own market setting, none, and its list is open"
                : $"the short-term dip reads its own market setting, {DipRule.MarketWordsOn(night.Index, night.Market)}, which {(night.MarketOpen ? "left its list open" : "closed its list")}";

    // The rule the dip's card is drawn by on a night, as its night's settings name it: the provisional rule before a
    // freeze and from 22.3 the live rule after one.
    // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
    public const string ProvisionalDip = "provisional";

    public static string RuleOf(DipNightRow night)
    {
        try
        {
            using var stored = System.Text.Json.JsonDocument.Parse(night.Settings);

            return stored.RootElement.TryGetProperty("rule", out var rule) && rule.GetString() is { } named ? named : ProvisionalDip;
        }
        catch (System.Text.Json.JsonException)
        {
            return ProvisionalDip;
        }
    }

    // How many members a setup passed on the night, the dip's passes among them, each member counted once.
    public static int PassedWithDip(IEnumerable<string> passed, IReadOnlyList<IndexResultRow> dips) =>
        passed.Concat(dips.Where(result => result.Passed).Select(result => result.Ticker)).Distinct(StringComparer.Ordinal).Count();

    // How an S&P 400's or 600's night went with the dip's part of it: the members a setup passed, the dip's among them,
    // and what it listed, held back for a trade still open, kept and ended that night beside the index families' own.
    public static IndexRunView WithDipSummary(IndexRunView run, IReadOnlyList<IndexResultRow> results, IReadOnlyList<IndexResultRow> dips, IReadOnlyList<DipPickRow> picks, IReadOnlyList<IndexTradeRow> trades, DateOnly session) =>
        run with
        {
            Passed = PassedWithDip(results.Where(result => result.Passed).Select(result => result.Ticker), dips),
            Listed = run.Listed + picks.Count(pick => pick.State == FamilyList.Listed),
            HeldBack = run.HeldBack + picks.Count(pick => pick.State == FamilyList.OpenTrade),
            Kept = run.Kept + trades.Count(trade => trade.Listed == session),
            Ended = run.Ended + trades.Count(trade => trade.EndedOn == session),
        };

    // The setting a dip night ran on and its rule in words, read off the settings its step stored, the provisional
    // setting's where a night stored none it can read.
    public static (DipSetting Setting, string Words) SettingOf(DipNightRow night)
    {
        try
        {
            using var stored = System.Text.Json.JsonDocument.Parse(night.Settings);
            var root = stored.RootElement;
            var setting = root.TryGetProperty("setting", out var shortForm) && shortForm.GetString() is { } stated ? DipSetting.FromShort(stated) : null;
            var words = root.TryGetProperty("words", out var rule) ? rule.GetString() : null;

            return (setting ?? DipRule.Provisional, words ?? (setting ?? DipRule.Provisional).Words);
        }
        catch (System.Text.Json.JsonException)
        {
            return (DipRule.Provisional, DipRule.Provisional.Words);
        }
    }

    // The cards with the dip's settings as chips beneath its heading, read off its night as its rule in words is, since
    // no registration states them before its freeze.
    public static IReadOnlyList<FamilyCardView> WithDipChips(IReadOnlyList<FamilyCardView> cards, DipNightRow? night, string index) =>
        night is null
            ? cards
            : [.. cards.Select(card => card.Family == DipRule.Name && card.RuleChoice is { Chosen.Live: true } rule
                ? card with { RuleChoice = rule with { Chips = [.. SettingOf(night).Setting.ChipsOn(index).Select(words => new RuleChipView(words, false))] } }
                : card)];

    // From 22.3 the dip's card where its night was drawn by a frozen rule: live since the day that rule was registered,
    // the versions standing beside it, each pick's why naming it, and the line every frozen card draws from the answer
    // its freeze followed, read against its family's freezes on the index as every frozen card's is; the cards as they
    // were where the night was drawn by the provisional rule.
    // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
    // see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
    public static IReadOnlyList<FamilyCardView> WithDipFreeze(IReadOnlyList<FamilyCardView> cards, string index, IReadOnlyList<CandidateRow> register, IReadOnlyList<RecordedAnswer> answers, DipNightRow? night)
    {
        if (night is null || RuleOf(night) is var drawnBy && drawnBy == ProvisionalDip)
        {
            return cards;
        }

        var family = Core.Candidates.OwnStepCandidate.FamilyOn(DipRule.Name, index);
        var at = PastTheNight(night.Session);
        var (live, variants) = StandingRows(family, register, at);

        if (live is null || live.Candidate != drawnBy)
        {
            return cards;
        }

        var (lineDrawn, lost, frozen) = SweepLines([.. answers.Where(answer => answer.Family == DipRule.Name)], new FamilyFreezes(live.Candidate, Freezes(family, register, at), null), false);

        return
        [
            .. cards.Select(card => card.Family == DipRule.Name
                ? card with { LiveSince = DateOnly.FromDateTime(live.RegisteredAt.UtcDateTime), Variants = variants, SweepFoundNone = lineDrawn, SweepLost = lost, Frozen = frozen }
                : card),
        ];
    }

    // The dip's own market check as the page's top reads each family's, so its tile and the line say its own and never
    // the swing families' check.
    public static MarketCheckView DipCheck(DipNightRow night) =>
        new(DipRule.Name, SetupFamilies.Dips.Label, night.MarketOpen, DipSettingWords(night));

    // The dip's card on an index's night, none where its step did not read the night, each pick's business state as the
    // night stored it.
    public static FamilyCardView? DipCard(
        UniverseChoice universe,
        DipNightRow? night,
        IReadOnlyList<DipPickRow> picks,
        IReadOnlyList<IndexResultRow> results,
        IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members,
        IReadOnlyList<ResearchedRow> researched,
        IReadOnlyDictionary<string, QueueState> queue,
        IReadOnlyList<FundamentalReadingRow>? readings = null)
    {
        if (night is null)
        {
            return null;
        }

        var family = SetupFamilies.Dips;
        var readingBy = (readings ?? []).ToDictionary(row => row.Ticker, StringComparer.Ordinal);
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
                        Queue: queue.GetValueOrDefault(pick.Ticker),
                        Business: Business(readingBy.GetValueOrDefault(pick.Ticker)));

                    var setting = SettingOf(night).Setting;

                    return new FamilyPickCell(row, pick.Place ?? 0, answer.Entry, answer.Stop, null, false, null, DipWhy(answer, universe.Name, setting, RuleOf(night) != ProvisionalDip), [], Exit: ExitWords(setting));
                }),
        ];

        return new FamilyCardView(
            family.Name,
            family.Label,
            family.Heading,
            family.Eyebrow,
            SettingOf(night).Words,
            0,
            0,
            null,
            0,
            listed,
            DipNotes(picks),
            listed.Length > 0 ? null : DipEmpty(universe, night, picks, results),
            HeldBack: picks.Count(pick => pick.State == FamilyList.OpenTrade),
            Passes: [.. picks.OrderBy(pick => pick.Place ?? int.MaxValue).ThenBy(pick => pick.Ticker, StringComparer.Ordinal).Select(pick => pick.Ticker)],
            NotComputed: night.Fault is not null);
    }

    // The exit a dip pick sells on, in the words its row draws where a target would stand, by its exit's dial.
    public static string ExitWords(DipSetting setting) =>
        setting.ExitRsiAbove is { } level ? FormattableString.Invariant($"its {DipRule.RsiSessions}-session RSI over {level}")
        : setting.ExitOnUpClose ? "its first up close"
        : FormattableString.Invariant($"over its {setting.ExitAverage}-session average");

    // Why the dip lists a stock tonight, its 2-session RSI as the night stored it and the rule that passed it, the
    // provisional setting or from 22.3 the index's live rule, its drop and its trend as that rule reads them, and how the
    // trade is sold in words, since it has no target to draw.
    static string DipWhy(IndexResultRow answer, string index, DipSetting setting, bool live)
    {
        var rule = live ? $"the {index}'s live rule" : $"the {index}'s provisional setting";
        var drop = setting.FallMoves is { } fall
            ? string.Create(CultureInfo.InvariantCulture, $"after a fall of at least {fall:0.##} typical moves over {DipRule.DropFallSessions} sessions")
            : "after a sharp drop";
        var volume = setting.VolumeUnder is { } under ? string.Create(CultureInfo.InvariantCulture, $" on a mean volume under {under:0.##} times its {DipRule.VolumeSessions}-session average") : string.Empty;
        var trend = string.Create(CultureInfo.InvariantCulture, $"above its {DipRule.TrendSessions}-session average")
            + (setting.FiftyOverLong ? string.Create(CultureInfo.InvariantCulture, $" with its {DipRule.FiftySessions}-session average above that") : string.Empty);
        var exit = setting.ExitRsiAbove is { } level ? string.Create(CultureInfo.InvariantCulture, $"its {DipRule.RsiSessions}-session RSI closes back above {level} or a close under its stop")
            : setting.ExitOnUpClose ? "a close above the close before or under its stop"
            : string.Create(CultureInfo.InvariantCulture, $"a close above its {setting.ExitAverage}-session average of closes or under its stop");

        return (answer.OrderBy is { } rsi
                ? string.Create(CultureInfo.InvariantCulture, $"Its {DipRule.RsiSessions}-session RSI closed at {rsi:0.0} {drop}{volume}, {trend} with no report in the drop or before its time limit, every gate of the short-term dip passing on {rule}.")
                : $"Every gate of the short-term dip passed on {rule} tonight.")
            + string.Create(CultureInfo.InvariantCulture, $" Bought at the next open, it is sold at the next open after {exit}, or at the close of its {answer.Cap ?? setting.TimeLimit}th session where neither came.");
    }

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

    // An index's setups on its Run page with the dip's row after every swing family's and before the sector heavyweights',
    // what it listed on the night and every trade its list has kept, provisional with its record waiting for its freeze;
    // the rows as they were on a night the dip did not read and before it kept a trade.
    public static IReadOnlyList<FamilyRunRow> WithDipRun(IReadOnlyList<FamilyRunRow> rows, DipNightRow? night, IReadOnlyList<DipPickRow> picks, IReadOnlyList<IndexTradeRow> trades, IReadOnlyList<CandidateRow>? register = null)
    {
        if (night is null && trades.Count == 0)
        {
            return rows;
        }

        // From 22.3 a night drawn by a frozen rule names the day it went live and the versions standing beside it, its
        // trades counted from its freeze.
        var live = night is not null && register is not null && RuleOf(night) is var drawnBy && drawnBy != ProvisionalDip
            && StandingRows(Core.Candidates.OwnStepCandidate.FamilyOn(DipRule.Name, night.Index), register, PastTheNight(night.Session)) is { Live: { } standing } held && standing.Candidate == drawnBy
            ? (Since: DateOnly.FromDateTime(standing.RegisteredAt.UtcDateTime), held.Variants)
            : ((DateOnly Since, int Variants)?)null;
        var counted = live is { } from ? [.. trades.Where(trade => trade.Listed >= from.Since)] : trades;

        var dip = new FamilyRunRow(
            DipRule.Name,
            SetupFamilies.Dips.Heading,
            live?.Since,
            live?.Variants ?? 0,
            picks.Count(pick => pick.State == FamilyList.Listed),
            counted.Count,
            counted.Count(trade => trade.EndedOn is null),
            counted.Count(trade => trade.EndedOn is not null),
            live is null ? SetupFamilies.Provisional : FormattableString.Invariant($"{counted.Count(trade => trade.EndedOn is not null)} finished since its freeze"));
        var at = rows.Select((row, place) => (row, place)).FirstOrDefault(one => one.row.Family == HeavyweightRule.Name).place;

        return rows.Any(row => row.Family == HeavyweightRule.Name) ? [.. rows.Take(at), dip, .. rows.Skip(at)] : [.. rows, dip];
    }

    // A stock's rows on the night's lists with the dip's beside them, which the name page's listed-under line reads: a dip
    // pick listed, held back for a trade still open or past its five as a family's own row, and one the dip gave way to as
    // its label on the row of the family that listed it.
    public static IReadOnlyList<FamilyPickRow> WithDipPicks(IReadOnlyList<FamilyPickRow> picks, IReadOnlyList<DipPickRow> dips, string ticker)
    {
        var own = dips.Where(pick => pick.Ticker == ticker).ToArray();
        var under = own.Where(pick => pick.State == FamilyList.UnderAnother).Select(pick => pick.ListedUnder).OfType<string>().ToHashSet(StringComparer.Ordinal);

        return
        [
            .. picks.Select(pick => pick.Ticker == ticker && pick.State == FamilyList.Listed && under.Contains(pick.Family) && !pick.Also.Contains(DipRule.Name)
                ? pick with { Also = [.. pick.Also, DipRule.Name] }
                : pick),
            .. own
                .Where(pick => pick.State is FamilyList.Listed or FamilyList.OpenTrade or FamilyList.PastFive)
                .Select(pick => new FamilyPickRow(pick.Session, pick.Ticker, DipRule.Name, pick.State, pick.Place, [], pick.HeldFamily, pick.HeldNight, pick.HeldIndex)),
        ];
    }

    // The stocks one gate short of the dip's buy point: each member whose stored answer passed the drop, the dip's setup,
    // and failed exactly one other gate, a report inside the window being an exclusion and never a gate narrowly missed,
    // in the family's order, the lowest 2-session RSI first and then the ticker, with that gate and its words; none where
    // the night stored no answer. A member whose stored year misses a session reads no drop, so none is drawn.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public static IReadOnlyList<CloseToBuyCell> DipCloseAcross(IReadOnlyList<IndexResultRow> results, IReadOnlyList<(string Ticker, string? Company, decimal? Close)> members)
    {
        var companies = members.ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal);
        var cells = new List<CloseToBuyCell>();

        foreach (var result in results.Where(result => !result.Passed && result.Gates is not null).OrderBy(result => result.OrderBy ?? double.MaxValue).ThenBy(result => result.Ticker, StringComparer.Ordinal))
        {
            var gates = FamilyRule.GatesOf(result.Gates!);
            var failed = gates.Where(gate => !gate.Passed).ToArray();

            if (gates.Any(gate => gate.Name == DipRule.DropGate && gate.Passed) && failed is [{ Name: not DipRule.ReportGate } missed])
            {
                cells.Add(new CloseToBuyCell(result.Ticker, companies.GetValueOrDefault(result.Ticker), DipRule.Name, SetupFamilies.Dips.Label, missed.Name, missed.Reason));
            }
        }

        return cells;
    }
}
