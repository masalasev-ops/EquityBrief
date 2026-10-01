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

    // Tried a few times a moment apart, since a process a test killed may still hold a file for a moment
    // after it ends, and left where it still cannot be removed: a leftover temporary directory is not a
    // reason to fail a run, whichever of the two exceptions Windows refuses the removal with.
    public void Dispose()
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);

                return;
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
                if (!Directory.Exists(Path))
                {
                    return;
                }

                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }
}
