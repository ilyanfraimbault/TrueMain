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

**The app has no browser chrome: no back/forward arrows and no patch label in the top bar.** Both made the window
read as a web page; the sidebar is how the app is navigated, and the bar keeps only the ⌘K champion search
(2026-09-28) — #1671.

**Picks are ranked from the player's pool or from the whole lane, never from a "meta" slice.** "My pool" is the
player's ten most-mastered champions (LCU champion mastery) that the tier list has on the lane; off, every champion
the tier list has on the lane, split into requests of 40 (the endpoint's ceiling) and merged in the endpoint's own
order — each candidate's score depends on it alone, so the merge is exact. Before an enemy picks or an ally locks
there is nothing to measure, so the podium shows win rates on the lane in the pool's order rather than "+0.0%". The
ranking itself is the endpoint's; the enemy-team component and a lane-first weighting are #1713 (2026-09-27) — #1675.

**The app draws the site's components, as labelled twin copies, not look-alikes.** Hand-ported versions (a skill
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

**The build pane lays out the core itself, from the site's icons, and draws the site's build tree smaller.** The
site's core view only puts the runes beside the rest from a 768 px container; the app's pane is ~560 px, so the runes
fell under everything and the build tree started off screen. `build/BuildCore.vue` sets summoners, skill order,
starter and boots as a 2 × 2 grid over the build path at 30 px, with the site's rune block to their right at its own
size and spacing, centred on their height — stretched to it, the rune rows spread too far apart; the tree twin takes its node size and gaps as app-specific props (28 / 10 / 20 px against the
site's 36 / 22 / 44). The build rows beside it are icons alone — the keystone with the secondary tree as its badge
(the site's own pairing), three items, the win rate — since an item's name spelled out said less than its icon and
cost the width; the column went from 22rem to 18rem, the most a 15-character Riot ID still fits. The site's core
twins it replaced were removed rather than kept unused (2026-09-28) — #1671.

**Icons are bundled at build time, routes live in the hash, and images are drawn without a `load` gate.** The
packaged app has no server to resolve an icon and a CSP that reaches only Data Dragon and Community Dragon
(`icon.provider: 'none'` + `clientBundle.scan`); Tauri's custom protocol serves files, not an SPA fallback, so a
history-mode path would 404 on reload; and WKWebView never reported `load` for images inserted after the first,
which left fade-in-on-load art invisible for good — the twinned `SkeletonImage` therefore draws as soon as it
decodes (2026-09-27).
