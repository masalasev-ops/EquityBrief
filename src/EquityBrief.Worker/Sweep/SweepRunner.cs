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
// to a file under the sweep's folder beside the store and never in it: the candidates by blocks of sessions, stage
// 1's ranking by blocks of designs, and stage 2 by design. A process started again reads what the saved chunks
// hold and goes on from the first one missing. Before each chunk it waits: while a night, the overnight queue or a
// drain holds its lock file, and on a weekday from 23:00 UTC, or from earlier where the chunk would run past it,
// until the night that falls then has taken its lock and let it go. A chunk that fails is tried once more, and
// failing again the run stops and the report says where and why. The first chunks of each stage are timed, and a
// run whose projection passes five days stops after stage 1 and reports rather than running on.
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
public sealed class SweepRunner
{
    public const int FirstSessions = 20;
    public const int SessionsPerChunk = 100;
    public const int FirstDesigns = 50;
    public const int DesignsPerChunk = 50;
    public const int CarriedForward = 5;

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

    // The run's saved state: the history's end, its fingerprint, the chunks done, the timings and anything that
    // failed.
    public sealed class State
    {
        public string? Through { get; set; }

        public string? Fingerprint { get; set; }

        public DateTimeOffset? Started { get; set; }

        public int CandidateChunks { get; set; }

        public bool CandidatesDone { get; set; }

        public int RankChunks { get; set; }

        public bool RanksDone { get; set; }

        public List<string> FineDone { get; set; } = [];

        public bool Finished { get; set; }

        public bool StoppedAfterStageOne { get; set; }

        public Dictionary<string, double> Seconds { get; set; } = [];

        public List<string> Timings { get; set; } = [];

        public List<string> Failures { get; set; } = [];

        public List<string> Pauses { get; set; } = [];

        public double? ProjectedHours { get; set; }
    }

    public State Load()
    {
        var path = Of("state.json");

        return File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State() : new State();
    }

    void Save(State state)
    {
        var path = Of("state.json");
        var next = path + ".next";

        File.WriteAllText(next, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(next, path, overwrite: true);
    }

    void Say(string line)
    {
        var stamped = FormattableString.Invariant($"{clock.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {line}");

        log.WriteLine(stamped);
        File.AppendAllText(Of("sweep.log"), stamped + Environment.NewLine);
    }

    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Of(SweepFolder.CandidatesFolder));
        Directory.CreateDirectory(Of("ranks"));
        Directory.CreateDirectory(Of("fine"));

        var state = Load();

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
        state.Started ??= clock.UtcNow;
        Save(state);

        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;

        List<SweepCandidate> candidates;

        if (state.CandidatesDone)
        {
            candidates = ReadCandidates();
        }
        else
        {
            watch.Restart();
            var series = new SweepSeries[inputs.Names.Count];

            Parallel.For(0, inputs.Names.Count, Parallelism(), name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

            var sessions = SweepColumns.Sessions(series, calendar);

            state.Seconds["series"] = watch.Elapsed.TotalSeconds;
            Say(FormattableString.Invariant($"series and cross-sections read over {inputs.Names.Count} name(s) and {calendar.Length} session(s) in {watch.Elapsed.TotalSeconds:0} s"));

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

                    using var file = File.Create(Of(SweepFolder.CandidatesFolder, FormattableString.Invariant($"chunk-{chunk:0000}.bin")));
                    using var writer = new BinaryWriter(file);
                    var all = found.SelectMany(list => list).OrderBy(one => one.Session).ThenBy(one => one.Name).ToArray();

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

                    state.Timings.Add(FormattableString.Invariant($"the candidates over the first {to - from} sessions took {chunkWatch.Elapsed.TotalSeconds:0.0} s, {perSession:0.00} s a session, projecting {hours:0.0} hours over the {calendar.Length - firstScored} scored sessions"));
                }

                Save(state);
                Say(FormattableString.Invariant($"candidates chunk {chunk + 1} of {chunks.Count} in {chunkWatch.Elapsed.TotalSeconds:0} s"));
            }

            state.CandidatesDone = true;
            Save(state);
            candidates = ReadCandidates();
        }

        Say(FormattableString.Invariant($"{candidates.Count} candidate(s) held"));

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

                File.WriteAllText(Of("ranks", FormattableString.Invariant($"chunk-{chunk:0000}.json")), JsonSerializer.Serialize(ranks.SelectMany(list => list).Select(RankRow.Of).ToArray()));

                return Task.CompletedTask;
            });

            state.RankChunks = chunk + 1;
            state.Seconds["stage1"] = state.Seconds.GetValueOrDefault("stage1") + chunkWatch.Elapsed.TotalSeconds;

            if (chunk == 0)
            {
                var perDesign = chunkWatch.Elapsed.TotalSeconds / slice.Length;

                state.Timings.Add(FormattableString.Invariant($"stage 1 over its first {slice.Length} selection designs, each with its 8 exits and 19,683 coarse settings, took {chunkWatch.Elapsed.TotalSeconds:0.0} s, {perDesign:0.00} s a design, projecting {perDesign * selections.Count / 3600:0.0} hours over the {selections.Count} selection designs"));
                state.ProjectedHours = ((state.Seconds.GetValueOrDefault("read") + state.Seconds.GetValueOrDefault("series") + state.Seconds.GetValueOrDefault("candidates")) / 3600)
                    + (perDesign * selections.Count / 3600);
            }

            Save(state);
            Say(FormattableString.Invariant($"stage 1 chunk {chunk + 1} of {rankChunks} in {chunkWatch.Elapsed.TotalSeconds:0} s"));
        }

        state.RanksDone = true;
        Save(state);

        var rows = ReadRanks();
        var carried = Carried(rows);

        // Stage 2, stopped where the whole run projects past five days.
        var fine = new Dictionary<SweepDesign, SweepVariation[]>();

        if (state.ProjectedHours is { } projected && TimeSpan.FromHours(projected) > Longest)
        {
            state.StoppedAfterStageOne = true;
            Say(FormattableString.Invariant($"the run projects {projected:0.0} hours, past five days, so it stops after stage 1"));
        }
        else
        {
            for (var at = 0; at < carried.Length; at++)
            {
                var design = carried[at].Design;
                var saved = Of("fine", FormattableString.Invariant($"design-{at}.bin"));

                if (state.FineDone.Contains(design.Key) && File.Exists(saved))
                {
                    fine[design] = ReadVariations(saved);

                    continue;
                }

                var designWatch = Stopwatch.StartNew();

                await WaitForTheStoreAsync(state, EstimateFor("stage2", state), cancellation);

                SweepVariation[]? variations = null;

                await RetriedAsync(state, "stage 2 for " + design.Key, () =>
                {
                    variations = SweepStages.Fine(candidates, design, nights);
                    WriteVariations(saved, variations);

                    return Task.CompletedTask;
                });

                fine[design] = variations!;
                state.Seconds["stage2"] = state.Seconds.GetValueOrDefault("stage2") + designWatch.Elapsed.TotalSeconds;

                if (state.FineDone.Count == 0)
                {
                    state.Timings.Add(FormattableString.Invariant($"stage 2's first design, {SweepGrid.Fine.Variations:N0} fine settings, took {designWatch.Elapsed.TotalSeconds:0.0} s"));
                }

                state.FineDone.Add(design.Key);
                Save(state);
                Say(FormattableString.Invariant($"stage 2 for {design.Key} in {designWatch.Elapsed.TotalSeconds:0} s"));
            }
        }

        state.Finished = true;
        Save(state);

        var report = SweepReport.Build(inputs, candidates, rows, carried.Select(row => row.Design).ToArray(), fine, state, nights, calendar, firstScored);

        File.WriteAllText(Of(SweepFolder.ReportFile), report);
        Say("the report is written: " + Of(SweepFolder.ReportFile));

        return 0;
    }

    // Stage 1's ranking: the share of a design's coarse settings viable, the median result of the viable ones
    // breaking a tie, then the design nearer the live rule's, counted in structural choices, and its key.
    public static IReadOnlyList<RankRow> Ranked(IEnumerable<RankRow> rows) =>
        [
            .. rows
                .OrderByDescending(row => row.ViableShare)
                .ThenByDescending(row => row.MedianMultiple ?? double.MinValue)
                .ThenBy(row => ChoicesFromTheLiveRule(row.Design))
                .ThenBy(row => row.Key, StringComparer.Ordinal),
        ];

    // Two designs whose stage 1 figures are the same to the last digit read the history the same way, the trend
    // rule's versions reading an uptrend alike being the common case, so stage 2 takes the five strongest whose
    // figures differ, each tie read through the design nearest the live rule's.
    public static RankRow[] Carried(IEnumerable<RankRow> rows)
    {
        var carried = new List<RankRow>();

        foreach (var row in Ranked(rows))
        {
            if (carried.Count == CarriedForward)
            {
                break;
            }

            if (!carried.Any(held => SameFigures(held, row)))
            {
                carried.Add(row);
            }
        }

        return [.. carried];
    }

    public static bool SameFigures(RankRow one, RankRow other) =>
        one.Viable == other.Viable && one.MedianMultiple == other.MedianMultiple;

    public static int ChoicesFromTheLiveRule(SweepDesign design)
    {
        var live = SweepDesign.Live;

        return (design.Strength != live.Strength ? 1 : 0)
            + (design.Uptrend != live.Uptrend ? 1 : 0)
            + (design.Support != live.Support ? 1 : 0)
            + (design.ReferenceHigh != live.ReferenceHigh ? 1 : 0)
            + (design.Trigger != live.Trigger ? 1 : 0)
            + (design.Plan != live.Plan ? 1 : 0)
            + (design.Hold != live.Hold || design.BreakEven != live.BreakEven ? 1 : 0)
            + (design.EarningsWindow != live.EarningsWindow ? 1 : 0);
    }

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

    // One design's stage 2 settings in index order, the stop and cell read back from where each sits.
    public static void WriteVariations(string path, SweepVariation[] variations)
    {
        var next = path + ".next";

        using (var file = File.Create(next))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(variations.Length);

            foreach (var one in variations)
            {
                writer.Write(one.Share);
                writer.Write(one.BreakEven);
                writer.Write(one.NoSkill);
                writer.Write(one.AverageMultiple);
                writer.Write(one.Scored);
                writer.Write(one.YearsBeatingBreakEven);
                writer.Write(one.WithoutBestYear);
                writer.Write(one.Blocks);
                writer.Write(one.Listing);
            }
        }

        File.Move(next, path, overwrite: true);
    }

    public static SweepVariation[] ReadVariations(string path)
    {
        var grid = SweepGrid.Fine;

        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        var variations = new SweepVariation[reader.ReadInt32()];

        for (var at = 0; at < variations.Length; at++)
        {
            variations[at] = new SweepVariation(
                at / grid.CellsPerStop,
                at % grid.CellsPerStop,
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                reader.ReadByte(),
                reader.ReadBoolean(),
                reader.ReadByte(),
                reader.ReadSingle());
        }

        return variations;
    }

    List<RankRow> ReadRanks() =>
        [.. Directory.GetFiles(Of("ranks"), "chunk-*.json").Order(StringComparer.Ordinal).SelectMany(path => JsonSerializer.Deserialize<RankRow[]>(File.ReadAllText(path)) ?? [])];

    static ParallelOptions Parallelism() => new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };
}

// Stage 1's row for one design as the run saves it.
public sealed record RankRow(
    string Key,
    int Settings,
    int Viable,
    double ViableShare,
    double? MedianMultiple,
    int? LiveScored,
    double? LiveShare,
    double? LiveBreakEven,
    double? LiveNoSkill,
    double? LiveMultiple,
    int? LiveYearsBeatingBoth)
{
    // Read back from the key, which is what the file holds.
    [System.Text.Json.Serialization.JsonIgnore]
    public SweepDesign Design => Parse(Key);

    public static RankRow Of(SweepDesignRank rank) =>
        new(
            rank.Design.Key,
            rank.Settings,
            rank.Viable,
            rank.ViableShare,
            rank.MedianMultiple,
            rank.Live?.Scored,
            rank.Live?.Share,
            rank.Live?.BreakEven,
            rank.Live?.NoSkill,
            rank.Live?.AverageMultiple,
            rank.Live?.YearsBeatingBoth);

    public static SweepDesign Parse(string key)
    {
        var parts = key.Split('|');

        return new SweepDesign(
            Enum.Parse<StrengthMeasure>(parts[0]),
            Enum.Parse<UptrendRule>(parts[1]),
            Enum.Parse<SupportKind>(parts[2]),
            int.Parse(parts[3], CultureInfo.InvariantCulture),
            Enum.Parse<TriggerKind>(parts[4]),
            Enum.Parse<PlanRule>(parts[5]),
            int.Parse(parts[6], CultureInfo.InvariantCulture),
            parts[7] == "breakeven",
            int.Parse(parts[8], CultureInfo.InvariantCulture));
    }
}

public sealed class SweepStopped(string message, Exception inner) : Exception(message, inner);
