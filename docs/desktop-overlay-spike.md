# Desktop overlay spike (#1673) — verdict

Can the companion app draw a panel over a live League game on macOS without taking the game's input? **Yes**, with
one correction to what the issue expected. Measured on 2026-10-01 over practice-tool games, League client 16.19,
on an ad-hoc-signed release build; the window configuration below is what `desktop/src-tauri/src/overlay`
now ships.

## What League's "Full Screen" is on macOS

Not a borderless window and not a fullscreen Space — the research the issue started from was wrong here. In game the
window server lists two screen-sized windows owned by the game process at layer **2147483628**, which is
`CGShieldingWindowLevel()`: the game **captures the display**, the macOS equivalent of Windows' exclusive fullscreen.
It takes no Space of its own (a window of another app on the desktop Space stays listed on screen behind it).

Consequences:

- A panel at `NSStatusWindowLevel` (25), the level the issue planned, is drawn **behind** the game.
- A panel **one level above the shielding level** (2147483629) is drawn over it, and the game keeps the focus
  throughout (the frontmost application stays the game).
- At that level the panel would cover every other application too, so it is shown **only while the game process
  (`com.riotgames.LeagueofLegends.GameClient`) is frontmost** and hidden the moment the player cmd-tabs away.

## The window configuration that works

A `tauri-nspanel` panel (2.1) built hidden, then shown with `orderFrontRegardless`:

| Setting | Value | Why |
|---|---|---|
| Level | `CGShieldingWindowLevel() + 1` | Above the captured display |
| Style mask | `NonactivatingPanel` added to Tauri's | Showing or clicking it never activates the app |
| Panel class | `canBecomeKeyWindow = false`, `canBecomeMainWindow = false` | It can never take the keyboard |
| `hidesOnDeactivate` | `false` | The app is never active while the game runs |
| Collection behaviour | `canJoinAllSpaces`, `fullScreenAuxiliary`, `stationary`, `ignoresCycle` | On any Space, out of cmd-tab and Mission Control |
| `ignoresMouseEvents` | `true` in game | Click-through: the game keeps every click |
| Window | `decorations(false)`, `focused(false)`, `visible(false)`, `skip_taskbar(true)` | Created without ever activating |

`PanelBuilder::no_activate(true)` must stay **off**: it switches the app to the `Prohibited` activation policy while
the panel is created, and the app came back with none of its windows on screen — its main window included.

## Shortcuts: hotkeys do not reach the app in game

Global hotkeys (`RegisterEventHotKey`, through `tauri-plugin-global-shortcut`) work everywhere **except over a game in
Full Screen**: with the display captured, the window server delivers none of them — tested with ctrl+shift and
option+shift pairs, both logged from Finder, neither once in game. Reading the keyboard's state instead
(`CGEventSourceKeyState` / `CGEventSourceFlagsState` on the HID system state, polled every 30 ms) works in game.

## Permissions

None. Drawing the panel, reading the frontmost application and reading the keyboard's state raised no Screen
Recording, Accessibility or Input Monitoring prompt, and nothing appeared under Privacy for the app.

## tauri-apps/tauri#5566 (levels differing between dev and release)

Not reproduced in the release build, which is the one that matters. The dev build was not re-measured once the
shielding level was found.

## Not covered

- **Signed and notarised build**: waits for #1679; every measurement here is on an ad-hoc-signed release build.
- **Windows**: not run. The expected outcome — an always-on-top layered window fails over exclusive fullscreen, and
  players are told to use Borderless, as Blitz and Overwolf do — is unverified, so the app reports the overlay as
  unsupported on Windows.
- **Screen recording** of the panel over a game: the in-game capture the product owner took stands in for it.

## Tooling

`desktop/tools/overlay-probe.swift` prints, once a second, the frontmost application and the on-screen windows of
League and TrueMain front to back with their layer and bounds — how every finding above was read. It needs no
permission:

```sh
swift desktop/tools/overlay-probe.swift 900 | tee overlay-probe.log
```
