# Smoke test of the in-game overlay on Windows (#1806), without League: the
# app replays a recorded game (`fixtures/ranked-game.jsonl`) and a stand-in
# window takes the game's place in front, with its window class and its
# process name. Each step reads the panels' windows (shown, styles, what the
# mouse would hit) and screenshots the desktop, checking the panels are drawn
# over the game rather than merely shown. Run by CI on a Windows runner, and
# by hand on any Windows machine:
#
#   cd desktop/app && npm ci && npm run tauri -- build --no-bundle
#   pwsh desktop/tools/overlay-smoke-windows.ps1 -App desktop/target/release/truemain-desktop.exe
#
# The preview is opened from the game page's settings through UI Automation,
# and a panel is dragged with the mouse, as a player places one.
#
# What it cannot stand in for: the real game's renderer (Borderless or
# Windowed, never Full Screen) and the real anti-cheat.

param(
    [Parameter(Mandatory = $true)] [string] $App,
    [string] $Tape = (Join-Path $PSScriptRoot "../fixtures/ranked-game.jsonl"),
    [string] $Out = (Join-Path ([System.IO.Path]::GetTempPath()) "truemain-overlay-smoke")
)

$ErrorActionPreference = "Stop"
$App = (Resolve-Path $App).Path
$Tape = (Resolve-Path $Tape).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Desk {
    public delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    /// The screen as composed, layered windows included (CAPTUREBLT).
    public static void CopyScreen(IntPtr target, int x, int y, int width, int height) {
        IntPtr screen = GetDC(IntPtr.Zero);
        BitBlt(target, 0, 0, width, height, screen, x, y, 0x00CC0020 | 0x40000000);
        ReleaseDC(IntPtr.Zero, screen);
    }

    public struct RECT { public int Left, Top, Right, Bottom; }
    public struct POINT { public int X, Y; }

    public class Window {
        public IntPtr Handle;
        public string Title;
        public bool Visible;
        public RECT Rect;
        public long ExStyle;
    }

    public static List<Window> Of(uint pid) {
        var found = new List<Window>();
        EnumWindows((window, data) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == pid) {
                var title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                RECT rect;
                GetWindowRect(window, out rect);
                found.Add(new Window {
                    Handle = window,
                    Title = title.ToString(),
                    Visible = IsWindowVisible(window),
                    Rect = rect,
                    ExStyle = GetWindowLongPtr(window, -20).ToInt64(),
                });
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string ClassOf(IntPtr window) {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    /// The top-level window a click at this point would reach.
    public static IntPtr HitAt(int x, int y) {
        var point = new POINT { X = x, Y = y };
        return GetAncestor(WindowFromPoint(point), 2);
    }
}
"@
[Desk]::SetProcessDPIAware() | Out-Null

# The stand-in game: a borderless window over the whole primary screen, in a
# flat colour, of the class given, that brings itself to the front.
$standIn = Join-Path $Out "stand-in.cs"
Set-Content -Path $standIn -Value @"
using System;
using System.Runtime.InteropServices;

static class StandIn {
    delegate IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX {
        public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint colorref);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    static readonly WndProc Proc = Handle;
    static IntPtr Handle(IntPtr window, uint message, IntPtr w, IntPtr l) {
        if (message == 0x0002) PostQuitMessage(0);
        return DefWindowProc(window, message, w, l);
    }

    // stand-in.exe <class> <title> <COLORREF hex>
    static void Main(string[] args) {
        SetProcessDPIAware();
        var c = new WNDCLASSEX();
        c.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
        c.lpfnWndProc = Proc;
        c.hInstance = GetModuleHandle(null);
        c.hbrBackground = CreateSolidBrush(Convert.ToUInt32(args[2], 16));
        c.lpszClassName = args[0];
        RegisterClassEx(ref c);
        IntPtr window = CreateWindowEx(0, args[0], args[1], 0x90000000, 0, 0, GetSystemMetrics(0), GetSystemMetrics(1), IntPtr.Zero, IntPtr.Zero, c.hInstance, IntPtr.Zero);
        // A tap of Alt lifts the foreground lock for a process started in the background.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(window);
        MSG message;
        while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
    }
}
"@
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gameExe = Join-Path $Out "League of Legends.exe"
& $csc /nologo /target:winexe "/out:$gameExe" $standIn
if ($LASTEXITCODE -ne 0) { throw "smoke: the stand-in does not compile" }
# Another app, and a window that is the game's by its class alone.
$otherExe = Join-Path $Out "Other app.exe"
$classOnlyExe = Join-Path $Out "Renamed game.exe"
Copy-Item $gameExe $otherExe
Copy-Item $gameExe $classOnlyExe

# Flat colours the panels' pixels are told apart from (COLORREF is 0x00BBGGRR).
$GameColor = [System.Drawing.Color]::FromArgb(30, 140, 60)
$GameColorRef = "003C8C1E"
$OtherColorRef = "00C8C8C8"

$script:failures = @()
function Expect([bool] $Condition, [string] $Message) {
    if ($Condition) { Write-Host "ok - $Message" }
    else { Write-Host "FAIL - $Message"; $script:failures += $Message }
}

function Panels() {
    @([Desk]::Of([uint32]$script:shell.Id) | Where-Object { $_.Title -like "TrueMain overlay-*" } | ForEach-Object {
        [pscustomobject]@{
            Slug    = $_.Title.Substring("TrueMain overlay-".Length)
            Handle  = $_.Handle
            Visible = $_.Visible
            Left    = $_.Rect.Left; Top = $_.Rect.Top; Right = $_.Rect.Right; Bottom = $_.Rect.Bottom
            ExStyle = ('0x{0:X8}' -f $_.ExStyle)
            Styles  = $_.ExStyle
        }
    })
}

function Shown() { @(Panels | Where-Object Visible | ForEach-Object Slug | Sort-Object) -join "," }

function StartStandIn([string] $Exe, [string] $Class, [string] $Title, [string] $Color) {
    $process = Start-Process $Exe -ArgumentList @($Class, "`"$Title`"", $Color) -PassThru
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $front = [Desk]::GetForegroundWindow()
        $owner = [uint32]0
        [Desk]::GetWindowThreadProcessId($front, [ref]$owner) | Out-Null
        if ($owner -eq $process.Id) { return $process }
    }
    $script:failures += "$Title never came to the front"
    Write-Host "FAIL - $Title never came to the front (front: $([Desk]::ClassOf([Desk]::GetForegroundWindow())))"
    return $process
}

# Press a button of the app's main window by its accessible name.
function Press([string] $Name) {
    $isMain = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:shell.Id),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "TrueMain"))
    $isButton = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = @()
    for ($i = 0; $i -lt 20; $i++) {
        $main = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $isMain)
        $buttons = if ($main) { @($main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $isButton)) } else { @() }
        $button = $buttons | Where-Object { $_.Current.Name -eq $Name } | Select-Object -First 1
        if ($button) {
            $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            return $true
        }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "no '$Name' among the main window's buttons: $(@($buttons | ForEach-Object { "'$($_.Current.Name)'" }) -join ', ')"
    return $false
}

# A drag with the left button, in small steps, as a hand would.
function Drag([int] $FromX, [int] $FromY, [int] $ByX, [int] $ByY) {
    [Desk]::SetCursorPos($FromX, $FromY) | Out-Null
    Start-Sleep -Milliseconds 200
    [Desk]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero)
    for ($step = 1; $step -le 20; $step++) {
        Start-Sleep -Milliseconds 25
        [Desk]::SetCursorPos($FromX + [int]($ByX * $step / 20), $FromY + [int]($ByY * $step / 20)) | Out-Null
    }
    Start-Sleep -Milliseconds 200
    [Desk]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero)
}

function Key([byte] $Code, [bool] $Down) {
    [Desk]::keybd_event($Code, 0, $(if ($Down) { 0 } else { 2 }), [UIntPtr]::Zero)
}

function Screenshot([string] $Name) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    [Desk]::CopyScreen($dc, $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height)
    $graphics.ReleaseHdc($dc)
    $graphics.Dispose()
    $bitmap.Save((Join-Path $Out "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap
}

# What a panel's rectangle holds on screen: the share of it that is still the
# game's flat colour, and how many colours it has (text, icons, a card).
function Drawn($Bitmap, $Panel) {
    $left = [Math]::Max(0, $Panel.Left); $top = [Math]::Max(0, $Panel.Top)
    $right = [Math]::Min($Bitmap.Width, $Panel.Right); $bottom = [Math]::Min($Bitmap.Height, $Panel.Bottom)
    $game = 0; $total = 0; $colors = @{}
    for ($y = $top; $y -lt $bottom; $y += 2) {
        for ($x = $left; $x -lt $right; $x += 2) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $total++
            if ([Math]::Abs($pixel.R - $GameColor.R) + [Math]::Abs($pixel.G - $GameColor.G) + [Math]::Abs($pixel.B - $GameColor.B) -lt 24) { $game++ }
            $colors[$pixel.ToArgb() -band 0xF8F8F8] = $true
        }
    }
    [pscustomobject]@{ Total = $total; GameShare = $(if ($total) { $game / $total } else { 1 }); Colors = $colors.Count }
}

function Report([string] $Name) {
    $bitmap = Screenshot $Name
    $front = [Desk]::GetForegroundWindow()
    $panels = Panels
    $state = [ordered]@{
        step       = $Name
        foreground = [Desk]::ClassOf($front)
        panels     = @($panels | ForEach-Object {
            $drawn = if ($_.Visible) { Drawn $bitmap $_ } else { $null }
            [ordered]@{
                slug = $_.Slug; visible = $_.Visible; exStyle = $_.ExStyle
                rect = @($_.Left, $_.Top, $_.Right, $_.Bottom)
                hit  = $(if ($_.Visible) { [Desk]::ClassOf([Desk]::HitAt([int](($_.Left + $_.Right) / 2), [int](($_.Top + $_.Bottom) / 2))) } else { $null })
                gameShare = $drawn.GameShare; colors = $drawn.Colors
            }
        })
    }
    $bitmap.Dispose()
    $state | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Out "$Name.json")
    Write-Host "[$Name] front: $($state.foreground); shown: $(Shown)"
    $state
}

$WS_EX_TRANSPARENT = 0x20; $WS_EX_TOPMOST = 0x8; $WS_EX_LAYERED = 0x80000; $WS_EX_NOACTIVATE = 0x8000000
$InGame = "next-item,stats,win-probability"

$env:TRUEMAIN_LCU_REPLAY = $Tape
$env:TRUEMAIN_LCU_REPLAY_SPEED = "0"
$env:RUST_LOG = "truemain_desktop_lib=debug,lcu=info"
$script:shell = Start-Process $App -PassThru -RedirectStandardOutput (Join-Path $Out "app.log") -RedirectStandardError (Join-Path $Out "app.err.log")
$standIns = @()
try {
    for ($i = 0; $i -lt 120 -and (Panels).Count -lt 5; $i++) { Start-Sleep -Milliseconds 500 }
    [Desk]::Of([uint32]$script:shell.Id) | ForEach-Object { [ordered]@{ title = $_.Title; class = [Desk]::ClassOf($_.Handle); visible = $_.Visible; exStyle = ('0x{0:X8}' -f $_.ExStyle) } } |
        ConvertTo-Json | Set-Content (Join-Path $Out "0-windows.json")
    Expect ((Panels).Count -eq 5) "the app builds its five panels' windows"
    # The replay has opened the game and every page has measured itself.
    Start-Sleep -Seconds 8
    Expect ((Shown) -eq "") "nothing shows while the app, not the game, is in front"

    $standIns += StartStandIn $gameExe "RiotWindowClass" "League of Legends (TM) Client" $GameColorRef
    Start-Sleep -Seconds 3
    $state = Report "1-game"
    Expect ((Shown) -eq $InGame) "over the game, the in-game panels show ($(Shown))"
    Expect ($state.foreground -eq "RiotWindowClass") "the game keeps the foreground"
    foreach ($panel in @($state.panels | Where-Object visible)) {
        $styles = (Panels | Where-Object Slug -eq $panel.slug).Styles
        Expect ((($styles -band ($WS_EX_TRANSPARENT -bor $WS_EX_LAYERED -bor $WS_EX_NOACTIVATE -bor $WS_EX_TOPMOST)) -eq ($WS_EX_TRANSPARENT -bor $WS_EX_LAYERED -bor $WS_EX_NOACTIVATE -bor $WS_EX_TOPMOST))) "$($panel.slug) is topmost, layered, click-through and never activated ($($panel.exStyle))"
        Expect ($panel.hit -eq "RiotWindowClass") "a click on $($panel.slug) reaches the game ($($panel.hit))"
        Expect ($panel.gameShare -lt 0.5 -and $panel.colors -ge 6) "$($panel.slug) is drawn over the game (game colour $([Math]::Round($panel.gameShare * 100))%, $($panel.colors) colours)"
    }

    Key 0x09 $true
    Start-Sleep -Milliseconds 800
    $state = Report "2-scoreboard"
    Key 0x09 $false
    Expect ((Shown) -eq "item-value,$InGame") "TAB held adds the item value ($(Shown))"
    $itemValue = $state.panels | Where-Object { $_.slug -eq "item-value" -and $_.visible }
    if ($itemValue) {
        Expect ($itemValue.gameShare -lt 0.5 -and $itemValue.colors -ge 6) "item-value is drawn (game colour $([Math]::Round($itemValue.gameShare * 100))%, $($itemValue.colors) colours)"
    }
    Start-Sleep -Milliseconds 800
    Expect ((Shown) -eq $InGame) "TAB released takes it away ($(Shown))"

    # Alt+Shift+O hides the overlay for the game, and brings it back.
    foreach ($expected in @("", $InGame)) {
        Key 0x12 $true; Key 0x10 $true; Key 0x4F $true
        Start-Sleep -Milliseconds 300
        Key 0x4F $false; Key 0x10 $false; Key 0x12 $false
        Start-Sleep -Milliseconds 800
        Expect ((Shown) -eq $expected) "Alt+Shift+O toggles the overlay ($(Shown))"
    }
    Report "3-shortcut" | Out-Null

    $standIns += StartStandIn $otherExe "OtherAppClass" "Another app" $OtherColorRef
    Start-Sleep -Seconds 2
    Report "4-another-app" | Out-Null
    Expect ((Shown) -eq "") "another app in front hides the overlay ($(Shown))"

    $standIns += StartStandIn $classOnlyExe "RiotWindowClass" "League of Legends (TM) Client" $GameColorRef
    Start-Sleep -Seconds 2
    Report "5-game-by-class" | Out-Null
    Expect ((Shown) -eq $InGame) "the game known by its window class alone gets the overlay back ($(Shown))"

    # The preview, from the game page's settings: every panel, taking the mouse.
    Expect (Press "Overlay settings") "the game page opens the overlay settings"
    Start-Sleep -Seconds 1
    Expect (Press "Place on screen") "the settings start the preview"
    Start-Sleep -Seconds 2
    $state = Report "6-preview"
    Expect ((Shown) -eq "item-value,loading,next-item,stats,win-probability") "the preview shows every panel ($(Shown))"
    foreach ($panel in @(Panels | Where-Object Visible)) {
        Expect (($panel.Styles -band $WS_EX_TRANSPARENT) -eq 0) "$($panel.Slug) takes the mouse in the preview ($($panel.ExStyle))"
    }

    $before = Panels | Where-Object Slug -eq "win-probability"
    Drag ([int](($before.Left + $before.Right) / 2)) ([int](($before.Top + $before.Bottom) / 2)) 300 200
    Start-Sleep -Seconds 1
    $after = Panels | Where-Object Slug -eq "win-probability"
    Report "7-dragged" | Out-Null
    Expect ([Math]::Abs($after.Left - $before.Left - 300) -le 4 -and [Math]::Abs($after.Top - $before.Top - 200) -le 4) "a drag moves the panel (by $($after.Left - $before.Left),$($after.Top - $before.Top))"

    Expect (Press "Done") "the settings end the preview"
    Start-Sleep -Seconds 1
    $saved = Get-Content (Join-Path $env:APPDATA "gg.truemain.desktop/overlay-settings.json") -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
    Expect ($null -ne $saved.winProbability.custom) "ending the preview saves where the panel was dragged ($($saved.winProbability.custom | ConvertTo-Json -Compress))"

    $standIns += StartStandIn $gameExe "RiotWindowClass" "League of Legends (TM) Client" $GameColorRef
    Start-Sleep -Seconds 2
    Report "8-placed" | Out-Null
    $placed = Panels | Where-Object Slug -eq "win-probability"
    Expect ((Shown) -eq $InGame) "back over the game, the in-game panels show ($(Shown))"
    Expect ([Math]::Abs($placed.Left - $after.Left) -le 2 -and [Math]::Abs($placed.Top - $after.Top) -le 2) "the dragged panel stays where it was put ($($placed.Left),$($placed.Top))"
    Expect (($placed.Styles -band $WS_EX_TRANSPARENT) -ne 0) "out of the preview, clicks go through again ($($placed.ExStyle))"
}
catch {
    Write-Host "FAIL - $_`n$($_.ScriptStackTrace)"
    $script:failures += "$_"
}
finally {
    $standIns | Where-Object { $_ -and -not $_.HasExited } | ForEach-Object { Stop-Process -Id $_.Id -Force }
    if (-not $script:shell.HasExited) { Stop-Process -Id $script:shell.Id -Force }
    Get-Content (Join-Path $Out "app.log") -ErrorAction SilentlyContinue | Select-String "overlay|frontmost|tape|game" | Select-Object -Last 40 | ForEach-Object { Write-Host "app: $_" }
}

if ($script:failures.Count) {
    throw "smoke: $($script:failures.Count) failed: $($script:failures -join '; ')"
}
Write-Host "the overlay smoke test passed"
