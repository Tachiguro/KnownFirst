[CmdletBinding()]
param(
    [ValidateRange(10, 60)]
    [int]$AliveSeconds = 12
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    Write-Host ("[{0}]" -f $Name)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}." -f $Name, $LASTEXITCODE)
    }
}

function Read-SharedText {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-StartupEvent {
    param(
        [Parameter(Mandatory = $true)][string[]]$Paths,
        [Parameter(Mandatory = $true)][datetime]$LaunchStartedUtc
    )

    foreach ($path in $Paths) {
        foreach ($line in (Read-SharedText -Path $path) -split "`r?`n") {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            try {
                $entry = $line | ConvertFrom-Json
                $entryTimestamp = [DateTimeOffset]::MinValue
                $timestampParsed = [DateTimeOffset]::TryParse(
                    [string]$entry.timestamp,
                    [ref]$entryTimestamp)
                if (($entry.eventId.id -eq 1001) -and
                    $timestampParsed -and
                    ($entryTimestamp.UtcDateTime -ge $LaunchStartedUtc.AddSeconds(-2))) {
                    return $true
                }
            }
            catch {
                continue
            }
        }
    }

    return $false
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root 'KnownFirst.csproj'
$solutionPath = Join-Path $root 'KnownFirst.slnx'
$testProjectPath = Join-Path $root 'KnownFirst.Tests\KnownFirst.Tests.csproj'
$windowsFramework = 'net10.0-windows10.0.19041.0'
$assetsPath = Join-Path $root 'obj\project.assets.json'
$executablePath = Join-Path $root "bin\Debug\$windowsFramework\win-x64\KnownFirst.exe"
$logDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KnownFirst\Logs'
$process = $null
$windowObserved = $false
$startupEventObserved = $false

Push-Location $root
try {
    foreach ($path in @($projectPath, $solutionPath, $testProjectPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required repository file not found: $path"
        }
    }

    $existingProcesses = @(Get-Process -Name 'KnownFirst' -ErrorAction SilentlyContinue)
    if ($existingProcesses.Count -gt 0) {
        throw 'A KnownFirst process is already running. Close it before starting the smoke test.'
    }

    Invoke-DotNetStep -Name 'Build server shutdown' -Arguments @(
        'build-server',
        'shutdown')
    foreach ($relativePath in @(
        'bin',
        'obj',
        'KnownFirst.Core\bin',
        'KnownFirst.Core\obj',
        'KnownFirst.Tests\bin',
        'KnownFirst.Tests\obj')) {
        $generatedPath = [System.IO.Path]::GetFullPath((Join-Path $root $relativePath))
        if (-not $generatedPath.StartsWith(
            $root.TrimEnd('\') + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the repository: $generatedPath"
        }

        if (Test-Path -LiteralPath $generatedPath) {
            Remove-Item -LiteralPath $generatedPath -Recurse -Force
        }
    }

    Invoke-DotNetStep -Name 'Plain restore' -Arguments @(
        'restore',
        $projectPath,
        '--force-evaluate',
        '--no-cache')

    if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
        throw "Restore did not create $assetsPath."
    }

    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $restoredFrameworks = @($assets.project.frameworks.PSObject.Properties.Name)
    $expectedFrameworks = @(
        'net10.0-android',
        'net10.0-ios',
        'net10.0-maccatalyst',
        $windowsFramework)
    $missingFrameworks = @($expectedFrameworks | Where-Object { $restoredFrameworks -notcontains $_ })
    if ($missingFrameworks.Count -gt 0) {
        throw ("Restore assets are missing target frameworks: {0}" -f ($missingFrameworks -join ', '))
    }

    Write-Host ("[Assets] {0}" -f ($restoredFrameworks -join ', '))
    Invoke-DotNetStep -Name 'Debug solution build' -Arguments @(
        'build',
        $solutionPath,
        '-c',
        'Debug',
        '--tl:off',
        '-nodeReuse:false')
    Invoke-DotNetStep -Name 'Windows Debug build' -Arguments @(
        'build',
        $projectPath,
        '-c',
        'Debug',
        '-f',
        $windowsFramework,
        '--no-restore',
        '--tl:off',
        '-nodeReuse:false')
    Invoke-DotNetStep -Name 'Automated tests' -Arguments @(
        'test',
        $testProjectPath,
        '-c',
        'Debug',
        '--no-restore',
        '--tl:off',
        '-nodeReuse:false',
        '--logger',
        'console;verbosity=minimal')

    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Windows build did not create $executablePath."
    }

    $knownLogFiles = @{}
    if (Test-Path -LiteralPath $logDirectory -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $logDirectory -Filter 'knownfirst-*.jsonl' -File) {
            $knownLogFiles[$file.FullName] = $true
        }
    }

    $launchStartedUtc = [DateTime]::UtcNow
    Write-Host '[Windows launch]'
    $process = Start-Process `
        -FilePath $executablePath `
        -WorkingDirectory (Split-Path $executablePath -Parent) `
        -WindowStyle Normal `
        -PassThru
    if ($null -eq $process) {
        throw 'Windows application launch did not return a process.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($AliveSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            throw ("KnownFirst exited during the smoke interval with exit code {0}." -f $process.ExitCode)
        }

        if ($process.MainWindowHandle -ne 0) {
            $windowObserved = $true
        }

        Start-Sleep -Milliseconds 250
    }

    $candidateLogs = @()
    if (Test-Path -LiteralPath $logDirectory -PathType Container) {
        $candidateLogs = @(Get-ChildItem -LiteralPath $logDirectory -Filter 'knownfirst-*.jsonl' -File |
            Where-Object { -not $knownLogFiles.ContainsKey($_.FullName) -or $_.LastWriteTimeUtc -ge $launchStartedUtc.AddSeconds(-2) } |
            Select-Object -ExpandProperty FullName)
    }
    if ($candidateLogs.Count -gt 0) {
        $startupEventObserved = Test-StartupEvent -Paths $candidateLogs -LaunchStartedUtc $launchStartedUtc
    }
    if (-not $startupEventObserved) {
        throw "KnownFirst stayed alive, but no startup-complete event was found in $logDirectory."
    }

    Write-Host ("PASS: KnownFirst remained alive for {0} seconds; process ID {1}; window observed = {2}; startup event observed = {3}." -f $AliveSeconds,$process.Id,$windowObserved,$startupEventObserved)
}
catch {
    Write-Error ("FAIL: {0}" -f $_.Exception.Message)
    exit 1
}
finally {
    if ($null -ne $process) {
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                $closeRequested = $process.CloseMainWindow()
                if ($closeRequested) {
                    Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
                }

                $process.Refresh()
                if (-not $process.HasExited) {
                    Stop-Process -Id $process.Id -Force
                    Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
                }
            }
        }
        catch {
            Write-Warning ("The smoke-test process could not be closed cleanly: {0}" -f $_.Exception.Message)
        }
    }

    Pop-Location
}
