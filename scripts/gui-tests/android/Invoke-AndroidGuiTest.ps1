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
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shortCommit = (& git -C $projectRoot rev-parse --short HEAD).Trim()
$runDirectory = Join-Path $projectRoot "artifacts\gui-tests\android\runs\$timestamp-$shortCommit-$Scenario"
$serverLog = Join-Path $runDirectory 'appium-server.log'
$serverErrorLog = Join-Path $runDirectory 'appium-server.stderr.log'

foreach ($directory in @($runDirectory, (Join-Path $runDirectory 'screenshots'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not (Test-Path -LiteralPath (Join-Path $harnessRoot 'node_modules') -PathType Container)) {
    throw 'The repository-managed Android GUI workspace is not installed. This runner never installs or updates dependencies.'
}

$serverProcess = $null
try {
    $serverProcess = Start-Process -FilePath 'npx.cmd' -WorkingDirectory $harnessRoot -PassThru -NoNewWindow `
        -ArgumentList @('--no-install', 'appium', 'server', '--address', '127.0.0.1', '--port', $AppiumPort) `
        -RedirectStandardOutput $serverLog -RedirectStandardError $serverErrorLog

    $deadline = (Get-Date).AddSeconds(30)
    $status = $null
    do {
        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:$AppiumPort/status" -Method Get -TimeoutSec 2
            if ($status.value.ready) { break }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ((Get-Date) -lt $deadline)

    if ($null -eq $status -or -not $status.value.ready) {
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
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -ErrorAction SilentlyContinue
    }
}
