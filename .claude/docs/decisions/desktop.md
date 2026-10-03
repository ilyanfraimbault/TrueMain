# Desktop companion

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

**The app is laid out like the reference client (DPM): a sidebar to every section the site has, and the gameflow
phase still opens the draft.** #1671 made the phase *the* navigation; the product owner asked for the reference
layout instead, so the player can reach the site's sections from the app. The phase rule survives as a push: entering
champion select opens the draft, and leaving it returns home — but only from the draft, so a page the player opened
by hand is never pulled away (2026-09-27) — #1671.

**The draft shows no win probability; the strip over the teams carries the phase clock, and the middle column the
lane duel.** The reference apps show a probability out of a model we do not have; a number that comes from neither
an endpoint nor a measurement is not one the app shows. The clock bar is neutral-coloured on purpose: a blue-to-red
bar in that slot would read as the probability it replaces. The duel shows the lane win rate over the games behind
the build and nothing else — the gold gap at 15 was cut as noise at a glance (2026-09-27) — #1671.

**One card on the board is selected, ringed in the primary colour: the champion whose build and lane duel are on
screen.** It is the player's own slot by default, even empty, since the picks ranked are for it; clicking any other
placed champion, ally or enemy, selects it and shows its build against its lane opponent, and clicking it again
returns to the player. Its lane opponent across the board carries a faint ring of the same colour, so the duel reads
as a pair — the marker follows the selection rather than staying on the player's own opponent. The board had carried
a ring per role in several colours (ours, the viewed one, our lane opponent, the slot being filled) and the product
owner read them as competing selections; the slot being filled now pulses its side's hairline instead of taking a
ring (2026-09-28) — #1671.

**Champion select is the live draft only; a draft played by hand is a development tool, fed to the shell as a
client would feed it.** The simulator first had its own sidebar entry, then lived on the champion select page (as a
control bar, then as a board to fill); the product owner wanted none of it in the product — it exists so a developer
can test champion select without launching a game. So `/draft` shows the live draft or waits for the next one, and the
simulator is `/dev/draft-sim`, dropped from builds: a champion select filled by clicking — our position, any pick or
ban in any order (a turn order to follow read as broken) — that posts the client's payloads (phase, summoner, mastery, the `/lol-champ-select/v1/session` body) to a dev-server relay, which a
debug build of the shell polls when `TRUEMAIN_LCU_SIM` is set (`npm run tauri:sim`) and applies like live events. That
keeps the whole path under test — parsing, state, phase navigation — without a fake client: the shell pins Riot's TLS
root, and a fake LCU would need a hole in a binary that ships (the reason `lcu::tape` replays above the transport). A
card shows its lane glyph inside while empty and under it once filled, never both (2026-09-28) — #1671.

**An enemy's lane is corrected from its lane icon, and a guessed lane carries no doubt mark.** The icon under each
enemy card opens the five lanes; picking one swaps the enemy with whoever held it, pinned like a drag (which still
works). The "?" beside an uncertain guess and the pin beside a corrected one were read as noise: a wrong lane is simply
the one to correct, and the "Reset lanes" button says corrections exist. The build view's header is the champion's
icon, name and lane icon — no "Your pick"/"Ally" wording — and the draft build's heading is "VS" with the opponent's
icon (2026-09-29) — #1671.

**The app has no browser chrome: no back/forward arrows and no patch label in the top bar.** Both made the window
read as a web page; the sidebar is how the app is navigated, and the bar keeps only the ⌘K champion search
(2026-09-28) — #1671.

**Picks are ranked from the player's pool or from the whole lane, never from a "meta" slice.** "My pool" is the
player's ten most-mastered champions (LCU champion mastery) that the tier list has on the lane; off, every champion
the tier list has on the lane, split into requests of 40 (the endpoint's ceiling) and merged in the endpoint's own
order — each candidate's score depends on it alone, so the merge is exact. Before an enemy picks or an ally locks
there is nothing to measure, so the podium shows win rates on the lane in the pool's order rather than "+0.0%". The
ranking itself is the endpoint's; the enemy-team component and a lane-first weighting are #1713 (2026-09-27) — #1675.

**The app draws the site's components, as labelled twin copies, not look-alikes** — superseded page by page by the shared layer above (#1732): a component that moves into the layer loses its twin. Hand-ported versions (a skill
order with plain letters, icons without tooltips) were rejected as "not the site". The copies follow the web↔admin
twin rule (`web-frontend-rules.md`): a header names the twin, differences are marked app-specific, and the app's
behaviour sits in shims beside them (`useSiteShims.ts`, `utils/static-data.ts`) so the copies stay verbatim. The
shared types and utils live under `desktop/app/shared/` at the site's paths so `~~/shared` imports resolve
unchanged. A verbatim copy over the size limit (`LeaderboardRow.vue`) is recorded in the size baseline beside its
twin rather than split away from it. The app's window is fixed and narrower than the site's pages, so where a site
row does not fit, the twin carries marked width adjustments (the leaderboard row gives the Riot ID its content width
and reserves the sub-mains column only where it shows) and the build view's narrow column uses the site's compact
home-page row instead, with the tag under the name — a truncated name was the first thing reported. The layer that would end the copies is #1687
(2026-09-27).

**The pages the app shares with the site are one implementation, in a Nuxt layer both apps extend (2026-10-01).**
The product owner's call: champions, tier list, matchup, truemains and favorites must be *the same pages* in the app
and on the site, not look-alikes kept in step by hand — the twin copies below drifted at the page level, where nothing
was copied, and every layout request applied to one side left the other behind. Those pages, and everything they are
built from, move into `web/layers/common`: the site registers it on its own (`web/layers/*`), the app `extends` it.
The layer sits inside `web/` because the site's image builds from `./web` alone. A shared page is a component
(`components/page/*`) that each app's route wraps with what is its own — the site's head tags, the app's scroll
container. The environment differences are a short host contract (the layer's `README.md`): `useApiFetch` (the site's
Nitro proxy; the app's shell, with the site's `/static/*` answered from Data Dragon in the same shapes),
`useChampionSlugs`, `useCanonicalIcon`, and two components the app cannot draw the site's way (`SkeletonImage`,
`RankIcon`). The design system (`theme.css`), the Nuxt UI theme and dark mode come with the layer, so the app's own
pages take the site's materials too, and the app reads the site's `shared/` instead of a copy. What stays the app's:
the dashboard, the draft and the champion page. The pages moved one PR each — tier list, champions, truemains, favorites, matchup — and the
twins they used went with them; the twins left serve the app's own pages (2026-10-01) — #1732.

**The build pane shows the site's core without its build path, and a true main's own build on a click.** The
site's core blocks (`Champion/Core/*`) keep the site's layout — summoners over starter, skill order over boots,
spread by flex, the rune block in a 272 px column beside them at the heights the site matched (148 / 152 px) — but
the runes go beside the rest from a 36rem container instead of 48rem, since the pane is ~630 px, and the build path
is left out: the build tree right under the core draws the same path item by item, and in a short pane the line
said it twice. A build with no branch to draw (a thin sample) states the path instead of the tree. The tree twin
takes its node size and gaps as app-specific props (28 / 10 / 20 px against the site's 36 / 22 / 44). The build rows
are icons alone — the keystone with the secondary tree as its badge, three items, the win rate — since an item's
name spelled out said less than its icon and cost the width; the column went from 22rem to 18rem, the most a
15-character Riot ID still fits. A true main in the list opens *their* build on the champion (the player-scoped
champion endpoint, their latest patch with enough games; on the lane asked, else across lanes) in place of the lane
build, with a line saying whose it is and a link to their profile — the reason to list them in a draft is to copy
what they run. The shell reads that one path pattern beside its fixed list (2026-09-28) — #1671.

**Icons are bundled at build time, routes live in the hash, and images are drawn without a `load` gate.** The
packaged app has no server to resolve an icon and a CSP that reaches only Data Dragon and Community Dragon
(`icon.provider: 'none'` + `clientBundle.scan`); Tauri's custom protocol serves files, not an SPA fallback, so a
history-mode path would 404 on reload; and WKWebView never reported `load` for images inserted after the first,
which left fade-in-on-load art invisible for good — the twinned `SkeletonImage` therefore draws as soon as it
decodes (2026-09-27).

**The beta ships unsigned, as GitHub pre-releases resolved by the site, and updates itself.** The product owner chose
an unsigned beta over paying for Apple notarisation and a Windows certificate before anyone has used the app; the
download page carries the one-time Gatekeeper / SmartScreen steps, and signing waits for the public release. The
installers are `desktop-v*` pre-releases on the site's own repository — pre-releases because GitHub's "latest" must
stay the site's, and `deploy-prod.yml` skips those tags because it runs on every published release. The site resolves
the newest one server-side (`/api/desktop/download/{platform}`, `/api/desktop/latest.json`), so neither the page nor
the installed app names a version and a new build needs no site deploy. The Tauri updater ships from the first beta —
without it every tester stays on whatever they installed — offering the update in a toast and never restarting on its
own, since a restart mid champion select would cost the draft. The download page is public, marked beta
(2026-09-28) — #1719. Revised: the app checks its feed every fifteen minutes, not only at launch, downloads
a new build in the background and installs it itself at launch when no champion select or game runs — see below.

**The app is versioned by hand, built on every bump for preprod, and promoted to production by hand.** A site release
is not an app release, so the app keeps its own version (`tauri.conf.json`), bumped in a PR; merging a bump to
`develop` builds it and publishes a `desktop-v<version>` pre-release, which preprod's site serves (channel `beta`).
Production (channel `stable`) serves only the one release promoted through the `Desktop promote` workflow — a
version typed in by the product owner, not computed from commits, and not tied to the site's release because the
promotion must wait for the endpoints the build reads to reach production. Supersedes "tag `desktop-v*` to ship";
the updater keeps polling production only, so testers install beta builds by hand — revised by #1779: each build polls its own site (2026-10-01) — #1772. Revised by #1799: every app change builds a preprod beta, and a bump
reaches production without a hand promotion, once production runs what it reads — see below.

**Every app change ships to preprod; a version bump ships to production once production runs what it reads
(2026-10-02).** The product owner's call: a tester should not wait for a throwaway bump to try an app change, and
nobody should have to remember a promotion. Each push to `develop` touching the app (`desktop/`, `web/layers/`,
`web/shared/`) builds the preprod flavour as `desktop-vX.Y.Z-beta.N` — the run number, not the commit SHA, because
the updater installs only a strictly greater semver; the SHA is in the notes, `X.Y.Z` the bumped version or its next
patch once that one has its production build. Only the version stays by hand: a bump builds the production flavour
as a draft, served on truemain.lol as soon as the site release running in production contains the bump commit or
does not differ from it on `web/` and `backend/{Api,Core,Data}` — checked after the build and after every site
release's rollout — so the production app is never newer than the production API it calls (the gap #1772's hand
promotion guarded against). `Desktop promote` remains for rollbacks. The last ten betas are kept, and the site reads
up to five pages of releases, so the stable one is never lost behind them — #1799.

**The dashboard is read from the player's own client, not from TrueMain's API, and built from the site's profile
components.** The dashboard is for whoever installed the app, and TrueMain only tracks true mains, so the site's
profile endpoints would leave most players with an empty page until #1682 settles an intake. The client already knows
every player: the shell reads the ranked standing, the profile background skin and the latest 20 games from the LCU
on demand (a Tauri command, not the pushed state — the record only moves when a game ends), each game's full
scoreboard once per launch (kill participation, damage share, both teams), and a game's timeline when its row is
opened (`player_game`), all cached by game.

The layout went through three rounds. A DPM-style page of widgets of its own (ranked card, recent-games list,
last-game card, champion podium, chips on the banner) was "not the site"; the site's profile components dropped in
verbatim then read too tall and out of step with the rest; and the site's match-detail panel, twinned into the
accordion, was a different colour and far too big inside a row (2026-09-30). So the ranked card (LP curve, twin — its
chart a plain-SVG stand-in for the site's vue-chrts wrapper) is the site's own, and the rest is derived from the
site's and names its source (`components/dashboard/`): 54 px match rows as separate cards (no container around the
history), a result edge instead of a tint, the build shown as on the site (spells over each other, keystone over the
secondary tree, the site's item grid); an accordion that stays on the row's surface with no card inside the card, its tabs the site's link tabs across the
whole row — Scoreboard on 40 px lines with each build laid out as on the match row, Build (laning @15, per-minute
figures, build order, the skill order centred as on the site, for any of the ten), Runes as small tiles (portrait and
keystone beside the page, the name in the tooltip), five a side — fed in the site's detail shape by `player_game`. The
history pages ten games at a time with the site's pagination; the banner and the cards read the latest 20, and a page
past what is loaded reads the client's older pages (`player_history`, 20 at a time, up to 100 games), the pager
offering one page more while the client still has some; champions and roles cards with the heading inside
like the ranked card. Every card is the same `surface rounded-lg`. The champions card reads performance (games, KDA,
win rate) where the site's reads play rate: the product owner asked for how they fare on their champions. The banner
keeps the player's skin with the site's header vocabulary (name, region flag, level) and repeats nothing the cards
beside it say.

The client keeps no LP history, so the app notes the Solo/Duo standing on this machine at each read, only when it
moved, 90 days deep; the curve and each game's LP gain come from those snapshots — the last before the game against
the last after it and before the next Solo/Duo game, null across a promotion or with a side missing — and start empty
on a fresh install rather than being estimated. The detail's laning @15 and first-to-level-2 are read from the
timeline by the site's rules (the frame at 15:00 against the opposing lane; the second skill point). TrueMain's
performance score and a participant's rank are not in the client's data and are not shown. The form tiles are the last
five games against the whole sample, in the ranked card's delta idiom (a trend arrow on the data axis, no sign); a
flat move or a too-short sample shows no delta. No composite player score. Remakes are kept out of the list and of
every average. The patch's best picks by lane stay as the no-client state (2026-09-30) — #1683.

**The app's lists are tables with fixed columns under headers; the tier list is the site's.** (The champions, truemains, favorites and matchup pages have since become the site's own, shared through the layer — #1732; a link to a page only the site has, such as a player's profile, opens it in the browser.) The truemains
leaderboard drawn with the site's `LeaderboardRow` did not line up at the app's width — the row sizes its columns with
flex spacers around a name that takes its content width, so lanes, champion, score and rank drifted from row to row
— and the product owner asked for the table the app's tier list had: a header of column labels and one grid shared
by every row (`utils/truemains-table.ts`, `leaderboard/TruemainsTableRow.vue`, derived from the site's row). The page
carries the site's search, filters and pagination (`LeaderboardFilters` twinned; `TruemainSearch` searches
`/truemains/search`, champions first to filter by). The champions page took that table (champion × lane, most picked
first) in place of a portrait grid, and the tier list became the site's — a card per tier of lane-badged portraits,
its rank, truemains-only and patch filters (`Champion/TierChip`, `EloFilter`, `TruemainToggle`, `SectionCard`
twinned). A champion's true mains list gained the same search, limited to that champion's mains, so any main can be
reached by name, not only the top five (2026-09-30) — #1719.

**The site announces the app right under the home hero, above the strongest picks and their mains, and shows the
real app rather than describing it.** The card under the two teasers and a download page stacked from an alert, a
status line and an accordion were read as unpolished; a first redesign in icon-tile cards was read as generic,
"AI-made". So `/download` is left-aligned typography over one large capture of the app in champion select, the
features are plain columns under a hairline. The home banner shows the app's dashboard instead, whole rather than
cropped — the product owner's call. The capture is the real UI over live API answers: `desktop/app`'s `npm run dev` on the `draft-locked` scenario
(whose build, win rates, games and tiers come from the production API through the dev proxy) at the 1180×760 window,
2× scale, with the dev scenario picker and the fixture player card hidden. Scenarios that carry a fixture
`recommendation` — the ranked-picks podium — are not captured, since those deltas are not measurements. The
dashboard capture is the one exception to "no fabricated numbers" on the site: it can only be taken on the `lobby`
scenario's fixture record (the synthetic player `Synthetic#TAPE` and invented games), and the product owner accepted
those figures for a picture of the app; it carries no TrueMain statistic (2026-10-01) — #1725.

**Game recording asks the player for two things, the resolution and the frame rate, and derives everything else.**
The product owner wants the app to record games and replay them with the player's kills, deaths and assists marked
(Outplayed's job), and to choose the best setting for their machine "without a configuration as complex as OBS",
tuned to break as little as possible. So the settings are `Native`/`1440p`/`1080p`/`720p` and 30/60 fps, plus on/off
(off by default), the queues recorded, a disk budget and a folder. The codec (H.264 in fragmented MP4, so a crash
mid-game still leaves a playable file), the bitrate (one bits-per-pixel constant, not a table per preset) and a
one-second keyframe interval are derived; a preset taller than the game window records at the window's size rather
than upscaling. The settings file is read field by field, a bad value falling back alone. The default is 1080p at
30 fps until #1745 measures what recording costs in game. The highlights come from the game's timeline in the match
history, the live feed noted during the game being the fallback; game time is mapped onto the video by an anchor
fitted from game-clock reads, segmented so a pause or a reconnect moves it. The disk budget deletes the oldest
unpinned recordings first and only the files the app wrote. Two consequences were accepted up front: the app stays an
unsigned beta even though macOS may then drop the Screen Recording permission at each update (the app must notice and
ask again, never record a black video), and a GPL capture library (libobs) is allowed, which would put the desktop app
under a GPL-compatible licence (2026-10-01) — #1744, #1754.

**The running game is read through the game's own Live Client Data API, polled every 2 s only while the phase is
`InProgress`, and the frontend receives a snapshot and then changes — never the payload on every poll.** The source is
`allgamedata` on `127.0.0.1:2999` (documented by Riot for third parties, no credentials, no memory reading), verified
against the same pinned Riot root as the client. Two seconds because nothing on the board moves faster than a
purchase, a level or a death, and the respawn timer counts down on screen between readings; the wait doubles to 5 s
while the game loads. Only what moves at the pace of the game is diffed — items, level, K/D/A, death (with the respawn
time read once, at the death) and respawn; creep score, gold and stats change on every reading and stay out until a
panel needs them. Updates are numbered so a missed one is answered by re-reading the state, not by drawing a board
with a gap. The derivation, the diff and the end-of-game rule live in one place (`live_client::GameFeed`) fed alike by
the live poll, a tape and the simulator, so a replay proves what the live path does. A tape keeps game readings raw,
unlike the client readings, because the panels still to come read fields the app does not parse yet (2026-10-01) —
#1748.

**The game opens its own page the way champion select opens the draft — but only from home or from the draft it
started out of.** A player who opened another page by hand during a game is never pulled away from it; the sidebar's
"Game" entry, badged "Live", takes them there. The game's end returns home only from the game page. On its own the page
shows what the in-game scoreboard shows — the ten players lane by lane, ours left and theirs mirrored right, items,
levels, K/D/A, spells, death timers, each side's kills and the clock — and nothing derived from it: it is the frame the
#1747 panels land in. What the API reveals about enemies out of vision (live or last-seen items, death timers) is
undecided until checked in a live game; the gold and next-item panels may only read enemy information the player can
see (`desktop/README.md`, "Reading the game") (2026-10-01) — #1748.

**The dashboard reads a game's roles from the participant slot, and its build and skill orders from TrueMain.** The
client's history lane is Riot's old position guess and files a roaming laner as a second jungler (a top Yone counted
twelve jungle games out of twenty). On a queue that assigns roles (normal draft, Solo/Duo, Flex, Swiftplay, Quickplay,
Clash) Riot lists each team in role order, participants 1–5 blue and 6–10 red, the order the client's scoreboard draws;
TrueMain's copy of the same games (Riot's `teamPosition`) agreed player for player. Other queues get no role and count
on no lane. The client's game timeline carries kills, buildings and epic monsters only, never a purchase or a skill
point, so the detail panel asks TrueMain's API for the game first (`/truemains/{nameTag}/matches/{matchId}`) and falls
back to the client, leaving the build and skill order sections out rather than announcing them empty (2026-10-01) —
#1768.

**Screen capture runs in a native helper process per platform, not inside the app.** On macOS a Swift executable
(`desktop/capture/macos`) captures with ScreenCaptureKit and encodes with AVAssetWriter through VideoToolbox; the
shell drives it over JSON lines on stdout and `stop` on stdin, and sees it as `game-recording`'s `Capture`. Swift
because those are first-class Swift APIs, where Rust bindings to them would be written and debugged blind; a separate
process because a crash in capture or the encoder must not take the app — or a champion select — down with it, and
because the Windows helper can then be whatever Windows captures best with, behind the same protocol. The output
size, bitrate and keyframe interval are still computed in Rust (`Quality::output_for`) and passed in, so the rule
has one implementation. The spike (`capture-spike`) ran this pair outside the app first; once a real game recorded
cleanly through it (1080p60, 0 dropped frames, no in-game FPS cost), the helper shipped inside the app bundle
(`Contents/MacOS`, beside the app's binary, ad-hoc signed before bundling) rather than as a Tauri sidecar with a
target-triple name, and the app and the spike drive it through one crate (`capture-helper`). The same helper cuts the
clips (an AVFoundation passthrough export) and takes the thumbnails, so the media stack that wrote a file is the one
that reads it (2026-10-01) — #1745, #1744.

**The Windows capture helper is a Rust executable behind the same protocol, added to the installer at release time
only.** `desktop/capture/windows` (`truemain-capture.exe`) speaks the macOS helper's commands and events exactly, so
neither the shell, the runner, the spike nor the pages have a Windows branch. It captures the game's window with
Windows.Graphics.Capture (no injection, no elevation), converts each frame to NV12 on the GPU with the D3D11 video
processor and hands the surfaces to Media Foundation's sink writer with hardware transforms on (NVENC, AMF or
QuickSync, a software encoder only where the GPU has none, reported in `started`), in fragmented MP4 like the macOS
file. Sound is the game's own process tree through WASAPI process loopback — not the system mix, which would carry a
voice chat. Rust rather than C++ or C# because the `windows` crate already in the app's lockfile covers every API,
type-checks from Linux, and keeps one toolchain; a separate process for the same reason as on macOS. It is not in
`tauri.conf.json`: a Tauri sidecar must exist for every build, `tauri dev` included, so `desktop-release.yml` builds
it and adds it as `externalBin` for the Windows bundle alone, and a development build finds it in `target/`. Windows
asks no permission to capture a window, so `access` reports whether the OS has window capture at all (10 1903+), and
an older Windows reads as unsupported. Without a tester with League on Windows, CI's smoke test records a repainting
window on a GPU-less runner — it proves the protocol, the file, the clip and the thumbnail, not the game or the
hardware encoder (2026-10-02) — #1797.

**The app records only against a real client, into a folder the player can find, and asks for Screen Recording
itself.** A tape or the simulator plays the client's events, not a game window, so neither starts the recorder. The
default folder is the system's videos folder (`~/Movies/TrueMain`), not the app's hidden data folder: the clips are
files the player keeps and shares. The game's objectives on the recap's timeline come from the match history's
timeline only, never the live feed, whose objective events do not always say which side took them — a side is not
guessed. The Screen Recording prompt is asked from the Recordings page, not at launch: macOS shows it once per app, so
a later request opens System Settings instead (2026-10-01) — #1744.

**Clips are cut by hand from the post-game recap in v1, saved clips are never pruned by the budget, and the recap
opens on its own only from the game page or the dashboard.** The product owner's flow (2026-10-01): at the end of a
recorded game the recap shows the game's timeline — the player's kills, deaths and assists, the objectives — and the
player selects periods on it, as many as they want, names each one and saves it as its own video; then they keep the
full game or delete it. This reverses #1744's "trimming a clip by hand: out of scope for v1": manual clips are the v1
way clips are made, and automatic clips (#1766) and instant replay (#1767) wait. A range proposes its own title from
what it holds ("Triple kill on Ahri", "Kill + Dragon") and keeps the player's once they rename it. A saved clip is a
file the player chose to keep, so the disk budget never deletes it (it still counts in the space used); a full game
the player kept is exempt too, and only undecided full games go, oldest first. `recording://recap` opens the recap
only over `/` or `/game` — the pages the game's own navigation would have taken the player to — never over a page
they opened by hand, the game page's rule. The Recordings page follows DPM's layout (the product owner's reference) in
the site's materials; its settings are the short list of #1744 — no OBS (2026-10-01) — #1755, #1777.

**The next item sits over the game board and answers one purchase: what to complete, and what to buy now.** The mains'
choice from where our build stands (#1749) is the decision; its share of the mains, one reason in the site's own
item-context words, the gold left and the components the gold in hand buys are what turns it into a purchase. Two
runners-up and the boots stay small beside it. It is asked again on any item change of the ten players and never on a
timer, because nothing else moves the answer. Our gold joins the game state in 50-gold steps — income crosses one about
every ten seconds — rather than every reading, so the board does not become a change per poll (2026-10-01) — #1751.

**Every app version is built twice, once per site, and each site serves only its own build (2026-10-01).** The product
owner's call: the app downloaded from preprod is a test build and must read preprod — API, the pages it opens in the
browser, its update feed — and the one downloaded from truemain.lol must read production. A build is tied to its site
at compile time (`TRUEMAIN_SITE_URL`, `src-tauri/src/site.rs`), so one binary cannot serve both: the release workflow
builds a production flavour and a preprod flavour of each version, side by side in the same `desktop-v*` release. The
file names say which is which — `truemain.dmg` / `truemain.exe` on production, `truemain-<version>.dmg` /
`truemain-<version>.exe` on preprod — and each flavour has its own update manifest (`latest.json`,
`latest-beta.json`), so an update never moves an app from one site to the other. The preprod flavour is *TrueMain
Beta* with its own bundle identifier, so a tester keeps both installed. The preprod origin is a repository secret
(`DESKTOP_BETA_SITE_URL`), never written in the repository, and a developer's local builds read it from a gitignored
`desktop/.env.local`. The webview no longer opens URLs itself: it asks the shell to open a *path* on the build's site
(`open_on_site`), which replaces the shell plugin's origin-scoped `open`. Revises the line of #1772 that the
updater polls production only (2026-10-01) — #1779.

**The app keeps itself current without the player re-downloading it (2026-10-01).** The product owner's call: a
companion left open all day must notice a new build while it runs, and installing one must not be a chore. Each
flavour polls its own site's feed at launch and every fifteen minutes (preprod's offers every bump merged to
`develop`, production's only the promoted one), downloads a newer build in the background, then offers "Restart now"
in a toast and in the sidebar. A build found at launch, with no champion select or game running, installs and
restarts at once — the player has nothing open to lose yet; one found later waits for the click or for the next
launch, so the app still never restarts under a running phase (`composables/useAppUpdate.ts`). "Check for Updates…"
sits where each platform keeps it: the application menu on macOS, under "About"; on Windows, whose window has no menu
bar, the menu of a tray icon (Open, Check for Updates…, Quit) — the shell only relays the click, the webview runs the
check and answers every outcome, up to date and offline included (`src-tauri/src/menu.rs`). The feed reads the
release's manifest as JSON whatever its type: GitHub serves assets as `application/octet-stream`, which `$fetch` read
as a Blob that the feed's cache relayed as `{}`, so installed betas were almost never offered an update
(`server/utils/desktop-manifest.ts`) — #1789.

**A tab changes on click; the wait shows under a loading bar across the window (2026-10-01).** The site keeps the
outgoing page on screen until the destination has its data, under a bar on the header's bottom edge (#1689). The app
inherited the await with the shared pages (#1732) but not the bar, and has no header to hang it under: a read through
the shell takes seconds when cold (3.7 s measured on the tier list), so a click left the previous tab on screen with
nothing saying it was taken — the product owner's report. The app's route files now catch the shared page's await in a
`<Suspense>` of their own, which shows the page's header over a skeleton, and a bar along the window's top edge counts
every load the pages make — not only navigations: a filter change or a cold matchup recommendation is a wait too. The
site keeps #1689 as it is; only reads no click asked for (the in-game next item) stay off the bar — #1788.

**The in-game overlay draws over the game and nowhere else, and never takes an input from it (2026-10-02).** The #1673
spike's measurements (`docs/desktop-overlay-spike.md`) set the windows: League's Full Screen captures the display, so
each panel sits one level above `CGShieldingWindowLevel` — and, at that level, is shown only while the game's own
process is frontmost, so it never covers the client or another app (the product owner's rule). It can never become key
and ignores the mouse in game: the game keeps every click and key, and the overlay is read at a glance, not used. Its
shortcut and TAB are read from the keyboard's state, because over a captured display no hotkey reaches the app — which
also settles #1752's macOS question: TAB is detected without an event tap, so without the Input Monitoring permission.
The overlay is five independent panels, each its own window placed on its own (#1671's choice over one HUD), on by
default and set up on its own Overlay page since #1819 (2026-10-03; the game page's slideover before) — each on/off and
where, by dragging it on a copy of the screen, taking it off with its ×, and dragging it back from a column of the
hidden panels, or by dragging it in an on-screen preview, the only time the panels take the mouse — plus size and
opacity for all. The five fixed spots stay each panel's default place, no longer a picker:

- **Next item**: **one item** — the next one, and the gold still to earn for it or that it can be bought now — and
  nothing else: no components (which one to buy first is not measured yet, so the overlay does not pretend to know),
  no runners-up, no boots; the game page keeps the full panel, from the same `useNextItemPanel`. Whole game or only
  while dead.
- **Win probability**: on screen the whole game, two percentages and a bar in the sides' colours, no labels.
- **Your pace**: CS per minute with its curve (from minute 3, so the minion-less start does not read as a climb), and
  gold per minute, from one sample per whole minute the feed keeps (`live_client::pace`) — a change a minute, not one
  per poll. Gold earned is not in the API: it is read as the inventory's cost plus the gold in hand. **Damage per
  minute was asked for and left out**: the Live Client API exposes no damage total, and a figure that comes from no
  measurement is not one the app shows.
- **Loading screen** (#1753): from the loading screen on — never in champion select, where ranked hides the other
  team — each player's games on their champion among their last twenty Summoner's Rift games and their win rate on it,
  and their last ten of those games as bars, blue a win and red a loss, oldest to newest, each naming its champion,
  role, KDA and date on hover (#1803: the ranked streak chip it replaces, "W3"/"L4", read as nothing), lane against lane. Read through the player's own
  client (gameflow session for the roster, match history by puuid), three requests at a time, ours and our lane
  opponent's first, never on TrueMain's Riot key. Counts only, no score made of them. Also on the companion window's
  loading state. The true-main mark the issue asks for needs a batch lookup by Riot ID on the API and is left for later.
  A player who hides their name (Streamer Mode: the session marks them `nameVisibilityType: HIDDEN`, or leaves out
  their puuid or name) stays anonymous: their champion and lane show with "Anonymous", never a name even if the client
  sent one, and their history is not requested. Our own line is read whatever the others are shown.
- **Item value**: only while TAB is held — each team's item gold, a chevron toward the side ahead with the gap, then
  each lane's — in its own panel rather than pinned to Riot's scoreboard rows, which move with resolution and HUD scale.
  Item gold is the full Data Dragon price of each held item, consumables and trinkets left out (#1752's rule).

No interactive mode in game: nothing on the panels needs a click. macOS only until a Windows pass is measured
(2026-10-02) — #1673, #1795, #1752.

**On Windows the overlay is the same panels in topmost, non-activating layered windows, over Borderless only
(2026-10-02).** The panels, their rule and their placement are the macOS ones: one driver (`overlay/panels.rs`) sizes,
places, shows and drags them on both platforms, and each platform supplies only its window layer. On Windows that is a
Tauri window given what Tauri does not combine on its own — `WS_EX_NOACTIVATE` and `SW_SHOWNOACTIVATE` so the game
keeps the foreground and the keyboard, `WS_EX_LAYERED | WS_EX_TRANSPARENT` so clicks go through (the layered alpha is
the opacity setting), `WS_EX_TOOLWINDOW` and topmost — shown only while the game owns the foreground window, known
by its window class (`RiotWindowClass`) or its process name (`League of Legends.exe`): the class needs no handle on
the game's process, which an anti-cheat may refuse (#1806). Windows draws nothing over a game in exclusive Full Screen, so the settings ask for Borderless or Windowed, as
other League companions do, rather than hooking the game's renderer, which an anti-cheat would see. The shortcut is
Alt+Shift+O, read with TAB from the keyboard's state as on macOS (no hook, no registered hotkey). Built without a
Windows tester: CI drives it on a Windows desktop over a stand-in game (#1806), but a real game has not been played
under it yet — #1798.

**The overlay shows a win probability, from the item-gold gap and the map (2026-10-02).** The product owner's call,
reversing for the in-game overlay the "no win probability" line of #1671 and #1747, and knowingly a formula rather than
a measured model (`utils/item-value.ts`): a logistic over, in log-odds, the item-gold lead relative to the gold the two
teams hold on average (×6, so the same gap weighs more early than late — a tenth of that average alone reads about
65 %), and what each side holds on the map — turrets destroyed (0.12 each), enemy inhibitors down right now (0.5 each,
standing again five minutes after they fall), elemental drakes (0.15 each, +0.6 for the soul at four), the Baron's
buff (0.9) and the Elder's (1.1) while they last (three minutes, two and a half — counted from the kill, since the feed
does not say when a holder dies). The map is read off the game's event feed (`live_client::objectives`), which every
player sees announced. The draft keeps no win probability (2026-10-02) — #1795.

**The player's own account is never highlighted (2026-10-02).** No tint, ring or bolder name on our row — not on the
loading screen, the game page's scoreboard, a dashboard match row's team strip, nor the opened match's scoreboard and
runes. The product owner's call: the player knows which one they are, and the mark only competes with what the row
says. Ours still orders the sides (our team first, our lane opponent's history read first) and the draft board's
selected card stays ringed, since that ring marks the selection, not the account — #1803.

**The app measures its own use: counters under an anonymous install id, folded per install and day; downloads are
counted by the site's redirect (2026-10-02).** The site's Umami cannot see the app — a static bundle in a webview with a
closed CSP — and GitHub's asset counters cannot say who kept the app or what they open. So the shell keeps a handful of
counters (launches, minutes open, page views, a few features) and sends them every few minutes to a public
`POST /desktop/telemetry`, which folds each batch into one Mongo document per install and UTC day
(`desktop_usage_days`): aggregates, not an event log, since every question the admin page asks is a sum or a distinct
count over install-days. The id is random and drawn on the first launch; nothing read from the League client (Riot ID,
PUUID, rank, games) and no address is stored. The usage expires after 13 months (the CNIL's ceiling for an
audience-measurement identifier); the download counters, which identify nobody, are kept. It is on by default and
turned off by "Share Anonymous Usage Data" in the native menu — not a toggle in the webview, so it costs no styled UI and
sits where macOS and Windows users look for app settings — and `/download` says what is sent. Downloads are counted on
the site's own `/api/desktop/download/{platform}` redirect, posted to `/internal` with the log-ingest key so a download
cannot be forged through the public proxy; crawlers and link previews are skipped. The public endpoint has no secret
to check — an installed app cannot keep one — so a forged batch is bounded by the per-address rate limit and the
per-batch caps, a risk taken knowingly for a beta's numbers — #1805.
