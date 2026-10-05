# The desktop app's real footprint on Windows (#1916): memory and CPU of the
# whole app, sampled over a fixed window while one scenario runs, reported as
# median and p95. Method and results: docs/desktop-footprint.md.
#
#   pwsh desktop/tools/measure-footprint.ps1 -Scenario <name> [-Seconds 300] [-Interval 5]
#
# "The app" is the shell (`TrueMain.exe` installed, `truemain-desktop.exe` in a
# dev build) and all its descendants by `ParentProcessId`
# (`Get-CimInstance Win32_Process`): WebView2's `msedgewebview2.exe` browser,
# renderer, GPU and utility processes, and the capture helper
# `truemain-capture.exe`.
#
# Counters, one `Get-Counter` sample per interval:
# - memory: `\Process(*)\Working Set - Private`, the private working set —
#   Task Manager's "Memory" column — summed over the tree;
# - CPU: `\Process(*)\% Processor Time`, as % of one core, summed.
# Counter names are the English ones: on a Windows in another language, run it
# from an English-language user or adapt them.
#
# Writes every sample to `footprint-<scenario>-<timestamp>.csv` in the current
# directory and prints one Markdown row for the doc's table, the app's version
# left for the hand to fill.

param(
    [Parameter(Mandatory)] [string] $Scenario,
    [int] $Seconds = 300,
    [int] $Interval = 5,
    [string[]] $ShellNames = @("TrueMain.exe", "truemain-desktop.exe")
)

$ErrorActionPreference = "Stop"

function Tree() {
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name)
    $members = @{}
    foreach ($shell in @($all | Where-Object { $ShellNames -contains $_.Name })) { $members[[int]$shell.ProcessId] = $shell.Name }
    # Descendants, to any depth: WebView2's renderers are its browser's children.
    do {
        $grew = $false
        foreach ($process in $all) {
            $id = [int]$process.ProcessId
            if (-not $members.ContainsKey($id) -and $members.ContainsKey([int]$process.ParentProcessId) -and $id -ne [int]$process.ParentProcessId) {
                $members[$id] = $process.Name
                $grew = $true
            }
        }
    } while ($grew)
    $members
}

function Percentile([double[]] $Values, [double] $Share) {
    $ordered = $Values | Sort-Object
    $ordered[[Math]::Min($ordered.Count - 1, [Math]::Max(0, [int][Math]::Round($Share * ($ordered.Count - 1))))]
}

if ((Tree).Count -eq 0) { throw "measure-footprint: the app is not running" }

$csv = Join-Path (Get-Location) "footprint-$Scenario-$(Get-Date -Format 'yyyyMMdd-HHmmss').csv"
"elapsed_s,processes,memory_mb,cpu_percent_of_one_core" | Set-Content $csv
$memories = [System.Collections.Generic.List[double]]::new()
$cpus = [System.Collections.Generic.List[double]]::new()
$started = Get-Date
while (((Get-Date) - $started).TotalSeconds -lt $Seconds) {
    # `% Processor Time` is measured between the two readings Get-Counter takes over the interval.
    $samples = (Get-Counter -Counter @('\Process(*)\ID Process', '\Process(*)\Working Set - Private', '\Process(*)\% Processor Time') -SampleInterval $Interval -MaxSamples 2 -ErrorAction SilentlyContinue)[-1].CounterSamples
    $members = Tree
    $byInstance = @{}
    foreach ($sample in $samples) {
        $instance = $sample.InstanceName
        if (-not $byInstance.ContainsKey($instance)) { $byInstance[$instance] = @{} }
        $byInstance[$instance][($sample.Path -split '\\')[-1]] = $sample.CookedValue
    }
    $memory = 0.0; $cpu = 0.0; $count = 0
    foreach ($values in $byInstance.Values) {
        if ($null -eq $values['id process'] -or -not $members.ContainsKey([int]$values['id process'])) { continue }
        $memory += $values['working set - private']
        $cpu += $values['% processor time']
        $count++
    }
    $memories.Add($memory / 1MB)
    $cpus.Add($cpu)
    "{0:0},{1},{2:0.0},{3:0.0}" -f ((Get-Date) - $started).TotalSeconds, $count, ($memory / 1MB), $cpu | Add-Content $csv
}

$os = (Get-CimInstance Win32_OperatingSystem)
$machine = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$memoryMedian = Percentile $memories.ToArray() 0.5
$cpuMedian = Percentile $cpus.ToArray() 0.5
Write-Host "samples: $csv"
"| Date | App | Scenario | Memory median / p95 (private working set) | CPU median / p95 (% of one core) | Windows | Machine |"
"| {0:yyyy-MM-dd} | <version> | {1} | {2:0} / {3:0} MB | {4:0.0} / {5:0.0} % | {6} {7} | {8} |" -f (Get-Date), $Scenario, $memoryMedian, (Percentile $memories.ToArray() 0.95), $cpuMedian, (Percentile $cpus.ToArray() 0.95), $os.Caption, $os.BuildNumber, $machine
