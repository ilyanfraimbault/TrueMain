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
twin rather than split away from it. The layer that would end the copies is #1687 (2026-09-27).

**Icons are bundled at build time, routes live in the hash, and images are drawn without a `load` gate.** The
packaged app has no server to resolve an icon and a CSP that reaches only Data Dragon and Community Dragon
(`icon.provider: 'none'` + `clientBundle.scan`); Tauri's custom protocol serves files, not an SPA fallback, so a
history-mode path would 404 on reload; and WKWebView never reported `load` for images inserted after the first,
which left fade-in-on-load art invisible for good — the twinned `SkeletonImage` therefore draws as soon as it
decodes (2026-09-27).
