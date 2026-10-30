using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Worker.Improving;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Quality;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// The live swing rules an index's families stand at as the ledger reads them: each family's rule by its family, and
// the words naming each live rule the ledger leaves unread with why.
public sealed record LiveRulesRead(IReadOnlyDictionary<string, IndexRule> Read, IReadOnlyList<string> Unread)
{
    public static LiveRulesRead None { get; } = new(new Dictionary<string, IndexRule>(StringComparer.Ordinal), []);

    // From 21.4 the two book families' live rules on the index as the register stood, by name and setting, the
    // provisional rule at its starting setting where none stands registered; each book setup's live pass is read under
    // the setting and its pick off the rule's own holdings.
    // see: A book family's setup is each member a reshuffle or a filing leaves at the ledger's gate, held while it clears the hold and never past a year against an equal-weighted hold of the index
    public (string Rule, QualitySettings Settings) Quality { get; init; } = (QualityFamily.ProvisionalRule, QualityRule.Starting);

    public (string Rule, ImprovingSweepSetting Setting) Improving { get; init; } = (ImprovingFamily.ProvisionalRule, ImprovingGrid.Starting);

    // The words a night's row and a build's run carry for the rules read as the register stood at an instant, none where
    // no live rule stands.
    public string? Words(DateTimeOffset at)
    {
        if (Read.Count == 0 && Unread.Count == 0)
        {
            return null;
        }

        var families = Read.Keys.OrderBy(family => LedgerSetups.Families.ToList().IndexOf(family)).ToArray();
        var instant = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string? read = families.Length switch
        {
            0 => null,
            1 => $"the {families[0]} read at its frozen live rule as the register stood at {instant}",
            _ => $"the {string.Join(" and the ", families)} read at their frozen live rules as the register stood at {instant}",
        };

        return string.Join(", ", new[] { read }.Concat(Unread).OfType<string>());
    }
}

// The S&P 400's and 600's frozen live rules as the setup ledger reads them. A family whose live rule stands registered
// on its index has its setups read under that rule: each row carries the rule's words, and its live pass is whether the
// rule passes the member as the night's own read of the rule gives it, the market check at the rule's own level, the
// floors at its multiple and its lowest close, its quality and each of its levels, every part read over the inputs as
// they stood on the session it is read on. The pullback and the breakout buy on the setup's own session. A drift's
// setup is anchored on its reaction session, and its rule buys on the first session from the reaction within its
// window where every part passes, or from 20.11 on the first close under the close before it within ten sessions after
// the reaction where the rule states that entry; so a drift setup's live pass is whether the rule buys that reaction on
// one of those sessions, each read as the night reads it on that session, its market check, its floors, its quality and
// its revenue level among them, and nought where no such session comes, where the first close under the one before
// finds its close at or under the stop or any part failing, or where the history read ends before the rule could buy
// it. The night's step writes a reaction's row unpassed where the rule has not bought it by then, and each later night
// inside the rule's reach reads every session since the reaction and passes the row where the rule bought it on one of
// them; the history build reads the sessions after the reaction that its history holds. A family with no live rule standing keeps its provisional reading, and so does one
// whose live rule states a part the ledger reads on no session, which the step's row and the build's words name.
// see: The setup ledger reads each S&P 400 and 600 family's frozen live rule where one stands, and a card draws setups like a pick only once its history is built under that rule
// see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
public static class LedgerLiveRules
{
    // The live swing rule of each family on an index as the night reads the register at an instant: the rules standing
    // then, each at its registration's settings, a rule whose evaluator moved or whose settings left its grid read by
    // none, a family's first live rule in the register's order as the night's page takes it; and a live rule stating a
    // part the ledger reads on no session left unread and named, its family read at its provisional rule.
    // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
    public static LiveRulesRead Of(IReadOnlyList<RegisterRow> register, DateTimeOffset at, string index)
    {
        var quality = QualityFamily.RulesOn(index, register, at)[0];
        var improving = ImprovingFamily.RulesOn(index, register, at)[0];
        var books = LiveRulesRead.None with { Quality = (quality.Rule, quality.Settings), Improving = (improving.Rule, improving.Setting) };

        if (index == IndexFamilies.LargeIndex)
        {
            return books;
        }

        var read = new Dictionary<string, IndexRule>(StringComparer.Ordinal);
        var unread = new List<string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in IndexFamilies.StandingRules(register, at).Rules.Where(rule => rule.Index == index && rule.Live && rule.Evaluator is not IndexHeavyweightCandidate))
        {
            if (!taken.Add(rule.Family))
            {
                continue;
            }

            if (Unread(rule) is { Count: > 0 } parts)
            {
                unread.Add($"the {rule.Family}'s live rule '{rule.Candidate}' states {string.Join(" and ", parts)}, which the ledger reads on no session, so its provisional rule is read");

                continue;
            }

            read[rule.Family] = rule;
        }

        return books with { Read = read, Unread = unread };
    }

    // The parts of a rule the ledger reads on no session, each named, none where it reads every part: the industry's
    // fall, the market switches, the peers' surprise and the tone, which the history the build reads holds no reading of.
    public static IReadOnlyList<string> Unread(IndexRule rule)
    {
        var p = rule.Parameters;
        var unread = new List<string>();

        if (p.GetValueOrDefault(IndexRuleCandidate.IndustryFallParameter) > 0)
        {
            unread.Add("the industry's fall");
        }

        if (p.GetValueOrDefault(IndexRuleCandidate.FundOverSpyParameter) > 0 || p.GetValueOrDefault(IndexRuleCandidate.CreditAverageParameter) > 0 || p.GetValueOrDefault(IndexRuleCandidate.CreditChangeParameter) > 0)
        {
            unread.Add("a market switch");
        }

        if (rule.Evaluator is IndexDriftCandidate && p.GetValueOrDefault(IndexDriftCandidate.PeersParameter) > 0)
        {
            unread.Add("the peers' surprise");
        }

        if (rule.Evaluator is IndexDriftCandidate && ReleaseTone.VersionOf(p) > 0)
        {
            unread.Add("the tone");
        }

        return unread;
    }

    // The words a setup read under a live rule carries: the rule's settings as its registration's name states them after
    // the family on its index.
    public static string Words(IndexRule rule) => IndexRules.Words(rule.Evaluator, rule.Parameters);

    // The sessions after a setup's own on which its family's live rule may still buy it: none for the pullback and the
    // breakout; for a drift buying at its listing its window less one, the wider window where the rule states one; and
    // for a drift buying on the first pullback the ten sessions after the reaction that pullback is read within.
    public static int Reach(IndexRule rule)
    {
        if (rule.Evaluator is not IndexDriftCandidate)
        {
            return 0;
        }

        var p = rule.Parameters;

        if (p.GetValueOrDefault(IndexDriftCandidate.EntryParameter) == 1)
        {
            return DriftSweep.FirstPullbackSessions;
        }

        var window = p[IndexDriftCandidate.WideWindowParameter] > 0 ? p[IndexDriftCandidate.WideWindowParameter] : p[IndexDriftCandidate.WindowSessionsParameter];

        return Math.Max(0, (int)window - 1);
    }

    // The members a live rule passes on the session its inputs are read at, as the night's own read of the rule gives it:
    // listed at the rule's settings with its market check open at its own level and no part of the rule failed; each with
    // the place among the sessions of the setup the pass buys, the session itself, or for a drift its reaction's.
    public static IReadOnlyDictionary<int, int> Passes(IndexNightInputs inputs, IndexRule rule, IndexLevels levels)
    {
        var read = IndexRules.Read(inputs, rule, levels);
        var passed = new Dictionary<int, int>();

        if (!read.Open)
        {
            return passed;
        }

        var reactions = rule.Evaluator is IndexDriftCandidate ? Reactions(inputs, rule.Parameters) : null;

        foreach (var (name, _) in read.Listed)
        {
            if (passed.ContainsKey(name) || read.FailsOn(name) is not null)
            {
                continue;
            }

            if (reactions is null)
            {
                passed[name] = inputs.At;
            }
            else if (reactions.TryGetValue(name, out var reaction))
            {
                passed[name] = reaction;
            }
        }

        return passed;
    }

    // The reaction session each member a drift lists on the session reads, as the night's own drift reads it over every
    // session read open: the reaction its listing on the session reads back to, or from 20.11, where the rule states
    // that entry, the reaction whose first pullback the session is.
    static Dictionary<int, int> Reactions(IndexNightInputs inputs, IReadOnlyDictionary<string, double> p)
    {
        double? window = p[IndexDriftCandidate.WideWindowParameter] > 0 ? p[IndexDriftCandidate.WideWindowParameter] : null;
        var sessions = IndexNightRead.AllOpen(inputs.Sessions);
        var reactions = new Dictionary<int, int>();

        if (p.GetValueOrDefault(IndexDriftCandidate.EntryParameter) == 1)
        {
            var backs = new Dictionary<int, int>();
            var setting = IndexNightRead.Places(DriftSweep.Grid,
            [
                p[IndexDriftCandidate.WindowSessionsParameter],
                p[IndexDriftCandidate.ReactionMovesParameter],
                p[IndexDriftCandidate.VolumeMultipleParameter],
                p[IndexDriftCandidate.TargetMultipleParameter],
            ]);

            IndexNightRead.DriftPullbacks(inputs, setting, p[IndexDriftCandidate.StopFloorParameter], window, sessions, backs);

            foreach (var (name, back) in backs)
            {
                reactions[name] = inputs.At - back;
            }

            return reactions;
        }

        foreach (var reading in new DriftSweep(inputs.Series, inputs.Members).Readings(sessions, inputs.At, window).Where(reading => reading.Session == inputs.At))
        {
            reactions.TryAdd(reading.Name, inputs.Series[reading.Name].SessionAt[reading.Bar - reading.Back]);
        }

        return reactions;
    }

    // A family's setups read under its live rule: each row carrying the rule's words and whether the rule passes it.
    public static FamilySetups Under(FamilySetups family, IndexRule rule, Func<SetupRow, bool> passes)
    {
        var words = Words(rule);

        return family with { Rows = [.. family.Rows.Select(row => row with { Rule = words, LivePass = passes(row) })] };
    }

    // A session's inputs as the history build reads a rule over them: every series but those named read as holding no
    // bar. A family's listings and every part of its rule read a member's own series and the session's breadth and its
    // places among every member, which these inputs keep whole, so those named read as they do over the whole.
    public static IndexNightInputs Masked(IndexNightInputs inputs, IReadOnlySet<int> names, IReadOnlyList<SweepSeries> empty) =>
        inputs with { Series = [.. inputs.Series.Select((one, name) => names.Contains(name) ? one : empty[name])] };

    // A series holding no bar, which a masked name reads as.
    public static SweepSeries Empty(string ticker) =>
        SweepColumns.Series(new SweepName(ticker, [], [], []), new Dictionary<DateOnly, int>());

    // Each member's quarters' revenue as first filed over the facts filed before a day, read as the night's own revenue
    // level reads them, through the filer the store matches it to, and none for a member it matches no filer for: the
    // day is the session before the night's, so a quarter is read once its filing is stored. A member's quarters are read
    // only when its revenue part is.
    // see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
    public static IReadOnlyDictionary<string, IReadOnlyList<QuarterRevenue>> RevenueBefore(IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>? facts, DateOnly before) =>
        new ByTicker<IReadOnlyList<QuarterRevenue>>(
            facts?.Keys ?? [],
            ticker => FirstFiledRevenue.Quarters(facts![ticker]
                .Where(fact => fact.Filed < before)
                .Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))));

    // The levels a live rule reads on a session of the history, each as its sweep reads it over the history: a member's
    // interest cover over its quarters as filed before the session, the S&P 500's breadth as it stood, and each member's
    // revenue as the night reads it; none of the parts the ledger reads on no session.
    public static IndexLevels HistoryLevels(
        IndexRule rule,
        DateOnly day,
        DateOnly before,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IReadOnlyDictionary<string, string?> sectors,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>? facts,
        IReadOnlyCollection<string> tickers,
        double? largeBreadth)
    {
        var p = rule.Parameters;
        IReadOnlyDictionary<string, bool?> coverage = p[IndexRuleCandidate.QualityParameter] == (int)IndexQuality.Cover
            ? new ByTicker<bool?>(tickers, ticker => MemberReadings.Coverage(income.GetValueOrDefault(ticker) ?? [], day, sectors.GetValueOrDefault(ticker)))
            : new Dictionary<string, bool?>(StringComparer.Ordinal);

        return IndexLevels.None with
        {
            Coverage = coverage,
            LargeBreadth = p[IndexRuleCandidate.LargeBreadthParameter] == 1 ? largeBreadth : null,
            Revenue = p.GetValueOrDefault(IndexDriftCandidate.RevenueGrewParameter) == 1 ? RevenueBefore(facts, before) : null,
        };
    }

    // A reading of each member, read the first time it is asked for, over the members it holds one for.
    sealed class ByTicker<TValue>(IEnumerable<string> tickers, Func<string, TValue> reading) : IReadOnlyDictionary<string, TValue>
    {
        readonly HashSet<string> held = new(tickers, StringComparer.Ordinal);
        readonly Dictionary<string, TValue> cache = new(StringComparer.Ordinal);

        public TValue this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys => held;

        public IEnumerable<TValue> Values => held.Select(key => this[key]);

        public int Count => held.Count;

        public bool ContainsKey(string key) => held.Contains(key);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
        {
            if (!held.Contains(key))
            {
                value = default!;

                return false;
            }

            if (!cache.TryGetValue(key, out var known))
            {
                known = reading(key);
                cache[key] = known;
            }

            value = known;

            return true;
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => held.Select(key => new KeyValuePair<string, TValue>(key, this[key])).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
