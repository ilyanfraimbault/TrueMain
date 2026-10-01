# Desktop capture spike (#1745)

Game recording (#1744) needs a screen capture and a hardware encoder on macOS
and Windows. This spike settles whether the chosen stack works on a real game
before any of it is wired into the app. This page is how to run it, and where
its results are recorded.

## What runs

Two processes, as the app will have them:

- **`truemain-capture`** (`desktop/capture/macos`, Swift) — the platform
  helper. ScreenCaptureKit captures the game's window, AVAssetWriter encodes
  it to H.264 + AAC through VideoToolbox (the Mac's hardware encoder) into an
  MP4 written in one-second fragments. A separate process so a crash in
  capture cannot take the app down. It speaks JSON lines on stdout
  (`window`, `started`, `progress`, `stopped`, `error`) and stops on `stop`
  on stdin, SIGINT or SIGTERM.
- **`truemain-capture-spike`** (`desktop/crates/capture-spike`, Rust) — drives
  the helper through `game-recording`'s session, the same state machine,
  anchor, highlights and storage the app will use. It waits for a game (the
  Live Client Data API answering), records it, reads the game clock every
  5 s and the live events every 2 s, stops when the League client says the
  game is over, then waits for the match history to resolve the highlights
  from the timeline.

It writes one folder per game: `video.mp4`, `recording.json`, `report.md`
(everything it measured) and `player.html` (the video with the highlights as
markers, a jump lands 5 s before each moment).

## Running it on a Mac

Either way, the first run asks for **Screen Recording** permission for the
terminal app it was started from (System Settings → Privacy & Security →
Screen Recording). Allow it, quit and reopen the terminal, and run again.

**Prebuilt** — no toolchain needed. From the `macOS capture spike` job of the
`Desktop` workflow (any desktop PR, or `develop`), download the
`truemain-capture-spike-macos` artifact, then:

```sh
unzip truemain-capture-spike-macos.zip
tar -xzf truemain-capture-spike-macos.tar.gz
xattr -dr com.apple.quarantine truemain-capture-spike
./truemain-capture-spike/truemain-capture-spike --resolution 1080p --fps 60
```

**From source** — needs Xcode's command-line tools and Rust:

```sh
desktop/capture/spike.sh --resolution 1080p --fps 60
```

Options: `--resolution native|1440p|1080p|720p` (default `1080p`),
`--fps 30|60` (default `30`), `--no-audio`, `--out DIR` (default
`./capture-spike`), and `--window-id N` when the helper picks the wrong
window — `truemain-capture list` prints every window it can see.

Start the spike, then start a game (a practice tool game is enough for a first
run). Ctrl+C stops it early; the video and report are still written.

## What to measure

For each run, `report.md` holds the measured part — the window and encoder
settings, frames delivered and dropped, the helper's CPU, the file size per
minute, how closely the clock reads fit the anchor, and how far the live feed's
moments sit from the timeline's. Its last section lists what only the player can
see; fill it in:

- the display mode (Windowed / Borderless / Full Screen), and whether the video
  shows the game throughout;
- in-game FPS (Ctrl+F) on the same scene without and with recording;
- in `player.html`, opened in Safari (the WebKit the app's window renders with):
  does the video play and seek, does each marker land just before its moment;
- the sound;
- when the permission was asked, and whether anything (an alt-tab, a Space
  switch) broke the capture.

Worth running at least: 1080p 30 and 1080p 60 in Borderless, and once in Full
Screen.

## Windows

No Windows machine with League is available yet (2026-10-01); the Windows
helper (Windows.Graphics.Capture + Media Foundation) waits for a tester.

## Results

Not measured yet. Each run's figures go here, copied from its `report.md`,
with the machine they were taken on.
