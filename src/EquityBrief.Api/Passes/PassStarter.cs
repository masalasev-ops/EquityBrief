using System.Diagnostics;
using EquityBrief.Core.Configuration;

namespace EquityBrief.Api.Passes;

// What a press of the name page's control asks for: the name, and the two options a
// page offers beside a plain pass.
public sealed record PassRequest(string Ticker, bool Refresh, bool PaidForLocal);

// What starting a pass came to: whether a process was started, and the line the page
// states beside the control either way.
public sealed record PassStart(bool Started, string Line);

// What starts a research pass. One implementation starts the worker's verb; the suite
// holds one that records what it was asked, so a route can be hosted without a pass
// reaching a model.
public interface IPassStarter
{
    PassStart Start(PassRequest request);
}

// The worker's research verb, started as a process of its own from the checkout the read
// surface runs in, and not waited for.
//
// The read surface writes nothing a pass writes and holds no reference to the worker, so
// what it does is hand over the name and the options and return: the pass writes its own
// rows and the page reads them off the run log as they land. The command is the one
// `RUNBOOK.md` gives for running a pass by hand, so the control does nothing the operator
// could not do from a shell, and the arguments go as a list, never through a shell, so a
// name cannot become a second command. The data root goes with it in the environment,
// which the worker's configuration reads over its own file, so the pass writes the store
// this surface reads rather than whichever one the worker's working directory resolves.
// see: The name page's control starts the worker's research verb, and the read API writes nothing it starts
public sealed class WorkerPassStarter(string? checkout, string dataRoot) : IPassStarter
{
    public const string Executable = "dotnet";

    // The configuration key as an environment variable, which is how the worker is told
    // which store to write.
    public static readonly string DataRootVariable = StoreLocation.DataRootKey.Replace(":", "__", StringComparison.Ordinal);

    // The solution file at the checkout's root, which is how the checkout is found from
    // wherever the surface's build output sits inside it.
    public const string SolutionFile = "EquityBrief.slnx";

    public static IReadOnlyList<string> Arguments(PassRequest request)
    {
        var arguments = new List<string>
        {
            "run",
            "--project",
            Path.Combine("src", "EquityBrief.Worker"),
            "--",
            "research",
            "--ticker",
            request.Ticker,
        };

        if (request.Refresh)
        {
            arguments.Add("--refresh");
        }

        if (request.PaidForLocal)
        {
            arguments.Add("--paid-for-local");
        }

        return arguments;
    }

    // How the process is started, stated apart from starting it so what a press would
    // run is asserted without running it.
    public ProcessStartInfo StartInfo(PassRequest request)
    {
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            WorkingDirectory = checkout ?? string.Empty,
        };

        foreach (var argument in Arguments(request))
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment[DataRootVariable] = dataRoot;

        return start;
    }

    public PassStart Start(PassRequest request)
    {
        if (checkout is null)
        {
            return new PassStart(false, $"no pass was started for {request.Ticker}: the read surface is not running inside a checkout, so there is no worker beside it to start");
        }

        try
        {
            using var process = Process.Start(StartInfo(request));

            return process is null
                ? new PassStart(false, $"no pass was started for {request.Ticker}: the worker's process did not start")
                : new PassStart(true, $"a research pass for {request.Ticker} has started, and what it writes lands on the run log as it runs");
        }
        catch (System.ComponentModel.Win32Exception failure)
        {
            return new PassStart(false, $"no pass was started for {request.Ticker}: {failure.Message}");
        }
    }

    // The checkout the surface runs in: the nearest directory at or above the given one
    // holding the solution file, or none where no such directory exists.
    public static string? Checkout(string from)
    {
        for (var directory = new DirectoryInfo(from); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
