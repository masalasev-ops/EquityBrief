using EquityBrief.Core.Time;
using EquityBrief.Worker.Families;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Candidates;

// The `register` verb: a candidate condition registered before anything scores it, and a
// retirement. Every attempt is one row on the run log, written by the registrar, a refusal of
// the command line included.
//
// A verb rather than a step, because a registration is a decision a person takes and never
// something a night arrives at: a register that filled itself would be the thing
// pre-registration exists to stop. Its own class rather than a local function in the program,
// so the suite runs the verb a person runs.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
public static class RegisterVerb
{
    public const string Name = "register";

    public const string RunPrefix = "register-";

    // The flag that registers phase 10's three at one instant, which takes nothing else: what
    // would be typed after it is written down in the set it names.
    public const string TheThree = "--the-three";

    // The flag that retires phase 10's three and registers the swing family at one instant.
    public const string TheFamily = "--the-family";

    // The flag that registers again, unchanged and at one instant, every standing candidate whose
    // evaluator a code change moved, on the evidence given.
    public const string Moved = "--moved";

    // The flag that registers the swing family again whole at one instant under the filter version already
    // open, on the evidence given, which is how a rule joins it.
    // see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it
    public const string TheFamilyAgain = "--the-family-again";

    // The flag that freezes a new setup family, its live rule and its variants registered at one instant,
    // naming the family.
    // see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
    public const string Family = "--family";

    // The flag that registers a new setup family again whole at one instant, naming the family, on the evidence
    // given, which is how a rule joins it once its freeze stands.
    // see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed
    public const string FamilyAgain = "--family-again";

    // The flag that freezes a family on the S&P 400 or the S&P 600: its live rule at the parameters given and up to eight
    // variants, each the live rule with the parameters it names moved, registered at one instant.
    // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
    public const string IndexFamily = "--index-family";

    public static IReadOnlyList<VerbForm> Forms { get; } =
    [
        new("--candidate", ["--candidate", "--rule", "--test", "--evaluator"], ["--parameters"], []),
        new("--retire", ["--retire", "--evidence"], [], []),
        new(TheThree, [], [], [TheThree]),
        new(TheFamily, [], [], [TheFamily]),
        new(Moved, ["--evidence"], [], [Moved]),
        new(Family, [Family], [], []),
        new(TheFamilyAgain, ["--evidence"], [], [TheFamilyAgain]),
        new(FamilyAgain, [FamilyAgain, "--evidence"], [], []),
        new(IndexFamily, [IndexFamily, "--index", "--parameters"], ["--variants"], []),
    ];

    // The run id, to the ten-millionth of a second, so two commands a second apart never share one.
    public static string RunIdAt(DateTimeOffset at) => FormattableString.Invariant($"{RunPrefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    public static async Task<int> RunAsync(
        string[] args,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (VerbStore.Refusal(databaseFile) is { } refused)
        {
            await error.WriteLineAsync("register: " + refused);

            return 1;
        }

        var registrar = new CandidateRegistrar(clock, databaseFile);
        var replay = new FamilyReplay(clock, databaseFile);
        var runId = RunIdAt(clock.UtcNow);

        try
        {
            return await FormAsync(args, registrar, replay, runId, clock.UtcNow, output, error);
        }
        catch (SqliteException collided) when (collided.SqliteErrorCode == 19 && collided.Message.Contains("run_log", StringComparison.Ordinal))
        {
            await error.WriteLineAsync(
                "register: another command wrote under the same run id at this instant, and nothing this one asked for was written. Run it again.");

            return 1;
        }
    }

    static async Task<int> FormAsync(
        string[] args,
        CandidateRegistrar registrar,
        FamilyReplay replay,
        string runId,
        DateTimeOffset at,
        TextWriter output,
        TextWriter error)
    {
        var (form, refusal) = VerbArguments.FormOf(args, Forms);

        if (form is null)
        {
            return await RefusedAsync(registrar, runId, refusal!, error);
        }

        string Given(string flag) => VerbArguments.Value(args, flag)!;

        if (form.Flag == "--retire")
        {
            return await Said(await registrar.RetireAsync(Given("--retire"), Given("--evidence"), runId), output, error);
        }

        if (form.Flag == TheFamily)
        {
            return await Said(await registrar.RegisterTheFamilyAsync(runId), output, error);
        }

        // A family rule registered again is replayed first, so its record carries on where every trade is reproduced.
        // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
        if (form.Flag == Moved)
        {
            await ReplayedAsync(await replay.MovedAsync(), output);

            return await Said(await registrar.RegisterMovedAgainAsync(Given("--evidence"), runId), output, error);
        }

        if (form.Flag == TheFamilyAgain)
        {
            return await Said(await registrar.RegisterTheFamilyAgainAsync(Given("--evidence"), runId), output, error);
        }

        if (form.Flag == FamilyAgain)
        {
            await ReplayedAsync(await replay.FamilyAsync(Given(FamilyAgain)), output);

            return await Said(await registrar.RegisterTheSetupFamilyAgainAsync(Given(FamilyAgain), Given("--evidence"), runId), output, error);
        }

        if (form.Flag == TheThree)
        {
            return await Said(await registrar.RegisterTogetherAsync(TheThreeCandidates.All, runId), output, error);
        }

        if (form.Flag == IndexFamily)
        {
            IReadOnlyDictionary<string, double> live;
            IReadOnlyList<IReadOnlyDictionary<string, double>> variants;

            try
            {
                live = VerbArguments.Parameters(Given("--parameters"));
                variants =
                [
                    .. (VerbArguments.Value(args, "--variants") ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(VerbArguments.Parameters),
                ];
            }
            catch (FormatException unreadable)
            {
                return await RefusedAsync(registrar, runId, unreadable.Message, error);
            }

            var (registrations, refused) = Indices.IndexRules.Freeze(Given(IndexFamily), Given("--index"), live, variants, await registrar.RowsAsync(), at);

            return registrations is null
                ? await RefusedAsync(registrar, runId, refused!, error)
                : await Said(await registrar.RegisterTogetherAsync(registrations, runId), output, error);
        }

        if (form.Flag == Family)
        {
            return TheSetupFamilies.For(Given(Family)) is { } family
                ? await Said(await registrar.RegisterTogetherAsync(family, runId), output, error)
                : await RefusedAsync(registrar, runId, $"no freeze is written for a family named '{Given(Family)}'; the families a freeze is written for are {string.Join(", ", TheSetupFamilies.Names)}.", error);
        }

        IReadOnlyDictionary<string, double> parameters;

        try
        {
            parameters = VerbArguments.Parameters(VerbArguments.Value(args, "--parameters"));
        }
        catch (FormatException unreadable)
        {
            return await RefusedAsync(registrar, runId, unreadable.Message, error);
        }

        return await Said(
            await registrar.RegisterAsync(Given("--candidate"), Given("--rule"), Given("--test"), Given("--evaluator"), parameters, runId),
            output,
            error);
    }

    // What each replayed rule's record does, a line a rule.
    static async Task ReplayedAsync(IReadOnlyList<FamilyReplayed> replayed, TextWriter output)
    {
        foreach (var one in replayed)
        {
            await output.WriteLineAsync(one.Reproduced
                ? $"replay: '{one.Candidate}' carries its record on: {one.Said}"
                : $"replay: '{one.Candidate}' restarts its record at this registration: {one.Said}");
        }
    }

    static async Task<int> RefusedAsync(CandidateRegistrar registrar, string runId, string refusal, TextWriter error)
    {
        await registrar.RecordRefusalAsync(runId, refusal);
        await error.WriteLineAsync("register: " + refusal);

        return 1;
    }

    static async Task<int> Said(RegistrationOutcome outcome, TextWriter output, TextWriter error)
    {
        if (outcome.Outcome == CandidateRegistrar.Refused)
        {
            await error.WriteLineAsync("register: " + outcome.Detail);

            return 1;
        }

        await output.WriteLineAsync("register: " + outcome.Detail);

        return 0;
    }
}
