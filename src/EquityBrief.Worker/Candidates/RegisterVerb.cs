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

    // The flag that registers a family's variants on the S&P 400 or the S&P 600 with no live rule, its provisional rule
    // staying the live one: each the provisional setting with the parameters it names moved, registered at one instant.
    // see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
    public const string IndexVariants = "--index-variants";

    // From 20.4 the switch that registers an index family's standing variants again whole with the ones given, on the
    // evidence given, which is how a variant joins them, and from 20.11 beside a frozen family's live rule.
    // see: An S&P 400 or 600 family's variants are registered again whole beside its provisional rule or its frozen live rule
    public const string Again = "--again";

    // The option naming the retired live rule a freeze replaces, which the freeze keeps in the background in the provisional
    // rule's place.
    // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
    public const string Replacing = "--replacing";

    // From 21.8 the flag that registers a family's ranking variant on an index beside its live rule: the live rule's
    // setting with its list ranked over the family's setups at the ledger's loose gates by the order given, score or
    // expected, refused where the family holds nine or the night's step on the index lists no rule by that order.
    // see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
    public const string Ranked = "--ranked";

    // From 21.9 the flag that registers a breakout's or a drift's graded exposure on an index beside its live rule: the
    // live rule's setting with the exposure at the breadth floor its market check reads, refused where the family holds
    // nine, the rule reads no market check, or the family is not one whose walk reads the exposure.
    // see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
    public const string Exposed = "--exposed";

    // From 21.16 the flag that registers an S&P 400 or 600 swing family's order by the expected value of the setups like
    // each candidate beside its live rule: the live rule's setting with its own list ordered by that figure, refused on
    // the S&P 500, whose breakout registers it with its family, for a family the index families' step does not keep, and
    // where the family holds nine.
    // see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
    public const string Ordered = "--ordered";

    // From 21.17 the option that registers the Fundamentals family's live rule on the index it names again at its setting
    // without the flag, the flagged rule standing beside it as a version, refused where the live rule asks no flag or no
    // version stands at that setting.
    // see: The Fundamentals family's live rule on the S&P 400 and 600 asks no flag, the flagged rule standing beside it as a version
    public const string WithoutFlag = "--without-flag";

    // From 21.18 the flag that registers a pullback's or a drift's variant at the target standing as a sell order beside its
    // live rule on the S&P 400 or 600, refused where the family holds nine, sets no target or its live rule stands at an exit
    // of the menu already.
    // see: The target as a sell order standing from the buy is an exit of the menu, and its variant registers where its family has room
    public const string Standing = "--standing";

    // From 23.10 the flag that gives a family's live rule the plan its long test recommends: the live rule registered again
    // with the plan and today's plan beside it as a variant where the family has room, or, with the switch below, the plan
    // registered as a variant beside the live rule, which keeps today's plan.
    // see: At phase 23's restart each price family's live rule takes the plan its long test recommends where it beats today's plan from 2019 above the ruled benchmark, today's plan kept beside it as a variant
    public const string Planned = "--plan";

    public const string VariantOnly = "--variant-only";

    // From 23.10's second part the flag that undoes an approval the operator withdrew: the family's live rule an approved change
    // registered, and the rule it replaced kept in the background, retired, and that earlier rule registered live again, at one
    // instant or not at all.
    // see: An approval the operator withdraws is drawn as withdrawn on their word and undone by the next restart's remedy
    public const string Withdraw = "--withdraw";

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
        new(IndexFamily, [IndexFamily, "--index", "--parameters"], ["--variants", Replacing], []),
        new(IndexVariants, [IndexVariants, "--index", "--variants"], ["--evidence"], [Again]),
        new(Ranked, [Ranked, "--index", "--by"], [], []),
        new(Exposed, [Exposed, "--index"], [], []),
        new(Ordered, [Ordered, "--index"], [], []),
        new(WithoutFlag, [WithoutFlag, "--evidence"], [], []),
        new(Standing, [Standing, "--index"], [], []),
        new(Planned, [Planned, "--combination", "--evidence"], [], [VariantOnly]),
        new(Withdraw, [Withdraw, "--evidence"], [], []),
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

        if (form.Flag == Planned)
        {
            return EquityBrief.Core.Loop.PlanPrices.Named(Given("--combination")) is { } combination
                ? await Said(await registrar.RegisterPlannedAsync(Given(Planned), combination, !args.Contains(VariantOnly, StringComparer.Ordinal), Given("--evidence"), runId), output, error)
                : await RefusedAsync(registrar, runId, $"'{Given("--combination")}' names no plan the test of the buy and sell points walks, so nothing was written.", error);
        }

        if (form.Flag == Withdraw)
        {
            return await Said(await registrar.WithdrawAsync(Given(Withdraw), Given("--evidence"), runId), output, error);
        }

        // A family rule registered again is replayed first, so its record carries on where every trade is reproduced.
        // see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
        if (form.Flag == Moved)
        {
            await ReplayedAsync(await replay.MovedAsync(), output);

            return await Said(await registrar.RegisterMovedAgainAsync(Given("--evidence"), runId), output, error);
        }

        // From 21.17 the Fundamentals family's live rule on the index given registered again at its setting without the
        // flag, the flagged rule standing beside it as a version, on the operator's ruling of 2026-10-31.
        // see: The Fundamentals family's live rule on the S&P 400 and 600 asks no flag, the flagged rule standing beside it as a version
        if (form.Flag == WithoutFlag)
        {
            var (retire, again, refusedAgain) = Improving.ImprovingRules.WithoutFlag(Given(WithoutFlag), await registrar.RowsAsync(), at);

            return again is null
                ? await RefusedAsync(registrar, runId, refusedAgain!, error)
                : await Said(await registrar.ReplaceTogetherAsync(Core.Candidates.CandidateFamily.SetupFamilyOf(again[0].Evaluator), retire!, again, Given("--evidence"), runId), output, error);
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

            // From 22.11 the quality family on any index: its live rule and the grid's settings one dial from it as its
            // versions, which its freeze reads off the grid rather than taking them given.
            // see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
            if (Given(IndexFamily) == Core.Families.QualityRule.Name)
            {
                if (variants.Count > 0 || VerbArguments.Value(args, Replacing) is not null)
                {
                    return await RefusedAsync(registrar, runId, "a quality freeze registers its versions off the history test's grid, one dial from its live rule, and keeps no rule in the background, so it takes neither --variants nor --replacing, and nothing was written.", error);
                }

                var (frozen, refusedFreeze) = Quality.QualityRules.Freeze(Given("--index"), live, await registrar.RowsAsync(), at);

                return frozen is null
                    ? await RefusedAsync(registrar, runId, refusedFreeze!, error)
                    : await Said(await registrar.RegisterTogetherAsync(frozen, runId), output, error);
            }

            // From 22.15 the Fundamentals family on any index: its live rule and the grid's settings one dial from it as its
            // versions, read off the grid as the quality family's are, and no rule kept in the background.
            // see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
            if (Given(IndexFamily) == Core.Families.ImprovingRule.Name)
            {
                if (variants.Count > 0 || VerbArguments.Value(args, Replacing) is not null)
                {
                    return await RefusedAsync(registrar, runId, "a Fundamentals freeze registers its versions off the history test's grid, one dial from its live rule, and keeps no rule in the background, so it takes no --variants and no --replacing, and nothing was written.", error);
                }

                var (improving, refusedImproving) = Improving.ImprovingRules.Freeze(Given("--index"), live, await registrar.RowsAsync(), at);

                return improving is null
                    ? await RefusedAsync(registrar, runId, refusedImproving!, error)
                    : await Said(await registrar.RegisterTogetherAsync(improving, runId), output, error);
            }

            // From 22.3 the short-term dip on any index: its live rule and the versions given, each one of its one-step
            // neighbours on its sweep's grid or its market variant, and no rule kept in the background.
            // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
            if (Given(IndexFamily) == Core.Families.DipRule.Name)
            {
                if (VerbArguments.Value(args, Replacing) is not null)
                {
                    return await RefusedAsync(registrar, runId, "a dip freeze keeps no rule in the background, so it takes no --replacing, and nothing was written.", error);
                }

                var (dips, refusedDips) = Dips.DipRules.Freeze(Given("--index"), live, variants, await registrar.RowsAsync(), at);

                return dips is null
                    ? await RefusedAsync(registrar, runId, refusedDips!, error)
                    : await Said(await registrar.RegisterTogetherAsync(dips, runId), output, error);
            }

            var (registrations, refused) = Indices.IndexRules.Freeze(Given(IndexFamily), Given("--index"), live, variants, await registrar.RowsAsync(), at, VerbArguments.Value(args, Replacing));

            return registrations is null
                ? await RefusedAsync(registrar, runId, refused!, error)
                : await Said(await registrar.RegisterTogetherAsync(registrations, runId), output, error);
        }

        if (form.Flag == IndexVariants)
        {
            IReadOnlyList<IReadOnlyDictionary<string, double>> variants;

            try
            {
                variants =
                [
                    .. Given("--variants")
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(VerbArguments.Parameters),
                ];
            }
            catch (FormatException unreadable)
            {
                return await RefusedAsync(registrar, runId, unreadable.Message, error);
            }

            var again = args.Contains(Again, StringComparer.Ordinal);
            var evidence = VerbArguments.Value(args, "--evidence");

            if (again != (evidence is not null))
            {
                return await RefusedAsync(
                    registrar,
                    runId,
                    again
                        ? "--again retires the variants standing, so it takes --evidence saying why, and nothing was written."
                        : "--evidence is given without --again, and a registration of variants beside the provisional rule retires nothing, so nothing was written.",
                    error);
            }

            if (again)
            {
                var (written, refusedAgain) = Indices.IndexRules.VariantsAgain(Given(IndexVariants), Given("--index"), variants, await registrar.RowsAsync(), at);

                if (written is null)
                {
                    return await RefusedAsync(registrar, runId, refusedAgain!, error);
                }

                // The variants standing are replayed first, as a family registered again is, and the replay says each
                // restarts, since no replay reads an S&P 400's or 600's rule.
                var family = Core.Candidates.IndexRuleCandidate.FamilyOn(Given(IndexVariants), Given("--index"));

                await ReplayedAsync(await replay.FamilyAsync(family), output);

                return await Said(await registrar.RegisterTheIndexVariantsAgainAsync(family, written, evidence!, runId), output, error);
            }

            var (registrations, refused) = Indices.IndexRules.Variants(Given(IndexVariants), Given("--index"), variants, await registrar.RowsAsync(), at);

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

        if (form.Flag == Exposed)
        {
            var (exposure, refusedExposure) = ExposedVariant(Given(Exposed), Given("--index"), await registrar.RowsAsync(), at);

            return exposure is null
                ? await RefusedAsync(registrar, runId, refusedExposure!, error)
                : await Said(await registrar.RegisterAsync(exposure.Candidate, exposure.Rule, exposure.Test, exposure.Evaluator, exposure.Parameters, runId), output, error);
        }

        if (form.Flag == Standing)
        {
            var (standing, refusedStanding) = StandingVariant(Given(Standing), Given("--index"), await registrar.RowsAsync(), at);

            return standing is null
                ? await RefusedAsync(registrar, runId, refusedStanding!, error)
                : await Said(await registrar.RegisterAsync(standing.Candidate, standing.Rule, standing.Test, standing.Evaluator, standing.Parameters, runId), output, error);
        }

        if (form.Flag == Ordered)
        {
            var (ordered, refusedOrder) = OrderedVariant(Given(Ordered), Given("--index"), await registrar.RowsAsync(), at);

            return ordered is null
                ? await RefusedAsync(registrar, runId, refusedOrder!, error)
                : await Said(await registrar.RegisterAsync(ordered.Candidate, ordered.Rule, ordered.Test, ordered.Evaluator, ordered.Parameters, runId), output, error);
        }

        if (form.Flag == Ranked)
        {
            var (ranking, refusedRanking) = RankedVariant(Given(Ranked), Given("--index"), Given("--by"), await registrar.RowsAsync(), at);

            return ranking is null
                ? await RefusedAsync(registrar, runId, refusedRanking!, error)
                : await Said(await registrar.RegisterAsync(ranking.Candidate, ranking.Rule, ranking.Test, ranking.Evaluator, ranking.Parameters, runId), output, error);
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

    // From 21.8 a family's ranking variant on an index: its live rule's name without the word live, its words and its
    // test, its evaluator and its setting with the ranking by the order given; or why none can be registered.
    // see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
    public static (Registration? Registration, string? Refusal) RankedVariant(string family, string index, string by, IReadOnlyList<Core.Candidates.RegisterRow> register, DateTimeOffset at)
    {
        var order = by switch
        {
            "score" => Core.Loop.RankingOrder.Score,
            "expected" => Core.Loop.RankingOrder.Expected,
            _ => Core.Loop.RankingOrder.None,
        };

        if (order == Core.Loop.RankingOrder.None)
        {
            return (null, $"'--by {by}' names no order; a ranking is by 'score', the index's newest learned score, or by 'expected', the expected value of the setups like each.");
        }

        if (!Loop.RankingProcedures.Approvable(index, family, order))
        {
            return (null, $"the night's step on the {Cards.DecisionCards.NameOf(index)} lists no {family} rule ranked by {Core.Loop.RankingChoice.Words(order)}, so none is registered.");
        }

        if (LiveOn(register, family, index, at) is not { } live)
        {
            return (null, $"no live {family} rule stands registered on the {Cards.DecisionCards.NameOf(index)}, so there is no rule to rank.");
        }

        var parameters = new Dictionary<string, double>(Core.Candidates.CandidateEvaluator.Read(live.Parameters), StringComparer.Ordinal)
        {
            [Core.Loop.RuleHooks.RankingParameter] = Core.Loop.RankingChoice.HookOf(order)!.Value,
        };
        var words = Loop.RankingProcedures.ChangeOf(order);

        return (new Registration("the " + live.Candidate[Core.Candidates.FamilyRecords.LivePrefix.Length..] + ", " + words, live.Rule, live.Test, live.Evaluator, parameters), null);
    }

    // A family's live rule standing on an index, none where none stands.
    static Core.Candidates.RegisterRow? LiveOn(IReadOnlyList<Core.Candidates.RegisterRow> register, string family, string index, DateTimeOffset at)
    {
        static bool OnIndex(Core.Candidates.CandidateEvaluator? evaluator, string family, string index) => evaluator switch
        {
            Core.Candidates.IndexRuleCandidate rule => rule.SetupFamily == family && rule.Index == index,
            Core.Candidates.OwnStepCandidate rule => rule.SetupFamily == family && rule.Index == index,
            Core.Candidates.FamilyRuleEvaluator rule => index == Loop.WalkForwardTester.LargeIndex && rule.Family == family,
            _ => false,
        };

        return Core.Candidates.CandidateFamily.Standing(register, at).FirstOrDefault(row => Core.Candidates.FamilyRecords.IsLive(row.Candidate) && OnIndex(Core.Candidates.CandidateEvaluators.Find(row.Evaluator), family, index));
    }

    // From 21.9 a breakout's or a drift's graded exposure on an index: its live rule's name without the word live and the
    // exposure's words after it, its words and its test, its evaluator and its setting with the exposure at the breadth
    // floor its market check reads; or why none can be registered.
    // see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
    public static (Registration? Registration, string? Refusal) ExposedVariant(string family, string index, IReadOnlyList<Core.Candidates.RegisterRow> register, DateTimeOffset at)
    {
        if (!Loop.ExposureProcedures.Families.Contains(family, StringComparer.Ordinal))
        {
            return (null, $"the per-rule test's walk of the {family} reads no exposure, so none is registered; the exposure is offered to the {string.Join(" and the ", Loop.ExposureProcedures.Families)}.");
        }

        if (LiveOn(register, family, index, at) is not { } live)
        {
            return (null, $"no live {family} rule stands registered on the {Cards.DecisionCards.NameOf(index)}, so there is no rule to grade.");
        }

        var p = Core.Candidates.CandidateEvaluator.Read(live.Parameters);

        if (Loop.ExposureProcedures.FloorOf(Core.Candidates.CandidateEvaluators.Find(live.Evaluator), p) is not { } floor)
        {
            return (null, $"the live {family} rule on the {Cards.DecisionCards.NameOf(index)} reads no market check, so it has none for the exposure to stand in the place of.");
        }

        return (new Registration("the " + live.Candidate[Core.Candidates.FamilyRecords.LivePrefix.Length..] + ", " + Loop.ExposureProcedures.WordsOf(floor), live.Rule, live.Test, live.Evaluator, Loop.ExposureProcedures.Exposed(p, floor)), null);
    }

    // From 21.16 an S&P 400 or 600 swing family's order by the expected value on an index: its live rule's name without the
    // word live and the order's words after it, its words and its test, its evaluator and its setting with the order on;
    // or why none can be registered.
    // see: An order by the expected value of the setups like each pick is registered where its family has room and its step can hand the figure, and walked unregistered elsewhere
    public static (Registration? Registration, string? Refusal) OrderedVariant(string family, string index, IReadOnlyList<Core.Candidates.RegisterRow> register, DateTimeOffset at)
    {
        if (index == Loop.WalkForwardTester.LargeIndex)
        {
            return (null, "the S&P 500's breakout registers its order by the expected value with its family, by '--family-again breakout', and its pullback and drift hold nine rules.");
        }

        if (LiveOn(register, family, index, at) is not { } live)
        {
            return (null, $"no live {family} rule stands registered on the {Cards.DecisionCards.NameOf(index)}, so there is no rule to order.");
        }

        if (Core.Candidates.CandidateEvaluators.Find(live.Evaluator) is not Core.Candidates.IndexRuleCandidate or Core.Candidates.IndexHeavyweightCandidate)
        {
            return (null, $"the index families' step does not list the {family} on the {Cards.DecisionCards.NameOf(index)} by a hook, so none is registered; the order is offered to the pullback, the breakout and the drift on the S&P 400 and 600.");
        }

        var parameters = new Dictionary<string, double>(Core.Candidates.CandidateEvaluator.Read(live.Parameters), StringComparer.Ordinal)
        {
            [Core.Loop.RuleHooks.ExpectedOrderParameter] = 1,
        };
        var words = Core.Loop.RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [Core.Loop.RuleHooks.ExpectedOrderParameter] = 1 }).Words()!;

        return (new Registration("the " + live.Candidate[Core.Candidates.FamilyRecords.LivePrefix.Length..] + ", " + words, live.Rule, live.Test, live.Evaluator, parameters), null);
    }

    // From 21.18 a family's variant at the target standing as a sell order on an index: its live rule's name without the
    // word live and the exit's words after it, its words and its test, its evaluator and its setting with the menu's
    // standing exit; or why none can be registered: an index or a family the standing target does not reach, no live
    // rule, or a live rule already standing at an exit of the menu. A family at nine is refused by the register.
    // see: The target as a sell order standing from the buy is an exit of the menu, and its variant registers where its family has room
    public static (Registration? Registration, string? Refusal) StandingVariant(string family, string index, IReadOnlyList<Core.Candidates.RegisterRow> register, DateTimeOffset at)
    {
        if (index == Loop.WalkForwardTester.LargeIndex)
        {
            return (null, "the S&P 500's pullback and drift hold nine rules and its breakout trails, so the per-rule test walks the standing target beside them unregistered.");
        }

        if (!Loop.ExitProcedures.StandsFor(index, family))
        {
            return (null, $"the {family} sets no target the standing order could rest at, so none is registered; it is offered to the pullback and the drift.");
        }

        if (LiveOn(register, family, index, at) is not { } live)
        {
            return (null, $"no live {family} rule stands registered on the {Cards.DecisionCards.NameOf(index)}, so there is no rule to stand the target of.");
        }

        var parameters = new Dictionary<string, double>(Core.Candidates.CandidateEvaluator.Read(live.Parameters), StringComparer.Ordinal);

        if (parameters.GetValueOrDefault(Core.Loop.RuleHooks.ExitParameter) > 0)
        {
            return (null, $"the live {family} rule on the {Cards.DecisionCards.NameOf(index)} stands at an exit of the menu already, which the standing target would replace, so none is registered.");
        }

        var standing = Core.Loop.ExitMenu.Swing.Single(choice => choice.Kind == Core.Loop.ExitKind.Standing);

        parameters[Core.Loop.RuleHooks.ExitParameter] = standing.Number;

        return (new Registration("the " + live.Candidate[Core.Candidates.FamilyRecords.LivePrefix.Length..] + ", " + standing.Words, live.Rule, live.Test, live.Evaluator, parameters), null);
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
