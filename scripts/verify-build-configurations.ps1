[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
	throw $Message
}

function Assert-True([bool]$Condition, [string]$Message) {
	if (-not $Condition) {
		Fail $Message
	}
}

function Invoke-MsbuildProperties {
	param(
		[Parameter(Mandatory = $true)][string]$ProjectPath,
		[Parameter(Mandatory = $true)][string[]]$Properties,
		[string]$Configuration,
		[string]$TargetFramework,
		[switch]$PassEmptyConfiguration
	)

	$args = @($ProjectPath, '-nologo')
	if ($PassEmptyConfiguration) {
		$args += '-p:Configuration='
	}
	elseif ($Configuration) {
		$args += '-p:Configuration=' + $Configuration
	}
	if ($TargetFramework) {
		$args += '-p:TargetFramework=' + $TargetFramework
	}
	foreach ($property in $Properties) {
		$args += '-getProperty:' + $property
	}

	$output = & dotnet msbuild @args 2>&1
	if ($LASTEXITCODE -ne 0) {
		Fail ("MSBuild evaluation failed for {0}`n{1}" -f $ProjectPath, ($output | Out-String))
	}

	$rawOutput = ($output | Out-String).Trim()
	if ($rawOutput.StartsWith('{')) {
		return ($rawOutput | ConvertFrom-Json).Properties
	}

	if ($Properties.Count -eq 1) {
		return [pscustomobject]@{ $Properties[0] = $rawOutput }
	}

	Fail ("MSBuild property output was not valid JSON for {0}`n{1}" -f $ProjectPath, $rawOutput)
}

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $root 'KnownFirst.slnx'
$mainProjectPath = Join-Path $root 'KnownFirst.csproj'
$coreProjectPath = Join-Path $root 'KnownFirst.Core\KnownFirst.Core.csproj'
$testProjectPath = Join-Path $root 'KnownFirst.Tests\KnownFirst.Tests.csproj'

foreach ($path in @($solutionPath, $mainProjectPath, $coreProjectPath, $testProjectPath)) {
	Assert-True (Test-Path $path) ("Required file not found: {0}" -f $path)
}

[xml]$solutionXml = Get-Content $solutionPath -Raw
$solutionProjectPaths = @($solutionXml.Solution.Project | ForEach-Object { $_.Path })
Assert-True ($solutionProjectPaths.Count -eq 3) ("Expected 3 projects in the solution, found {0}." -f $solutionProjectPaths.Count)

foreach ($relativePath in $solutionProjectPaths) {
	$fullPath = Join-Path $root $relativePath
	Assert-True (Test-Path $fullPath) ("Solution references missing project: {0}" -f $relativePath)
}

[xml]$mainProjectXml = Get-Content $mainProjectPath -Raw
[xml]$testProjectXml = Get-Content $testProjectPath -Raw

$projectFilesToInspect = @(
	$mainProjectPath,
	$testProjectPath,
	$coreProjectPath
)

foreach ($projectFile in $projectFilesToInspect) {
	[xml]$projectXml = Get-Content $projectFile -Raw
	$includes = @()
	foreach ($node in $projectXml.Project.ItemGroup.ChildNodes) {
		if ($node.NodeType -ne 'Element') { continue }
		foreach ($attributeName in @('Include', 'Update', 'Remove', 'Link')) {
			if ($node.Attributes[$attributeName]) {
				$includes += $node.Attributes[$attributeName].Value
			}
		}
	}

	foreach ($include in $includes) {
		if ($include -match '(^|[\\/])(bin|obj|artifacts)([\\/]|$)') {
			Fail ("Generated output path is referenced from {0}: {1}" -f $projectFile, $include)
		}
	}
}

$knownFirstProps = Invoke-MsbuildProperties -ProjectPath $mainProjectPath -Properties @('Configurations', 'TargetFrameworks')
$configurations = ($knownFirstProps.Configurations -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
Assert-True ($configurations -contains 'Debug') 'Debug configuration is missing from KnownFirst.csproj.'
Assert-True ($configurations -contains 'Release') 'Release configuration is missing from KnownFirst.csproj.'
Assert-True ($configurations -contains 'BetaDiagnostic') 'BetaDiagnostic configuration is missing from KnownFirst.csproj.'

$targetFrameworks = ($knownFirstProps.TargetFrameworks -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
Assert-True ($targetFrameworks -contains 'net10.0-android') 'net10.0-android is missing from KnownFirst.csproj TargetFrameworks.'
Assert-True ($targetFrameworks -contains 'net10.0-windows10.0.19041.0') 'net10.0-windows10.0.19041.0 is missing from KnownFirst.csproj TargetFrameworks.'

$defaultEvaluation = Invoke-MsbuildProperties `
	-ProjectPath $mainProjectPath `
	-Properties @('Configuration', 'IntermediateOutputPath', 'DefaultItemExcludes')
Assert-True ($defaultEvaluation.Configuration -eq 'Debug') ("A plain project evaluation must default Configuration to Debug. Got '{0}'." -f $defaultEvaluation.Configuration)
Assert-True ($defaultEvaluation.IntermediateOutputPath -match 'obj[\\/]Debug[\\/]') ("The default intermediate path must include the Debug configuration. Got '{0}'." -f $defaultEvaluation.IntermediateOutputPath)
Assert-True ($defaultEvaluation.DefaultItemExcludes -match 'artifacts[\\/]') 'Generated artifacts must be excluded from default project items.'

$emptyGlobalEvaluation = Invoke-MsbuildProperties `
	-ProjectPath $mainProjectPath `
	-Properties @('Configuration', 'IntermediateOutputPath', 'TargetFrameworks') `
	-PassEmptyConfiguration
Assert-True ($emptyGlobalEvaluation.Configuration -eq 'Debug') ("An empty global Configuration must resolve to Debug. Got '{0}'." -f $emptyGlobalEvaluation.Configuration)
Assert-True ($emptyGlobalEvaluation.IntermediateOutputPath -match 'obj[\\/]Debug[\\/]') ("An empty global Configuration produced an unsafe intermediate path: '{0}'." -f $emptyGlobalEvaluation.IntermediateOutputPath)
Assert-True ($emptyGlobalEvaluation.TargetFrameworks -eq $knownFirstProps.TargetFrameworks) 'An empty global Configuration changed the target-framework list.'

$treatAsLocalProperties = @($mainProjectXml.Project.TreatAsLocalProperty -split ';' | ForEach-Object { $_.Trim() })
Assert-True ($treatAsLocalProperties -contains 'Configuration') 'KnownFirst.csproj must treat Configuration as local so the empty Visual Studio global value can be replaced safely.'

foreach ($configuration in @('Debug', 'BetaDiagnostic')) {
	$packageVersions = @($targetFrameworks | ForEach-Object {
		$properties = Invoke-MsbuildProperties `
			-ProjectPath $mainProjectPath `
			-Properties @('PackageVersion') `
			-Configuration $configuration `
			-TargetFramework $_
		$properties.PackageVersion
	})
	$distinctPackageVersions = @($packageVersions | Sort-Object -Unique)
	Assert-True ($distinctPackageVersions.Count -eq 1) ("{0} must use one PackageVersion across all target frameworks. Got: {1}" -f $configuration, ($distinctPackageVersions -join ', '))
}

$androidDebugProps = Invoke-MsbuildProperties -ProjectPath $mainProjectPath -Properties @('ApplicationVersion', 'ApplicationDisplayVersion') -Configuration 'Debug' -TargetFramework 'net10.0-android'
$androidDiagnosticProps = Invoke-MsbuildProperties -ProjectPath $mainProjectPath -Properties @('ApplicationVersion', 'ApplicationDisplayVersion') -Configuration 'BetaDiagnostic' -TargetFramework 'net10.0-android'
$windowsDebugProps = Invoke-MsbuildProperties -ProjectPath $mainProjectPath -Properties @('ApplicationVersion', 'ApplicationDisplayVersion') -Configuration 'Debug' -TargetFramework 'net10.0-windows10.0.19041.0'
$windowsDiagnosticProps = Invoke-MsbuildProperties -ProjectPath $mainProjectPath -Properties @('ApplicationVersion', 'ApplicationDisplayVersion', 'DefineConstants') -Configuration 'BetaDiagnostic' -TargetFramework 'net10.0-windows10.0.19041.0'

foreach ($props in @($androidDebugProps, $androidDiagnosticProps, $windowsDebugProps, $windowsDiagnosticProps)) {
	Assert-True ($props.ApplicationVersion -match '^\d+$') ("ApplicationVersion must be numeric. Got '{0}'." -f $props.ApplicationVersion)
	Assert-True (-not [string]::IsNullOrWhiteSpace($props.ApplicationDisplayVersion)) 'ApplicationDisplayVersion must not be empty.'
}

Assert-True ($androidDebugProps.ApplicationDisplayVersion -match 'beta') ("Android Debug display version should retain beta text. Got '{0}'." -f $androidDebugProps.ApplicationDisplayVersion)
Assert-True ($androidDiagnosticProps.ApplicationDisplayVersion -match 'beta') ("BetaDiagnostic display version should retain beta text. Got '{0}'." -f $androidDiagnosticProps.ApplicationDisplayVersion)
Assert-True ($windowsDebugProps.ApplicationDisplayVersion -match '^\d+\.\d+\.\d+$') ("Windows Debug display version must be numeric semver. Got '{0}'." -f $windowsDebugProps.ApplicationDisplayVersion)
Assert-True ($windowsDiagnosticProps.ApplicationDisplayVersion -match '^\d+\.\d+\.\d+$') ("Windows BetaDiagnostic display version must be numeric semver. Got '{0}'." -f $windowsDiagnosticProps.ApplicationDisplayVersion)
Assert-True (($windowsDiagnosticProps.DefineConstants -split ';') -contains 'KNOWNFIRST_DIAGNOSTICS') 'BetaDiagnostic must define KNOWNFIRST_DIAGNOSTICS on Windows.'

$testProjectProps = Invoke-MsbuildProperties -ProjectPath $testProjectPath -Properties @('TargetFramework')
Assert-True ($testProjectProps.TargetFramework -eq 'net10.0') ("KnownFirst.Tests.csproj TargetFramework should be net10.0. Got '{0}'." -f $testProjectProps.TargetFramework)

$coreProjectProps = Invoke-MsbuildProperties -ProjectPath $coreProjectPath -Properties @('TargetFramework')
Assert-True ($coreProjectProps.TargetFramework -eq 'net10.0') ("KnownFirst.Core.csproj TargetFramework should be net10.0. Got '{0}'." -f $coreProjectProps.TargetFramework)

$testProjectReferences = @(
	($testProjectXml.Project.ItemGroup.ProjectReference | ForEach-Object { $_.Include })
)
foreach ($reference in $testProjectReferences) {
	$resolvedReference = Join-Path (Split-Path $testProjectPath -Parent) $reference
	Assert-True (Test-Path $resolvedReference) ("Missing test project reference: {0}" -f $reference)
}

Write-Host 'Build configuration verification passed.'
