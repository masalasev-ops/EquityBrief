using System.Diagnostics;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Reading;

// read-surface, the 12.3 correction: a worker a press starts from a copy of the night's clean build, which holds no
// secrets file, reads the checkout's secrets file by the path the press hands it, as the night's own worker does.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class ReadSurface
{
    [Fact]
    public void AWorkerAPressStartsFromACopyOfTheNightsBuildReadsTheCheckoutsSecretsFile()
    {
        using var root = new TemporaryDirectory();

        // The night's build as the script makes it, its settings and no secrets file, and the checkout's secrets file.
        var checkout = Path.Combine(root.Path, "checkout");
        var made = Path.Combine(root.Path, "night-build");
        var beside = Path.Combine(root.Path, "beside");

        Directory.CreateDirectory(Path.GetDirectoryName(NightBuild.SecretsFileIn(checkout))!);
        Directory.CreateDirectory(made);
        Directory.CreateDirectory(beside);
        File.WriteAllText(Path.Combine(made, WorkerDrainLauncher.Assembly), "the night's worker");
        File.WriteAllText(Path.Combine(made, "appsettings.json"), """{ "EquityBrief": { "Probe": "shipped" } }""");
        File.WriteAllText(Path.Combine(beside, WorkerDrainLauncher.Assembly), "the worker beside the surface");
        File.WriteAllText(NightBuild.SecretsFileIn(checkout), """{ "EquityBrief": { "Probe": "the checkout's secret" } }""");

        var asked = new List<ProcessStartInfo>();
        var launcher = new WorkerDrainLauncher(
            checkout,
            beside,
            Path.Combine(root.Path, "data-root"),
            FixedClock.At(UtcAt("2026-10-06T19:00:00Z"), SessionZones.UnitedStates),
            info =>
            {
                asked.Add(info);

                return true;
            },
            () => made);

        Assert.True(launcher.Start().Started);

        var info = Assert.Single(asked);
        var copy = Path.GetDirectoryName(info.ArgumentList[0])!;

        // Started from a copy of the night's build, which holds no secrets file of its own.
        Assert.Equal(File.ReadAllText(Path.Combine(made, WorkerDrainLauncher.Assembly)), File.ReadAllText(info.ArgumentList[0]));
        Assert.False(File.Exists(Path.Combine(copy, "appsettings.Secrets.json")));

        if (Environment.GetEnvironmentVariable(NightBuild.SecretsFileVariable) is { Length: > 0 } named)
        {
            // A surface whose own environment names a secrets file hands that one on.
            Assert.Equal(named, info.Environment[NightBuild.SecretsFileVariable]);

            return;
        }

        // The worker's configuration over the copy and the path handed it reads the checkout's secret over the
        // shipped setting, which the copy alone would read.
        Assert.Equal(NightBuild.SecretsFileIn(checkout), info.Environment[NightBuild.SecretsFileVariable]);
        Assert.Equal("the checkout's secret", WorkerConfiguration.Build(copy, info.Environment[NightBuild.SecretsFileVariable])["EquityBrief:Probe"]);
        Assert.Equal("shipped", WorkerConfiguration.Build(copy, null)["EquityBrief:Probe"]);
    }
}
