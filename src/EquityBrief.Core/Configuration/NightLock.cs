namespace EquityBrief.Core.Configuration;

// The lock one night holds under the data root while it runs, so a second night, a run of the rest of
// one among them, is refused rather than writing the same session beside it.
//
// The lock is a file opened for this process alone and removed when it is closed. The operating system
// closes it for a process that ends from outside, so a night that dies gives it up; a file one left
// behind on a system that does not remove it on close is no longer held, and taking it again succeeds.
// Beside it a second file names the night holding it, which the pages read, since the lock itself cannot
// be read while it is held.
// see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
public sealed class NightLock : IDisposable
{
    public const string FileName = "night.lock";
    public const string HolderFileName = "night.lock.run";

    // How many times taking the lock is tried, a fifth of a second apart, since a page reading whether it
    // is held opens it for as long as the read takes.
    const int Attempts = 10;
    static readonly TimeSpan Apart = TimeSpan.FromMilliseconds(200);

    readonly FileStream held;
    readonly string holder;

    NightLock(FileStream held, string holder)
    {
        this.held = held;
        this.holder = holder;
    }

    // The lock for the night named, or none where another holds it.
    public static NightLock? Take(string dataRoot, string night)
    {
        Directory.CreateDirectory(dataRoot);

        var path = Path.Combine(dataRoot, FileName);

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                var holder = Path.Combine(dataRoot, HolderFileName);

                File.WriteAllText(holder, night);

                return new NightLock(stream, holder);
            }
            catch (Exception busy) when (busy is IOException or UnauthorizedAccessException && attempt < Attempts)
            {
                Thread.Sleep(Apart);
            }
            catch (Exception busy) when (busy is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    // The night holding the lock, or none where no night holds it. A lock held by a night that wrote no
    // name for itself is named as a night.
    public static string? Holder(string dataRoot)
    {
        var path = Path.Combine(dataRoot, FileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            return null;
        }
        catch (Exception gone) when (gone is FileNotFoundException or UnauthorizedAccessException)
        {
            // Gone, or being removed by the night that held it.
            return null;
        }
        catch (IOException)
        {
            var holder = Path.Combine(dataRoot, HolderFileName);

            try
            {
                return File.Exists(holder) ? File.ReadAllText(holder).Trim() : "a night";
            }
            catch (IOException)
            {
                return "a night";
            }
        }
    }

    public void Dispose()
    {
        try
        {
            File.Delete(holder);
        }
        catch (IOException)
        {
        }

        held.Dispose();
    }
}
