using EquityBrief.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker;

// The worker's configuration: its settings file beside the build, the secrets file beside it where the
// build was made from a checkout holding one, the secrets file the environment names by path, and the
// environment last, so a variable still wins. The named file is how a night built from a clean copy of the
// committed code, which holds no secrets, reads the main checkout's without a copy of it.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public static class WorkerConfiguration
{
    public static IConfiguration Build(string baseDirectory, string? secretsFile)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Secrets.json", optional: true);

        if (secretsFile is { Length: > 0 })
        {
            builder.AddJsonFile(Path.GetFullPath(secretsFile), optional: true);
        }

        return builder.AddEnvironmentVariables().Build();
    }

    public static IConfiguration Build() =>
        Build(AppContext.BaseDirectory, Environment.GetEnvironmentVariable(NightBuild.SecretsFileVariable));
}
