# TrueMain desktop companion

A companion app for the draft phase. It reads the running League client through
its local API, and shows what to do next instead of asking what to look up.

Tracking issue: **#1671**.

## What it does today

- Finds the running client on **macOS and Windows** — process arguments first,
  lockfile as a fallback — and reconnects on its own when the client restarts on
  a new port.
- Follows the client's **WebSocket event stream**, so champion select updates as
  picks land rather than on a poll.
- Switches screen on the **gameflow phase**: no client, lobby (dashboard), champion
  select (draft).
- Draws the draft: your champion and lane, both teams, bans, the phase timer.

## What it does not do yet

- **The lane opponent is not resolved.** Champion select does not expose enemy
  roles; the guesser is #1674 and its editable assignment panel is #1677.
- **No recommendation.** The draft endpoint is #1675.
- **The dashboard is a placeholder.** It needs #1682 first — our database holds
  true mains only, so a player we do not track has nothing to render.
- **No in-game overlay.** That is v2, gated on the spike in #1673.

## Layout

```
desktop/
  crates/lcu/     pure Rust client for the League client API — no Tauri, no GUI
  src-tauri/      the Tauri v2 shell: owns the connection, derives the state
  app/            Nuxt 4 SPA (ssr: false) rendering that state
```

`crates/lcu` deliberately carries no Tauri dependency, so it builds and tests on
any platform — including Linux CI, where the shell itself cannot build. Run its
tests with `cargo test -p lcu` from `desktop/`.

The **navigation rule lives in Rust** (`src-tauri/src/state.rs`), not in the
frontend: which screen belongs to which phase is a product decision, and two
implementations of it would drift.

## Building

Prerequisites: [Rust](https://rustup.rs), Node 22+, and Xcode command line tools
on macOS.

```sh
cd desktop/app && npm install
npm run tauri build        # bundles into desktop/src-tauri/target/release/bundle
```

For development, with hot reload and the app pointed at the Nuxt dev server:

```sh
cd desktop/app && npm run tauri dev
```

`npm run dev` alone opens the frontend in a plain browser with no client
attached — useful for working on the UI without League installed.

### Signing

Unsigned builds are quarantined by Gatekeeper and read as "damaged". Until
signing lands (#1679), a locally built `.app` runs after:

```sh
xattr -dr com.apple.quarantine /path/to/TrueMain.app
```

`minimumSystemVersion` is **13.3**, which is the floor the design system's
`oklch` and `color-mix()` usage imposes on WKWebView (Safari 16.4).

## Policy boundaries

Read-only against the client's local API. No memory reading, no injection, no
action taken on the player's behalf — no auto-accept, no instalock. Riot states
that apps on the LCU and in-game APIs are expected to keep working under
Vanguard; memory reading is what it blocks. Writing a rune page on an explicit
click is #1678, and is gated on the DevRel confirmation in #1680.
