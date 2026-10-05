# Desktop app footprint

How much memory and CPU the desktop app really uses, measured the same way every time (#1916). The figure on
`/download` comes from this page and nowhere else: a number is measured here or not shown.

## Method

Two scripts, one per platform, sample the whole app every 5 s over a fixed window (300 s by default) and report the
median and the p95:

```sh
desktop/tools/measure-footprint.sh <scenario> [seconds] [interval]            # macOS
pwsh desktop/tools/measure-footprint.ps1 -Scenario <scenario> [-Seconds] [-Interval]   # Windows
```

Each writes its samples to `footprint-<scenario>-<timestamp>.csv` and prints a row for the tables below.

**What counts as the app** — the whole process tree, helpers included:

| | Processes | How they are found |
|---|---|---|
| macOS | the shell (`TrueMain`, `truemain-desktop` in a dev build), `truemain-capture`, WebKit's WebContent / Networking / GPU services | descendants of the shell, plus every process whose *responsible* process is the shell (`responsibility_get_pid_responsible_for_pid`, Activity Monitor's grouping, no root needed): WebKit's XPC services are launchd's children, not the app's |
| Windows | `TrueMain.exe` (`truemain-desktop.exe` in a dev build), every `msedgewebview2.exe` it started, `truemain-capture.exe` | descendants by `ParentProcessId` (`Get-CimInstance Win32_Process`), to any depth |

**Counters**, summed over the tree at each sample:

| | Memory | CPU |
|---|---|---|
| macOS | physical footprint, `proc_pid_rusage` → `ri_phys_footprint`: Activity Monitor's "Memory", what `footprint -p` prints | user + system time over the interval, % of one core |
| Windows | `\Process(*)\Working Set - Private`: Task Manager's "Memory" | `\Process(*)\% Processor Time`, % of one core |

**Scenarios**:

| Id | Scenario | How |
|---|---|---|
| `idle` | the dashboard, client open, no phase | a real client |
| `draft` | champion select | the draft simulator (`npm run tauri:sim`); spot-check a real one |
| `game` | in game, overlay on (default panels), recording off | a real game — a practice tool or custom game is fine |
| `game-rec` | as `game`, recording on at 1080p60 | a real game |
| `game-standin` | regression only, labelled as such | `/dev/game-sim`, or the Windows stand-in game of `overlay-smoke-windows.ps1` |

Start the window once the scenario is settled (the game past its loading screen, the dashboard loaded). Each row
names the app version, the OS and the machine. CI numbers, if any, catch regressions; they are never the `/download`
figure.

**When to measure again**: a Tauri or WebView major, a webview added or removed, a change to recording, or a release
that changes what runs during a game. The `/download` figure is refreshed with it.

## What runs during a game

- **Webviews.** The main window, with the game page open. Since #1916 an overlay panel has a webview only while it
  can show: switched on, from the game's loading screen to its end (or in the preview). Before, all four panels had
  one from launch, switched on or not, for the app's life.
- **Shell.** The Live Client Data poll (2 s, 5 s while loading), the overlay's watch (keys every 30 ms, the
  frontmost app every ~240 ms), the client's websocket, the loading screen's reads, the usage counts (60 s tick, a
  send every 5 min).
- **Recording**, when on: the `truemain-capture` helper, encoding in hardware.

Waste removed in #1916, before any measurement:

- an overlay panel's webview exists only while it can show (above);
- identical API reads asked at once by two webviews — the next item, asked by the game page and the overlay's panel
  on the same reading of the game — go out as one request (`SharedReads`, `src-tauri/src/api.rs`);
- the update check's 15-minute timer skips its ticks during a game, when nothing would install anyway.

## Results

No measurement has been taken yet: the tables fill as runs are made on real machines. Until then `/download` shows
no figure.

### macOS

| Date | App | Scenario | Memory median / p95 (physical footprint) | CPU median / p95 (% of one core) | macOS | Machine |
|---|---|---|---|---|---|---|

### Windows

| Date | App | Scenario | Memory median / p95 (private working set) | CPU median / p95 (% of one core) | Windows | Machine |
|---|---|---|---|---|---|---|
