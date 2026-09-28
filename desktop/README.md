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
- **A sidebar to every section the site has** — dashboard, champions, tier list,
  matchup, truemains, favorites — plus **champion select**, live, or planned
  by hand on the same page. The **gameflow phase still drives the screen**: champion select opens the draft on
  its own, and leaving it goes back home (only from the draft, never from a page
  the player opened by hand).
- **Draws the draft like the reference client**: each side's bans and the phase
  clock over the two teams as tall pick cards with their tier on their lane, our
  lane duel between them (champion vs opponent, and the lane win rate over the
  games behind the build).
- **Ranks the picks while ours is open**: "My pool" asks the draft endpoint
  (#1675) about the player's ten most-mastered champions played on the lane;
  off, every champion the tier list has on the lane (split into requests of 40,
  the endpoint's ceiling). Before any enemy pick or locked ally there is nothing
  to measure, so the podium shows win rates on the lane instead of "+0.0%".
- **Shows the build to run against the draft as it stands** once the pick is
  locked (or on a click), from the endpoint behind the site's matchup page
  (`POST /champions/{id}/composition-build`), fed every locked pick of both teams
  on its lane, next to the champion's lane builds and its best true mains. It
  re-asks on every lock, and a lock landing while a request is out aborts that
  request (in Rust, and through the fetch signal in a browser) rather than
  queueing behind it. A matchup never recorded falls back to the lane's
  standard build, labelled as such.
- **Reads any champion's build**: clicking a pick shows that champion's build
  from its own side of the draft; a second click comes back to ours.
- **Resolves the enemy lanes** (#1674) and lets you correct them by dragging
  one enemy onto another, pinning your correction so the rest re-solve around
  it (#1677).
- **Reads your champion mastery** once per login, to rank your own pool
  (`championPool`, most points first) without asking what you play.
- **Uses the site's own components** for everything the site already draws —
  the build tree and rune block, the game-entity tooltips, the leaderboard row,
  rank and region marks, the role picker — as labelled twin copies (see
  "Sharing with the site" below). The build's core keeps the site's blocks
  without the build path, which the tree under it draws (`build/BuildCore.vue`).
- **Shows a true main's own build** on a click in the build view's list — the
  site's player-scoped champion endpoint, through the shell.

## What it does not do yet

- **The ranking sees the lane opponent and our locked allies, not the rest of
  the enemy team**, and weighs the two equally. Both are backend work: #1713.
- **No win probability.** The reference apps show one out of a model we do not
  have; the draft strip carries the clock instead (#1671).
- **The rune import is written but not reachable.** Its client-side write path
  and the rule that protects the player's own pages are implemented and tested
  (`crates/lcu/src/runes.rs`); there is no button because the draft endpoint does
  not return a rune page yet (#1678).
- **The dashboard knows the player only by what the client says.** Their own
  numbers need #1682 — our database holds true mains only.
- **No in-game overlay.** That is v2, gated on the spike in #1673.

## Sharing with the site

The app is its own Nuxt project, so the site's components reach it as **twin
copies** — the repo's rule for a file two apps need (`decisions/web-frontend-rules.md`):
each copied file says so in a header naming its twin, and stays identical to it
except for lines marked app-specific. The shared types and utilities sit under
`app/shared/` at the same paths as `web/shared/`, so the copies keep their
`~~/shared/...` imports unchanged. What the app does differently lives beside
them, not inside them:

- `composables/useSiteShims.ts` answers the site composables the copies call
  (`useChampionSlugs`, `useBuildResolvers`, `useCanonicalIcon`) for an app with
  no image server and no player pages;
- `utils/static-data.ts` builds the site's static-data shapes from Data Dragon
  and Community Dragon in the webview, where the site does it in Nitro;
- `SkeletonImage`, `RankIcon`, `FavoriteToggle` and `LeaderboardRow` carry the
  app-specific differences, each stated in its header.

A Nuxt layer would replace the copies; that is #1687.

## Reaching the API

The API has **no public host**: the site reaches it through the web app's Nitro
proxy, inside the deployment's private network. The desktop app has no Nitro
server, so it uses that same public proxy (`https://truemain.lol/api`) as its
entry point, overridable at build time with `TRUEMAIN_API_BASE`.

Those calls go through **Rust**, not the webview's `fetch`. From the webview
they would be subject to CORS against an origin the site was never configured
for, and would force the content-security policy open to a remote host. From
Rust neither applies and the CSP stays closed. The draft and build calls have a
command each; the pages' reads go through `api_get`, which forwards a short
allow-list of read-only paths (tier list, truemains leaderboard and search, and
one true main's build on a champion — `/truemains/{nameTag}/champions/{id}`,
checked segment by segment) and nothing else.

Static game data is the exception: the webview fetches Data Dragon and Community
Dragon itself (the CSP lets those two hosts through), the same files the site's
static endpoints read. A true main's page opens on truemain.lol in the player's
browser — the shell's `open` is scoped to that origin.

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
of states the client would have pushed — `?scenario=draft-locked#/draft` links
to one directly (the route is in the hash). This is for working on the UI with
hot reload; it proves nothing below the frontend. `app/app/fixtures/scenarios.json`
holds the states. Everything else is real data: the dev server proxies `/api`
to `https://truemain.lol/api` (`nuxt.config.ts`, dev only), since there is no
Rust to ask. In a production build the picker never renders — `import.meta.dev`
is false — but Nuxt still bundles it, and the fixtures sit in a small lazy chunk
that is never fetched.

### Planning a draft by hand

Outside a live champion select, the **Champ select** page is a board to fill in
any order, read by the same draft screen (a real champion select takes the page
over the moment one starts). Choose your lane in the strip; click any empty card
or ban slot to put a champion there (the picker lists the champions played on
that lane first); a placed card can be changed or taken off on hover; clicking
a suggestion makes it your pick. Enemies stand on the lane they were placed on
rather than the one the guesser would give them. No turns, no timer. It works in
the packaged app and in `npm run dev`, with or without a client — with one, "My
pool" ranks the logged-in player's champions.

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
