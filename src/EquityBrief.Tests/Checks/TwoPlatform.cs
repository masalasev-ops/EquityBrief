namespace EquityBrief.Tests.Checks;

// two-platform. The suite passes on both runners.
//
// The passing is CI's own result and cannot be asserted from inside one run.
// What is asserted here is that the workflow still declares both runners and
// still hands each of them the CI script, because the way this claim fails
// quietly is a matrix that lost a leg and a green run that no longer covers it.
public class TwoPlatform
{
    [Fact]
    public void TheWorkflowStillRunsBothRunners()
    {
        var workflow = File.ReadAllText(Repository.Workflow);

        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("macos-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/ci.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("tools/ci.sh", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinuxJobIsThereAsAnInstrumentAndNotAsAThirdPlatform()
    {
        // Linux is not a platform this tool supports. The job exists so the
        // pipeline opens its files on a case-sensitive filesystem, which is the
        // one fault neither of the operator's machines can see.
        var workflow = File.ReadAllText(Repository.Workflow);

        Assert.Contains("ubuntu-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("case-sensitivity", workflow, StringComparison.Ordinal);
    }
}
