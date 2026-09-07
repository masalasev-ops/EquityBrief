namespace EquityBrief.Tests;

// A throwaway directory, for tests that run the repository's entry points
// somewhere other than the repository.
internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a reason to fail a run.
        }
    }
}
