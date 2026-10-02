# Smoke test of the Windows capture helper, without League: records a window
# that repaints itself, cuts a clip out of the video and takes a thumbnail,
# checking each answer of the helper's protocol. Run by CI on a Windows
# runner, and by hand on any Windows machine:
#
#   cargo build --release --manifest-path desktop/Cargo.toml -p truemain-capture
#   pwsh desktop/capture/windows/smoke.ps1 -Helper desktop/target/release/truemain-capture.exe
#
# A runner has no GPU: frames are converted on the CPU and encoded in
# software there, so this proves the protocol, the capture and the file, not
# the GPU path.

param(
    [Parameter(Mandatory = $true)] [string] $Helper,
    [string] $Out = (Join-Path ([System.IO.Path]::GetTempPath()) "truemain-capture-smoke"),
    [int] $Seconds = 8
)

$ErrorActionPreference = "Stop"
$Helper = (Resolve-Path $Helper).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null

function Events([string[]] $Arguments) {
    $lines = & $Helper @Arguments 2>$null
    @($lines | Where-Object { $_ -like "{*" } | ForEach-Object { $_ | ConvertFrom-Json })
}

function Expect([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw "smoke: $Message" }
    Write-Host "ok - $Message"
}

$usage = Events @()
Expect ($usage.Count -eq 1 -and $usage[0].event -eq "error" -and $usage[0].kind -eq "usage") "no command is a usage error"

$access = Events @("access")
Expect ($access[0].event -eq "access" -and $access[0].granted -eq $true) "window capture is supported"

$noGame = Events @("probe")
Expect ($noGame[0].kind -eq "no-window") "without a game, probe finds no game window"

# A window that repaints every 30 ms: Windows.Graphics.Capture sends a frame
# only when the window changes.
$title = "TrueMain capture smoke $PID"
$form = @"
Add-Type -AssemblyName System.Windows.Forms
`$form = New-Object System.Windows.Forms.Form
`$form.Text = '$title'
`$form.Width = 960; `$form.Height = 540
`$form.StartPosition = 'CenterScreen'
`$timer = New-Object System.Windows.Forms.Timer
`$timer.Interval = 30
`$script:hue = 0
`$timer.Add_Tick({ `$script:hue = (`$script:hue + 7) % 255; `$form.BackColor = [System.Drawing.Color]::FromArgb(`$script:hue, 255 - `$script:hue, 128) })
`$timer.Start()
`$form.Add_Shown({ `$form.Activate() })
[System.Windows.Forms.Application]::Run(`$form)
"@
$painter = Start-Process powershell -ArgumentList @("-NoProfile", "-STA", "-Command", $form) -PassThru
try {
    $window = $null
    for ($i = 0; $i -lt 40 -and -not $window; $i++) {
        Start-Sleep -Milliseconds 500
        $window = Events @("list") | Where-Object { $_.title -eq $title } | Select-Object -First 1
    }
    Expect ($null -ne $window) "list finds the test window ($($window.windowId))"

    $probe = Events @("probe", "--window-id", "$($window.windowId)")
    Expect ($probe[0].event -eq "window" -and $probe[0].width -gt 0 -and $probe[0].height -gt 0) "probe sizes it at $($probe[0].width)x$($probe[0].height)"

    $video = Join-Path $Out "smoke.mp4"
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $Helper
    $start.Arguments = "record --out `"$video`" --width 1280 --height 720 --fps 30 --bitrate 4000000 --keyframe-interval 30 --window-id $($window.windowId)"
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $recorder = [System.Diagnostics.Process]::Start($start)

    function NextEvent([string] $Name, [int] $TimeoutSeconds) {
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $read = $recorder.StandardOutput.ReadLineAsync()
            if (-not $read.Wait(($deadline - (Get-Date)).TotalMilliseconds)) { break }
            if ($null -eq $read.Result) { break }
            Write-Host "  helper: $($read.Result)"
            $parsed = $read.Result | ConvertFrom-Json
            if ($parsed.event -eq $Name) { return $parsed }
            if ($parsed.event -eq "error") { throw "smoke: the helper failed: $($read.Result)" }
        }
        throw "smoke: no ``$Name`` from the helper in $TimeoutSeconds s"
    }

    $started = NextEvent "started" 30
    Expect ($started.width -eq 1280 -and $started.height -eq 720) "record starts at 1280x720 (conversion on the $($started.converter), hardware encoder: $($started.hardware))"
    Start-Sleep -Seconds $Seconds
    $recorder.StandardInput.WriteLine("stop")
    $stopped = NextEvent "stopped" 60
    $recorder.WaitForExit(10000) | Out-Null
    Expect ($stopped.frames -ge (10 * $Seconds)) "record wrote $($stopped.frames) frames ($($stopped.dropped) dropped)"
    Expect ([math]::Abs($stopped.durationMs - 1000 * $Seconds) -lt 2000) "the video lasts $($stopped.durationMs) ms"
    Expect ((Get-Item $video).Length -gt 10000) "the video is $((Get-Item $video).Length) bytes"

    $clip = Join-Path $Out "clip.mp4"
    $clipped = Events @("clip", "--in", $video, "--out", $clip, "--start-ms", "2000", "--end-ms", "5000")
    Expect ($clipped[0].event -eq "clipped" -and $clipped[0].durationMs -ge 2500 -and $clipped[0].durationMs -le 4500) "clip 2-5 s lasts $($clipped[0].durationMs) ms"
    Expect ((Get-Item $clip).Length -gt 1000) "the clip is $((Get-Item $clip).Length) bytes"

    $thumbnail = Join-Path $Out "thumbnail.jpg"
    $taken = Events @("thumbnail", "--in", $video, "--out", $thumbnail, "--at-ms", "3000", "--width", "320")
    Expect ($taken[0].event -eq "thumbnail" -and $taken[0].width -eq 320) "thumbnail is $($taken[0].width)x$($taken[0].height)"
    $bytes = [System.IO.File]::ReadAllBytes($thumbnail)
    Expect ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xD8) "the thumbnail is a JPEG"

    $fromClip = Events @("thumbnail", "--in", $clip, "--out", (Join-Path $Out "clip.jpg"), "--at-ms", "0", "--width", "160")
    Expect ($fromClip[0].event -eq "thumbnail") "the clip decodes"
}
finally {
    if ($painter -and -not $painter.HasExited) { Stop-Process -Id $painter.Id -Force }
}
