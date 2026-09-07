using EquityBrief.Core.Configuration;
using EquityBrief.Data.Migrations;
using Microsoft.Extensions.Configuration;

// The nightly run and the overnight queue. Scheduling lives outside the
// application, so this is one command an external scheduler invokes rather than
// a service that schedules itself.
// see: Nothing is written against one operating system

return (args.Length > 0 ? args[0] : string.Empty) switch
{
    "migrate" => Migrate(),
    _ => NoVerb(),
};

static int NoVerb()
{
    Console.Error.WriteLine(
        "EquityBrief.Worker: no verb given. The only verb built is 'migrate'. The nightly run " +
        "is checkpoint 1.1.");

    return 1;
}

static int Migrate()
{
    // The configuration files sit beside the assembly; the data root they name
    // is resolved against the working directory. The secrets file is registered
    // before the environment so an environment variable still wins.
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Secrets.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

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
