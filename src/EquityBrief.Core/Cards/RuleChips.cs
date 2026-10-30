using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Cards;

// One setting of a rule as its card draws it: the setting it stands for, by the parameter its registration states it
// under, and its short words.
public sealed record RuleChip(string Key, string Words);

// The kinds of rule a card draws, each with a table of short words of its own: the S&P 500's pullback, breakout, drift
// and sector heavyweights, and each family's rules on the S&P 400 or 600.
public enum RuleKind
{
    Pullback,
    Breakout,
    Drift,
    Heavyweights,
    IndexPullback,
    IndexBreakout,
    IndexDrift,
    IndexHeavyweights,
    Quality,
    Improving,
}

// A rule's settings as a card's chips, written by code from the parameters its registration states, or from the settings
// its night stored for a rule no registration states, through one table of short words a kind of rule; and the chips a
// variant differs from its live or provisional rule by, which its tab is named by. It reads parameters and computes no
// reading.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public static class RuleChips
{
    // The value a registration states for a setting that is off, as a ceiling no reading reaches.
    const double Unbounded = 1e9;

    // The chips a kind of rule draws for its parameters, in the table's order, a setting the kind states none of or that
    // is off where off is drawn as nothing left out; and the hooks an approval set, where the rule states any, last.
    public static IReadOnlyList<RuleChip> Of(RuleKind kind, IReadOnlyDictionary<string, double> p)
    {
        var chips = new List<RuleChip>();

        void Add(string key, string? words)
        {
            if (words is not null)
            {
                chips.Add(new RuleChip(key, words));
            }
        }

        double? At(string key) => p.TryGetValue(key, out var value) ? value : null;

        switch (kind)
        {
            case RuleKind.Pullback:
                Add("strengthFloor", At("strengthFloor") is { } strength ? Invariant($"stronger than {Percent(strength)}") : null);
                Add("depth", At("depthLow") is { } low && At("depthHigh") is { } high ? Invariant($"{Number(low)} to {Number(high)} moves down") : null);
                Add("dryUpCeiling", At("dryUpCeiling") is { } dryUp ? Off(dryUp) ? "no dry-up gate" : Invariant($"volume under {Number(dryUp)} times") : null);
                Add("arrivalSessions", At("arrivalSessions") is { } arrival ? Invariant($"turns up within {Number(arrival)}") : null);
                Add("earningsWindowSessions", At("earningsWindowSessions") is { } window ? Invariant($"no report in {Number(window)} sessions") : null);
                Add("rewardToRiskFloor", At("rewardToRiskFloor") is { } reward ? Invariant($"{Number(reward)} times the risk") : null);
                Add("stop", At("stopLow") is { } stopLow && At("stopHigh") is { } stopHigh ? Invariant($"stop {Number(stopLow)} to {Number(stopHigh)} moves") : null);
                Add("market", At(SwingFilterRule.MarketGateParameter) == 0 ? "market check off" : At("breadthFloor") is { } breadth ? Invariant($"breadth at least {Percent(breadth)}") : null);
                Add(SwingFilterRule.TradeParameter, At(SwingFilterRule.TradeParameter) switch
                {
                    0 => "the ladder's plan",
                    1 => "the swing plan",
                    2 => "the plan clear of the noise",
                    _ => null,
                });
                Add(SwingFilterRule.SkipDeterioratingParameter, At(SwingFilterRule.SkipDeterioratingParameter) == 1
                    ? At(SwingFilterRule.FiledStateParameter) == 1 ? "no business deteriorating in its SEC filings" : "no deteriorating business"
                    : null);
                Add("leaders", At(SwingFilterRule.LeaderSectorsParameter) is > 0 and var sectors && At(SwingFilterRule.LeaderShareOfParameter) is > 0 and var share
                    ? Invariant($"top {Number(sectors)} sectors, top 1 in {Number(share)}")
                    : null);
                Add(SwingFilterRule.BestOfParameter, At(SwingFilterRule.BestOfParameter) is > 0 and var best ? Invariant($"best {Number(best)} a night") : null);
                Add(SwingFilterRule.RaisedEstimatesParameter, At(SwingFilterRule.RaisedEstimatesParameter) == 1 ? "estimates raised" : null);
                break;

            case RuleKind.Breakout:
            case RuleKind.IndexBreakout:
                Add(BreakoutCandidate.HighSessionsParameter, At(BreakoutCandidate.HighSessionsParameter) is { } sessions ? Invariant($"{Number(sessions)}-session high") : null);
                Add(BreakoutCandidate.VolumeMultipleParameter, At(BreakoutCandidate.VolumeMultipleParameter) is { } volume ? Invariant($"volume {Number(volume)} times") : null);
                Add(BreakoutCandidate.RangeCeilingParameter, At(BreakoutCandidate.RangeCeilingParameter) is { } ceiling ? ceiling == 0 || Off(ceiling) ? "any range" : Invariant($"ranges at {Number(ceiling)}") : null);
                Add(BreakoutCandidate.StopMovesParameter, At(BreakoutCandidate.StopMovesParameter) is { } stop ? "stop " + Moves(stop) : null);
                Add(IndexBreakoutCandidate.NearnessParameter, At(IndexBreakoutCandidate.NearnessParameter) is > 0 and var near ? Invariant($"within {Number(near)} of the year's high") : null);
                Add(IndexBreakoutCandidate.RecencyParameter, At(IndexBreakoutCandidate.RecencyParameter) switch
                {
                    > 0 => "high within 63 sessions",
                    < 0 => "high older than 63 sessions",
                    _ => null,
                });
                Add(IndexBreakoutCandidate.HighVolumeParameter, At(IndexBreakoutCandidate.HighVolumeParameter) is > 0 and var heavy ? Invariant($"volume at least {Number(heavy)} times") : null);
                break;

            case RuleKind.Drift:
            case RuleKind.IndexDrift:
                Add(DriftCandidate.WindowSessionsParameter, At(DriftCandidate.WindowSessionsParameter) is { } within ? Invariant($"within {Number(within)} sessions") : null);
                Add(DriftCandidate.ReactionMovesParameter, At(DriftCandidate.ReactionMovesParameter) is { } reaction ? "up " + Moves(reaction) : null);
                Add(DriftCandidate.VolumeMultipleParameter, At(DriftCandidate.VolumeMultipleParameter) is { } multiple ? Invariant($"volume {Number(multiple)} times") : null);
                Add("target", (At(DriftCandidate.TargetRiskMultipleParameter) ?? At(IndexDriftCandidate.TargetMultipleParameter)) is { } target ? Invariant($"target {Number(target)} times the risk") : null);
                Add("stopFloor", (At(DriftCandidate.StopFloorMovesParameter) ?? At(IndexDriftCandidate.StopFloorParameter)) is > 0 and var floor ? "stop at least " + Moves(floor) : null);
                Add(IndexDriftCandidate.WideWindowParameter, At(IndexDriftCandidate.WideWindowParameter) is > 0 and var wide ? Invariant($"window {Number(wide)} sessions") : null);
                Add(IndexDriftCandidate.PeersParameter, At(IndexDriftCandidate.PeersParameter) == 1 ? "peers beat first" : null);

                // From 20.11 the entry on the first pullback after the reaction and the revenue level, each drawn where
                // the rule states it on.
                // see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
                // see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
                Add(IndexDriftCandidate.EntryParameter, At(IndexDriftCandidate.EntryParameter) == 1 ? "bought on its first lower close" : null);
                Add(IndexDriftCandidate.RevenueGrewParameter, At(IndexDriftCandidate.RevenueGrewParameter) == 1 ? "revenue above a year before" : null);
                break;

            case RuleKind.Heavyweights:
            case RuleKind.IndexHeavyweights:
                if (At(IndexHeavyweightCandidate.DesignParameter) == IndexHeavyweightCandidate.DesignB)
                {
                    Add(IndexHeavyweightCandidate.DesignParameter, "design (b)");
                    Add(IndexHeavyweightCandidate.IndustriesParameter, At(IndexHeavyweightCandidate.IndustriesParameter) is { } industries ? industries == 0 ? "every leading industry" : Invariant($"{Number(industries)} strongest industries") : null);
                    Add(IndexHeavyweightCandidate.WindowParameter, At(IndexHeavyweightCandidate.WindowParameter) is { } over ? Invariant($"over {Number(over)} sessions") : null);
                    Add(IndexHeavyweightCandidate.MembersParameter, At(IndexHeavyweightCandidate.MembersParameter) is { } members ? Count(members, "member") + " an industry" : null);
                    break;
                }

                Add(SectorHeavyweightCandidate.LargestParameter, At(SectorHeavyweightCandidate.LargestParameter) is { } largest ? largest == 0 || Off(largest) ? "every company" : Invariant($"{Number(largest)} largest") : null);
                Add(SectorHeavyweightCandidate.LookBackParameter, At(IndexHeavyweightCandidate.TwelveOneParameter) == 1
                    ? "twelve months less the latest"
                    : At(SectorHeavyweightCandidate.LookBackParameter) is { } back ? Invariant($"{Number(back)}-session look-back") : null);
                Add(SectorHeavyweightCandidate.LeadersParameter, At(SectorHeavyweightCandidate.LeadersParameter) is { } leaders ? Count(leaders, "leader") : null);
                Add(SectorHeavyweightCandidate.FundParameter, At(SectorHeavyweightCandidate.FundParameter) switch
                {
                    1 => "against the fund",
                    0 => "against the members",
                    _ => null,
                });
                Add(SectorHeavyweightCandidate.BetaParameter, At(SectorHeavyweightCandidate.BetaParameter) == 1 ? "beta at least 1" : null);
                Add(SectorHeavyweightCandidate.WeeklyParameter, At(SectorHeavyweightCandidate.WeeklyParameter) == 1 ? "weekly" : null);
                Add("exit", (At(SectorHeavyweightCandidate.LeadingExitParameter), At(SectorHeavyweightCandidate.AverageExitParameter)) switch
                {
                    (1, 1) => "sold on either exit",
                    (1, _) => "sold on losing the lead",
                    (_, 1) => "sold under the 200-day",
                    _ => null,
                });
                break;

            // From 22.11 Quality at a fair price's five dials.
            case RuleKind.Quality:
                Add(QualityCandidate.FloorParameter, At(QualityCandidate.FloorParameter) is { } scoreFloor ? Invariant($"a score of {Number(scoreFloor)} in 9") : null);
                Add(QualityCandidate.ValuationParameter, At(QualityCandidate.ValuationParameter) switch
                {
                    0 => "its price not read",
                    1 => "at or below its own usual price",
                    2 => "in the cheapest third of its own range",
                    _ => null,
                });
                Add(QualityCandidate.TrendParameter, At(QualityCandidate.TrendParameter) == 1 ? "above its 200-day average" : null);
                Add(QualityCandidate.BookParameter, At(QualityCandidate.BookParameter) is { } book ? Invariant($"{Number(book)} holdings") : null);
                Add(QualityCandidate.SaleParameter, At(QualityCandidate.SaleParameter) == 1 ? "also sold on a filing" : "sold at reshuffles");
                break;

            // From 22.15 the Fundamentals family's six dials, a check the setting does not ask left out.
            case RuleKind.Improving:
                Add(ImprovingCandidate.BuyParameter, At(ImprovingCandidate.BuyParameter) is { } bar ? Invariant($"buy at {Number(bar)} in 9") : null);
                Add(ImprovingCandidate.FlagParameter, At(ImprovingCandidate.FlagParameter) == 1 ? "a flag the night before" : null);
                Add(ImprovingCandidate.RiseParameter, At(ImprovingCandidate.RiseParameter) == 1 ? "above its previous filing's score" : null);
                Add(ImprovingCandidate.FasterParameter, At(ImprovingCandidate.FasterParameter) == 1 ? "sales speeding up" : null);
                Add(ImprovingCandidate.HoldParameter, At(ImprovingCandidate.HoldParameter) is { } hold ? Invariant($"held at {Number(hold)} in 9") : null);
                Add(ImprovingCandidate.LimitParameter, At(ImprovingCandidate.LimitParameter) is { } limit ? Invariant($"{Number(limit)} sessions at most") : null);
                break;
        }

        if (kind is RuleKind.IndexPullback)
        {
            Add("strength", At(IndexPullbackCandidate.StrengthParameter) is { } strength ? Invariant($"strength {Number(strength)}") : null);
            Add("depth", At(IndexPullbackCandidate.DepthLowParameter) is { } low && At(IndexPullbackCandidate.DepthHighParameter) is { } high ? Invariant($"{Number(low)} to {Number(high)} moves down") : null);
            Add(IndexPullbackCandidate.DryUpParameter, At(IndexPullbackCandidate.DryUpParameter) is { } dryUp ? dryUp == 0 || Off(dryUp) ? "no dry-up gate" : Invariant($"volume under {Number(dryUp)} times") : null);
            Add(IndexPullbackCandidate.FreshnessParameter, At(IndexPullbackCandidate.FreshnessParameter) is { } fresh ? Invariant($"fresh within {Number(fresh)}") : null);
            Add(IndexPullbackCandidate.RewardToRiskParameter, At(IndexPullbackCandidate.RewardToRiskParameter) is { } reward ? Invariant($"{Number(reward)} times the risk") : null);
            Add("stop", At(IndexPullbackCandidate.StopLowParameter) is { } stopLow && At(IndexPullbackCandidate.StopHighParameter) is { } stopHigh ? Invariant($"stop {Number(stopLow)} to {Number(stopHigh)} moves") : null);
            Add(IndexPullbackCandidate.BandParameter, At(IndexPullbackCandidate.BandParameter) is { } band ? Invariant($"band strength {Number(band)}") : null);
        }

        if (kind is RuleKind.IndexPullback or RuleKind.IndexBreakout or RuleKind.IndexDrift or RuleKind.IndexHeavyweights)
        {
            chips.AddRange(Levels(kind, p));
        }

        if (kind is RuleKind.Breakout or RuleKind.Drift)
        {
            Add(MarketSwitches.IndexAverageParameter, At(MarketSwitches.IndexAverageParameter) is > 0 and var average ? Invariant($"index above its {Number(average)}-day") : null);
            Add(MarketSwitches.VixLookbackParameter, At(MarketSwitches.VixLookbackParameter) is > 0 and var vix ? Invariant($"VIX falling over {Number(vix)}") : null);
        }

        // From 20.4 the drift's tone condition, read after every other part of its rule, where its registration names a
        // reading version.
        // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
        if (kind is RuleKind.Drift or RuleKind.IndexDrift)
        {
            Add(Releases.ReleaseTone.VersionParameter, Releases.ReleaseTone.VersionOf(p) > 0 ? Releases.ReleaseTone.Words : null);
        }

        // The change an approval set on the rule, in the words its hooks state, where it states any.
        Add("hooks", RuleHooks.Of(p).Words());

        return chips;
    }

    // The levels every swing family's rule on the S&P 400 or 600 states beside its own dials, each drawn whatever its
    // value, so a variant moving one differs from its provisional rule there: the dollar volume floor, the lowest close,
    // the quality, and the market check, naming the floor its breadth is read at where the rule states one, which for the
    // pullback carries its own market dial beside the breadth it reads where the dial reads otherwise than that floor.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    static IEnumerable<RuleChip> Levels(RuleKind kind, IReadOnlyDictionary<string, double> p)
    {
        double At(string key, double otherwise) => p.TryGetValue(key, out var value) ? value : otherwise;

        yield return new RuleChip(IndexRuleCandidate.FloorsParameter, Invariant($"dollar floor {Number(At(IndexRuleCandidate.FloorsParameter, 1))} times"));

        if (p.TryGetValue(IndexRuleCandidate.LowestCloseParameter, out var lowest))
        {
            yield return new RuleChip(IndexRuleCandidate.LowestCloseParameter, Invariant($"close at least ${Number(lowest)}"));
        }

        yield return new RuleChip(IndexRuleCandidate.QualityParameter, At(IndexRuleCandidate.QualityParameter, 1) switch
        {
            0 => "no quality gate",
            2 => "profit and cover",
            _ => "the profit gate",
        });

        if (kind == RuleKind.IndexHeavyweights)
        {
            yield break;
        }

        foreach (var (key, words) in new (string Key, string? Words)[]
        {
            (IndexRuleCandidate.HoldParameter, At(IndexRuleCandidate.HoldParameter, 0) is > 0 and var hold ? Invariant($"held {Number(hold)} sessions") : null),
            (IndexRuleCandidate.IndustryFallParameter, At(IndexRuleCandidate.IndustryFallParameter, 0) is > 0 and var fall ? Invariant($"industry not falling over {Number(fall)}") : null),
            (IndexRuleCandidate.FundOverSpyParameter, At(IndexRuleCandidate.FundOverSpyParameter, 0) is > 0 and var fund ? Invariant($"fund leading SPY over {Number(fund)}") : null),
            (IndexRuleCandidate.CreditAverageParameter, At(IndexRuleCandidate.CreditAverageParameter, 0) == 1 ? "HYG above its 50-day" : null),
            (IndexRuleCandidate.CreditChangeParameter, At(IndexRuleCandidate.CreditChangeParameter, 0) == 1 ? "HYG up over 63 sessions" : null),
        })
        {
            if (words is not null)
            {
                yield return new RuleChip(key, words);
            }
        }

        var breadth = At(IndexRuleCandidate.LargeBreadthParameter, 0);
        double? floor = p.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var stated) && stated > 0 ? stated : null;
        var level = floor is { } at ? Invariant($" at {Percent(at)}") : string.Empty;
        var dial = kind == RuleKind.IndexPullback && p.TryGetValue(IndexPullbackCandidate.MarketParameter, out var market)
            ? market == 0
                ? ", its own dial off"
                : floor is null
                    ? Invariant($" at {Percent(market)}")
                    : floor == market ? string.Empty : Invariant($", its own dial at {Percent(market)}")
            : string.Empty;

        yield return new RuleChip(IndexRuleCandidate.LargeBreadthParameter, breadth switch
        {
            IndexRuleCandidate.NoMarketCheck => IndexRuleCandidate.MarketCheckOff,
            1 => "market check on the S&P 500's breadth" + level + dial,
            _ => "market check on its own breadth" + level + dial,
        });
    }

    // The chips a chosen rule differs from its live or provisional rule by: each the chosen rule states in other words or
    // the live rule states none of, and each the live rule states that the chosen rule states none of, named as without
    // it; none for the live rule itself.
    public static IReadOnlyList<RuleChip> Differing(IReadOnlyList<RuleChip> live, IReadOnlyList<RuleChip> chosen)
    {
        var liveBy = live.ToDictionary(chip => chip.Key, StringComparer.Ordinal);
        var chosenKeys = chosen.Select(chip => chip.Key).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. chosen.Where(chip => !liveBy.TryGetValue(chip.Key, out var same) || same.Words != chip.Words),
            .. live.Where(chip => !chosenKeys.Contains(chip.Key)).Select(chip => new RuleChip(chip.Key, "without " + chip.Words)),
        ];
    }

    // A variant's tab: its number by its first registration and the chips it differs by, one beside another.
    // The fundamentals-first family's chips, read off the setting its night stored on any index: the revenue growth it
    // asks, the margins it asks to widen and the cash it asks for.
    public static IReadOnlyList<RuleChip> Of(FundamentalsSetting setting) =>
    [
        new("growth", Invariant($"revenue up more than {Percent(setting.GrowthFloor)}")),
        new("margins", setting.Margins switch
        {
            MarginRule.Both => "gross and operating margins up",
            MarginRule.Operating => "operating margin up",
            _ => "gross or operating margin up",
        }),
        new("cash", Invariant($"cash at least {Number(setting.CashFloor)} times income")),
    ];

    public static string TabWords(int variant, IReadOnlyList<RuleChip> differing) =>
        Invariant($"V{variant} · ") + (differing.Count == 0 ? "the same settings" : string.Join(" · ", differing.Select(chip => chip.Words)));

    static bool Off(double value) => double.IsPositiveInfinity(value) || value >= Unbounded;

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    static string Percent(double share) => (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    static string Count(double count, string noun) => Invariant($"{Number(count)} {noun}{(count == 1 ? string.Empty : "s")}");

    static string Moves(double count) => Count(count, "move");

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
