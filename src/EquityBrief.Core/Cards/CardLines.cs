using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;

namespace EquityBrief.Core.Cards;

// Whether a line of a pick's card passes, notes something without warning, or warns.
public enum CardVerdict
{
    Tick,
    Note,
    Warn,
}

// One line of a pick's card: its place, its name, its verdict and the reason in words.
public sealed record CardLine(int Number, string Name, CardVerdict Verdict, string Words)
{
    public string Mark => Verdict switch
    {
        CardVerdict.Tick => "tick",
        CardVerdict.Note => "note",
        _ => "warning",
    };
}

// What the business line reads of a member's four newest quarters filed before the night: their net income summed and
// their operating income over their interest expense, each none where fewer than four were filed or one states none, the
// cover none as well where they file no interest expense, and whether the company is a financial one, whose cover is not
// read.
public sealed record BusinessReading(decimal? NetIncome, decimal? Cover, bool NoInterest, bool Financial, int Quarters);

// A sector's place among its index's sectors on a night: its name, its members' mean return over the window, its place
// from the top and how many sectors were ranked.
public sealed record SectorPlace(string Sector, double Return, int Place, int Of, int Members);

// The index's breadth against the floor its swing lists are read at, and whether the family that listed the pick reads
// the market check at all.
public sealed record MarketReading(double? Breadth, double Floor, bool ReadByTheFamily);

// The next report on file after the night: its date, whether it comes after the close, and the sessions from the night to
// the session it moves, none where that session lies past the exchange calendar's table.
public sealed record ReportAhead(DateOnly Date, EventTiming Timing, int? Sessions);

// The checklist on a pick's card, a function a line. Each reads what the night stored, warns where its value says to and
// names in words what it could not read, a reading not held warning rather than passing; none decides anything, and none
// reads a figure the night did not store.
// see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
// see: The card warns of a report inside the time three in four of the rule's trades had ended by, and notes one inside the cap alone
public static class CardLines
{
    public const string TrendName = "Trend and strength";
    public const string BusinessName = "Business";
    public const string MarketName = "Market and sector";
    public const string EarningsName = "Earnings in the hold";
    public const string CostName = "Liquidity and cost";

    // The window a sector's members' mean return is read over.
    public const int SectorSessions = 63;

    // A pick passed every gate its family reads to be listed, so its first line is a tick on every card and says which.
    public static CardLine Trend(string family, string index, IReadOnlyList<string> gates) =>
        new(1, TrendName, CardVerdict.Tick, gates.Count > 0
            ? $"Passed every gate of the {family} on the {index}: {string.Join("; ", gates)}."
            : $"Passed every gate of the {family} on the {index}.");

    // The four newest quarters filed before the night, read as the profit gate and the coverage read them, with the
    // cover stated as a ratio so the card's own floor can be moved.
    public static BusinessReading Business(IEnumerable<FiledIncome> quarters, DateOnly night, string? sector)
    {
        var read = MemberReadings.FiledBefore(quarters, night);
        var financial = string.Equals(sector, MemberReadings.Financials, StringComparison.Ordinal);
        var full = read.Count >= MemberReadings.Quarters;
        decimal? net = full && read.All(quarter => quarter.NetIncome is not null) ? read.Sum(quarter => quarter.NetIncome!.Value) : null;

        if (!full || read.Any(quarter => quarter.OperatingIncome is null))
        {
            return new BusinessReading(net, null, false, financial, read.Count);
        }

        var interest = read.Sum(quarter => Math.Abs(quarter.InterestExpense ?? 0m));

        return interest == 0m
            ? new BusinessReading(net, null, true, financial, read.Count)
            : new BusinessReading(net, read.Sum(quarter => quarter.OperatingIncome!.Value) / interest, false, financial, read.Count);
    }

    // The business line: a warning where the four quarters sum their net income to nothing or less, cover their interest
    // under the card's floor, or read deteriorating, and where any of the three is not held, saying which.
    public static CardLine Business(BusinessReading reading, string? state, CardSettings settings)
    {
        var against = new List<string>();
        var missing = new List<string>();
        var said = new List<string>();

        if (reading.NetIncome is not { } net)
        {
            missing.Add(reading.Quarters < MemberReadings.Quarters
                ? FormattableString.Invariant($"{reading.Quarters} of the four quarters the profit reading needs are filed")
                : "a quarter's net income is not filed");
        }
        else if (net <= 0m)
        {
            against.Add("its four newest quarters sum their net income to nothing or less");
        }
        else
        {
            said.Add("its four newest quarters sum their net income above nothing");
        }

        if (reading.Financial)
        {
            said.Add("its cover is not read, a financial company's interest being its cost of business");
        }
        else if (reading.NoInterest)
        {
            said.Add("it files no interest expense on them");
        }
        else if (reading.Cover is not { } cover)
        {
            missing.Add("its cover cannot be read from fewer than four quarters' operating income");
        }
        else if (cover < settings.CoverFloor)
        {
            against.Add(FormattableString.Invariant($"its operating income is {cover:0.00} times its interest, under the card's floor of {settings.CoverFloor:0.##}"));
        }
        else
        {
            said.Add(FormattableString.Invariant($"its operating income is {cover:0.00} times its interest"));
        }

        if (state is null)
        {
            missing.Add("the night read no state from its reported quarters");
        }
        else if (string.Equals(state, "deteriorating", StringComparison.Ordinal))
        {
            against.Add("its reported quarters read deteriorating");
        }
        else
        {
            said.Add($"its reported quarters read {state}");
        }

        var verdict = against.Count > 0 || missing.Count > 0 ? CardVerdict.Warn : CardVerdict.Tick;
        var parts = against.Concat(missing.Select(part => "not held: " + part)).Concat(said);

        return new CardLine(2, BusinessName, verdict, Sentence(parts));
    }

    // The members' mean returns by sector, each sector ranked from the highest; a sector none of whose members holds a
    // return is not ranked.
    public static IReadOnlyList<SectorPlace> Rank(IEnumerable<(string Sector, double Return)> members)
    {
        var sectors = members
            .Where(member => member.Sector.Length > 0 && double.IsFinite(member.Return))
            .GroupBy(member => member.Sector, StringComparer.Ordinal)
            .Select(group => (Sector: group.Key, Return: group.Average(member => member.Return), Members: group.Count()))
            .OrderByDescending(sector => sector.Return)
            .ThenBy(sector => sector.Sector, StringComparer.Ordinal)
            .ToArray();

        return [.. sectors.Select((sector, at) => new SectorPlace(sector.Sector, sector.Return, at + 1, sectors.Length, sector.Members))];
    }

    // The market and sector line: the index's breadth against its floor in words, and a warning where the stock's sector
    // stands among the card's bottom places of the index's sectors, or is not ranked.
    public static CardLine Market(MarketReading market, SectorPlace? sector, string index, CardSettings settings)
    {
        var breadth = market.Breadth is { } read
            ? FormattableString.Invariant($"the {index}'s breadth is {read:0.0%} against its floor of {market.Floor:0%}")
            : $"the {index}'s breadth was not read";
        var check = market.ReadByTheFamily ? breadth : breadth + ", a check this rule does not read";

        if (sector is null)
        {
            return new CardLine(3, MarketName, CardVerdict.Warn, Sentence([check, $"not held: its sector is not filed or not ranked among the {index}'s"]));
        }

        var bottom = sector.Place > sector.Of - settings.SectorBottom;
        var place = FormattableString.Invariant(
            $"its sector, {sector.Sector}, stands {Ordinal(sector.Place)} of {sector.Of} on the {index} by its {sector.Members} members' mean return over {SectorSessions} sessions, {sector.Return:0.0%}");

        return bottom
            ? new CardLine(3, MarketName, CardVerdict.Warn, Sentence([place + FormattableString.Invariant($", among the bottom {settings.SectorBottom}"), check]))
            : new CardLine(3, MarketName, CardVerdict.Tick, Sentence([check, place]));
    }

    // The sessions from a night to the session a report moves, by the earnings rule's own reading: a report before the open,
    // or stating no timing, moves its own day's session, and one after the close the session after; counted on the
    // exchange's calendar, none past its table and none where the report moved the night or an earlier session.
    public static int? SessionsTo(DateOnly night, DateOnly report, EventTiming timing)
    {
        var day = timing == EventTiming.After ? report.AddDays(1) : report;

        if (day < ExchangeClosures.CoveredFrom || day > ExchangeClosures.CoveredThrough)
        {
            return null;
        }

        while (!ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);

            if (day > ExchangeClosures.CoveredThrough)
            {
                return null;
            }
        }

        return day <= night ? null : ExchangeClosures.SessionsBetween(night, day).Count + 1;
    }

    // The earnings line: a warning where the next report moves a session inside the time by which the card's share of the
    // rule's replayed trades had ended, or where no report is on file; a note where it falls inside the rule's cap alone;
    // and a tick where it falls after both. A rule with no record reads the cap alone, and warns inside it.
    public static CardLine Earnings(ReportAhead? report, int? held, int? cap, CardSettings settings)
    {
        if (report is null)
        {
            return new CardLine(4, EarningsName, CardVerdict.Warn, "No report date is on file for it, so a report inside the hold cannot be ruled out.");
        }

        var when = FormattableString.Invariant($"Its next report, on {report.Date:yyyy-MM-dd}{Timing(report.Timing)}");

        if (report.Sessions is not { } sessions)
        {
            return new CardLine(4, EarningsName, CardVerdict.Warn, when + ", moves a session past the exchange calendar's table, so it cannot be placed in the hold.");
        }

        var moves = FormattableString.Invariant($"{when}, moves the session {sessions} {Plural(sessions, "session")} after the night");
        var share = Share(settings.HeldShare);

        if (held is { } by && sessions <= by)
        {
            return new CardLine(4, EarningsName, CardVerdict.Warn, FormattableString.Invariant($"{moves}, inside the {by} sessions by which {share} of the rule's trades had ended."));
        }

        if (cap is { } most && sessions <= most)
        {
            return held is { } after
                ? new CardLine(4, EarningsName, CardVerdict.Note, FormattableString.Invariant($"{moves}, inside the rule's cap of {most} sessions but after the {after} by which {share} of its trades had ended."))
                : new CardLine(4, EarningsName, CardVerdict.Warn, FormattableString.Invariant($"{moves}, inside the rule's cap of {most} sessions; the rule has no record to read its trades' holding time from."));
        }

        return new CardLine(4, EarningsName, CardVerdict.Tick, cap is { } capped
            ? FormattableString.Invariant($"{moves}, after the rule's cap of {capped} sessions.")
            : held is { } beyond
                ? FormattableString.Invariant($"{moves}, after the {beyond} sessions by which {share} of the rule's trades had ended.")
                : $"{moves}.");
    }

    // The liquidity and cost line: the mean dollar volume over 50 sessions and the round trip at the published table, in
    // multiples of the trade's risk, a warning at or over the card's value and where either is not held; a rule with no
    // stop reads its round trip in per cent of the buy and is not warned on it.
    public static CardLine Cost(decimal? dollarVolume, double? roundTrip, bool noStop, bool largeCompanies, CardSettings settings)
    {
        var parts = new List<string>();
        var warn = false;

        if (dollarVolume is { } volume)
        {
            parts.Add(FormattableString.Invariant($"it traded {volume / 1_000_000m:N1} million dollars a session over the {MemberReadings.DollarVolumeSessions} sessions to the night"));
        }
        else
        {
            parts.Add(FormattableString.Invariant($"not held: its dollar volume over {MemberReadings.DollarVolumeSessions} sessions"));
            warn = true;
        }

        if (roundTrip is not { } trip || !double.IsFinite(trip))
        {
            parts.Add("not held: its round trip at the published table");
            warn = true;
        }
        else if (noStop)
        {
            parts.Add(FormattableString.Invariant($"its round trip at the published table is {trip:0.00}% of the buy, read in per cent since the rule sets no stop"));
        }
        else
        {
            var over = trip >= settings.RoundTripRisks;

            warn |= over;
            parts.Add(over
                ? FormattableString.Invariant($"its round trip at the published table is {trip:0.000} of the trade's risk, at or over the card's {settings.RoundTripRisks:0.00}")
                : FormattableString.Invariant($"its round trip at the published table is {trip:0.000} of the trade's risk"));
        }

        if (largeCompanies)
        {
            parts.Add("the table's largest size band reads high for a company this large, whose measured spread is about 0.03%");
        }

        return new CardLine(5, CostName, warn ? CardVerdict.Warn : CardVerdict.Tick, Sentence(parts));
    }

    // A share of the trades in words, three in four for 0.75.
    public static string Share(double share) => share switch
    {
        0.75 => "three in four",
        0.5 => "half",
        0.9 => "nine in ten",
        _ => FormattableString.Invariant($"{share:0%}"),
    };

    static string Timing(EventTiming timing) => timing switch
    {
        EventTiming.After => " after the close",
        EventTiming.Before => " before the open",
        _ => ", its timing not stated",
    };

    static string Plural(int count, string noun) => count == 1 ? noun : noun + "s";

    static string Ordinal(int place) => (place % 100) switch
    {
        11 or 12 or 13 => FormattableString.Invariant($"{place}th"),
        _ => (place % 10) switch
        {
            1 => FormattableString.Invariant($"{place}st"),
            2 => FormattableString.Invariant($"{place}nd"),
            3 => FormattableString.Invariant($"{place}rd"),
            _ => FormattableString.Invariant($"{place}th"),
        },
    };

    // Parts joined into one sentence, its first letter raised.
    static string Sentence(IEnumerable<string> parts)
    {
        var joined = string.Join("; ", parts);

        return joined.Length == 0 ? string.Empty : char.ToUpperInvariant(joined[0]) + joined[1..] + ".";
    }
}
