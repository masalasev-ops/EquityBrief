using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The sweep as one long-running process that looks after itself.
//
// It reads the history once, when computing starts, and fixes its end there. It then works in chunks, each saved
// to a file under the run's folder beside the store and never in it: the candidates by blocks of sessions with
// their benchmarks, the point-in-time check, stage 1's ranking by blocks of designs, step (b)'s trials, step
// (c)'s crossings by blocks, and stage 2 by design. A process started again reads what the saved chunks hold
// and goes on from the first one missing, under the build that started the run and no other, and a finished run
// is never written again. Before each chunk it waits: while a night, the overnight queue or a drain holds its
// lock file, and on a weekday from 23:00 UTC, or from earlier where the chunk would run past it, until the night
// that falls then has taken its lock and let it go. A chunk that fails is tried once more, and failing again the
// run stops and the report says where and why. The first chunks of each stage are timed, a run whose projection
// passes five days stops after stage 1 and reports rather than running on, and stage 2's sample shrinks where
// the whole run would pass five days, never its dials.
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class SweepRunner
{
    public const int FirstSessions = 20;
    public const int SessionsPerChunk = 100;
    public const int FirstDesigns = 50;
    public const int DesignsPerChunk = 50;
    public const int CrossingsPerChunk = 50;
    public const int CarriedForward = SweepSearch.Carried;

    // The night's pause: from 23:00 UTC on a weekday, and the latest the run waits for a night that never came.
    public static readonly TimeSpan PauseFrom = TimeSpan.FromHours(23);
    public static readonly TimeSpan GiveUpWaitingAt = TimeSpan.FromHours(6);
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(60);
    public const int ClearPolls = 2;
    public static readonly TimeSpan Longest = TimeSpan.FromDays(5);

    readonly IClock clock;
    readonly string dataRoot;
    readonly string databaseFile;
    readonly string folder;
    readonly Func<TimeSpan, CancellationToken, Task> wait;
    readonly TextWriter log;

    public SweepRunner(IClock clock, string dataRoot, string databaseFile, string folder, TextWriter log, Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        this.clock = clock;
        this.dataRoot = dataRoot;
        this.databaseFile = databaseFile;
        this.folder = folder;
        this.log = log;
        this.wait = wait ?? Task.Delay;
    }

    string Of(params string[] parts) => Path.Combine([folder, .. parts]);

    // The build a run is computed by, which a run started again must match: the module's own id, new on every
    // build, so a saved chunk is never joined by one computed by other code.
    public static string BuildId => typeof(SweepRunner).Assembly.ManifestModule.ModuleVersionId.ToString("n");

    // The run's saved state: the history's end, its fingerprint, the build, the chunks done, the timings and
    // anything that failed.
    public sealed class State
    {
        public string? Through { get; set; }

        public string? Fingerprint { get; set; }

        public string? Build { get; set; }

        public DateTimeOffset? Started { get; set; }

        public int CandidateChunks { get; set; }

        public bool CandidatesDone { get; set; }

        public bool PointInTimeDone { get; set; }

        public int PointInTimeDifferences { get; set; }

        public int RankChunks { get; set; }

        public bool RanksDone { get; set; }

        public bool TrialsDone { get; set; }

        public int CrossChunks { get; set; }

        public bool CrossDone { get; set; }

        public List<string> SearchDone { get; set; } = [];

        public bool Finished { get; set; }

        public bool StoppedAfterStageOne { get; set; }

        public bool StoppedAtPointInTime { get; set; }

        public double SampleBudgetSeconds { get; set; } = SweepSearch.SampleBudget.TotalSeconds;

        public Dictionary<string, double> Seconds { get; set; } = [];

        public List<string> Timings { get; set; } = [];

        public List<string> Failures { get; set; } = [];

        public List<string> Pauses { get; set; } = [];

        public List<string> Decisions { get; set; } = [];

        public double? ProjectedHours { get; set; }
    }

    public State Load()
    {
        var path = Of("state.json");

        return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json) ?? new State() : new State();
    }

    void Save(State state)
    {
        var path = Of("state.json");
        var next = path + ".next";

        File.WriteAllText(next, JsonSerializer.Serialize(state, Json));
        File.Move(next, path, overwrite: true);
    }

    void Say(string line)
    {
        var stamped = FormattableString.Invariant($"{clock.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {line}");

        log.WriteLine(stamped);
        File.AppendAllText(Of("sweep.log"), stamped + Environment.NewLine);
    }

    // The options every saved file is written and read with: a figure a setting has none of is NaN, which the
    // serializer refuses unless told to write it by name.
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Of(SweepFolder.CandidatesFolder));
        Directory.CreateDirectory(Of("ranks"));
        Directory.CreateDirectory(Of("cross"));
        Directory.CreateDirectory(Of("search"));

        var state = Load();

        if (state.Finished)
        {
            Say("this run finished and is never written again; start another run to sweep again");

            return 2;
        }

        if (state.Build is { } build && build != BuildId)
        {
            Say(FormattableString.Invariant($"this run was started by build {build} and this is build {BuildId}; a run goes on under the build that started it, so start another run"));

            return 2;
        }

        try
        {
            return await RunStagesAsync(state, cancellation);
        }
        catch (SweepStopped stopped)
        {
            File.WriteAllText(Of(SweepFolder.ReportFile), SweepReport.Stopped(state, stopped.Message));
            Say("the run stopped and its report says where: " + Of(SweepFolder.ReportFile));

            return 1;
        }
    }

    async Task<int> RunStagesAsync(State state, CancellationToken cancellation)
    {
        await WaitForTheStoreAsync(state, TimeSpan.Zero, cancellation);

        var history = new SweepHistory(databaseFile);
        var through = state.Through is { } fixedEnd
            ? DateOnly.ParseExact(fixedEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : await history.NewestSessionAsync(cancellation);

        Say(FormattableString.Invariant($"reading the history through {through:yyyy-MM-dd}"));

        var watch = Stopwatch.StartNew();
        var inputs = await history.ReadAsync(through, Say, cancellation);

        state.Seconds["read"] = watch.Elapsed.TotalSeconds;

        if (state.Fingerprint is { } held && held != inputs.Fingerprint && !state.CandidatesDone)
        {
            // The store's history moved under a run started before it, a refetch after a corporate action being
            // the one way it can: the candidates are computed again from the start rather than joined.
            Say("the history read differs from the one the saved candidates were computed over; computing them again");
            state.CandidateChunks = 0;

            foreach (var file in Directory.GetFiles(Of(SweepFolder.CandidatesFolder)))
            {
                File.Delete(file);
            }
        }

        state.Through = through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        state.Fingerprint ??= inputs.Fingerprint;
        state.Build ??= BuildId;
        state.Started ??= clock.UtcNow;
        Save(state);

        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;

        // The series are needed by the candidates and by the point-in-time check, so they are computed on every
        // start; they take seconds.
        watch.Restart();
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, Parallelism(), name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);

        state.Seconds["series"] = watch.Elapsed.TotalSeconds;
        Say(FormattableString.Invariant($"series and cross-sections read over {inputs.Names.Count} name(s) and {calendar.Length} session(s) in {watch.Elapsed.TotalSeconds:0} s"));

        List<SweepCandidate> candidates;

        if (state.CandidatesDone)
        {
            candidates = ReadCandidates();
        }
        else
        {
            var chunks = CandidateChunks(firstScored, calendar.Length);

            for (var chunk = state.CandidateChunks; chunk < chunks.Count; chunk++)
            {
                var (from, to) = chunks[chunk];
                var chunkWatch = Stopwatch.StartNew();

                await WaitForTheStoreAsync(state, EstimateFor("candidates", state), cancellation);

                await RetriedAsync(state, FormattableString.Invariant($"candidates for sessions {calendar[from]:yyyy-MM-dd} to {calendar[to - 1]:yyyy-MM-dd}"), () =>
                {
                    var found = new List<SweepCandidate>[series.Length];

                    Parallel.For(0, series.Length, Parallelism(), name => found[name] = SweepCandidates.For(series[name], name, sessions, calendar, firstScored, from, to));

                    var all = found.SelectMany(list => list).OrderBy(one => one.Session).ThenBy(one => one.Name).ToArray();

                    SweepBenchmark.Fill(all, series, members, Parallelism().MaxDegreeOfParallelism);

                    using var file = File.Create(Of(SweepFolder.CandidatesFolder, FormattableString.Invariant($"chunk-{chunk:0000}.bin")));
                    using var writer = new BinaryWriter(file);

                    writer.Write(all.Length);

                    foreach (var candidate in all)
                    {
                        candidate.Write(writer);
                    }

                    return Task.CompletedTask;
                });

                state.CandidateChunks = chunk + 1;
                state.Seconds["candidates"] = state.Seconds.GetValueOrDefault("candidates") + chunkWatch.Elapsed.TotalSeconds;

                if (chunk == 0)
                {
                    var perSession = chunkWatch.Elapsed.TotalSeconds / (to - from);
                    var hours = perSession * (calendar.Length - firstScored) / 3600;

                    state.Timings.Add(FormattableString.Invariant($"the candidates with their benchmarks over the first {to - from} sessions took {chunkWatch.Elapsed.TotalSeconds:0.0} s, {perSession:0.00} s a session, projecting {hours:0.0} hours over the {calendar.Length - firstScored} scored sessions"));
                }

                Save(state);
                Say(FormattableString.Invariant($"candidates chunk {chunk + 1} of {chunks.Count} in {chunkWatch.Elapsed.TotalSeconds:0} s"));
            }

            state.CandidatesDone = true;
            Save(state);
            candidates = ReadCandidates();
        }

        Say(FormattableString.Invariant($"{candidates.Count} candidate(s) held"));

        // The point-in-time check, before stage 1, and the run stops on any difference.
        PointInTimeResult pointInTime;

        if (state.PointInTimeDone && File.Exists(Of("point-in-time.json")))
        {
            pointInTime = JsonSerializer.Deserialize<PointInTimeResult>(File.ReadAllText(Of("point-in-time.json")), Json)!;
        }
        else
        {
            var checkWatch = Stopwatch.StartNew();
            var samples = SweepPointInTime.Sample(series, candidates, calendar, inputs.LiveListed, SweepSearch.Seed);
            var scratch = Path.Combine(Path.GetTempPath(), "equitybrief-sweep", SweepFolder.RunName(state.Started!.Value));

            Say(FormattableString.Invariant($"rebuilding {samples.Count} name-session(s) with the night's own components, {inputs.LiveListed.Count} of them the live list's"));

            pointInTime = await new SweepPointInTime(scratch, Parallelism().MaxDegreeOfParallelism).CheckAsync(series, samples, inputs.LiveListed.Count, cancellation);

            File.WriteAllText(Of("point-in-time.json"), JsonSerializer.Serialize(pointInTime, Json));
            state.PointInTimeDone = true;
            state.PointInTimeDifferences = pointInTime.Differences.Count;
            state.Seconds["point-in-time"] = checkWatch.Elapsed.TotalSeconds;
            state.Timings.Add(FormattableString.Invariant($"the point-in-time check rebuilt {pointInTime.Compared} name-session(s) in {checkWatch.Elapsed.TotalSeconds:0.0} s and found {pointInTime.Differences.Count} difference(s)"));
            Save(state);
            Say(state.Timings[^1]);
        }

        if (!pointInTime.Clean)
        {
            state.StoppedAtPointInTime = true;
            Save(state);
            File.WriteAllText(Of(SweepFolder.ReportFile), SweepReport.StoppedAtPointInTime(state, pointInTime));
            Say("the sweep did not read every session as it stood; the run stops before stage 1 and the report lists every difference: " + Of(SweepFolder.ReportFile));

            return 1;
        }

        // Stage 1.
        var selections = SweepAxes.Designs(selectionOnly: true);
        var rankChunks = (selections.Count + DesignsPerChunk - 1) / DesignsPerChunk;

        for (var chunk = state.RankChunks; chunk < rankChunks && !state.RanksDone; chunk++)
        {
            var slice = selections.Skip(chunk * DesignsPerChunk).Take(DesignsPerChunk).ToArray();
            var chunkWatch = Stopwatch.StartNew();

            await WaitForTheStoreAsync(state, EstimateFor("ranks", state), cancellation);

            await RetriedAsync(state, FormattableString.Invariant($"stage 1 designs {(chunk * DesignsPerChunk) + 1} to {(chunk * DesignsPerChunk) + slice.Length}"), () =>
            {
                var ranks = new IReadOnlyList<SweepDesignRank>[slice.Length];

                Parallel.For(0, slice.Length, Parallelism(), at => ranks[at] = SweepStages.Rank(candidates, slice[at], nights));

                File.WriteAllText(Of("ranks", FormattableString.Invariant($"chunk-{chunk:0000}.json")), JsonSerializer.Serialize(ranks.SelectMany(list => list).Select(RankRow.Of).ToArray(), Json));

                return Task.CompletedTask;
            });

            state.RankChunks = chunk + 1;
            state.Seconds["stage1"] = state.Seconds.GetValueOrDefault("stage1") + chunkWatch.Elapsed.TotalSeconds;

            if (chunk == 0)
            {
                var perDesign = chunkWatch.Elapsed.TotalSeconds / slice.Length;

                state.Timings.Add(FormattableString.Invariant($"stage 1 over its first {slice.Length} selection designs, each with its 8 exits and 19,683 coarse settings walked with one open trade a stock, took {chunkWatch.Elapsed.TotalSeconds:0.0} s, {perDesign:0.00} s a design, projecting {perDesign * selections.Count / 3600:0.0} hours over the {selections.Count} selection designs"));
                state.ProjectedHours = ((state.Seconds.GetValueOrDefault("read") + state.Seconds.GetValueOrDefault("series") + state.Seconds.GetValueOrDefault("candidates") + state.Seconds.GetValueOrDefault("point-in-time")) / 3600)
                    + (perDesign * selections.Count / 3600);
            }

            Save(state);
            Say(FormattableString.Invariant($"stage 1 chunk {chunk + 1} of {rankChunks} in {chunkWatch.Elapsed.TotalSeconds:0} s"));
        }

        state.RanksDone = true;
        Save(state);

        var rows = ReadRanks();

        if (state.ProjectedHours is { } projected && TimeSpan.FromHours(projected) > Longest)
        {
            state.StoppedAfterStageOne = true;
            state.Finished = true;
            Save(state);
            Say(FormattableString.Invariant($"the run projects {projected:0.0} hours, past five days, so it stops after stage 1"));

            var stoppedReport = SweepReport.Build(inputs, candidates, rows, [], [], [], [], [], pointInTime, state, nights, calendar, firstScored);

            File.WriteAllText(Of(SweepFolder.ReportFile), stoppedReport);
            Say("the report is written: " + Of(SweepFolder.ReportFile));

            return 0;
        }

        // Step (b): each condition setting alone on the ten strongest designs at their coarse centres.
        var strongest = SweepStages.Strongest(rows, SweepSearch.DesignsTried);
        IReadOnlyList<ConditionTrial> trials;
        IReadOnlyList<ConditionVerdict> verdicts;

        if (state.TrialsDone && File.Exists(Of("conditions.json")))
        {
            var saved = JsonSerializer.Deserialize<ConditionsFile>(File.ReadAllText(Of("conditions.json")), Json)!;

            trials = saved.Trials;
            verdicts = saved.Verdicts;
        }
        else
        {
            var stepWatch = Stopwatch.StartNew();
            var found = new List<ConditionTrial>();

            await WaitForTheStoreAsync(state, TimeSpan.FromMinutes(10), cancellation);

            await RetriedAsync(state, "step (b), each condition setting alone on the ten strongest designs", () =>
            {
                var perDesign = new List<ConditionTrial>[strongest.Length];

                Parallel.For(0, strongest.Length, Parallelism(), at =>
                {
                    var design = strongest[at].Design;
                    var picks = SweepStages.Picks(candidates, design);
                    var centre = SweepSearch.CoarseCentre(picks, design, nights);
                    var baseline = SweepStages.Measures(picks, design, centre, ConditionSetting.Off, nights);

                    perDesign[at] = [.. SweepConditions.Settings.Select(pair => SweepSearch.Trial(picks, design, centre, pair.Condition, pair.Setting, baseline, nights))];
                });

                found.AddRange(perDesign.SelectMany(list => list));

                return Task.CompletedTask;
            });

            trials = found;
            verdicts = SweepSearch.Verdicts(trials);
            File.WriteAllText(Of("conditions.json"), JsonSerializer.Serialize(new ConditionsFile(trials, verdicts), Json));
            state.TrialsDone = true;
            state.Seconds["step-b"] = stepWatch.Elapsed.TotalSeconds;
            state.Timings.Add(FormattableString.Invariant($"step (b), {SweepConditions.Settings.Count} condition settings on {strongest.Length} designs, {trials.Count} trials, took {stepWatch.Elapsed.TotalSeconds:0.0} s"));
            Save(state);
            Say(state.Timings[^1]);
        }

        var survivors = verdicts.Where(verdict => verdict.Survives).Select(verdict => verdict.Condition).ToArray();

        Say(FormattableString.Invariant($"{survivors.Length} condition(s) survive step (b): {string.Join(", ", survivors.Select(SweepConditions.Name))}"));

        // Step (c): every survivor on and off at its middle, crossed with the ten designs and their structural
        // neighbours over the coarse settings.
        var combinations = SweepSearch.Combinations(survivors);
        var crossings = strongest
            .Select(row => row.Design.Selection)
            .SelectMany(selection => new[] { selection }.Concat(SweepSearch.SelectionNeighbours(selection)))
            .Distinct()
            .SelectMany(selection => combinations.Select(combination => (Selection: selection, Combination: combination)))
            .ToArray();
        var crossChunks = (crossings.Length + CrossingsPerChunk - 1) / CrossingsPerChunk;

        for (var chunk = state.CrossChunks; chunk < crossChunks && !state.CrossDone; chunk++)
        {
            var slice = crossings.Skip(chunk * CrossingsPerChunk).Take(CrossingsPerChunk).ToArray();
            var chunkWatch = Stopwatch.StartNew();

            await WaitForTheStoreAsync(state, EstimateFor("cross", state), cancellation);

            await RetriedAsync(state, FormattableString.Invariant($"step (c) crossings {(chunk * CrossingsPerChunk) + 1} to {(chunk * CrossingsPerChunk) + slice.Length}"), () =>
            {
                var results = new IReadOnlyList<CombinationRow>[slice.Length];

                Parallel.For(0, slice.Length, Parallelism(), at => results[at] = SweepSearch.Cross(candidates, slice[at].Selection, slice[at].Combination, nights));

                File.WriteAllText(Of("cross", FormattableString.Invariant($"chunk-{chunk:0000}.json")), JsonSerializer.Serialize(results.SelectMany(list => list).ToArray(), Json));

                return Task.CompletedTask;
            });

            state.CrossChunks = chunk + 1;
            state.Seconds["step-c"] = state.Seconds.GetValueOrDefault("step-c") + chunkWatch.Elapsed.TotalSeconds;

            if (chunk == 0)
            {
                var perCrossing = chunkWatch.Elapsed.TotalSeconds / slice.Length;

                state.Timings.Add(FormattableString.Invariant($"step (c) over its first {slice.Length} crossings of a selection design and a combination, {combinations.Count} combination(s) of {survivors.Length} survivor(s), took {chunkWatch.Elapsed.TotalSeconds:0.0} s, {perCrossing:0.00} s a crossing, projecting {perCrossing * crossings.Length / 3600:0.0} hours over the {crossings.Length} crossings"));
            }

            Save(state);
            Say(FormattableString.Invariant($"step (c) chunk {chunk + 1} of {crossChunks} in {chunkWatch.Elapsed.TotalSeconds:0} s"));
        }

        state.CrossDone = true;
        Save(state);

        var crossRows = ReadCross();
        var carried = SweepSearch.StrongestRows(crossRows, CarriedForward);

        // Stage 2, by design, the sample's budget shrunk where the whole run would pass five days.
        var results = new List<SweepDesignResult>();
        var liveMeasures = SweepStages.Direct(candidates, SweepDesign.Live, SweepGrid.Extended.Carry(SweepGrid.Fine, DialSetting.LiveOnFine), ConditionSetting.Off, nights);

        for (var at = 0; at < carried.Count; at++)
        {
            var row = carried[at];
            var design = RankRow.Parse(row.DesignKey);
            var combination = ConditionSetting.FromIndexes(row.Combination.Split(',').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray());
            var saved = Of("search", FormattableString.Invariant($"design-{at}.json"));

            if (state.SearchDone.Contains(row.DesignKey + "@" + row.Combination) && File.Exists(saved))
            {
                results.Add(JsonSerializer.Deserialize<SweepDesignResult>(File.ReadAllText(saved), Json)!);

                continue;
            }

            var elapsedHours = (clock.UtcNow - state.Started!.Value).TotalHours;
            var left = Longest.TotalHours - elapsedHours;
            var designsLeft = carried.Count - at;

            if (left / designsLeft < state.SampleBudgetSeconds / 3600)
            {
                state.SampleBudgetSeconds = Math.Max(60, left / designsLeft * 3600);
                state.Decisions.Add(FormattableString.Invariant($"the sample's budget shrank to {state.SampleBudgetSeconds / 3600:0.00} hours a design before design {at + 1}, since the whole run would have passed five days; the dials and the conditions are unchanged"));
                Save(state);
                Say(state.Decisions[^1]);
            }

            var designWatch = Stopwatch.StartNew();

            await WaitForTheStoreAsync(state, TimeSpan.FromSeconds(state.SampleBudgetSeconds) + TimeSpan.FromMinutes(30), cancellation);

            SweepDesignResult? result = null;

            await RetriedAsync(state, "stage 2 for " + row.DesignKey + " under " + row.Combination, () =>
            {
                result = Search(candidates, design, combination, liveMeasures, nights, TimeSpan.FromSeconds(state.SampleBudgetSeconds));
                File.WriteAllText(saved, JsonSerializer.Serialize(result, Json));

                return Task.CompletedTask;
            });

            results.Add(result!);
            state.Seconds["stage2"] = state.Seconds.GetValueOrDefault("stage2") + designWatch.Elapsed.TotalSeconds;
            state.Timings.Add(FormattableString.Invariant($"stage 2's design {at + 1}, {result!.SampleSize:N0} sampled settings of {result.GridSize:N0} at {result.PerPointSeconds * 1000:0.00} ms each and {result.Evaluations:N0} evaluations in all, took {designWatch.Elapsed.TotalSeconds:0.0} s"));
            state.SearchDone.Add(row.DesignKey + "@" + row.Combination);
            Save(state);
            Say(state.Timings[^1]);
        }

        state.Finished = true;
        Save(state);

        var report = SweepReport.Build(inputs, candidates, rows, trials, verdicts, crossRows, carried, results, pointInTime, state, nights, calendar, firstScored);

        File.WriteAllText(Of(SweepFolder.ReportFile), report);
        Say("the report is written: " + Of(SweepFolder.ReportFile));

        return 0;
    }

    // Stage 2 for one design: the coarse settings under its combination, the sample within the budget, the line
    // fixed, the leaders' depth, the proposal, its refinement, the look beyond a grid end and the slices.
    public static SweepDesignResult Search(IReadOnlyList<SweepCandidate> candidates, SweepDesign design, ConditionSetting combination, SweepMeasures live, int nights, TimeSpan budget)
    {
        var space = SweepSpace.For(combination.On);
        var picks = SweepStages.Picks(candidates, design);
        var search = new SweepDesignSearch(design, picks, nights, space);
        var parallelism = Parallelism().MaxDegreeOfParallelism;
        var notes = new List<string>();

        search.EvaluateCoarse(combination, parallelism);

        var (sampleSize, gridSize, perPoint) = search.EvaluateSample(budget, SweepSearch.Seed, parallelism);

        search.FixTheLine(SweepSearch.PlateauMargin);

        var leaders = search.LeadersOf(SweepSearch.Leaders);
        var (proposal, trailing) = search.Propose(leaders, live);
        var refinement = new List<string>();
        var extensions = new List<string>();
        var limits = new List<string>();
        IReadOnlyList<SweepSlice> slices = [];

        if (proposal is not null)
        {
            proposal = search.Refine(proposal, live, refinement);
            proposal = search.Extend(proposal, live, refinement, extensions, limits);
            slices = search.Slices(proposal.Point);
        }
        else
        {
            notes.Add(leaders.Count == 0 ? "no evaluated setting meets the floors, so there is no leader and no proposal" : "every leader trails the live rule's edge in a recent year, so none is proposed");
        }

        // The same design at the other margins, each proposed and refined from the same leaders, so the operator
        // can rule the margin over what each gives; the ruled line is put back after.
        var otherMargins = new List<SweepMarginProposal>();
        var line = search.Line;

        foreach (var margin in SweepSearch.OtherMargins)
        {
            if (float.IsNaN(search.BestEdge))
            {
                break;
            }

            search.SetLine(search.BestEdge - (float)margin);

            var (other, _) = search.Propose(leaders, live);

            if (other is not null)
            {
                other = search.Refine(other, live, []);
                otherMargins.Add(new SweepMarginProposal(margin, space.Describe(other.Point), other.Summary.Edge, other.Depth.Depth));
            }
            else
            {
                otherMargins.Add(new SweepMarginProposal(margin, "none stands", float.NaN, 0));
            }
        }

        search.SetLine(line);

        return new SweepDesignResult(
            design.Key,
            combination.Key,
            [.. combination.On],
            gridSize,
            sampleSize,
            perPoint,
            search.Evaluations,
            search.BestEdge,
            search.Line,
            leaders.Count,
            trailing,
            proposal,
            refinement,
            extensions,
            limits,
            slices,
            otherMargins,
            notes);
    }

    sealed record ConditionsFile(IReadOnlyList<ConditionTrial> Trials, IReadOnlyList<ConditionVerdict> Verdicts);

    // A chunk run, and run once more where it fails; failing again the run stops, its report written with what
    // it has, saying where and why.
    public async Task RetriedAsync(State state, string chunk, Func<Task> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await work();

                return;
            }
            catch (Exception failure) when (attempt == 1 && failure is not OperationCanceledException)
            {
                Say($"{chunk} failed and is tried once more: {failure.Message}");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                state.Failures.Add($"{chunk} failed twice and the run stopped there: {failure.GetType().Name}: {failure.Message}");
                Save(state);
                Say(state.Failures[^1]);

                throw new SweepStopped(state.Failures[^1], failure);
            }
        }
    }

    // How long a chunk of a kind took last, so the pause starts early enough that no chunk runs past 23:00 UTC.
    static TimeSpan EstimateFor(string kind, State state) => kind switch
    {
        "candidates" => TimeSpan.FromSeconds(Math.Max(60, state.Seconds.GetValueOrDefault("candidates") / Math.Max(1, state.CandidateChunks) * 1.5)),
        "ranks" => TimeSpan.FromSeconds(Math.Max(60, state.Seconds.GetValueOrDefault("stage1") / Math.Max(1, state.RankChunks) * 1.5)),
        "cross" => TimeSpan.FromSeconds(Math.Max(60, state.Seconds.GetValueOrDefault("step-c") / Math.Max(1, state.CrossChunks) * 1.5)),
        _ => TimeSpan.FromMinutes(5),
    };

    // Waits until nothing holds the store: no night, queue or drain lock held, and on a weekday from 23:00 UTC,
    // less the time a chunk takes, until the night that falls then has taken its lock and let it go. A lock let
    // go is read clear on two polls in a row before the run goes on, so a drain the night starts as it ends is
    // seen holding its own. A night window no night came in is given up seven hours after it opened.
    public async Task WaitForTheStoreAsync(State state, TimeSpan chunk, CancellationToken cancellation)
    {
        DateTimeOffset? waitedFrom = null;
        DateTimeOffset? windowFrom = null;
        DateTimeOffset? lastHeld = null;
        var sawTheNight = false;
        var held = new SortedSet<string>(StringComparer.Ordinal);

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();

            var now = clock.UtcNow;
            var nightHeld = NightLock.Holder(dataRoot) is not null;
            var drainHeld = DrainHeld();

            if (nightHeld)
            {
                held.Add("the night's lock");
            }

            if (drainHeld)
            {
                held.Add("a drain's lock");
            }

            if (nightHeld || drainHeld)
            {
                lastHeld = now;
            }

            sawTheNight |= nightHeld;

            if (windowFrom is null && InTheNightsWindow(now, chunk))
            {
                windowFrom = now;
            }

            if (waitedFrom is null && (lastHeld == now || windowFrom is not null))
            {
                waitedFrom = now;
                Say(windowFrom is not null ? "pausing for the night" : "waiting while " + string.Join(" and ", held) + " is held");
            }

            var clear = lastHeld is not { } heldAt || now - heldAt >= ClearPolls * Poll;
            var nightOver = windowFrom is not { } window || sawTheNight || now - window >= GiveUpWaitingAt + (TimeSpan.FromHours(24) - PauseFrom);

            if (clear && nightOver)
            {
                if (waitedFrom is { } since)
                {
                    var why = held.Count > 0 ? string.Join(" and ", held) : "the night's window";

                    state.Pauses.Add(FormattableString.Invariant($"{since:yyyy-MM-dd HH:mm}Z to {now:yyyy-MM-dd HH:mm}Z, {why}{(windowFrom is not null && !sawTheNight ? ", no night having run" : string.Empty)}"));
                    Save(state);
                    Say("going on after the wait");
                }

                return;
            }

            await wait(Poll, cancellation);
        }
    }

    // A weekday's night window: from 23:00 UTC less the chunk about to start, so the chunk ends by 23:00.
    public static bool InTheNightsWindow(DateTimeOffset now, TimeSpan chunk)
    {
        var ends = now + chunk;

        return ends.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && ends.TimeOfDay >= PauseFrom
            || (now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && now.TimeOfDay >= PauseFrom);
    }

    bool DrainHeld()
    {
        var path = Path.Combine(dataRoot, EquityBrief.Core.Research.WorkerDrainLauncher.CopiesFolder, EquityBrief.Worker.Research.DrainLock.FileName);

        if (!File.Exists(path))
        {
            return false;
        }

        // A drain holds the file shared with nobody, so any open fails while one runs.
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // The candidates' chunks: the first twenty scored sessions alone, timed, and a hundred at a time after.
    static IReadOnlyList<(int From, int To)> CandidateChunks(int firstScored, int sessions)
    {
        var chunks = new List<(int, int)> { (firstScored, Math.Min(sessions, firstScored + FirstSessions)) };

        for (var from = firstScored + FirstSessions; from < sessions; from += SessionsPerChunk)
        {
            chunks.Add((from, Math.Min(sessions, from + SessionsPerChunk)));
        }

        return chunks;
    }

    List<SweepCandidate> ReadCandidates()
    {
        var candidates = new List<SweepCandidate>();

        foreach (var path in Directory.GetFiles(Of(SweepFolder.CandidatesFolder), "chunk-*.bin").Order(StringComparer.Ordinal))
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            var count = reader.ReadInt32();

            for (var at = 0; at < count; at++)
            {
                candidates.Add(SweepCandidate.Read(reader));
            }
        }

        return [.. candidates.OrderBy(one => one.Session).ThenBy(one => one.Name)];
    }

    List<RankRow> ReadRanks() =>
        [.. Directory.GetFiles(Of("ranks"), "chunk-*.json").Order(StringComparer.Ordinal).SelectMany(path => JsonSerializer.Deserialize<RankRow[]>(File.ReadAllText(path), Json) ?? [])];

    List<CombinationRow> ReadCross() =>
        [.. Directory.GetFiles(Of("cross"), "chunk-*.json").Order(StringComparer.Ordinal).SelectMany(path => JsonSerializer.Deserialize<CombinationRow[]>(File.ReadAllText(path), Json) ?? [])];

    static ParallelOptions Parallelism() => new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
}

public sealed class SweepStopped(string message, Exception inner) : Exception(message, inner);
