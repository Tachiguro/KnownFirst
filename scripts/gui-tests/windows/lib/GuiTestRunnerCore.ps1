<#
.SYNOPSIS
    Shared library for the KnownFirst Windows GUI test runner.

.DESCRIPTION
    Provides monitor selection/window placement, a restricted set of `winapp ui` wrappers
    (inspect / search / invoke / set-value / get-value / wait-for / window-only screenshot -
    never click, send-keys, drag, touch, pen, hover, or --capture-screen), a real-data
    hash/timestamp guard, and evidence (report.md / summary.json / runner.log / screenshots)
    helpers. Scenario scripts dot-source this file and call into it; they never shell out to
    `winapp` directly, so the "no forbidden input" contract has a single enforcement point.

    Windows PowerShell 5.1 compatible: no ternary operator, no null-coalescing operator.
#>

Set-StrictMode -Version Latest

# --- Module-level (script-scope) state, initialized by Initialize-GuiTestRun --------------

$script:RunnerLogPath = $null
$script:TargetHwnd = $null
$script:TargetPid = $null
$script:EvidenceDir = $null
$script:ScreenshotsDir = $null
$script:UiaDir = $null
$script:ScreenshotHashMap = @{}
$script:ScreenshotSequence = 0
$script:StepLog = New-Object System.Collections.Generic.List[object]
$script:AssertionLog = New-Object System.Collections.Generic.List[object]

# --- Logging ---------------------------------------------------------------------------

function Write-RunnerLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('Info', 'Warn', 'Error', 'Trace')][string]$Level = 'Info'
    )

    $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
    $line = "[$timestamp] [$Level] $Message"

    $color = 'Gray'
    switch ($Level) {
        'Info' { $color = 'White' }
        'Warn' { $color = 'Yellow' }
        'Error' { $color = 'Red' }
        'Trace' { $color = 'DarkGray' }
    }
    Write-Host $line -ForegroundColor $color

    if ($script:RunnerLogPath) {
        Add-Content -LiteralPath $script:RunnerLogPath -Value $line -Encoding UTF8
    }
}

# --- Run / evidence directory setup -----------------------------------------------------

function New-GuiTestRunContext {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ScenarioId
    )

    $shortCommit = (& git -C $ProjectRoot rev-parse --short HEAD 2>$null | Select-Object -First 1).Trim()
    if (-not $shortCommit) { $shortCommit = 'unknown' }

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $runId = "$timestamp-$shortCommit"

    $evidenceDir = Join-Path $ProjectRoot "artifacts\gui-tests\windows\runs\$runId"
    $profilesRoot = Join-Path $ProjectRoot "artifacts\gui-tests\windows\profiles"
    $liveProfileDir = Join-Path $profilesRoot $runId

    foreach ($dir in @($evidenceDir, "$evidenceDir\screenshots", "$evidenceDir\uia", "$evidenceDir\profile", $liveProfileDir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if ((Get-ChildItem -LiteralPath $liveProfileDir -Force | Measure-Object).Count -gt 0) {
        throw "Isolated profile directory '$liveProfileDir' is not new/empty. Failing closed rather than reusing it."
    }

    $script:EvidenceDir = $evidenceDir
    $script:ScreenshotsDir = Join-Path $evidenceDir 'screenshots'
    $script:UiaDir = Join-Path $evidenceDir 'uia'
    $script:RunnerLogPath = Join-Path $evidenceDir 'runner.log'
    $script:ScreenshotHashMap = @{}
    $script:ScreenshotSequence = 0
    $script:StepLog = New-Object System.Collections.Generic.List[object]
    $script:AssertionLog = New-Object System.Collections.Generic.List[object]

    New-Item -ItemType File -Path $script:RunnerLogPath -Force | Out-Null

    Write-RunnerLog "GUI test run starting. Scenario = $ScenarioId, RunId = $runId"
    Write-RunnerLog "Evidence directory: $evidenceDir"
    Write-RunnerLog "Isolated live profile directory: $liveProfileDir"

    return [pscustomobject]@{
        ProjectRoot     = $ProjectRoot
        ScenarioId      = $ScenarioId
        RunId           = $runId
        ShortCommit     = $shortCommit
        EvidenceDir     = $evidenceDir
        ScreenshotsDir  = $script:ScreenshotsDir
        UiaDir          = $script:UiaDir
        ReportPath      = Join-Path $evidenceDir 'report.md'
        SummaryPath     = Join-Path $evidenceDir 'summary.json'
        RunnerLogPath   = $script:RunnerLogPath
        LiveProfileDir  = $liveProfileDir
        EvidenceProfileDir = Join-Path $evidenceDir 'profile'
    }
}

# --- Monitor selection and window placement ---------------------------------------------
#
# Monitor identity (device name, target device path, friendly name, output technology) comes
# from the Windows display configuration APIs (QueryDisplayConfig / DisplayConfigGetDeviceInfo),
# never from array/enumeration order or from parsing "\\.\DISPLAYn" as a Windows Settings
# display number - neither is a reliable source of truth (see the -1714,-52 incident this
# replaced: a "\\.\DISPLAY1" that was not actually the laptop's internal panel). Desktop/working-
# area bounds and primary status are correlated onto that identity via
# System.Windows.Forms.Screen, matched by GDI device name.

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class GuiTestWin32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    public static extern bool AreDpiAwarenessContextsEqual(IntPtr dpiContextA, IntPtr dpiContextB);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
}

public static class GuiTestDisplayConfig
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // Opaque: QueryDisplayConfig requires a correctly SIZED mode-info buffer, but this runner
    // never reads mode-info content (only path source/target identity), so only the size (64
    // bytes on x64, matching the native union) needs to be reserved.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);
}
'@ -ErrorAction SilentlyContinue

# A caller that has not opted into per-monitor DPI awareness gets "DPI-virtualized"
# (silently rescaled/repositioned) results from GetSystemMetrics-based monitor enumeration,
# GetWindowRect and SetWindowPos. On a mixed-DPI multi-monitor setup this makes the working
# area this script reads and the physical pixels a real (DPI-aware) window like KnownFirst
# occupies disagree - producing exactly the kind of impossible off-monitor bounds this
# monitor-placement rework exists to fix. Must be set once, before any Screen/GetWindowRect/
# SetWindowPos/display-config call in this process, and is verified rather than assumed.
$DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = [IntPtr]-4
[void][GuiTestWin32]::SetProcessDpiAwarenessContext($DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
$script:CurrentDpiAwarenessContext = [GuiTestWin32]::GetThreadDpiAwarenessContext()
if (-not [GuiTestWin32]::AreDpiAwarenessContextsEqual($script:CurrentDpiAwarenessContext, $DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) {
    throw ('This process could not be switched to per-monitor-v2 DPI awareness, which monitor ' +
        'discovery and window placement both depend on for correct measurement across mixed-DPI ' +
        'displays. Failing closed rather than measuring/placing windows using unreliable ' +
        'DPI-virtualized coordinates.')
}

# Output-technology values from the DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY enum (wingdi.h).
# INTERNAL, DISPLAYPORT_EMBEDDED and UDI_EMBEDDED are documented embedded-panel technologies;
# LVDS is the legacy internal-panel interface. These four are treated as internal/embedded
# candidates. Everything else (HDMI, DVI, external DisplayPort, VGA/HD15, wireless, indirect/
# virtual, ...) is external or non-physical and is never treated as internal.
$script:InternalOutputTechnologyValues = [uint32[]]@(2147483648, 6, 11, 13)

function ConvertTo-OutputTechnologyName {
    param([Parameter(Mandatory = $true)][uint32]$RawValue)
    switch ($RawValue) {
        4294967295 { return 'OTHER' }              # -1 as UINT32
        0 { return 'HD15' }
        1 { return 'SVIDEO' }
        2 { return 'COMPOSITE_VIDEO' }
        3 { return 'COMPONENT_VIDEO' }
        4 { return 'DVI' }
        5 { return 'HDMI' }
        6 { return 'LVDS' }
        8 { return 'D_JPN' }
        9 { return 'SDI' }
        10 { return 'DISPLAYPORT_EXTERNAL' }
        11 { return 'DISPLAYPORT_EMBEDDED' }
        12 { return 'UDI_EXTERNAL' }
        13 { return 'UDI_EMBEDDED' }
        14 { return 'SDTVDONGLE' }
        15 { return 'MIRACAST' }
        16 { return 'INDIRECT_WIRED' }
        17 { return 'INDIRECT_VIRTUAL' }
        18 { return 'DISPLAYPORT_USB_TUNNEL' }
        2147483648 { return 'INTERNAL' }           # 0x80000000
        default { return ('UNKNOWN(0x{0:X8})' -f $RawValue) }
    }
}

function Test-IsInternalOutputTechnology {
    param([Parameter(Mandatory = $true)][uint32]$RawValue)
    return ($script:InternalOutputTechnologyValues -contains $RawValue)
}

function Get-DisplayMonitors {
    Add-Type -AssemblyName System.Windows.Forms

    $QDC_ONLY_ACTIVE_PATHS = [uint32]0x00000002
    $DISPLAYCONFIG_PATH_ACTIVE = [uint32]0x00000001
    $DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = [uint32]1
    $DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = [uint32]2
    $ERROR_SUCCESS = 0

    [uint32]$numPaths = 0
    [uint32]$numModes = 0
    $sizeResult = [GuiTestDisplayConfig]::GetDisplayConfigBufferSizes($QDC_ONLY_ACTIVE_PATHS, [ref]$numPaths, [ref]$numModes)
    if ($sizeResult -ne $ERROR_SUCCESS) {
        throw "GetDisplayConfigBufferSizes failed (error $sizeResult). Cannot enumerate displays via Windows display configuration."
    }
    if ($numPaths -eq 0) {
        throw 'GetDisplayConfigBufferSizes reported zero active display paths.'
    }

    $pathArrayType = [type]'GuiTestDisplayConfig+DISPLAYCONFIG_PATH_INFO'
    $modeArrayType = [type]'GuiTestDisplayConfig+DISPLAYCONFIG_MODE_INFO'
    $pathArray = [System.Array]::CreateInstance($pathArrayType, $numPaths)
    $modeArray = [System.Array]::CreateInstance($modeArrayType, $numModes)

    $queryResult = [GuiTestDisplayConfig]::QueryDisplayConfig(
        $QDC_ONLY_ACTIVE_PATHS, [ref]$numPaths, $pathArray, [ref]$numModes, $modeArray, [IntPtr]::Zero)
    if ($queryResult -ne $ERROR_SUCCESS) {
        throw "QueryDisplayConfig failed (error $queryResult). Cannot enumerate displays via Windows display configuration."
    }

    $screensByDeviceName = @{}
    foreach ($screen in [System.Windows.Forms.Screen]::AllScreens) {
        $screensByDeviceName[$screen.DeviceName.ToUpperInvariant()] = $screen
    }

    $records = New-Object System.Collections.Generic.List[object]
    $logicalIndex = 0

    for ($i = 0; $i -lt $numPaths; $i++) {
        $path = $pathArray[$i]
        if (($path.flags -band $DISPLAYCONFIG_PATH_ACTIVE) -eq 0) { continue }

        $sourceHeader = New-Object GuiTestDisplayConfig+DISPLAYCONFIG_DEVICE_INFO_HEADER
        $sourceHeader.type = $DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
        $sourceHeader.size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'GuiTestDisplayConfig+DISPLAYCONFIG_SOURCE_DEVICE_NAME')
        $sourceHeader.adapterId = $path.sourceInfo.adapterId
        $sourceHeader.id = $path.sourceInfo.id
        $sourceName = New-Object GuiTestDisplayConfig+DISPLAYCONFIG_SOURCE_DEVICE_NAME
        $sourceName.header = $sourceHeader
        $srcResult = [GuiTestDisplayConfig]::DisplayConfigGetDeviceInfo([ref]$sourceName)
        if ($srcResult -ne $ERROR_SUCCESS) {
            Write-RunnerLog "DisplayConfigGetDeviceInfo (source name) failed for display path $i (error $srcResult); skipping this path." -Level Warn
            continue
        }

        $targetHeader = New-Object GuiTestDisplayConfig+DISPLAYCONFIG_DEVICE_INFO_HEADER
        $targetHeader.type = $DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
        $targetHeader.size = [System.Runtime.InteropServices.Marshal]::SizeOf([type]'GuiTestDisplayConfig+DISPLAYCONFIG_TARGET_DEVICE_NAME')
        $targetHeader.adapterId = $path.targetInfo.adapterId
        $targetHeader.id = $path.targetInfo.id
        $targetName = New-Object GuiTestDisplayConfig+DISPLAYCONFIG_TARGET_DEVICE_NAME
        $targetName.header = $targetHeader
        $tgtResult = [GuiTestDisplayConfig]::DisplayConfigGetDeviceInfo([ref]$targetName)
        if ($tgtResult -ne $ERROR_SUCCESS) {
            Write-RunnerLog "DisplayConfigGetDeviceInfo (target name) failed for display path $i (error $tgtResult); skipping this path." -Level Warn
            continue
        }

        $gdiDeviceName = $sourceName.viewGdiDeviceName
        $screen = $screensByDeviceName[$gdiDeviceName.ToUpperInvariant()]
        if (-not $screen) {
            Write-RunnerLog "Display path $i (GDI device '$gdiDeviceName') has no matching System.Windows.Forms.Screen; it cannot be mapped to a usable desktop work area, so it is excluded from selection." -Level Warn
            continue
        }

        $outputTechnologyRaw = [uint32]$targetName.outputTechnology
        $friendlyName = $targetName.monitorFriendlyDeviceName
        if ([string]::IsNullOrWhiteSpace($friendlyName)) { $friendlyName = $gdiDeviceName }

        $record = [pscustomobject]@{
            LogicalIndex        = $logicalIndex
            DeviceName           = $gdiDeviceName
            TargetDevicePath     = $targetName.monitorDevicePath
            FriendlyName         = $friendlyName
            IsPrimary            = $screen.Primary
            IsInternalCandidate  = (Test-IsInternalOutputTechnology -RawValue $outputTechnologyRaw)
            OutputTechnology     = (ConvertTo-OutputTechnologyName -RawValue $outputTechnologyRaw)
            OutputTechnologyRaw  = $outputTechnologyRaw
            Bounds               = [pscustomobject]@{
                X = $screen.Bounds.X; Y = $screen.Bounds.Y
                Width = $screen.Bounds.Width; Height = $screen.Bounds.Height
            }
            WorkingArea          = [pscustomobject]@{
                X = $screen.WorkingArea.X; Y = $screen.WorkingArea.Y
                Width = $screen.WorkingArea.Width; Height = $screen.WorkingArea.Height
            }
            Area                 = $screen.Bounds.Width * $screen.Bounds.Height
        }
        $records.Add($record) | Out-Null
        $logicalIndex++
    }

    if ($records.Count -eq 0) {
        throw 'Windows display configuration query returned no active display path that could be resolved to a usable desktop work area.'
    }

    return $records.ToArray()
}

function Format-DisplayMonitorTable {
    param([Parameter(Mandatory = $true)][object[]]$Monitors)

    $rows = foreach ($m in $Monitors) {
        [pscustomobject]@{
            Index      = $m.LogicalIndex
            DeviceName = $m.DeviceName
            Friendly   = $m.FriendlyName
            Primary    = $m.IsPrimary
            Internal   = $m.IsInternalCandidate
            Technology = $m.OutputTechnology
            Bounds     = ('{0},{1} {2}x{3}' -f $m.Bounds.X, $m.Bounds.Y, $m.Bounds.Width, $m.Bounds.Height)
            WorkArea   = ('{0},{1} {2}x{3}' -f $m.WorkingArea.X, $m.WorkingArea.Y, $m.WorkingArea.Width, $m.WorkingArea.Height)
            TargetPath = $m.TargetDevicePath
        }
    }
    return (($rows | Format-Table -AutoSize | Out-String).TrimEnd())
}

function Select-InternalMonitor {
    param([Parameter(Mandatory = $true)][object[]]$Monitors)

    $candidates = @($Monitors | Where-Object { $_.IsInternalCandidate })
    if ($candidates.Count -eq 0) {
        throw 'No internal display was found via Windows display configuration (no active display path reports an internal/embedded output technology). Failing closed rather than falling back to another monitor.'
    }
    if ($candidates.Count -gt 1) {
        $names = ($candidates | ForEach-Object { "$($_.FriendlyName) ($($_.DeviceName))" }) -join ', '
        throw "Multiple internal-display candidates were found and cannot be resolved deterministically: $names. Failing closed rather than guessing."
    }
    return [pscustomobject]@{
        Monitor = $candidates[0]
        Reason  = 'Sole active display path reporting an internal/embedded output technology (Windows display configuration).'
    }
}

function Select-PrimaryMonitor {
    param([Parameter(Mandatory = $true)][object[]]$Monitors)

    $candidates = @($Monitors | Where-Object { $_.IsPrimary })
    if ($candidates.Count -eq 0) {
        throw 'No display reported as primary (System.Windows.Forms.Screen.Primary) was found.'
    }
    if ($candidates.Count -gt 1) {
        throw 'Multiple displays reported as primary; cannot resolve deterministically.'
    }
    return [pscustomobject]@{ Monitor = $candidates[0]; Reason = 'Windows-reported primary display.' }
}

function Select-LargestNonPrimaryMonitor {
    param([Parameter(Mandatory = $true)][object[]]$Monitors)

    $candidates = @($Monitors | Where-Object { -not $_.IsPrimary })
    if ($candidates.Count -eq 0) {
        throw 'No non-primary display was detected; -MonitorTarget LargestNonPrimary requires at least one non-primary monitor.'
    }
    $selected = $candidates | Sort-Object -Property Area -Descending | Select-Object -First 1
    return [pscustomobject]@{ Monitor = $selected; Reason = 'Largest-area non-primary display (Bounds Width*Height), among non-primary displays.' }
}

function Select-MonitorByDeviceName {
    param(
        [Parameter(Mandatory = $true)][object[]]$Monitors,
        [Parameter(Mandatory = $true)][string]$DeviceName
    )

    $candidates = @($Monitors | Where-Object { $_.DeviceName -eq $DeviceName })
    if ($candidates.Count -eq 0) {
        $available = ($Monitors | ForEach-Object { $_.DeviceName }) -join ', '
        throw "No display matches -MonitorDeviceName '$DeviceName'. Available device names: $available"
    }
    if ($candidates.Count -gt 1) {
        throw "Multiple displays matched -MonitorDeviceName '$DeviceName'; cannot resolve deterministically."
    }
    return [pscustomobject]@{ Monitor = $candidates[0]; Reason = "Explicit -MonitorDeviceName match ('$DeviceName')." }
}

function Select-DisplayMonitor {
    param(
        [Parameter(Mandatory = $true)][object[]]$Monitors,
        [Parameter(Mandatory = $true)][ValidateSet('Internal', 'Primary', 'LargestNonPrimary', 'DeviceName')][string]$MonitorTarget,
        [string]$MonitorDeviceName
    )

    switch ($MonitorTarget) {
        'Internal' { return (Select-InternalMonitor -Monitors $Monitors) }
        'Primary' { return (Select-PrimaryMonitor -Monitors $Monitors) }
        'LargestNonPrimary' { return (Select-LargestNonPrimaryMonitor -Monitors $Monitors) }
        'DeviceName' {
            if ([string]::IsNullOrWhiteSpace($MonitorDeviceName)) {
                throw '-MonitorDeviceName is required when -MonitorTarget is DeviceName.'
            }
            return (Select-MonitorByDeviceName -Monitors $Monitors -DeviceName $MonitorDeviceName)
        }
    }
}

function Get-WindowRectSnapshot {
    # Raw (non-DWM) outer window rect via GetWindowRect, in physical pixels (this process is
    # per-monitor-v2 DPI aware - see the DPI-awareness enforcement above).
    param([Parameter(Mandatory = $true)][IntPtr]$Hwnd)

    $rect = New-Object GuiTestWin32+RECT
    [GuiTestWin32]::GetWindowRect($Hwnd, [ref]$rect) | Out-Null
    return [pscustomobject]@{
        Left = $rect.Left; Top = $rect.Top; Right = $rect.Right; Bottom = $rect.Bottom
        Width = $rect.Right - $rect.Left; Height = $rect.Bottom - $rect.Top
    }
}

function Get-WindowExtendedFrameBounds {
    # The DWM-visible frame bounds, which can differ from GetWindowRect by an invisible resize-
    # border/drop-shadow margin. Containment must be checked against this, not the raw window
    # rect, or a window can appear fully placed while a few invisible pixels poke onto the next
    # monitor. This is the "most reliable visible-window bounds" source: physical pixels, not
    # DPI-virtualized, and not the raw (frame-inflated) window rect.
    param([Parameter(Mandatory = $true)][IntPtr]$Hwnd)

    $DWMWA_EXTENDED_FRAME_BOUNDS = 9
    $rect = New-Object GuiTestWin32+RECT
    $hr = [GuiTestWin32]::DwmGetWindowAttribute(
        $Hwnd, $DWMWA_EXTENDED_FRAME_BOUNDS, [ref]$rect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type]'GuiTestWin32+RECT'))
    if ($hr -ne 0) {
        throw ('DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS) failed (HRESULT 0x{0:X8}) for HWND {1}.' -f $hr, $Hwnd)
    }
    return [pscustomobject]@{
        Left = $rect.Left; Top = $rect.Top; Right = $rect.Right; Bottom = $rect.Bottom
        Width = $rect.Right - $rect.Left; Height = $rect.Bottom - $rect.Top
    }
}

function Get-WindowFrameMargin {
    # Non-client margin between the raw window rect and the DWM-visible frame, measured fresh
    # against the window's CURRENT position/DPI (this margin can itself change with DPI, so a
    # margin measured on one monitor must not be reused after the window has moved to another).
    param([Parameter(Mandatory = $true)][IntPtr]$Hwnd)

    $raw = Get-WindowRectSnapshot -Hwnd $Hwnd
    $visible = Get-WindowExtendedFrameBounds -Hwnd $Hwnd
    return [pscustomobject]@{
        Left = $visible.Left - $raw.Left
        Top = $visible.Top - $raw.Top
        Right = $raw.Right - $visible.Right
        Bottom = $raw.Bottom - $visible.Bottom
    }
}

function Get-CenteredRectInWorkingArea {
    # A rectangle centered in the given working area, sized to a fraction of it. Used for the
    # staging move: deliberately small and margin-agnostic, so it is safely inside the target
    # monitor regardless of the (not-yet-known-for-this-DPI) non-client frame margin.
    param(
        [Parameter(Mandatory = $true)][object]$WorkingArea,
        [Parameter(Mandatory = $true)][double]$SizeFraction
    )

    $width = [int]([Math]::Floor($WorkingArea.Width * $SizeFraction))
    $height = [int]([Math]::Floor($WorkingArea.Height * $SizeFraction))
    $left = $WorkingArea.X + [int]([Math]::Floor(($WorkingArea.Width - $width) / 2))
    $top = $WorkingArea.Y + [int]([Math]::Floor(($WorkingArea.Height - $height) / 2))
    return [pscustomobject]@{ Left = $left; Top = $top; Width = $width; Height = $height }
}

function Get-MonitorHandleForRegion {
    # HMONITOR for the center point of a Bounds/WorkingArea-shaped object (X, Y, Width, Height).
    param([Parameter(Mandatory = $true)][object]$Region)

    $MONITOR_DEFAULTTONEAREST = [uint32]2
    $point = New-Object GuiTestWin32+POINT
    $point.X = $Region.X + [int]([Math]::Floor($Region.Width / 2))
    $point.Y = $Region.Y + [int]([Math]::Floor($Region.Height / 2))
    return [GuiTestWin32]::MonitorFromPoint($point, $MONITOR_DEFAULTTONEAREST)
}

function Get-MonitorHandleForWindow {
    # HMONITOR that HWND currently belongs to - the ground truth used to prove placement landed
    # on the selected monitor, independent of any coordinate-based containment arithmetic.
    param([Parameter(Mandatory = $true)][IntPtr]$Hwnd)

    $MONITOR_DEFAULTTONEAREST = [uint32]2
    return [GuiTestWin32]::MonitorFromWindow($Hwnd, $MONITOR_DEFAULTTONEAREST)
}

function Measure-MonitorOverlapPixels {
    # Overlap area, in pixels, between a visible-bounds rect and a monitor's Bounds rect.
    param([Parameter(Mandatory = $true)][object]$VisibleBounds, [Parameter(Mandatory = $true)][object]$MonitorBounds)

    $overlapLeft = [Math]::Max($VisibleBounds.Left, $MonitorBounds.X)
    $overlapTop = [Math]::Max($VisibleBounds.Top, $MonitorBounds.Y)
    $overlapRight = [Math]::Min($VisibleBounds.Right, ($MonitorBounds.X + $MonitorBounds.Width))
    $overlapBottom = [Math]::Min($VisibleBounds.Bottom, ($MonitorBounds.Y + $MonitorBounds.Height))
    $overlapWidth = [Math]::Max(0, ($overlapRight - $overlapLeft))
    $overlapHeight = [Math]::Max(0, ($overlapBottom - $overlapTop))
    return ($overlapWidth * $overlapHeight)
}

function Wait-ForWindowBoundsStable {
    # Polls GetWindowRect until it stops changing (a per-monitor-DPI-aware app can keep
    # resizing itself for a short time after a cross-monitor SetWindowPos, e.g. WM_DPICHANGED-
    # driven auto-resize) or the timeout elapses. Never throws: callers measure/act on whatever
    # the last-observed bounds are, and decide pass/fail from that.
    param(
        [Parameter(Mandatory = $true)][IntPtr]$Hwnd,
        [int]$TimeoutMs = 3000,
        [int]$PollIntervalMs = 150,
        [int]$StableReadingsRequired = 3
    )

    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $stableCount = 0
    $lastBounds = Get-WindowRectSnapshot -Hwnd $Hwnd
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds $PollIntervalMs
        $currentBounds = Get-WindowRectSnapshot -Hwnd $Hwnd
        if ($currentBounds.Left -eq $lastBounds.Left -and $currentBounds.Top -eq $lastBounds.Top -and
            $currentBounds.Width -eq $lastBounds.Width -and $currentBounds.Height -eq $lastBounds.Height) {
            $stableCount++
        }
        else {
            $stableCount = 0
        }
        $lastBounds = $currentBounds
        if ($stableCount -ge $StableReadingsRequired) { break }
    }
    return $lastBounds
}

function Move-WindowIntoMonitorWorkingArea {
    # Places KnownFirst inside the selected monitor's working area using SetWindowPos - no
    # ShowWindow(SW_MAXIMIZE) - in two stages:
    #
    #  1. Staging: move to a small (<=~70%), centered rectangle safely inside the target working
    #     area. Jumping straight from the primary display (often a different DPI) to a full-size
    #     final rectangle races the OS's WM_DPICHANGED-driven auto-resize: a per-monitor-DPI-
    #     aware app can resize itself (to preserve its logical/DIP size) *after* our SetWindowPos
    #     call returns, silently overriding the size we requested (observed on this app: a
    #     request sized to the working area came back ~35-100% larger once its own DPI handling
    #     ran). Staging absorbs that transition first, away from any monitor edge.
    #  2. Final fit: once bounds have stabilized and MonitorFromWindow confirms the window is on
    #     the selected monitor, measure the non-client frame margin fresh (it can also change
    #     with DPI) and fit the window to the working area. Repeated, bounded, until the visible
    #     DWM frame is fully contained or the retry budget is exhausted.
    param(
        [Parameter(Mandatory = $true)][long]$Hwnd,
        [Parameter(Mandatory = $true)][object]$Monitor,
        [object[]]$AllMonitors = @()
    )

    $hwndPtr = [IntPtr]$Hwnd
    $SW_RESTORE = 9
    $SWP_NOZORDER = 0x0004
    $StagingSizeFraction = 0.7
    $MaxCorrectivePasses = 3

    [GuiTestWin32]::ShowWindowAsync($hwndPtr, $SW_RESTORE) | Out-Null
    Start-Sleep -Milliseconds 150

    $beforeBounds = Get-WindowRectSnapshot -Hwnd $hwndPtr
    $workingArea = $Monitor.WorkingArea
    $expectedMonitorHandle = Get-MonitorHandleForRegion -Region $workingArea

    Write-RunnerLog ("Placement: selected monitor '{0}' ({1}); working area ({2},{3}) {4}x{5} (physical pixels)" -f `
        $Monitor.FriendlyName, $Monitor.DeviceName, $workingArea.X, $workingArea.Y, $workingArea.Width, $workingArea.Height)
    Write-RunnerLog ("Placement: window bounds before staging: ({0},{1})-({2},{3}) = {4}x{5}" -f `
        $beforeBounds.Left, $beforeBounds.Top, $beforeBounds.Right, $beforeBounds.Bottom, $beforeBounds.Width, $beforeBounds.Height)

    # --- Stage 1: staging move --------------------------------------------------------------
    $stagingRect = Get-CenteredRectInWorkingArea -WorkingArea $workingArea -SizeFraction $StagingSizeFraction
    Write-RunnerLog ("Placement: staging rectangle requested: ({0},{1}) {2}x{3} ({4:P0} of working area, centered, never crosses a display boundary)" -f `
        $stagingRect.Left, $stagingRect.Top, $stagingRect.Width, $stagingRect.Height, $StagingSizeFraction)
    $stagedOk = [GuiTestWin32]::SetWindowPos(
        $hwndPtr, [IntPtr]::Zero, $stagingRect.Left, $stagingRect.Top, $stagingRect.Width, $stagingRect.Height, $SWP_NOZORDER)
    if (-not $stagedOk) {
        throw "SetWindowPos (staging pass) failed to move HWND $Hwnd toward monitor $($Monitor.FriendlyName) ($($Monitor.DeviceName))."
    }

    $stagingActualBounds = Wait-ForWindowBoundsStable -Hwnd $hwndPtr -TimeoutMs 4000 -PollIntervalMs 150 -StableReadingsRequired 3
    $monitorHandleAfterStaging = Get-MonitorHandleForWindow -Hwnd $hwndPtr
    $stagingMonitorMatch = ($monitorHandleAfterStaging -eq $expectedMonitorHandle)
    Write-RunnerLog ("Placement: actual bounds after staging: ({0},{1})-({2},{3}) = {4}x{5}; MonitorFromWindow matches selected monitor: {6}" -f `
        $stagingActualBounds.Left, $stagingActualBounds.Top, $stagingActualBounds.Right, $stagingActualBounds.Bottom, `
        $stagingActualBounds.Width, $stagingActualBounds.Height, $stagingMonitorMatch) -Level $(if ($stagingMonitorMatch) { 'Info' } else { 'Warn' })

    # --- Stage 2: final fit, with bounded corrective retries --------------------------------
    $passResults = New-Object System.Collections.Generic.List[object]
    $contained = $false
    $rawBounds = $null
    $visibleBounds = $null
    $monitorHandle = $monitorHandleAfterStaging
    $onExpectedMonitor = $stagingMonitorMatch

    for ($pass = 1; $pass -le $MaxCorrectivePasses; $pass++) {
        $margin = Get-WindowFrameMargin -Hwnd $hwndPtr
        $target = [pscustomobject]@{
            Left   = $workingArea.X - $margin.Left
            Top    = $workingArea.Y - $margin.Top
            Width  = $workingArea.Width + $margin.Left + $margin.Right
            Height = $workingArea.Height + $margin.Top + $margin.Bottom
        }
        Write-RunnerLog ("Placement: final rectangle requested (pass {0}/{1}): ({2},{3}) {4}x{5} [frame margin L={6} T={7} R={8} B={9}]" -f `
            $pass, $MaxCorrectivePasses, $target.Left, $target.Top, $target.Width, $target.Height, `
            $margin.Left, $margin.Top, $margin.Right, $margin.Bottom)

        $movedFinal = [GuiTestWin32]::SetWindowPos(
            $hwndPtr, [IntPtr]::Zero, $target.Left, $target.Top, $target.Width, $target.Height, $SWP_NOZORDER)
        if (-not $movedFinal) {
            throw "SetWindowPos (final pass $pass) failed to place HWND $Hwnd onto monitor $($Monitor.FriendlyName) ($($Monitor.DeviceName))."
        }

        Wait-ForWindowBoundsStable -Hwnd $hwndPtr -TimeoutMs 2500 -PollIntervalMs 150 -StableReadingsRequired 2 | Out-Null
        $rawBounds = Get-WindowRectSnapshot -Hwnd $hwndPtr
        $visibleBounds = Get-WindowExtendedFrameBounds -Hwnd $hwndPtr
        $monitorHandle = Get-MonitorHandleForWindow -Hwnd $hwndPtr
        $onExpectedMonitor = ($monitorHandle -eq $expectedMonitorHandle)

        $contained = (
            $onExpectedMonitor -and
            $visibleBounds.Left -ge $workingArea.X -and
            $visibleBounds.Top -ge $workingArea.Y -and
            $visibleBounds.Right -le ($workingArea.X + $workingArea.Width) -and
            $visibleBounds.Bottom -le ($workingArea.Y + $workingArea.Height))

        $overlaps = New-Object System.Collections.Generic.List[object]
        foreach ($otherMonitor in $AllMonitors) {
            if ($otherMonitor.DeviceName -eq $Monitor.DeviceName) { continue }
            $overlapPixels = Measure-MonitorOverlapPixels -VisibleBounds $visibleBounds -MonitorBounds $otherMonitor.Bounds
            $overlaps.Add([pscustomobject]@{
                DeviceName    = $otherMonitor.DeviceName
                FriendlyName  = $otherMonitor.FriendlyName
                OverlapPixels = $overlapPixels
            }) | Out-Null
            $overlapLevel = if ($overlapPixels -gt 0) { 'Warn' } else { 'Trace' }
            Write-RunnerLog ("Placement: pass {0} overlap with '{1}' ({2}): {3} px" -f $pass, $otherMonitor.FriendlyName, $otherMonitor.DeviceName, $overlapPixels) -Level $overlapLevel
        }

        Write-RunnerLog ("Placement: actual bounds after pass {0}: raw=({1},{2})-({3},{4}) visible=({5},{6})-({7},{8}); MonitorFromWindow matches selected: {9}; contained: {10}" -f `
            $pass, $rawBounds.Left, $rawBounds.Top, $rawBounds.Right, $rawBounds.Bottom, `
            $visibleBounds.Left, $visibleBounds.Top, $visibleBounds.Right, $visibleBounds.Bottom, $onExpectedMonitor, $contained)

        $passResults.Add([pscustomobject]@{
            Pass              = $pass
            RequestedRect     = $target
            FrameMargin       = $margin
            RawBounds         = $rawBounds
            VisibleBounds     = $visibleBounds
            OnExpectedMonitor = $onExpectedMonitor
            Contained         = $contained
            Overlaps          = $overlaps.ToArray()
        }) | Out-Null

        if ($contained) { break }
    }

    if (-not $contained) {
        Write-RunnerLog ("Placement: window could not be fully contained within the selected monitor working area after {0} corrective pass(es)." -f $passResults.Count) -Level Error
    }

    return [pscustomobject]@{
        BeforeBounds                      = $beforeBounds
        StagingRequested                  = $stagingRect
        StagingActualBounds               = $stagingActualBounds
        StagingMonitorMatch               = $stagingMonitorMatch
        RawBounds                         = $rawBounds
        VisibleBounds                     = $visibleBounds
        WorkingArea                       = [pscustomobject]@{ X = $workingArea.X; Y = $workingArea.Y; Width = $workingArea.Width; Height = $workingArea.Height }
        CorrectivePasses                  = $passResults.ToArray()
        CorrectivePassCount               = $passResults.Count
        MonitorFromWindowMatchesSelected  = $onExpectedMonitor
        Contained                         = $contained
    }
}

# --- winapp wrappers: ONLY inspect / search / invoke / set-value / get-value / wait-for / --
# --- window-only screenshot / list-windows are exposed. No click, send-keys, drag, touch, --
# --- pen, hover, or --capture-screen exist anywhere in this file. -------------------------

function Invoke-WinAppCommand {
    param(
        # Not [Parameter(Mandatory)]: Mandatory on a string[] parameter implicitly rejects any
        # array containing an empty-string element (ParameterArgumentValidationErrorEmptyString-
        # NotAllowed), which a '-w' argument built from a not-yet-set $script:TargetHwnd would
        # trigger even though the array itself is perfectly well-formed.
        [string[]]$Arguments = @(),
        [switch]$AllowFailure
    )

    Write-RunnerLog "winapp $($Arguments -join ' ')" -Level Trace
    # Deliberately not redirecting stderr (no 2>&1): under $ErrorActionPreference = 'Stop',
    # capturing a native command's stderr into the pipeline wraps each line as a terminating
    # NativeCommandError even on a clean exit, which would bypass -AllowFailure entirely.
    # stderr still reaches the console directly; only stdout is captured here.
    $output = & winapp @Arguments
    $exitCode = $LASTEXITCODE
    if ($output) {
        Write-RunnerLog (($output | Out-String).TrimEnd()) -Level Trace
    }

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "winapp command failed (exit $exitCode): winapp $($Arguments -join ' ')`n$($output -join "`n")"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = $output
        Succeeded = ($exitCode -eq 0)
    }
}

# winapp --json output is heterogeneous: sibling elements in the same array often have
# different property sets (e.g. only some have "name" or "children"). Under
# Set-StrictMode -Version Latest, dotting into a property a specific object instance does not
# have throws instead of returning $null, so every read of a parsed JSON element goes through
# this helper rather than direct dot-access.
function Get-JsonProperty {
    param([Parameter(Mandatory = $true)]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object.PSObject.Properties.Match($Name).Count -eq 0) { return $null }
    return $Object.$Name
}

function Get-UiaElementText {
    # Reads text from an element that does not expose TextPattern/ValuePattern (e.g. a plain
    # container with role="group"/"status"), by inspecting it and concatenating its own Name
    # with any children's Name values. Used for elements where get-value returns nothing.
    param([Parameter(Mandatory = $true)][string]$Selector, [int]$Depth = 5)

    $inspectResult = Invoke-UiaInspect -Selector $Selector -Depth $Depth
    if (-not $inspectResult.Succeeded) {
        return ''
    }

    try {
        $parsed = $inspectResult.Output | Out-String | ConvertFrom-Json
        $elements = @($parsed.windows[0].elements)
        $element = $elements | Where-Object { (Get-JsonProperty $_ 'automationId') -eq $Selector } | Select-Object -First 1
        if (-not $element) {
            return ''
        }
        $parts = New-Object System.Collections.Generic.List[string]
        $ownName = Get-JsonProperty $element 'name'
        if ($ownName) { $parts.Add($ownName) }
        $children = Get-JsonProperty $element 'children'
        if ($children) {
            foreach ($child in @($children)) {
                $childName = Get-JsonProperty $child 'name'
                if ($childName) { $parts.Add($childName) }
            }
        }
        return ($parts -join ' ')
    }
    catch {
        Write-RunnerLog "Failed to parse element text for '$Selector': $_" -Level Warn
        return ''
    }
}

function Invoke-UiaInspect {
    param([Parameter(Mandatory = $true)][string]$Selector, [int]$Depth = 6)
    $cliArgs = @('ui', 'inspect', $Selector, '-w', "$script:TargetHwnd", '-d', "$Depth", '--json')
    $result = Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure
    return $result
}

function Invoke-UiaSearch {
    param([Parameter(Mandatory = $true)][string]$Selector, [int]$Max = 20)
    $cliArgs = @('ui', 'search', $Selector, '-w', "$script:TargetHwnd", '--max', "$Max", '--json')
    return Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure
}

function Invoke-UiaInvoke {
    param([Parameter(Mandatory = $true)][string]$Selector, [switch]$AllowFailure)
    $cliArgs = @('ui', 'invoke', $Selector, '-w', "$script:TargetHwnd")
    return Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure:$AllowFailure
}

# --- ComboBox helper: expand -> read option Name/selector list -> (optionally select) -----
# --- -> best-effort collapse. Reusable by any scenario that touches a native <select>. -----

function Get-ComboBoxOptionsOnce {
    param([Parameter(Mandatory = $true)][string]$ComboBoxId)

    $inspectResult = Invoke-UiaInspect -Selector $ComboBoxId -Depth 10
    if (-not $inspectResult.Succeeded) {
        return @()
    }
    try {
        $parsed = $inspectResult.Output | Out-String | ConvertFrom-Json
        $elements = @($parsed.windows[0].elements)
        $comboElement = $elements | Where-Object { (Get-JsonProperty $_ 'automationId') -eq $ComboBoxId } | Select-Object -First 1
        if (-not $comboElement) {
            return @()
        }
        $children = Get-JsonProperty $comboElement 'children'
        if (-not $children) {
            return @()
        }
        return @($children | ForEach-Object {
            [pscustomobject]@{ Name = (Get-JsonProperty $_ 'name'); Selector = (Get-JsonProperty $_ 'selector') }
        })
    }
    catch {
        Write-RunnerLog "Failed to parse combobox options for '$ComboBoxId': $_" -Level Warn
        return @()
    }
}

function Get-ComboBoxOptions {
    param(
        [Parameter(Mandatory = $true)][string]$ComboBoxId,
        [switch]$LeaveExpanded
    )

    Invoke-UiaInvoke -Selector $ComboBoxId | Out-Null
    Start-Sleep -Milliseconds 400

    $options = @(Get-ComboBoxOptionsOnce -ComboBoxId $ComboBoxId)
    if ($options.Count -eq 0) {
        # The flyout can occasionally report "collapsed" immediately after the expand toggle
        # (WinUI popup lifecycle timing); one retry with a longer wait resolves it in practice.
        Write-RunnerLog "No options found for '$ComboBoxId' on first read; retrying once." -Level Trace
        Start-Sleep -Milliseconds 500
        $options = @(Get-ComboBoxOptionsOnce -ComboBoxId $ComboBoxId)
    }

    if (-not $LeaveExpanded) {
        Invoke-UiaInvoke -Selector $ComboBoxId -AllowFailure | Out-Null
        Start-Sleep -Milliseconds 200
    }

    return $options
}

function Select-ComboBoxOption {
    param(
        [Parameter(Mandatory = $true)][string]$ComboBoxId,
        [Parameter(Mandatory = $true)][string]$OptionText
    )

    $options = @(Get-ComboBoxOptions -ComboBoxId $ComboBoxId -LeaveExpanded)
    $match = $options | Where-Object { $_.Name -eq $OptionText } | Select-Object -First 1
    if (-not $match) {
        # Leave the dropdown in a clean state even when the option cannot be found.
        Invoke-UiaInvoke -Selector $ComboBoxId -AllowFailure | Out-Null
        throw "Option '$OptionText' was not found in combobox '$ComboBoxId'. Available: $(($options | ForEach-Object { $_.Name }) -join ', ')"
    }

    Invoke-UiaInvoke -Selector $match.Selector | Out-Null
    Start-Sleep -Milliseconds 300
    # Best-effort: the flyout does not always support a second toggle-collapse after a
    # selection has been made. A screenshot afterward documents the real visual state either way.
    Invoke-UiaInvoke -Selector $ComboBoxId -AllowFailure | Out-Null
    Start-Sleep -Milliseconds 200

    return Invoke-UiaGetValue -Selector $ComboBoxId
}

function Invoke-UiaSetValue {
    # $Value is intentionally not [Parameter(Mandatory)]: Mandatory on a string parameter
    # rejects an empty string outright, but setting a field to '' (clearing it) is legitimate.
    param([Parameter(Mandatory = $true)][string]$Selector, [string]$Value = '')
    $cliArgs = @('ui', 'set-value', $Selector, $Value, '-w', "$script:TargetHwnd")
    return Invoke-WinAppCommand -Arguments $cliArgs
}

function Invoke-UiaGetValue {
    param([Parameter(Mandatory = $true)][string]$Selector, [switch]$AllowFailure)
    $cliArgs = @('ui', 'get-value', $Selector, '-w', "$script:TargetHwnd")
    $result = Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure:$AllowFailure
    if ($result.Succeeded) {
        return ($result.Output | Out-String).Trim()
    }
    return $null
}

function Invoke-UiaWaitFor {
    param(
        [Parameter(Mandatory = $true)][string]$Selector,
        [int]$TimeoutMs = 5000,
        [switch]$Gone,
        [string]$Value,
        [switch]$Contains
    )
    $cliArgs = @('ui', 'wait-for', $Selector, '-w', "$script:TargetHwnd", '-t', "$TimeoutMs")
    if ($Gone) { $cliArgs += '--gone' }
    if ($Value) { $cliArgs += @('--value', $Value) }
    if ($Contains) { $cliArgs += '--contains' }
    return Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure
}

function Get-WinAppWindowList {
    param([Parameter(Mandatory = $true)][string]$AppName)
    $cliArgs = @('ui', 'list-windows', '-a', $AppName, '--json')
    $result = Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure
    if (-not $result.Succeeded) {
        return @()
    }
    try {
        return ($result.Output | Out-String | ConvertFrom-Json)
    }
    catch {
        return @()
    }
}

# --- Screenshots: window-only capture, SHA-256 dedup --------------------------------------

function Save-DedupedScreenshot {
    param(
        [Parameter(Mandatory = $true)][string]$StepId
    )

    # A failure before the window was ever detected leaves $script:TargetHwnd empty, which would
    # otherwise be passed as an empty -w value and make winapp reject the whole command line (so the
    # failure evidence for the earliest, most interesting failures would be lost along with a
    # misleading parser error in the runner log). There is simply no window to capture yet.
    if (-not $script:TargetHwnd) {
        Write-RunnerLog "Screenshot for step '$StepId' skipped: no target window has been detected yet." -Level Warn
        return $null
    }

    $script:ScreenshotSequence++
    $candidateName = ('{0:D3}-{1}.png' -f $script:ScreenshotSequence, $StepId)
    $candidatePath = Join-Path $script:ScreenshotsDir $candidateName

    # Deliberately no --capture-screen: window-only capture of the exact target HWND.
    $cliArgs = @('ui', 'screenshot', '-w', "$script:TargetHwnd", '-o', $candidatePath)
    $result = Invoke-WinAppCommand -Arguments $cliArgs -AllowFailure
    if (-not $result.Succeeded -or -not (Test-Path -LiteralPath $candidatePath)) {
        Write-RunnerLog "Screenshot capture failed for step '$StepId'." -Level Warn
        return $null
    }

    $hash = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash
    if ($script:ScreenshotHashMap.ContainsKey($hash)) {
        $retainedName = $script:ScreenshotHashMap[$hash]
        Remove-Item -LiteralPath $candidatePath -Force
        Write-RunnerLog "Screenshot for '$StepId' is byte-identical to '$retainedName'; reusing it." -Level Trace
        return $retainedName
    }

    $script:ScreenshotHashMap[$hash] = $candidateName
    Write-RunnerLog "Screenshot saved: $candidateName (sha256=$hash)" -Level Trace
    return $candidateName
}

function Save-UiaDump {
    # $Content is intentionally not [Parameter(Mandatory)]: an empty options list is a valid,
    # meaningful dump (e.g. a combobox that unexpectedly has zero entries), not a caller error.
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Content = ''
    )
    $path = Join-Path $script:UiaDir "$Name.txt"
    Set-Content -LiteralPath $path -Value $Content -Encoding UTF8
    return $path
}

# --- Step / assertion tracking -------------------------------------------------------------

function Add-RunStep {
    param(
        [Parameter(Mandatory = $true)][int]$StepNumber,
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Kind = 'action',
        [string]$BeforeScreenshot,
        [string]$AfterScreenshot,
        [string]$Detail
    )
    $entry = [pscustomobject]@{
        StepNumber       = $StepNumber
        Name             = $Name
        Kind             = $Kind
        BeforeScreenshot = $BeforeScreenshot
        AfterScreenshot  = $AfterScreenshot
        Detail           = $Detail
        TimestampUtc     = (Get-Date).ToUniversalTime().ToString('o')
    }
    $script:StepLog.Add($entry) | Out-Null
    Write-RunnerLog "Step $($StepNumber): $Name" -Level Info
    return $entry
}

function Add-Assertion {
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [string]$Detail,
        [string]$FailureScreenshot
    )
    $entry = [pscustomobject]@{
        Number            = $Number
        Description       = $Description
        Result            = if ($Passed) { 'Pass' } else { 'Fail' }
        Detail            = $Detail
        FailureScreenshot = $FailureScreenshot
    }
    $script:AssertionLog.Add($entry) | Out-Null

    if ($Passed) {
        Write-RunnerLog "Assertion $($Number) PASSED: $Description" -Level Info
    }
    else {
        Write-RunnerLog "Assertion $($Number) FAILED: $Description ($Detail)" -Level Error
    }
    return $entry
}

function Assert-NoBlankOrErrorState {
    # Shared blank-screen/global-failure invariant (KF-MEANING-002 GUI hardening, Part 3), callable by
    # every workflow scenario. Fails the assertion (and captures the standard failure screenshot via
    # Assert-UiaCondition) when: the global ErrorBoundary is visible; the expected route/page marker does
    # not appear within the bounded transition timeout (this also catches a transition spinner that never
    # resolves, since the marker can only appear once loading truly finishes); or the app process has
    # already exited. Never records document or context text - only selector names and booleans.
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string]$RouteMarkerSelector,
        [int]$TransitionTimeoutMs = 8000
    )

    $processAlive = $false
    if ($script:TargetPid) {
        $processAlive = $null -ne (Get-Process -Id $script:TargetPid -ErrorAction SilentlyContinue)
    }

    $errorBoundaryWait = Invoke-UiaWaitFor -Selector 'app-error-boundary' -TimeoutMs 500
    $errorBoundaryVisible = $errorBoundaryWait.Succeeded

    $routeMarkerWait = Invoke-UiaWaitFor -Selector $RouteMarkerSelector -TimeoutMs $TransitionTimeoutMs
    $routeMarkerPresent = $routeMarkerWait.Succeeded

    $passed = $processAlive -and (-not $errorBoundaryVisible) -and $routeMarkerPresent
    $detail = "processAlive=$processAlive errorBoundaryVisible=$errorBoundaryVisible routeMarkerPresent=$routeMarkerPresent (routeMarkerSelector='$RouteMarkerSelector', transitionTimeoutMs=$TransitionTimeoutMs)"

    if (-not $passed) {
        Write-RunnerLog "Blank-screen/error-boundary invariant FAILED: $detail" -Level Error
    }

    return Assert-UiaCondition -Number $Number -Description $Description -Condition $passed -Detail $detail
}

function Assert-UiaCondition {
    param(
        [Parameter(Mandatory = $true)][int]$Number,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][bool]$Condition,
        [string]$Detail
    )

    $failureScreenshot = $null
    if (-not $Condition) {
        $failureScreenshot = Save-DedupedScreenshot -StepId ("assertion-{0}-failure" -f $Number)
    }
    return Add-Assertion -Number $Number -Description $Description -Passed $Condition -Detail $Detail -FailureScreenshot $failureScreenshot
}

# --- Isolated-database scalar queries (read-only, integer-scalar only) -----------------
#
# Scenarios must prove durable database outcomes (candidate status, Sense/Meaning/Card/Variant
# counts, PRAGMA user_version) directly, not only from the UI. Every assertion this framework
# needs is a simple integer COUNT/scalar, so this stays deliberately narrow: a raw P/Invoke
# wrapper around the app's own e_sqlite3.dll (already present next to the built executable —
# no new managed dependency, no network, read-only). It is never used to write to the isolated
# database, and it is never pointed at anything but a path under the isolated GUI-test profile
# (enforced by the caller passing a path already validated via GuiTestProfile-style checks).

$script:GuiTestSqliteTypeLoaded = $false

function Initialize-GuiTestSqliteType {
    param([Parameter(Mandatory = $true)][string]$NativeLibraryDirectory)

    if ($script:GuiTestSqliteTypeLoaded) {
        return
    }

    # e_sqlite3.dll must be discoverable on the process search path for the DllImport below to
    # resolve it (it sits next to the built KnownFirst.exe, not on the system PATH).
    $env:PATH = "$NativeLibraryDirectory;$env:PATH"

    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class GuiTestSqlite
{
    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open(byte[] filename, out IntPtr db);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nBytes, out IntPtr stmt, IntPtr tail);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr stmt, int col);

    [DllImport("e_sqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    private static byte[] ToUtf8NulTerminated(string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        byte[] result = new byte[encoded.Length + 1];
        Array.Copy(encoded, result, encoded.Length);
        result[encoded.Length] = 0;
        return result;
    }

    private static string ReadUtf8String(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) { return string.Empty; }
        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0) { length++; }
        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        return Encoding.UTF8.GetString(buffer);
    }

    // Returns 0 when the query produces no row (COUNT(*) always produces exactly one row, so
    // this only applies to the rare scalar query that genuinely finds nothing).
    public static long QueryScalarInt(string databasePath, string sql)
    {
        IntPtr db;
        int openResult = sqlite3_open(ToUtf8NulTerminated(databasePath), out db);
        if (openResult != 0)
        {
            throw new InvalidOperationException("sqlite3_open failed (" + openResult + ") for '" + databasePath + "'.");
        }

        try
        {
            IntPtr stmt;
            byte[] sqlBytes = ToUtf8NulTerminated(sql);
            int prepareResult = sqlite3_prepare_v2(db, sqlBytes, sqlBytes.Length, out stmt, IntPtr.Zero);
            if (prepareResult != 0)
            {
                string message = ReadUtf8String(sqlite3_errmsg(db));
                throw new InvalidOperationException("sqlite3_prepare_v2 failed (" + prepareResult + "): " + message + " SQL: " + sql);
            }

            try
            {
                int stepResult = sqlite3_step(stmt);
                if (stepResult == SQLITE_ROW)
                {
                    return sqlite3_column_int64(stmt, 0);
                }
                if (stepResult == SQLITE_DONE)
                {
                    return 0;
                }
                throw new InvalidOperationException("sqlite3_step failed (" + stepResult + "). SQL: " + sql);
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }
        finally
        {
            sqlite3_close(db);
        }
    }
}
'@ -ErrorAction Stop

    $script:GuiTestSqliteTypeLoaded = $true
}

function Get-GuiTestSqliteScalarInt {
    # Read-only integer-scalar query against an isolated GUI-test SQLite database. $DatabasePath
    # must already be under the isolated profile root; callers are responsible for that (this
    # function performs no path validation of its own, since it has no notion of "the current
    # run" - GuiTestProfile-style validation happens where the path is first resolved).
    param(
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$Sql,
        [Parameter(Mandatory = $true)][string]$NativeLibraryDirectory
    )

    Initialize-GuiTestSqliteType -NativeLibraryDirectory $NativeLibraryDirectory
    if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
        throw "Isolated GUI-test database not found at '$DatabasePath'."
    }
    return [GuiTestSqlite]::QueryScalarInt($DatabasePath, $Sql)
}

# --- Real-data guard -------------------------------------------------------------------

function Get-RealKnownFirstDataFingerprint {
    $localAppData = $env:LOCALAPPDATA
    $candidateFiles = New-Object System.Collections.Generic.List[string]

    $dataDirs = Get-ChildItem -Path $localAppData -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'com.tachiguro.knownfirst\Data' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container }

    foreach ($dataDir in $dataDirs) {
        Get-ChildItem -LiteralPath $dataDir -Filter 'knownfirst.db3*' -File -ErrorAction SilentlyContinue |
            ForEach-Object { $candidateFiles.Add($_.FullName) }
    }

    $fingerprints = foreach ($path in $candidateFiles) {
        $item = Get-Item -LiteralPath $path
        [pscustomobject]@{
            Path             = $path
            Sha256           = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
            Length           = $item.Length
        }
    }

    return @($fingerprints)
}

function Test-RealDataUnchanged {
    param(
        [Parameter(Mandatory = $true)][object[]]$Before,
        [Parameter(Mandatory = $true)][object[]]$After
    )

    $beforeByPath = @{}
    foreach ($item in $Before) { $beforeByPath[$item.Path] = $item }
    $afterByPath = @{}
    foreach ($item in $After) { $afterByPath[$item.Path] = $item }

    $allPaths = @($beforeByPath.Keys) + @($afterByPath.Keys) | Select-Object -Unique
    $differences = New-Object System.Collections.Generic.List[string]

    foreach ($path in $allPaths) {
        $b = $beforeByPath[$path]
        $a = $afterByPath[$path]
        if ($null -eq $b -and $null -ne $a) {
            $differences.Add("New real-data file appeared: $path")
            continue
        }
        if ($null -ne $b -and $null -eq $a) {
            $differences.Add("Real-data file disappeared: $path")
            continue
        }
        if ($b.Sha256 -ne $a.Sha256 -or $b.LastWriteTimeUtc -ne $a.LastWriteTimeUtc -or $b.Length -ne $a.Length) {
            $differences.Add("Real-data file changed: $path (before sha256=$($b.Sha256) len=$($b.Length) mtime=$($b.LastWriteTimeUtc); after sha256=$($a.Sha256) len=$($a.Length) mtime=$($a.LastWriteTimeUtc))")
        }
    }

    return [pscustomobject]@{
        Unchanged   = ($differences.Count -eq 0)
        Differences = @($differences)
    }
}

# --- Process launch / close -------------------------------------------------------------

function Start-KnownFirstUnderTest {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$GuiTestRoot
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "KnownFirst executable not found at $ExecutablePath."
    }

    $env:KNOWNFIRST_GUI_TEST_ROOT = $GuiTestRoot
    Write-RunnerLog "Launching $ExecutablePath with KNOWNFIRST_GUI_TEST_ROOT=$GuiTestRoot"
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    Remove-Item Env:KNOWNFIRST_GUI_TEST_ROOT -ErrorAction SilentlyContinue

    $deadline = (Get-Date).AddSeconds(20)
    $hwnd = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $windows = Get-WinAppWindowList -AppName 'KnownFirst'
        $match = $windows | Where-Object { $_.processId -eq $process.Id }
        if ($match) {
            $hwnd = $match[0].hwnd
            break
        }
    }

    if (-not $hwnd) {
        throw "KnownFirst launched (PID $($process.Id)) but no window was detected within the timeout."
    }

    $script:TargetPid = $process.Id
    $script:TargetHwnd = $hwnd
    Write-RunnerLog "KnownFirst window ready. PID = $($process.Id), HWND = $hwnd (0x$('{0:X}' -f [long]$hwnd))"

    return [pscustomobject]@{ ProcessId = $process.Id; Hwnd = $hwnd; Process = $process }
}

function Stop-KnownFirstUnderTest {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-RunnerLog "Process $ProcessId already exited." -Level Trace
        return
    }
    Write-RunnerLog "Closing KnownFirst process (PID $ProcessId)."
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

# --- Report generation -------------------------------------------------------------------

function Write-GuiTestReport {
    param(
        [Parameter(Mandatory = $true)][object]$Context,
        [Parameter(Mandatory = $true)][hashtable]$Metadata,
        [Parameter(Mandatory = $true)][bool]$OverallSucceeded
    )

    $passCount = @($script:AssertionLog | Where-Object { $_.Result -eq 'Pass' }).Count
    $failCount = @($script:AssertionLog | Where-Object { $_.Result -eq 'Fail' }).Count
    $screenshotFiles = Get-ChildItem -LiteralPath $script:ScreenshotsDir -Filter '*.png' -File -ErrorAction SilentlyContinue
    $screenshotCount = @($screenshotFiles).Count
    $duplicateCount = $script:ScreenshotSequence - $screenshotCount

    $summary = [ordered]@{
        scenarioId              = $Context.ScenarioId
        runId                   = $Context.RunId
        succeeded               = $OverallSucceeded
        commit                  = $Metadata.Commit
        branch                  = $Metadata.Branch
        monitorTarget           = $Metadata.MonitorTarget
        monitorDeviceNameArg    = $Metadata.MonitorDeviceNameArg
        monitors                = $Metadata.Monitors
        selectedMonitor         = $Metadata.SelectedMonitor
        monitorSelectionReason  = $Metadata.MonitorSelectionReason
        placement               = $Metadata.Placement
        finalWindowBounds       = $Metadata.FinalWindowBounds
        steps                   = $script:StepLog.ToArray()
        assertions              = $script:AssertionLog.ToArray()
        assertionsPassed        = $passCount
        assertionsFailed        = $failCount
        screenshotCount         = $screenshotCount
        duplicateScreenshotsAvoided = [Math]::Max(0, $duplicateCount)
        realDataUnchanged       = $Metadata.RealDataUnchanged
        realDataDifferences     = $Metadata.RealDataDifferences
        physicalInputUsed       = $false
        generatedAtUtc          = (Get-Date).ToUniversalTime().ToString('o')
    }
    ($summary | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Context.SummaryPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# KnownFirst GUI Test Report") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add("Scenario: $($Context.ScenarioId)") | Out-Null
    $lines.Add("Run ID: $($Context.RunId)") | Out-Null
    $lines.Add("Commit: $($Metadata.Commit) (branch $($Metadata.Branch))") | Out-Null
    $lines.Add("Overall result: $(if ($OverallSucceeded) { 'PASS' } else { 'FAIL' })") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('## Monitor') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add("MonitorTarget: $($Metadata.MonitorTarget)$(if ($Metadata.MonitorDeviceNameArg) { " (MonitorDeviceName: $($Metadata.MonitorDeviceNameArg))" } else { '' })") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('Detected displays (identity from Windows display configuration; a display is only called "Display N" here if that number was actually resolved from Windows display configuration - otherwise device/friendly names are used):') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('```') | Out-Null
    if ($Metadata.Monitors) { $lines.Add((Format-DisplayMonitorTable -Monitors $Metadata.Monitors)) | Out-Null }
    $lines.Add('```') | Out-Null
    $lines.Add('') | Out-Null
    # A scenario that fails before monitor selection leaves SelectedMonitor null; under
    # Set-StrictMode -Version Latest, dotting into it would throw and lose the whole report for
    # exactly the runs whose evidence matters most.
    if ($Metadata.SelectedMonitor) {
        $lines.Add("Selected monitor: $($Metadata.SelectedMonitor.FriendlyName) ($($Metadata.SelectedMonitor.DeviceName))") | Out-Null
    }
    else {
        $lines.Add('Selected monitor: (none - the scenario failed before a monitor was selected)') | Out-Null
    }
    $lines.Add("Selection reason: $($Metadata.MonitorSelectionReason)") | Out-Null
    $lines.Add('') | Out-Null
    if ($Metadata.Placement) {
        $lines.Add("KnownFirst bounds before staging: $($Metadata.Placement.BeforeBounds | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("Staging rectangle requested: $($Metadata.Placement.StagingRequested | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("Staging actual bounds: $($Metadata.Placement.StagingActualBounds | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("MonitorFromWindow matched selected monitor after staging: $($Metadata.Placement.StagingMonitorMatch)") | Out-Null
        $lines.Add('') | Out-Null
        $lines.Add("Corrective passes: $($Metadata.Placement.CorrectivePassCount)") | Out-Null
        foreach ($pass in $Metadata.Placement.CorrectivePasses) {
            $lines.Add("  Pass $($pass.Pass): requested=$($pass.RequestedRect | ConvertTo-Json -Compress) raw=$($pass.RawBounds | ConvertTo-Json -Compress) visible=$($pass.VisibleBounds | ConvertTo-Json -Compress) onExpectedMonitor=$($pass.OnExpectedMonitor) contained=$($pass.Contained)") | Out-Null
            foreach ($overlap in $pass.Overlaps) {
                $lines.Add("    Overlap with $($overlap.FriendlyName) ($($overlap.DeviceName)): $($overlap.OverlapPixels) px") | Out-Null
            }
        }
        $lines.Add('') | Out-Null
        $lines.Add("KnownFirst bounds after placement (raw window rect): $($Metadata.Placement.RawBounds | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("KnownFirst bounds after placement (visible DWM frame): $($Metadata.Placement.VisibleBounds | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("Selected monitor working area: $($Metadata.Placement.WorkingArea | ConvertTo-Json -Compress)") | Out-Null
        $lines.Add("MonitorFromWindow matches selected monitor (final): $($Metadata.Placement.MonitorFromWindowMatchesSelected)") | Out-Null
        $lines.Add("Containment result: $(if ($Metadata.Placement.Contained) { 'CONTAINED - the visible window is fully inside the selected working area, on the selected monitor, with zero pixels on any other monitor' } else { 'NOT CONTAINED - the visible window crosses outside the selected working area, or is not on the selected monitor' })") | Out-Null
    }
    $lines.Add('') | Out-Null
    $lines.Add("Final KnownFirst window bounds: $($Metadata.FinalWindowBounds | ConvertTo-Json -Compress)") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('## Steps') | Out-Null
    $lines.Add('') | Out-Null
    foreach ($step in $script:StepLog) {
        $shot = if ($step.AfterScreenshot) { $step.AfterScreenshot } elseif ($step.BeforeScreenshot) { $step.BeforeScreenshot } else { '(none)' }
        $lines.Add("$($step.StepNumber). [$($step.Kind)] $($step.Name) - screenshot: $shot") | Out-Null
        if ($step.Detail) { $lines.Add("   $($step.Detail)") | Out-Null }
    }
    $lines.Add('') | Out-Null
    $lines.Add('## Assertions') | Out-Null
    $lines.Add('') | Out-Null
    foreach ($assertion in $script:AssertionLog) {
        $marker = if ($assertion.Result -eq 'Pass') { 'PASS' } else { 'FAIL' }
        $lines.Add("$($assertion.Number). [$marker] $($assertion.Description)") | Out-Null
        if ($assertion.Detail) { $lines.Add("   $($assertion.Detail)") | Out-Null }
        if ($assertion.FailureScreenshot) { $lines.Add("   Screenshot: $($assertion.FailureScreenshot)") | Out-Null }
    }
    $lines.Add('') | Out-Null
    $lines.Add("Assertions passed: $passCount, failed: $failCount") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('## Real-data guard') | Out-Null
    $lines.Add('') | Out-Null
    if ($Metadata.RealDataUnchanged) {
        $lines.Add('Real database/WAL/SHM files were unchanged before vs. after the run.') | Out-Null
    }
    else {
        $lines.Add('REAL DATA CHANGED - investigate immediately:') | Out-Null
        foreach ($diff in $Metadata.RealDataDifferences) { $lines.Add("- $diff") | Out-Null }
    }
    $lines.Add('') | Out-Null
    $lines.Add('## Screenshots') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add("Physical screenshot files: $screenshotCount (duplicates avoided via SHA-256 dedup: $([Math]::Max(0, $duplicateCount)))") | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('## Phase 1 limitations') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('- Native OS dialogs (file pickers, etc.) are excluded from Phase 1 because they can still take foreground focus away from the user''s primary display.') | Out-Null
    $lines.Add('- Only one monitor/viewport profile runs per invocation (selected via -MonitorTarget); the 1440x900 / 900x900 / 430x932 / 932x430 responsive profiles are deferred to scenario 002.') | Out-Null
    $lines.Add('- Only UI Automation operations that do not simulate physical mouse/keyboard input were used.') | Out-Null

    Set-Content -LiteralPath $Context.ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
}
