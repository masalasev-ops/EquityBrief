using EquityBrief.Core.Research;

namespace EquityBrief.Worker.Research;

// One drain at a time over a store.
//
// Every press starts a drain and the night starts one, so without this several could run at
// once, each taking the next request, and the order the queue page states would not be the
// order passes run in. A drain holds a file under the data root open, shared with nobody, for
// as long as it runs, and one started while another holds it waits for it to end and then
// takes whatever the other left. A claim is still one write only one drain can make, so a
// drain that got past this by any other route would still take nothing another holds.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
public static class DrainLock
{
    public const string FileName = "drain.lock";

    // Held until the returned value is disposed. The pause is handed in, so a test decides
    // when the drain holding it ends rather than waiting on the machine.
    public static async Task<IDisposable> AcquireAsync(string dataRoot, Func<Task> pause, CancellationToken cancellation = default)
    {
        var folder = Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, FileName);

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await pause();
            }
        }
    }
}
