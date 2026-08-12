[CmdletBinding()]
param(
    [ValidateSet('P16A-SettingsReleaseNotesNavigation')]
    [string]$Scenario = 'P16A-SettingsReleaseNotesNavigation',

    [Parameter(Mandatory = $true)]
    [string]$DeviceId,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ChromedriverExecutable,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedCommit,

    [ValidateRange(1024, 65535)]
    [int]$AppiumPort = 4723
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$allowedPackage = 'com.tachiguro.knownfirst.guitest'
$harnessRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $harnessRoot '..\..\..')).Path
$listenerCommand = Get-Command -Name 'Get-NetTCPConnection' -ErrorAction SilentlyContinue
if ($null -eq $listenerCommand) {
    throw 'Get-NetTCPConnection is required to verify ownership of the Appium listener.'
}

function Get-AppiumPortListeners {
    param([int]$Port)

    try {
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop)
    }
    catch {
        throw "Failed to inspect listeners on Appium port $Port. $($_.Exception.Message)"
    }

    return @($listeners | Where-Object { $_.LocalPort -eq $Port })
}

function Test-OwnedLoopbackListener {
    param(
        [object[]]$Listeners,
        [int]$OwnedProcessId
    )

    if ($Listeners.Count -eq 0) {
        return $false
    }

    $foreignListeners = @($Listeners | Where-Object { $_.OwningProcess -ne $OwnedProcessId })
    if ($foreignListeners.Count -gt 0) {
        throw "A foreign listener was detected on Appium port $AppiumPort."
    }

    $nonLoopbackListeners = @($Listeners | Where-Object { $_.LocalAddress -notin @('127.0.0.1', '::1') })
    if ($nonLoopbackListeners.Count -gt 0) {
        throw "The owned Appium listener on port $AppiumPort is not loopback-only."
    }

    return @($Listeners | Where-Object {
        $_.OwningProcess -eq $OwnedProcessId -and $_.LocalAddress -in @('127.0.0.1', '::1')
    }).Count -gt 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shortCommit = (& git -C $projectRoot rev-parse --short HEAD).Trim()
$runDirectory = Join-Path $projectRoot "artifacts\gui-tests\android\runs\$timestamp-$shortCommit-$Scenario"
$serverLog = Join-Path $runDirectory 'appium-server.log'
$serverErrorLog = Join-Path $runDirectory 'appium-server.stderr.log'

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $harnessRoot 'node_modules') -PathType Container)) {
    throw 'The repository-managed Android GUI workspace is not installed. This runner never installs or updates dependencies.'
}

$appiumEntry = Join-Path $harnessRoot 'node_modules\appium\index.js'
if (-not (Test-Path -LiteralPath $appiumEntry -PathType Leaf)) {
    throw "The repository-managed Appium entry point is missing: $appiumEntry"
}

$nodeCommand = Get-Command -Name 'node' -CommandType Application -ErrorAction Stop | Select-Object -First 1
$nodeExecutable = $nodeCommand.Source
if ([string]::IsNullOrWhiteSpace($nodeExecutable)) {
    throw 'A Node executable is required to launch the repository-managed Appium entry point.'
}

$existingListeners = @(Get-AppiumPortListeners -Port $AppiumPort)
if ($existingListeners.Count -gt 0) {
    throw "A pre-existing listener already owns Appium port $AppiumPort."
}

$serverProcess = $null
try {
    $serverProcess = Start-Process -FilePath $nodeExecutable -WorkingDirectory $harnessRoot -PassThru -NoNewWindow `
        -ArgumentList @($appiumEntry, 'server', '--address', '127.0.0.1', '--port', $AppiumPort) `
        -RedirectStandardOutput $serverLog -RedirectStandardError $serverErrorLog

    $deadline = (Get-Date).AddSeconds(30)
    $status = $null
    $ready = $false
    do {
        $serverProcess.Refresh()
        if ($serverProcess.HasExited) {
            throw "The owned Appium process exited before readiness with code $($serverProcess.ExitCode)."
        }

        $listeners = @(Get-AppiumPortListeners -Port $AppiumPort)
        if (Test-OwnedLoopbackListener -Listeners $listeners -OwnedProcessId $serverProcess.Id) {
            try {
                $status = Invoke-RestMethod -Uri "http://127.0.0.1:$AppiumPort/status" -Method Get -TimeoutSec 2
            }
            catch {
                $status = $null
            }

            if ($null -ne $status -and $null -ne $status.value -and $status.value.ready) {
                $serverProcess.Refresh()
                if ($serverProcess.HasExited) {
                    throw "The owned Appium process exited after reporting readiness with code $($serverProcess.ExitCode)."
                }

                $verifiedListeners = @(Get-AppiumPortListeners -Port $AppiumPort)
                if (-not (Test-OwnedLoopbackListener -Listeners $verifiedListeners -OwnedProcessId $serverProcess.Id)) {
                    throw 'The owned Appium listener disappeared after reporting readiness.'
                }

                $ready = $true
                break
            }
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    if (-not $ready) {
        throw 'The owned loopback Appium server did not become ready within 30 seconds.'
    }

    $env:KNOWNFIRST_ANDROID_GUI_RUN_DIRECTORY = $runDirectory
    $env:KNOWNFIRST_ANDROID_GUI_DEVICE_ID = $DeviceId
    $env:KNOWNFIRST_ANDROID_GUI_CHROMEDRIVER = $ChromedriverExecutable
    $env:KNOWNFIRST_ANDROID_GUI_APPIUM_PORT = $AppiumPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $env:KNOWNFIRST_ANDROID_GUI_EXPECTED_COMMIT = $ExpectedCommit
    & node (Join-Path $harnessRoot 'runner.mjs')
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:KNOWNFIRST_ANDROID_GUI_RUN_DIRECTORY -ErrorAction SilentlyContinue
    Remove-Item Env:KNOWNFIRST_ANDROID_GUI_DEVICE_ID -ErrorAction SilentlyContinue
    Remove-Item Env:KNOWNFIRST_ANDROID_GUI_CHROMEDRIVER -ErrorAction SilentlyContinue
    Remove-Item Env:KNOWNFIRST_ANDROID_GUI_APPIUM_PORT -ErrorAction SilentlyContinue
    Remove-Item Env:KNOWNFIRST_ANDROID_GUI_EXPECTED_COMMIT -ErrorAction SilentlyContinue
    if ($serverProcess) {
        try {
            $serverProcess.Refresh()
            if (-not $serverProcess.HasExited) {
                Stop-Process -Id $serverProcess.Id -ErrorAction SilentlyContinue
            }
        }
        catch {
            # Cleanup must not replace the primary harness result.
        }
    }
}
