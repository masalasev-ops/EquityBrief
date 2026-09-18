using EquityBrief.Core.Providers;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker;

// What the night hands the overnight queue: the local model, its settings and its lane, how long the
// queue works, and what holds the machine awake while it does.
//
// A record of its own beside the night's feeds rather than a member of them, because
// those are the feeds whose count the limits table bounds, and the local model is the one
// thing the night reaches that is not. It holds the local lane and nothing an open reaches:
// no paid model, no search, no name's news and no filings.
// see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name
public sealed record NightQueue(
    ILocalModelFeed LocalModel,
    LocalModelSettings Settings,
    IReadOnlyList<string> Lane,
    TimeSpan Limit,
    IMachineAwake Awake)
{
    // A night over a capture: the recorded local model in the same folder, this machine's
    // default lane and settings, and the default limit.
    public static NightQueue FromFixture(string folder, IMachineAwake? awake = null) =>
        new(
            new RecordedLocalModelFeed(folder),
            new LocalModelSettings(null, null, null, null, null),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            awake ?? new MachineAwake());

    // What the night's verb hands the overnight queue, read from configuration: the local model's
    // settings and lane, the queue's hours, and the local model from the night's own source.
    // Here rather than in the verb, so what a night reads from its settings is asserted where
    // it is read.
    public static NightQueue From(IConfiguration configuration, string? source, string? fixtureFolder, IMachineAwake awake) =>
        Resolve(
            source,
            fixtureFolder,
            LocalLane.Settings(configuration),
            LocalLane.Sections(configuration),
            OvernightQueue.Limit(configuration),
            awake);

    // The night's own source decides which local model the queue reaches, resolved where
    // every other on-demand feed is, so a night over a capture reaches no model runtime and
    // a live night reaches the operator's. The lane is taken as it was read: reading it
    // checks it, and the writer checks it again before a pass writes anything, so a third
    // check here was one nothing could reach, which 6.10's sweep showed by removing it.
    public static NightQueue Resolve(
        string? source,
        string? fixtureFolder,
        LocalModelSettings settings,
        IReadOnlyList<string> lane,
        TimeSpan limit,
        IMachineAwake awake) =>
        new(OnDemandFeeds.LocalModelFor(source, fixtureFolder, settings), settings, lane, limit, awake);
}
