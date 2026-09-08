# The one place a PowerShell entry point hands its work to a bash script.
#
# Every bash entry point in this repository ships with a .ps1 beside it, because
# calling an extensionless bash script by name from PowerShell produces no
# output, leaves $LASTEXITCODE unset and leaves $? true, so a gate that never
# executed is indistinguishable from one that passed.
#
# Written as a script rather than a function on purpose. A PowerShell function's
# return value is its output stream, so returning $LASTEXITCODE from one would
# swallow everything the script printed.
param(
    [Parameter(Mandatory)][string] $Script,
    [Parameter(ValueFromRemainingArguments = $true)] $Arguments
)

$target = Join-Path $PSScriptRoot $Script
$probe = $target -replace '\\', '/'

# Every bash on PATH, and then the one that can see the script.
#
# Not the first one. On a Windows machine with the optional WSL feature enabled,
# C:\Windows\System32\bash.exe is the WSL launcher and it comes first on PATH.
# It cannot open a Windows path, so handing it this script prints an
# advertisement for installing a distribution and exits 1, which is neither the
# gate running nor the named message this wrapper exists to print. The operator's
# own machine is in that state, and tools/ci.ps1 is a documented command.
#
# Chosen by asking each candidate whether it can see the script rather than by
# ruling out binaries whose paths look like WSL. The property that matters is
# whether the script can be read, and a check for the property does not go stale
# when a launcher moves or a new one appears.
$candidates = New-Object System.Collections.Generic.List[string]

foreach ($found in @(Get-Command bash -CommandType Application -All -ErrorAction SilentlyContinue)) {
    $candidates.Add($found.Source)
}

# And the bash beside git, which is usually not on PATH.
#
# A default Git for Windows install puts git.exe in cmd\ and bash.exe in bin\,
# and only cmd\ goes on PATH. So on a stock Windows machine with Git installed
# there is a working bash present and unreachable by name, while the WSL
# launcher is reachable by name and does not work. Looking for bash relative to
# git is what makes the documented commands run from a PowerShell prompt.
$git = @(Get-Command git -CommandType Application -ErrorAction SilentlyContinue) |
    Select-Object -First 1

if ($git) {
    $root = Split-Path (Split-Path $git.Source)

    foreach ($relative in @('bin\bash.exe', 'usr\bin\bash.exe')) {
        $beside = Join-Path $root $relative

        if (Test-Path -LiteralPath $beside) {
            $candidates.Add($beside)
        }
    }
}

$saved = $ErrorActionPreference
$bash = $null

foreach ($candidate in $candidates) {
    $reachable = $false

    try {
        # Continue for the length of the probe. ci.ps1 sets Stop, and a native
        # command writing to stderr under Stop is a terminating error, so a
        # candidate that fails loudly would kill the wrapper instead of being
        # passed over.
        $ErrorActionPreference = 'Continue'
        & $candidate -c "test -f '$probe'" *> $null
        $reachable = ($LASTEXITCODE -eq 0)
    }
    catch {
        $reachable = $false
    }
    finally {
        $ErrorActionPreference = $saved
    }

    if ($reachable) {
        $bash = $candidate
        break
    }
}

if (-not $bash) {
    # Written to the error stream directly rather than with Write-Error. A caller
    # that sets $ErrorActionPreference to Stop, which tools/ci.ps1 does, turns
    # Write-Error into a terminating error, so the exit below never runs, the run
    # dies at 1 and the failed-at-step line never prints. The safety property
    # survived that and the documented 127 did not.
    $found = if ($candidates.Count -eq 0) {
        'No bash found on PATH or beside git'
    }
    else {
        "None of the $($candidates.Count) bash binaries found can read the script " +
        "($($candidates -join ', '))"
    }

    [Console]::Error.WriteLine(
        "$found, so tools/$Script cannot be run. Install Git for Windows, or " +
        'add a bash that can read a Windows path to PATH, and run this again. A WSL launcher ' +
        'does not count: it is on PATH as bash and cannot open this checkout. Exiting 127 ' +
        'rather than 0, because a gate that never ran must not look like one that passed.')
    exit 127
}

& $bash $target @Arguments
exit $LASTEXITCODE
