using System.Collections.Concurrent;
using System.Reflection;
using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests;

// A store a chain of components builds over fixed inputs at a fixed instant, built once a run and copied for each
// test that asks for it. The chain writes the same rows every time it runs, so a copy is the store a fresh run would
// have built, and each test writes into a copy of its own, so none reads another's rows.
//
// Only a static method with no arguments is kept, since a lambda or a method over a value would key two different
// stores on one method.
// see: The suite builds each store a chain of components writes over the fixture once a run and hands every test a copy of its own
internal static class StoreSnapshots
{
    static readonly ConcurrentDictionary<MethodInfo, Lazy<Task<TemporaryStore>>> Kept = new();

    // Every migration applied once to a store of its own, the file every new store's migrated copy starts from.
    static readonly Lazy<TemporaryStore> Schema = new(() =>
    {
        var store = new TemporaryStore();

        MigrationRunner.Standard().Apply(store.DatabaseFile);

        return store;
    });

    static StoreSnapshots() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var kept in Kept.Values.Where(kept => kept.IsValueCreated && kept.Value.IsCompletedSuccessfully))
        {
            kept.Value.Result.Dispose();
        }

        if (Schema.IsValueCreated)
        {
            Schema.Value.Dispose();
        }
    };

    internal static string MigratedFile() => Schema.Value.Released();

    internal static async Task<TemporaryStore> CopyOf(Func<Task<TemporaryStore>> build)
    {
        if (build.Target is not null || !build.Method.IsStatic || build.Method.GetParameters().Length > 0)
        {
            throw new ArgumentException($"{build.Method.Name} is not a static method with no arguments, so its store cannot be kept for every caller.", nameof(build));
        }

        var template = await Kept.GetOrAdd(build.Method, _ => new Lazy<Task<TemporaryStore>>(build)).Value;

        return template.Copy();
    }
}
