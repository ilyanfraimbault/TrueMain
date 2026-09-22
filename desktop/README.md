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

- **Resolves the enemy lanes** (#1674) and lets you correct them by drag and
  drop or by two clicks, pinning your correction so the rest re-solve around it
  (#1677).
- **Names your lane opponent**, with how sure it is — a coin flip says so.

## What it does not do yet

- **No pick, rune or build recommendation on screen.** The endpoint ranks
  candidates (#1675) but the app sends an empty candidate pool: it does not know
  the player's champion pool yet, which is #1682.
- **The rune import is written but not reachable.** Its client-side write path
  and the rule that protects the player's own pages are implemented and tested
  (`crates/lcu/src/runes.rs`); there is no button because the draft endpoint does
  not return a rune page yet (#1678).
- **The dashboard is a placeholder.** It needs #1682 — our database holds true
  mains only, so a player we do not track has nothing to render.
- **No in-game overlay.** That is v2, gated on the spike in #1673.

## Reaching the API

The API has **no public host**: the site reaches it through the web app's Nitro
proxy, inside the deployment's private network. The desktop app has no Nitro
server, so it uses that same public proxy (`https://truemain.lol/api`) as its
entry point, overridable at build time with `TRUEMAIN_API_BASE`.

Those calls go through **Rust**, not the webview's `fetch`. From the webview
they would be subject to CORS against an origin the site was never configured
for, and would force the content-security policy open to a remote host. From
Rust neither applies and the CSP stays closed.

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
npm run tauri build        # bundles into desktop/target/release/bundle
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
