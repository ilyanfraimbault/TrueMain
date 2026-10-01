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
(2026-09-28) — #1719.

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
