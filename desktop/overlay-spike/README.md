# Overlay spike (#1673) — test protocol

Can a TrueMain panel sit over a live League game on macOS without taking the
game's input? This folder holds what it takes to answer that on a real game.
The verdict and the window configuration that worked go to `docs/` afterwards;
this file is only the protocol.

The panel is built only with the `overlay-spike` Cargo feature
(`src-tauri/src/overlay_spike.rs`), so no regular build carries it. It is a
`tauri-nspanel` panel at the `Status` level with `NonactivatingPanel`,
`canJoinAllSpaces` + `fullScreenAuxiliary` + `stationary` + `ignoresCycle`, a
class that can never become key, and `hidesOnDeactivate` off. It starts
click-through, top-right of the main screen, and shows a ticking clock, a
button and a hover zone (`app/public/overlay-spike.html`).

Found while building it: `PanelBuilder::no_activate(true)` must stay off. It
flips the app to the `Prohibited` activation policy while the panel is created,
and the app came back with **none** of its windows on screen, the main window
included. Creating the window `focused(false)` + `visible(false)` and showing it
with `orderFrontRegardless` does the same job without that side effect.

| Shortcut         | Effect                                      |
|------------------|---------------------------------------------|
| `ctrl+shift+O`   | show / hide the panel                       |
| `ctrl+shift+I`   | toggle click-through ↔ interactive (mouse)  |

## Build

```sh
cd desktop/app && npm ci
npm run tauri:overlay:build
```

It builds an ad-hoc-signed `TrueMain Overlay Spike.app` in
`desktop/target/release/bundle/macos/`, under its own bundle id so it never
collides with an installed TrueMain. Signing and notarisation wait for #1679, so
this is the closest available to the release build the issue asks for. Start it
from a terminal to see its log:

```sh
"desktop/target/release/bundle/macos/TrueMain Overlay Spike.app/Contents/MacOS/truemain-desktop"
```

The dev build is `npm run tauri:overlay` (from `desktop/app`). Run both passes
below on the release build first, then repeat pass 2 under the dev build:
tauri-apps/tauri#5566 says levels and collection behaviours can differ between
the two.

Environment variables, to try variants without rebuilding:

- `TRUEMAIN_OVERLAY_LEVEL=floating|status|screensaver|<number>` — the window
  level (default `status`, 25).
- `TRUEMAIN_OVERLAY_ACCESSORY=1` — run the app as an accessory (no Dock icon),
  in case a regular app's panel does not join the game's Space.

## Probe

In a second terminal, before starting the game:

```sh
swift desktop/overlay-spike/probe.swift 1800 | tee overlay-probe.log
```

Every change, it prints the **frontmost app** (who receives the keyboard) and
the on-screen windows of League and TrueMain **front to back** (`#n` is the
stacking order, lower is in front), with their layer and bounds. It needs no
permission.

## Pass 1 — what League's "Full Screen" is

In-game settings → Video → Window mode **Full Screen**. In game:

- [ ] Open Mission Control (F3 / three-finger swipe up): is League a
      **separate Space** at the top, or a window on the desktop?
- [ ] In the probe log while in game: does the TrueMain **main window**
      (1180×760) disappear from the on-screen list? It lives on the desktop
      Space only, so it disappears if League took its own Space and stays if
      League is borderless.
- [ ] League's window bounds equal to the screen frame?

Repeat with **Borderless** and **Windowed**.

## Pass 2 — the panel over a game (per window mode)

Start the spike app, then a game (a practice tool is enough).

- [ ] The panel is visible over the game, clock ticking.
- [ ] The game keeps every input with the panel up: move, cast, chat (Enter),
      shop (P), camera. The probe shows `front=League of Legends` the whole time.
- [ ] Click-through: clicking where the panel sits hits the game, the panel's
      click counter stays at 0.
- [ ] `ctrl+shift+I` → interactive: the hover zone lights up and counts, the
      button counts clicks. After a click, does the game still take the
      keyboard? (probe: `front` must stay League; app log: no
      `the panel BECAME KEY` line).
- [ ] `ctrl+shift+I` again → back to click-through.
- [ ] `ctrl+shift+O` hides and shows the panel in game. Do the shortcuts reach
      us while League is frontmost? Does League also see the keypress?
- [ ] cmd-tab out and back: the panel is still there, the game still takes input.
- [ ] Switch Space (ctrl+→ / ctrl+←) and back: same.
- [ ] macOS asked for no permission (Screen Recording, Accessibility, Input
      Monitoring): nothing in System Settings → Privacy for the spike app.
- [ ] Record the screen (cmd+shift+5) for the issue's acceptance.

If the panel does not show over a Full Screen game, retry with
`TRUEMAIN_OVERLAY_ACCESSORY=1`, then with `TRUEMAIN_OVERLAY_LEVEL=screensaver`,
and note which one changed it.

## What to bring back

The app's terminal log, `overlay-probe.log`, the screen recording, and the
checklist above ticked per window mode and per build (release / dev).
