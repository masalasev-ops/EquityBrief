using EquityBrief.Core.Configuration;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker;
using Microsoft.Extensions.Configuration;

// The nightly run and the overnight queue. Scheduling lives outside the
// application, so this is one command an external scheduler invokes rather than
// a service that schedules itself.
// see: Nothing is written against one operating system

return (args.Length > 0 ? args[0] : string.Empty) switch
{
    "migrate" => Migrate(),
    "nightly" => await NightlyRun(args),
    _ => NoVerb(),
};

static int NoVerb()
{
    Console.Error.WriteLine(
        "EquityBrief.Worker: no verb given. Two are built: 'migrate' applies pending migrations, " +
        "and 'nightly --fixture <folder>' runs the night's steps in order.");

    return 1;
}

// The night, invoked by tools/nightly and by nothing else in this repository:
// scheduling lives outside the application.
// see: Nothing is written against one operating system
static async Task<int> NightlyRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await Nightly.RunAsync(
        store,
        Argument(args, "--fixture") ?? string.Empty,
        Argument(args, "--index") ?? "GSPC",
        SystemClock.ForUnitedStatesSessions(),
        Console.Out,
        Console.Error);
}

static string? Argument(string[] args, string name)
{
    var at = Array.IndexOf(args, name);

    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static IConfiguration Configuration() =>
    new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Secrets.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

static int Migrate()
{
    // The configuration files sit beside the assembly; the data root they name
    // is resolved against the working directory. The secrets file is registered
    // before the environment so an environment variable still wins.
    var configuration = Configuration();

    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var outcome = MigrationRunner.Standard().Apply(store.DatabaseFile);

    Console.WriteLine($"store: {store.DatabaseFile}");

    if (outcome.Applied.Count == 0)
    {
        Console.WriteLine($"no pending migrations, schema version {outcome.To}");
    }
    else
    {
        Console.WriteLine(
            $"applied {outcome.Applied.Count} migration(s), {outcome.From} to {outcome.To}: " +
            string.Join(", ", outcome.Applied));
    }

    return 0;
}
