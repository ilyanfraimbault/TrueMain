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
  matchup, truemains, favorites — plus **champion select**. The **gameflow phase still drives the screen**: champion select opens the draft on
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
- **Resolves the enemy lanes** (#1674) and lets you correct them: the lane icon
  under an enemy opens the five lanes, and picking one swaps it with whoever
  held it (dragging one enemy onto another does the same), pinned so the rest
  re-solve around the correction (#1677).
- **Reads your champion mastery** once per login, to rank your own pool
  (`championPool`, most points first) without asking what you play.
- **Opens on your profile**, read from your own client whether TrueMain tracks
  you or not: your ranked standing, the skin you chose as profile background and
  your latest 20 games (`player_record`, on demand, again after each game), with
  each game's scoreboard read once per launch for kill participation, damage
  share and both teams, and a game's timeline when its row is opened
  (`player_game`, in the site's match-detail shape). The history pages back
  through older games as it is paged (`player_history`). It is drawn with the site's
  ranked card, and rows, a compact match detail and cards derived from the
  site's (`components/dashboard/`); the LP curve and per-game LP come from
  standings the app notes on this machine (`utils/lp-history.ts`). None of it
  is sent anywhere.
- **Uses the site's own components** for everything the site already draws —
  the build tree and rune block, the game-entity tooltips, the leaderboard row,
  rank and region marks, the role picker — as labelled twin copies (see
  "Sharing with the site" below). The build's core keeps the site's blocks
  without the build path, which the tree under it draws (`build/BuildCore.vue`).
- **Shows a true main's own build** on a click in the build view's list — the
  site's player-scoped champion endpoint, through the shell.
- **Follows you into the game** (#1748): once the phase is `InProgress` the
  shell reads the game's own Live Client Data API (see "Reading the game"
  below) and the game page opens on its own — from home or from the draft the
  game started out of, never from a page you opened by hand — and its end takes
  you home. The page shows the ten players lane by lane, ours on the left and
  theirs mirrored on the right: champion and level, summoner spells, Riot ID,
  K/D/A and items, an item that just landed ringed for a few seconds, a dead
  player's portrait greyed under the seconds left before they respawn, and the
  kills of each side and the game clock above. It is the frame the in-game
  panels of #1747 land in.
- **Says what to complete next** (#1751): over the board, the legendary the
  champion's mains complete from where your build stands in a game like this
  one (`POST /champions/{id}/next-item`, #1749), with the situation behind it,
  the gold left to finish it, the components your gold buys now, two
  runners-up and the boots while you have none. Asked again whenever anyone's
  items change.
- **Draws over the game** (#1795, macOS): three small panels, each placed on
  its own — the next item alone (the item and the gold still to earn for it, or
  that it can be bought now), a win probability estimated from the item-gold
  gap and the map (turrets, inhibitors down, drakes, Baron, Elder), your CS per
  minute with its curve and gold per minute, the loading screen's ten players
  (games and win rate on their champion, ranked streak — #1753), and, while TAB is held, each team's item gold and each lane's gap —
  only while the game is the frontmost app, never over the client or anything
  else. Click-through and never focused, so the game keeps every click and key;
  ⌥⇧O hides them for the rest of the game. Set up from the game page (each panel
  on/off and where — five spots, or anywhere by dragging it in a preview — the
  next item's moment, size, opacity).
  The window layer is the #1673 spike's verdict, `docs/desktop-overlay-spike.md`.

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
- **No overlay on Windows yet.** The window layer is macOS's; on Windows the
  overlay settings say so and the game page carries the panel.
- **The next item reads the draft, not the enemies' builds yet.** What they
  have actually bought is #1750; the gold standing and the loading screen are
  #1752 and #1753.
- **Game recording is macOS only.** No Windows capture helper exists yet
  (#1745's Windows half waits for a tester); on Windows the Recordings page
  says so. Automatic clips (#1766) and instant replay (#1767) are not built.

## Recording games

When the player turns recording on (off by default) and a game of a recorded
queue starts, the shell (`src-tauri/src/recording/`) starts the capture helper
on the game window, follows the game clock and the live feed so the moments
land on the video, and when the game ends closes the video and emits
`recording://recap` so the app opens the recap. It then waits up to five
minutes for the match history to list the game and finalises the recording
from its timeline: the player's kills, deaths and assists, the objectives, the
K/D/A and the result. Recordings go to `~/Movies/TrueMain` (`Videos\TrueMain`
on Windows) unless the player picked a folder; clips cut in the recap go to
its `clips/` subfolder, each its own `clip.mp4` + `clip.json` + thumbnail. The
helper also cuts the clips (a passthrough export, no re-encoding) and takes the
thumbnails (`crates/capture-helper`, shared with the spike).

Recording needs a real client and a real game window: a tape or the simulator
plays the client's events but records nothing. Screen Recording is asked for
from the Recordings page — macOS shows its prompt once per app; after a
refusal the button opens System Settings. An unsigned build loses the
permission on each update (its signature changes), which the page shows.

The webview side (`app/app/pages/recordings/`, `components/recordings/`,
`components/recap/`, `composables/useRecordings.ts`) reads all of it through
the shell's recording commands and events (`types/recordings.ts` mirrors them):
the **Recordings** page lists full games and clips in DPM's layout with search,
filters and grouping; the **recap** (`/recordings/<id>`) plays a full game over
its timeline of moments, cuts clips by hand — drag across the timeline, or I / O
at the playhead, several ranges, each named and saved as its own file — and keeps
or deletes the full game; a **clip player** renames, favourites and deletes a
clip; the **settings** slideover holds the short list of choices; and a
dashboard row offers **Watch** when its game was recorded. Files reach the
webview through Tauri's asset protocol (`convertFileSrc`).

In development the shell finds the helper `swift build` leaves in
`capture/macos/.build/` — build it once with
`swift build -c release --package-path desktop/capture/macos` — or the one
named by `TRUEMAIN_CAPTURE_HELPER`. The permission is then the terminal's,
since the terminal starts `tauri dev`.

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
server, so it uses that same public proxy (`<site>/api`) as its entry point.

**Every build belongs to one site** (`src-tauri/src/site.rs`): the API it reads,
the pages it opens in the browser and the update feed it polls. The site is
`TRUEMAIN_SITE_URL` at build time, production (`https://truemain.lol`) when
unset. The release workflow builds each version twice — `truemain.*` against
production, `truemain-<version>.*` (named *TrueMain Beta*, its own bundle
identifier, so both install side by side) against preprod — and each site's
download page serves its own (`docs/ci.md`, "Desktop releases"). The preprod
address is never in the repository: CI reads it from the
`DESKTOP_BETA_SITE_URL` secret, and a developer puts it in `desktop/.env.local`
(gitignored):

```sh
# desktop/.env.local — read by `npm run tauri` (the shell) and by nuxt.config.ts (`npm run dev`)
TRUEMAIN_SITE_URL=http://<preprod host>:3001
```

With that file, `npm run tauri dev`, `npm run tauri:sim` and a local
`npm run tauri build` all talk to preprod; without it, to production.

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
static endpoints read. A true main's page opens on the build's site in the player's
browser, through the shell's `open_on_site` command: the webview names a path,
the shell adds its own origin, so the webview cannot open any other host.

## Reading the game

While a game runs, the **game process** — not the League client — serves the
**Live Client Data API** on `https://127.0.0.1:2999/liveclientdata/`. Riot
documents it for third-party use; it needs no credentials and reads no memory.
The game page reads one endpoint, `allgamedata`: every player's champion, side,
lane, level, items, K/D/A, summoner spells, `isDead` and `respawnTimer`, the
active player's own numbers (gold, stats, runes, abilities), the event feed and
the game clock. The request goes through `lcu::LiveClient` (`crates/lcu/src/live.rs`,
the one client for this API, shared with the game recording); `crates/live-client`
derives the page's state from it:

- **When.** The shell polls only while the gameflow phase is `InProgress` — the
  same rule (`AppState::screen`) that opens the game page — and stops the
  moment the phase leaves it. A crashed game the client offers to rejoin
  (`Reconnect`) is not read.
- **How often.** Every **2 s** while the game answers. Nothing the page shows
  moves faster than a purchase, a level or a death, and the respawn timer is
  counted down on screen between readings. While it does not answer the wait
  doubles up to **5 s**, which bounds how long after loading the board appears.
- **TLS.** Riot's documentation gives one root certificate (`riotgames.pem`)
  for both the client and the game, so the game goes through the same pinned
  verifier (`lcu::tls`), not a trust-everything one.
- **What reaches the frontend.** Not the payload: a **snapshot** when a game is
  first read, then only **updates** — a player's items, a level, a K/D/A, a
  death with its respawn time, a respawn — each numbered so the frontend can
  tell a missed one and re-read the whole state (`live_client::GameFeed`,
  `useLiveGame.ts`). Creep score, gold and stats change on nearly every reading
  and are left out until a panel needs them at a pace it can justify.

### What the API is known to show about enemies

The gold standing (#1752) and the next item's enemy axes (#1750) are only
legitimate on what the player can see in game, so this has to be settled before
they read enemies. **Nothing below has been checked against a live game yet** —
the reader was written and tested against Riot's documented schema and a
synthetic tape.

| | Status |
| --- | --- |
| `allgamedata` and `playerlist` list all ten players, enemies included, with their items, level, K/D/A, spells, `isDead` and `respawnTimer` | Documented by Riot (field list and sample) |
| Riot's documentation says nothing about vision, spectating or the loading screen | Read in the documentation |
| An enemy's items update while they are out of vision (live), or only when seen (last-seen) | **To verify in a live game** |
| An enemy's `isDead` / `respawnTimer` is exposed while they die out of vision | **To verify** — the in-game scoreboard shows enemy death timers, so the API showing them would add nothing the player cannot see |
| The loading screen: the port refuses connections, answers an error status, or answers a payload with no players | **To verify** — the reader treats all three as "no game yet" and retries |
| Spectator mode: `activePlayer` is an error object rather than a player | Expected from community reports; parsed either way, with no side marked as ours |

How to verify: record a real game (`TRUEMAIN_LCU_RECORD`, below — the tape
keeps every `allgamedata` reading raw) and compare, for an enemy laner, the
reading where an item appears in their inventory with the moment they were last
in vision; note in the same game what the first readings after the loading
screen hold. Update this table with what the tape shows.

## Layout

```
desktop/
  crates/lcu/             pure Rust client for the League client API — no Tauri, no GUI
  crates/live-client/     the running game's own API, and the in-game state derived from it
  crates/shell-state/     the app's state and the screen it calls for, and the overlay's
                          settings and rules (when it shows, where)
  crates/game-recording/  game recording minus the capture (#1744): settings, highlights,
                          game-clock anchor, storage budget — no Tauri, no GUI
  crates/capture-helper/  drives the capture helper: records, cuts clips, takes thumbnails,
                          reads the Screen Recording permission — shared by app and spike
  crates/capture-spike/   dev tool: records one game through the capture helper and
                          reports what it measured (#1745)
  capture/macos/          the macOS capture helper (Swift, ScreenCaptureKit) — built on
                          a Mac only; run with capture/spike.sh
  tools/                  overlay-probe.swift: the window server's view of a game (#1673)
  src-tauri/              the Tauri v2 shell: owns the connections, derives the state;
                          src/overlay/ is the overlay's window (macOS)
  app/                    Nuxt 4 SPA (ssr: false) rendering that state
```

The crates deliberately carry no Tauri dependency, so they build and test on
any platform — including Linux CI, where the shell itself cannot build. Run
their tests with `cargo test -p lcu -p live-client -p shell-state -p game-recording -p capture-helper` from
`desktop/`.

The **navigation rule lives in Rust** (`crates/shell-state/src/lib.rs`), not in the
frontend: which screen belongs to which phase is a product decision, and two
implementations of it would drift.

## Testing without a game

Champion select is the app's subject and the hardest state to reach: it needs a
real game, it lasts a couple of minutes, and it cannot be paused to look at a
panel. The game after it is the same problem at thirty minutes. Two ways in,
covering different depths.

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

A recording made during a game also holds every `allgamedata` reading of it,
raw (`"kind": "game"` lines), and a replay hands them to the same game feed the
live poll does, at the pace they were read. `fixtures/ranked-game.jsonl` is a
committed, **synthetic** game — the draft fixture's ten champions, sixteen
readings from 0:15 to 27:32 three seconds apart, with purchases, level-ups,
kills, deaths and respawns along the way, in Riot's documented payload shape.
It opens in game and stays there, so the page is what is left on screen:

```sh
TRUEMAIN_LCU_REPLAY=fixtures/ranked-game.jsonl ./TrueMain.app/Contents/MacOS/truemain-desktop
```

A recorded game is large (a reading every two seconds, ten players each) and
names all ten players, which is one more reason `recordings/` stays out of git.

Point a replay at a backend you are editing with `TRUEMAIN_API_BASE`, which is
read at startup and overrides the build's site (a local API has no `/api`
prefix); `TRUEMAIN_SITE_URL`, read at startup too, repoints a built app at
another site as a whole:

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
to the site's `/api` — preprod with `desktop/.env.local`, production without
(`nuxt.config.ts`, dev only) — since there is no Rust to ask. In a production build the picker never renders — `import.meta.dev`
is false — but Nuxt still bundles it, and the fixtures sit in a small lazy chunk
that is never fetched.

The overlay's page opens the same way, alone, as the panel shows it:
`?scenario=in-game-late#/overlay` (reload after changing only the hash — the
page stands outside the app's shell from its first load), and `&preview` adds
the placing state. Its window — level, focus, click-through, the frontmost
rule, dragging — exists only in the shell; `tools/overlay-probe.swift` reads
what the window server does with it during a real game
(`docs/desktop-overlay-spike.md`).

### Recordings without a game (development)

The recordings pages also run in `npm run dev` outside Tauri, on an in-memory
library (`app/app/fixtures/recordings.ts`, answered by
`app/app/utils/recordings-dev.ts`): full games and clips dated today, yesterday
and earlier — the `lobby` scenario's game ids, so its dashboard rows offer
"Watch" — with moments, settings and a status. Saving a clip, keeping,
deleting and renaming all work until the next reload.

```sh
# Any local MP4 plays as every recording, with seeking (HTTP Range); `ffmpeg`,
# when installed, cuts the thumbnails from it. Never commit a video.
TRUEMAIN_DEV_RECORDING=/path/to/video.mp4 npm run dev
```

Without the variable the player shows a placeholder and the cards the
champion's splash. A short video against a 30-minute fixture plays only its own
length. `?recordings=off|empty|permission|unsupported|missing` (before the `#`)
opens the page on that state, and `__devRecordingRecap('<id>')` in the console
stands in for the shell's `recording://recap` — it opens the recap only from
the dashboard or the game page, like the real event.

### Simulating a champion select (development)

A champion select needs a real game. In development one can be played by hand
instead, and the app takes it for a real one — navigation, parsing and all:

```sh
cd desktop/app && npm run tauri:sim
```

then open **http://localhost:3003/#/dev/draft-sim** in a browser beside the
app. That page is the League client in champion select: choose the position you
play, then click any pick or ban, on either side and in any order, to put a
champion there — the picker lists the lane's champions first. Your own pick is
hovered until **Lock in**; the others lock at once. The first click starts the
champion select (a synthetic `Simulated#DEV` player, with a mastery list of each
lane's most played champions for "My pool"); **Game starts** and **Dodge** end it
the way the client does, **Clear** empties the board. The app answers every
click live — suggestions, lanes, builds.

The page sends what the client would — gameflow phase, summoner, mastery, and
`/lol-champ-select/v1/session` in the client's shape — as tape readings to a
relay on the dev server (`server/routes/__sim/lcu.ts`), and the shell, started
with `TRUEMAIN_LCU_SIM`, polls it and applies each reading like a live event
(`src-tauri/src/sim.rs`). Both halves exist only in development: the page is
dropped from builds, the relay answers only under `npm run dev`, and the shell's
reader is not compiled into a release build. Like a tape, it stays above the
transport — no fake client, no TLS hole.

### Simulating a game (development)

The same relay plays a game: with `npm run tauri:sim` running, open
**http://localhost:3003/#/dev/game-sim**. **Start game** sends the phase and
the first reading of `fixtures/ranked-game.jsonl`, and the app opens its game
page; **Play** sends the next reading at the chosen pace, **Next reading** one
at a time, and any reading can be clicked to send it — an earlier one goes back
in time, items sold and levels lost. **End game** moves the phase on, which
clears the board and takes the app home. The shell hands each reading to the
game feed exactly as the live poll would.

In a plain browser, the **In game** dev scenarios show the page on the state the
feed holds at a given reading of that tape; a test in `crates/live-client`
(`tests/scenarios.rs`) keeps them equal to it.

## Building

Prerequisites: [Rust](https://rustup.rs), Node 22+, and Xcode command line tools
on macOS.

```sh
cd desktop/app && npm install
# macOS: the capture helper goes inside the bundle (`bundle.macOS.files`)
swift build -c release --package-path ../capture/macos
mkdir -p ../src-tauri/binaries && cp "$(swift build -c release --package-path ../capture/macos --show-bin-path)/truemain-capture" ../src-tauri/binaries/
codesign --force --sign - ../src-tauri/binaries/truemain-capture
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
