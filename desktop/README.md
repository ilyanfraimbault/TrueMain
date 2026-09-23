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
- Draws the draft: both teams as splash banners on their lanes (lane icons on
  empty slots too), each side's bans, the phase timer.
- **Shows the build to run against the draft as it stands** — runes, summoner
  spells, skill order, starter, boots, build path and build tree — from the
  endpoint behind the site's matchup page (`POST /champions/{id}/composition-build`),
  fed every locked pick of both teams on its lane. It re-asks on every lock, and
  a lock landing while a request is out aborts that request (in Rust, and through
  the fetch signal in a browser) rather than queueing behind it. A matchup never
  recorded falls back to the lane's standard build, labelled as such.
- **Says how hard the lane is**: the site's own verdict (`laneVerdict`, gold gap
  at 15 over the same sampled games), with the lane win rate beside it.
- **Reads any champion's build**: clicking a pick shows that champion's build
  from its own side of the draft; a second click comes back to ours.

- **Resolves the enemy lanes** (#1674) and lets you correct them by dragging
  one enemy onto another, pinning your correction so the rest re-solve around
  it (#1677). A click on an enemy now reads its build instead.
- **Names your lane opponent**, with how sure it is — a coin flip says so.

## What it does not do yet

- **No pick recommendation on screen.** The endpoint ranks candidates (#1675)
  but the app sends an empty candidate pool: it does not know the player's
  champion pool yet, which is #1682.
- **The build view is a copy of the site's**, not the site's own components:
  `BuildTree`, `ItemRankBadge` and the lane verdict are ported by hand, and the
  icons carry no tooltip. Sharing them through a Nuxt layer is #1687.
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

The **navigation rule lives in Rust** (`crates/shell-state/src/lib.rs`), not in the
frontend: which screen belongs to which phase is a product decision, and two
implementations of it would drift.

## Testing without a game

Champion select is the app's subject and the hardest state to reach: it needs a
real game, it lasts a couple of minutes, and it cannot be paused to look at a
panel. Two ways in, covering different depths.

### A tape — the whole Rust path, no client

A tape is one recorded champion select, replayed as often as needed. It holds
the client's own payloads, so a replay goes through the same parsing, the same
state derivation and the same navigation rule as a live session.

```sh
# Record the next session you play. Nothing to do while it runs.
TRUEMAIN_LCU_RECORD=recordings/my-draft.jsonl ./TrueMain.app/Contents/MacOS/truemain-desktop

# Play one back, at the pace it happened.
TRUEMAIN_LCU_REPLAY=fixtures/ranked-draft.jsonl ./TrueMain.app/Contents/MacOS/truemain-desktop

# `0` skips every wait, which opens the tape on its final state.
TRUEMAIN_LCU_REPLAY_SPEED=0 TRUEMAIN_LCU_REPLAY=fixtures/ranked-draft.jsonl ...
```

While replaying, the app never looks for a client: the tape stands in for it,
and the last state stays on screen when the tape ends.

`fixtures/ranked-draft.jsonl` is committed and **synthetic** — a full ranked
draft written by hand, from bans to the pick that ends it. A tape you record is
not: it carries your Riot ID and the champions of everyone in your lobby, so
`recordings/` is gitignored. Only the endpoints the app acts on are ever
recorded; the client's socket also carries your friends list, your chat and your
notifications, and none of that is written.

Tapes are JSON Lines and meant to be edited: change a champion, delete a pick,
retime an event. `lcu::tape` describes the format.

Point a replay at a backend you are editing with `TRUEMAIN_API_BASE`, which is
read at startup and overrides the value baked in at build time:

```sh
TRUEMAIN_API_BASE=http://localhost:5008 TRUEMAIN_LCU_REPLAY=fixtures/ranked-draft.jsonl ...
```

### Dev scenarios — the UI alone, in a browser

```sh
cd desktop/app && npm run dev        # then pick a scenario at the bottom
```

`npm run dev` outside Tauri has no client and no Rust, so it opens on a picker
of states the client would have pushed — `?scenario=draft-locked` links to one
directly. This is for working on the UI with hot reload; it proves nothing below
the frontend. `app/app/fixtures/scenarios.json` holds the states, and the lane
answer each draft scenario shows. The build is real data: the dev server proxies
`/api` to `https://truemain.lol/api` (`nuxt.config.ts`, dev only), since there
is no Rust to ask. In a production build the picker
never renders — `import.meta.dev` is false — but Nuxt still bundles it, and the
fixtures sit in a 2 kB lazy chunk that is never fetched.

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
