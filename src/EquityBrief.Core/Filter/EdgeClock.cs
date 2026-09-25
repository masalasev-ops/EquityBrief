using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Filter;

// One swing family candidate as the edge clock states it: the filter version it was defined against and
// the settings the live filter has moved since, its non-empty blocks against the floor its first look is
// read at, its resolved setups, and, from the floor on, its share against its own planned break-even and
// its calibrated null.
public sealed record EdgeCandidate(
    string Candidate,
    bool Live,
    string DefinedAgainst,
    IReadOnlyList<string> Moved,
    DateOnly? First,
    int SessionsRun,
    Measured Record);

// One group of the near-miss attribution: the setups one gate or one exclusion alone rejected, or the
// setups the filter admitted, with their record read the way a candidate's is.
public sealed record NearMissGroup(string Group, string Kind, int Rows, Measured Record);

// A swing filter row as the near misses read it: each gate's answer, the exclusions and whether it passed,
// with its own plan's outcome where one was scored.
public sealed record NearMissRow(
    DateOnly Session,
    bool Market,
    bool Trend,
    bool Setup,
    bool Trigger,
    bool Trade,
    IReadOnlyList<string> Exclusions,
    bool Passed,
    CandidateSetup? Outcome);

// The edge clock over the swing family and the near misses beside it, each read off the stored gate rows
// and the outcomes their own plans came to. Nothing here evaluates a gate or recounts a night: what the
// shape clock may recompute from stored readings, the edge clock reads as the night stored it, so no
// setting moved since can change a figure it states.
// owes: The swing family's first look
// see: The swing filter's shape is calibrated over its ordinary nights, a night one cause pushes past a quarter and twice its usual share is left out, and each band spans a third to three times what the ruled filter passes
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: A gate's near misses are the setups it alone rejected, each group read against its own break-even and null and withheld below the block floor
public static class EdgeClock
{
    public const string Admitted = "admitted";
    public const string Gate = "gate";
    public const string Exclusion = "exclusion";

    // The three exclusions, in the order section 11 states them.
    public static IReadOnlyList<string> Exclusions { get; } = [SwingGates.EarningsExclusion, SwingGates.SuspectExclusion, SwingGates.GapExclusion];

    // The sessions from a candidate's first night by which a look can first be read: its blocks all
    // non-empty, the last one closed and its setups' outcome windows closed after it.
    public static int EarliestSessionsFor(int look) =>
        (Looks.At[look] * Blocks.Sessions) - 1 + ForwardReturnSeries.SetupSessionCap;

    // The first look, which can retire a candidate or leave it and cannot promote it, and the second,
    // the earliest a promotion can come at.
    public static int FirstLookSessions => EarliestSessionsFor(0);

    public static int EarliestPromotionSessions => EarliestSessionsFor(1);

    // Each swing family candidate standing at the instant, the live filter first, with its record read
    // over its own setups from the first night that evaluated it.
    public static IReadOnlyList<EdgeCandidate> Candidates(
        IReadOnlyList<RegisterRow> register,
        IReadOnlyDictionary<string, IReadOnlyList<CandidateSetup>> setups,
        IReadOnlyDictionary<string, DateOnly> firstNights,
        DateOnly night,
        DateTimeOffset at)
    {
        var standing = CandidateFamily.Standing(register, at)
            .Where(row => row.Evaluator == SwingFilterRule.EvaluatorName)
            .ToArray();

        var live = standing.LastOrDefault(row => SwingFamily.IsLive(row.Candidate));
        var liveSettings = live is null ? null : Parameters(live.Parameters);

        return
        [
            .. standing
                .OrderBy(row => SwingFamily.IsLive(row.Candidate) ? 0 : 1)
                .ThenBy(row => row.Id)
                .Select(row =>
                {
                    // The version a candidate was defined against is the one the live filter's candidate
                    // registered beside it names, and the settings moved since are those in which that
                    // candidate's parameters and the live one's standing now differ.
                    var defining = register
                        .Where(other => other.Event == CandidateFamily.Registered && SwingFamily.IsLive(other.Candidate) && other.RegisteredAt == row.RegisteredAt)
                        .FirstOrDefault();
                    var definedSettings = defining is null ? null : Parameters(defining.Parameters);
                    var moved = definedSettings is null || liveSettings is null
                        ? []
                        : definedSettings.Keys.Where(key => !liveSettings.TryGetValue(key, out var now) || now != definedSettings[key]).Order(StringComparer.Ordinal).ToArray();

                    var first = firstNights.TryGetValue(row.Candidate, out var opened) ? opened : (DateOnly?)null;

                    return new EdgeCandidate(
                        row.Candidate,
                        SwingFamily.IsLive(row.Candidate),
                        defining is null ? "none" : VersionOf(defining.Candidate),
                        moved,
                        first,
                        first is { } from ? SessionsFrom(from, night) : 0,
                        CandidateRecord.For(setups.GetValueOrDefault(row.Candidate, []), first ?? night, night, ReasonVerdict.Significance));
                }),
        ];
    }

    // The near misses over the rows a filter version stored: the setups the filter admitted, and for each
    // gate and each exclusion the setups it alone rejected, every other gate passing and no other
    // exclusion applying. A row with no plan of its own is counted in its group and scores nothing, which
    // is every row the setup gate rejects, since a member with no setup has no band to stop below.
    public static IReadOnlyList<NearMissGroup> NearMisses(IReadOnlyList<NearMissRow> rows, DateOnly night)
    {
        var first = rows.Count == 0 ? night : rows.Min(row => row.Session);

        bool[] Gates(NearMissRow row) => [row.Market, row.Trend, row.Setup, row.Trigger, row.Trade];

        NearMissGroup Group(string name, string kind, IEnumerable<NearMissRow> members)
        {
            var held = members.ToArray();

            return new NearMissGroup(
                name,
                kind,
                held.Length,
                CandidateRecord.For([.. held.Where(row => row.Outcome is not null).Select(row => row.Outcome!.Value)], first, night, ReasonVerdict.Significance));
        }

        var groups = new List<NearMissGroup>
        {
            Group(Admitted, Admitted, rows.Where(row => row.Passed)),
        };

        for (var at = 0; at < SwingGates.Order.Length; at++)
        {
            var gate = at;

            groups.Add(Group(
                SwingGates.Order[gate],
                Gate,
                rows.Where(row => row.Exclusions.Count == 0 && Gates(row).Select((passed, index) => index == gate ? !passed : passed).All(holds => holds))));
        }

        foreach (var exclusion in Exclusions)
        {
            groups.Add(Group(
                exclusion,
                Exclusion,
                rows.Where(row => Gates(row).All(passed => passed) && row.Exclusions.Count == 1 && row.Exclusions[0] == exclusion)));
        }

        return groups;
    }

    static int SessionsFrom(DateOnly first, DateOnly night) =>
        night <= first ? 0 : ExchangeClosures.SessionsBetween(first, night).Count + (ExchangeClosures.IsSession(night) ? 1 : 0);

    static string VersionOf(string liveCandidate)
    {
        var at = liveCandidate.LastIndexOf("version ", StringComparison.Ordinal);

        return at < 0 ? "none" : liveCandidate[(at + "version ".Length)..];
    }

    static Dictionary<string, double> Parameters(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return document.RootElement.EnumerateObject()
            .Where(value => value.Value.ValueKind == JsonValueKind.Number)
            .ToDictionary(value => value.Name, value => value.Value.GetDouble(), StringComparer.Ordinal);
    }

    public static string Figure(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
