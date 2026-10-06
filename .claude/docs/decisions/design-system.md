# Design system

Part of the [decision log](../decisions.md). Format: **Decision** — why — `source`.

## The rose-gold-only surface rule is reversed: neutral surfaces, a scarce accent, and a data axis of its own (2026-08-10)

**Decided in #1060 (part of the #1059 redesign), reversing the "surfaces are rose-gold-only" rule that
`main.css` had carried as the successor to an earlier emerald-only one.** The old rule paired a warm accent
(`rosegold-400 #e58f83`) with a warm neutral (`mauve-900 #211d1e`) and forbade any second hue on a surface.
The accent therefore sat on a background already halfway to its own colour, so nothing separated: the site
read as one flat warm mass. What replaced it:

- **`ink` replaces `mauve`** — a near-neutral, faintly cool charcoal. The `rosegold` ramp is **unchanged**;
  it simply reads far more saturated once nothing around it is warm. The fix was never the hue.
- **Rose gold is scarce on purpose**: brand and interaction only (logo, active nav, focus rings, primary
  buttons, links, selected states, the hero accent word). It never colours a data value and is never a
  generic surface tint. Scarcity is the whole mechanism — an accent applied to everything is not an accent.
- **Measurements get their own cold→warm axis** (`--color-data-*`: teal good → neutral → amber bad), and
  unlike the `--color-stat-*` tooltip vocabulary it **is** allowed as `bg-*` / `ring-*` / `border-*`. The
  former rule banned semantic colour on data outright, which on a stats site gave away half the legibility:
  a 52% and a 9% rendered identically. Green/red was rejected rather than overlooked — `rosegold-500
  #d9736c` is itself a desaturated red, and a red "loss" beside the accent in a dense table is a coin flip
  to read. Teal and amber share no hue with the brand, so a coloured number can never be mistaken for an
  interactive one. The activity heatmap moved onto this axis at the same time, retiring the #927 reasoning
  that a second hue on the grid would be an intrusion.
- **The tier ladder rides the same axis.** Its medal metaphor (rose-gold → gold → silver → bronze → iron)
  broke twice over once amber meant "bad": A and C read as warnings, and `tier-s` was *literally*
  `rosegold-400`, giving the best tier the brand colour and no comparative meaning at all.

> ⚠️ **The two bullets above were reversed on 2026-08-11 — see the entry below.** The cold→warm data axis and
> the teal tier ladder are gone; measurements are rose gold again and the medal ladder is back. Everything
> else in this entry (ink surfaces, the four-step opaque elevation, `surface` replacing `glass`, dark-only)
> still stands.
- **`surface` replaces `glass`** at all 55 call sites plus the global `UCard` / `UBadge` themes. Translucency
  everywhere meant nothing was ever *on top of* anything. Paired with it, the elevation ladder was
  un-flattened: `--ui-bg-muted` and `--ui-bg-elevated` had both pointed at `neutral-800`, so the whole app
  had two levels — page and not-page — and depth had to be carried by borders that were themselves
  translucent. There are now four distinct opaque steps. `glass` is removed outright: it was kept at first for
  the home hero, but the hero's own search field reads better solid against the eclipse, which left the
  utility with no call site — and a material the docs call load-bearing with nothing using it is how a design
  system starts lying about itself.
- **A second family carries the numbers.** `--font-mono` had been deliberately aliased to Inter; it now
  points at Geist Mono, used by the `stat-value` / `stat-label` utilities. The old scale put a value and its
  label one step apart (`text-sm` over `text-xs`, same family, same weight), so a dense row read as noise.

## Measurements are rose gold again: the cold→warm data axis is withdrawn (2026-08-11)

**Decided by the product owner in #1096, reversing two bullets of the #1060 entry above** — the `--color-data-*`
cold→warm axis and the teal tier ladder. Everything else #1060 shipped (ink surfaces, four opaque elevation
steps, `surface` over `glass`, dark-only, the scoped eclipse) is untouched and stays.

The teal was doing what a two-hue scale is meant to do. The call was not that it failed at its job, but that
the site should read as rose gold and should not carry a cyan it never wanted. Recorded plainly because the
#1060 reasoning is still on this page and will read as current otherwise.

- **The axis is one-sided now.** `--color-data-good` is `rosegold-400`; below average simply steps down the
  neutral ramp (`--color-data-bad` is `ink-500`). A losing win rate is *not* flagged in a warning colour, it
  is merely not highlighted. Consumers did not change — `rate-tone.ts`, `StatBlock`, `MetricBar` and
  `TierBadge` all read the same tokens, so this was a token edit, not a component sweep.
- **The medal ladder is back** (rose gold → gold → silver → bronze → iron). #1060 retired it on the grounds
  that gold and bronze are amber and amber meant "bad"; with the warm end of the axis gone, the collision it
  was avoiding no longer exists. `PlayerPerformance.vue` reads `--color-tier-*` directly, so the performance
  verdicts followed for free (the dedication ranks did too, until #1701 dropped them for the OTP/Main verdict).
- **The activity heatmap returns to rose gold / neutral**, which is where #927 had it. The sign of a period is
  now carried by *accent vs grey* rather than by two opposed hues, which puts more weight on intensity: a
  one-game losing period is a faint grey cell. That is the intended read — it is barely a signal.
  > ⚠️ **Withdrawn on 2026-09-03 — see "The activity grid answers presence" below.** The grid no longer
  > carries the win rate in its colour at all; the rest of this entry stands.
- **The top of the axis has a second step — written down in #1237, months after it shipped.** `--color-gold`
  sits above `--color-data-good` for a *standout* value: a Perfect KDA, a 75+ performance score
  (`MatchRow.vue`). It arrived with the match history and was never documented, so `DESIGN_SYSTEM.md`,
  `main.css` and this entry all described a two-tone axis the code had not had for months. The call was to
  document the step rather than retire it — the grading is right, and a three-tone read is what an op.gg-style
  row needs — and to leave it on `--color-gold` rather than mint a `--color-data-standout`: it is the same
  token the MVP crown wears, and that identity *is* the point, since the number and the accolade are saying
  the same thing. A second name for one hex is how those two drift apart. **"One-sided" is therefore a claim
  about the bottom of the axis**: there is still no opposed hue for "bad". The standout step is the one member
  of the axis that is text and small marks only — a gold fill would out-shout the accent it exists to cap.

**The cost, stated so nobody re-derives it in surprise: the accent is no longer exclusive to interaction.**
#1060's central mechanism was that rose gold meant "you can touch this" and nothing else, which is what let it
stay legible while scarce. A rose-gold number is now a *good* number, not a clickable one. Affordance has to
come from shape and position — a border, a cursor, a control's own chrome — and never from hue alone. Any
future "make it obvious this is clickable" that reaches for the accent alone will not work any more.

**The eclipse is scoped to the home hero** (`AppBackdrop.vue` moved out of `app.vue`). As a viewport-fixed
layer behind every route its corona passed *through* the champion and leaderboard tables: rows near the glow
rendered a visibly different luminance from those outside it, and a table that changes brightness down its own
length cannot be scanned. The signature is kept where the page's job is atmosphere, and dropped where the
page's job is numbers. Share cards keep the eclipse regardless of the page they were shared from — a share
card advertises the site.

**Light mode is gone** — the toggle, the five `dark:` variants, and the `.dark` scoping of the surface
tokens. It was never designed or tested (five variants in 119 files), and keeping it half-defined meant
every future token decision had to be made twice. The module has no "forced" switch — `preference` is only a
default — so the colour-mode `storageKey` was moved at the same time, retiring the `light` value a returning
visitor might still carry from before the toggle was removed.

Reviewable at `pages/dev/design-system.vue`, which is the compensating control for having no Storybook and no
SFC-mounting test setup: every colour family, elevation step and material on one screen, stripped from
production builds like the other `dev/*` playgrounds.

**Measurements are set in Inter again: the mono stat face is withdrawn.**
#1060 lifted the `--font-mono` → Inter alias and put `stat-value` / `stat-label` on Geist Mono, on the argument
that a technical face gives numbers presence and that the value/label pair needs two registers. Withdrawn by the
product owner in #1111: across a dense page it read as a second, unrelated typeface rather than as a register.
The pair keeps its separation from size, weight, casing and tracking — which was always doing most of the work —
and `tabular-nums` still aligns the columns. Geist Mono stays loaded for the few places monospace is the *meaning*
rather than a flourish: tier letters, the empty-slot glyph, hex codes on `/dev/design-system`. One edit in
`main.css` reaches every stat on the site, which is why the family lives in the utility and not at the call
sites — #1111.

## A failed icon is hollow; a loading one is solid and moving (2026-09-02)

**`SkeletonImage` backs every icon on the site, and its two placeholder states used to differ only by
`animate-pulse`** — same `bg-ink-700` fill, one animated.
That is not enough of a difference to read: when the `/_ipx` route broke in 1.20.0 and *every* icon on the
site failed, the pages did not look broken, they looked slow. Failed now renders `bg-ink-800` inside an inset
`ring-ink-700`, so a dead icon reads as an empty slot rather than as a slot still filling.
Two constraints shaped the fix. The loading fill must stay whatever `ui.skeleton.base` is set to in
`app.config.ts` (`bg-ink-700`), because this is the most numerous skeleton on the site and a drift there is
what "loading" looks like everywhere. And the distinction has to be pure CSS on the element that already
exists — a champion page carries ~470 of these, so a broken-image glyph or an extra element each is precisely
the cost `SkeletonImage` exists to avoid. The rule lives in `app/utils/icon-placeholder.ts` so it is pinned by
a test rather than by reading a template.
↳ **An icon whose source never arrives gets that same hollow box, and never a raw Riot id.** The failed state
covered an image that 404s; it did not cover a slot with no `src` at all, which is what a *failed static-data
fetch* produces — the map then resolves no id, so every icon it backs is sourceless for good. Those slots
pulsed forever (the same "broken looks slow" failure, one level up) or printed the id they could not resolve:
the matchup page showed `Spell 4` and `Spell 12` where the summoner icons belong. `isIconUnresolved` draws
"final and still empty" like a failed image; the id-derived labels are gone from the summoner and skill-order
call sites, the latter because `ItemRankBadge` already prints the Q/W/E key beside the icon. A real name is
still shown when the static data carries one and only the icon URL is missing.
↳ **Finality is declared by the caller (`settled`), never inferred from the absence of `pending`.** Most call
sites never wire their fetch state: the build tabs' leading item icon is rendered on ids alone, on purpose,
because the item map is a deferred client-only fetch that lands after the builds and gating the slots on it
reflowed the whole bar. Inferring "settled" from a missing prop would have turned that window — and every
other un-instrumented consumer of a loading map — into a hollow *failed* flash, i.e. the 1.20.0 confusion
in reverse. Only a caller that owns the fetch state can say the source is final, so only those say it.

## The activity grid answers presence, not win rate (2026-09-03)

**Decided in #1452, withdrawing the heatmap bullet of the #1096 entry above (and, behind it, the two-hue read
#927 shipped).** The grid used to spend its loudest channel on how the games went: rose for a period above
50%, the neutral ramp below it, with alpha blending decisiveness and volume. Three consequences, all of them
visible on the profile:

- Two quantities fought over one channel, so nearly every cell was a slightly different smudge and none of
  them was comparable to its neighbour at a glance.
- The card's own summary line *already* states the record and the rate, and every cell states them again on
  hover. The squares were the third telling of a fact the reader had twice, and the only telling of nothing.
- Half of a normal month rendered on the grey ramp, so the card read as switched off — for a player who had
  in fact been queueing every day.

**What the squares say now is `did this player queue, and how much`:** one rose-gold ramp, four discrete
steps, keyed on games played against the busiest cell in the series. No grey and no second hue — a losing
Tuesday and a winning Tuesday of the same size are the same tile, and the tooltip is where the difference
lives. The one exception is the per-game view, where every cell holds exactly one game and volume therefore
says nothing at all: there the step falls back to the result, so the strip keeps a shape instead of being a
flat rose bar. (#1473 later made that fallback explicit — it is chosen by the window, not inferred from
`maxGames <= 1`, which mislabelled a patch the player never queued twice a day on. The four tabs also became
three *windows* over one unit there; see `decisions/product-player-profile.md`.)

Two rules survive the change untouched: **an idle period is not a lost one** (`games: 0` keeps its own tile,
visibly clear of the bottom of the ramp — it is the one thing the payload can offer but not enforce), and
**the accent still means "above average"** where the axis is used for a measurement; the grid simply is not
measuring one any more.

The same issue fixed the layout the two-ramp read was hiding behind. The tiles were a fixed 11 px on an
`auto-fill` grid, packed left — a confetti strip floating in a card several times its own width — and #1452
stretched them (`auto-fit` + `minmax`, capped per view) into a band spanning the card, the week and patch
views dropping the square entirely for full-width captioned bands.

> ⚠️ **The stretch was withdrawn in #1479.** Once #1473 made every window a run of *days*, a stretched tile
> was a fat lozenge and eleven of them read as a row of buttons, not as a grid. The tiles are back to a fixed
> 14 px square packed from the left, identical in every window, and the captions, the legend and the coverage
> line went with the stretch: the shape of a patch is carried by the density of small squares, and everything
> that was printed around them is on the hover panel of the cell it belongs to.

A seven-row weekday calendar (columns as weeks, GitHub's own shape) was built first and **rejected by the
product owner**: these series are a month long at most, so the block stood as a narrow tower in a wide card
and forced the readout to sit beside it. The reference it was copied from works because it holds a year. The
rule that came out of it: *the shape follows the card, not the reference* — this card is a wide band, so the
grid runs along it.

The palette moved with the layout. The ramp deliberately **overshoots the rose-gold stops at both ends** —
lighter than `rosegold-400`, darker than `rosegold-900` — because a contribution grid lives on the distance
between its quietest and its loudest tile, and held inside the palette's own range the four steps were four
shades of brick.

**It runs light to dark, not dark to light.** A one-game day is a pale rose and an all-evening one is a deep
one. That is the product owner's reading of the scale — density is weight, and weight is dark — and it is
the opposite of the GitHub grid the layout was compared against, so the direction is worth stating: the
reflex when extending this is to sort the stops the other way. Its consequence is the idle tile, which can no
longer be told from the ramp by lightness alone and is therefore marked by the *absence of hue* — an
unmistakable neutral grey, still painted clearly above the card surface. It went through two rounds of being
too dark, where it read as a hole in the grid rather than as a day off, and a grid with holes in it has no
shape to compare against.


## Long-form text uses Nuxt UI's prose layer, themed to the site's scale (2026-09-17)

**Decision:** `ui: { prose: true }` is enabled and the text pages (`/about`, `/privacy`, `/terms`) are written with
`Prose*` components, whose theme lives under `ui.prose` in `app.config.ts` — #1624.

- **Why.** The three pages repeated one hand-written vocabulary (`text-lg font-semibold text-highlighted` per h2,
  `space-y-3` per section, `text-sm leading-relaxed text-muted` per article). The next piece of prose (a methodology
  page, a changelog) inherits the theme instead of copying the classes a fourth time.
- **The site's scale, not Nuxt UI's.** The defaults are tuned for documentation (`text-2xl` bold h2, 20 px paragraph
  gaps, `font-medium` links, `text-pretty`). The override reproduces the pages as they were set — verified
  pixel-identical against the previous build — so the migration changed the source, not the page. The one visible
  difference kept on purpose: a hovered link shows Nuxt UI's 1 px bottom border instead of an underline, together
  with its focus outline.
- **The cost, measured** (production builds, gzip). Nuxt UI adds every prose theme to Tailwind's sources, so the
  site-wide entry stylesheet grows from 35.0 KB to 38.6 KB (+3.6 KB, +10 %) on every page. It also registers the
  ~45 `Prose*` components as *global*, which cost another +2.5 KB in the entry script; `nuxt.config.ts` turns that
  off (`components:extend`), since nothing renders MDC and the pages resolve the components at compile time —
  the entry is back to +0.2 KB (the theme in `app.config.ts`). Excluding the unused themes from the CSS with
  `@source not` was rejected: it would silently leave the next `Prose*` component a page adopts unstyled — the
  static-extraction trap `DESIGN_SYSTEM.md` already warns about.

## Page transitions are a fade-in of the content only (2026-09-18, 2026-09-27)

**Decision:** page changes animate only the page content: the new page fades in over 180 ms with a 6 px rise,
the old one leaves at once — #1621, #1714. Since #1689 (2026-09-23) this runs on Vue's `<Transition>` through
`app.pageTransition` (`name: 'page'`), no longer on the View Transitions API (`experimental.viewTransition`).

- **Why the mechanism changed (#1689).** Every page now awaits its API data in setup, under a loading bar (see
  `web-frontend-rules.md`). A view transition starts in the router's `beforeResolve` and freezes the whole frame
  until `page:finish` — the loading bar included — and drops the animation after 4 s; it had already been opted
  out on the champion page for that reason. Vue's `<Transition>` wraps the page `<Suspense>` and only plays once
  the destination has resolved, so the old page and the bar stay live through the wait, and the champion-page
  opt-out (`utils/view-transition.ts`, `plugins/view-transition.client.ts`) is gone.
- **Content only.** The header and footer sit outside `<NuxtPage>`, so they never move.
- **Never two pages at once, and no `out-in` (#1714, 2026-09-27).** A plain cross-fade shows two dense tables on
  top of each other half-way through, which reads as noise on this UI (observed on paused frames). The first
  answer was `mode: 'out-in'` with a 90 ms fade-out, but `out-in` only renders the incoming page from the
  outgoing one's `afterLeave`, and `<Suspense>` drops that callback when another navigation starts during the
  leave: clicking through the header quickly left `<main>` empty for good, in most runs of a fast tab sequence
  on production. The leave is now `display: none` — instant, so there is no window to interrupt and still no
  frame holding both pages (checked frame by frame). The 90 ms fade-out is the price.
- **Where it does not run.** Nuxt keys the page on its path, so the champion page's filter clicks and the pagers
  (same-path `router.replace`) never animate; a `prefers-reduced-motion: reduce` media query drops the fade-in,
  and `<Transition>` then swaps at once.

## One error vocabulary: a page for a dead route, an alert for a dead region, a toast for an action (2026-09-22)

**Decided in #1661.** Error reporting had drifted into three overlapping surfaces with no rule about which
applied when, so the same failure was routinely told twice. Five web pages called `useErrorToast` *and*
rendered an inline `UAlert` carrying the same `describeFetchError` line; `AccountsSeed` did the same in the
portal. The composable's own docblock called this deliberate ("complements rather than replaces"), which is
what let it spread to every page that fetched.

**The rule is one surface per kind of event, and never two surfaces for one event:**

- **Error page (`UError`)** — the route itself cannot exist or cannot render (404, 503, a fatal SSR throw).
- **Inline alert (`FetchErrorAlert`)** — one *region* of a live page failed and the rest is still usable.
  Persistent, where the missing content was. This is the default for a fetch failure.
- **Toast (`useActionToast`)** — the outcome of an action the user just took that would otherwise leave no
  trace: a link copied, a form submitted, a bulk run finished. **Never a page-load failure.** A toast
  disappears, and the message a reader most needs to re-read is the one explaining why a panel is empty.

**The refetch carve-out, built in #1668.** The one honest case for a toast on a fetch failure is a
*refetch* — a filter, sort or pager click that fails while the previous content stays on screen, where the
action needs an answer and the inline alert may be scrolled out of view. #1661 found it with no call sites
(Nuxt's `useAsyncData` resets `data` to `options.default()` in its catch, so a failed refetch wiped the content
and the alert took its place) and left it to #1668. `useRefetchFallback` (`web/layers/common`) now keeps the
last payload a request *succeeded* with and splits the failure in two: nothing to keep (the first load) →
`error`, the inline alert, no toast, unchanged; previous payload standing in → `staleError`, reported **once**
as `useActionToast().failure` plus a muted `StaleContentNotice` ("Couldn't update — showing the previous
results", with Retry) beside the content — a notice, never a second alert. Wired on `/champions`, the tier list,
`/champions/[slug]` (and its player-scoped mirror, which shares `useChampion`), `/truemains` and `/matchup`. A
payload is never kept across its **scope** — the champion a page or a recommendation is about — so another
champion's build is never drawn under this one's name; inside it, rows fetched under the previous filters are
kept on purpose, which is the opposite of the admin's same-key `useLastGoodPayload` (#1426). The leaderboard
composable opts in per consumer (`refetchFailureTitle`): the champion page's Truemains card does not, since its
"refetch" is another champion. The dev mock has a knob for it: `NUXT_DEV_MOCK_FAIL=<regex>` fails every mocked
request whose `path?query` (plus a POST body) matches.

**Both apps now have an `error.vue`, and both render inside their own chrome.** Web's replaced a hand-rolled
centred panel that sat outside `AppHeader` / `AppFooter` — it told a visitor they had left the site. The
portal had **no** `error.vue` at all and fell through to Nuxt's stock page, with no sidebar and no way back;
it now renders inside `NuxtLayout`, so the nav and the ⌘K palette stay up. (`UError` needs `w-full` there:
`UDashboardGroup` is a flex row, so the `<main>` shrinks to its content and its `items-center` then centres
inside that narrow box.)

**The two apps keep opposite copy rules, and that is the point.** The public site derives *everything* from
the status code — `describeHttpStatus`, the same line the inline alerts read — because a raw `statusMessage`
carries the request path on a 404 and the proxied backend's detail on a 5xx. The portal shows the real
reason: the ProblemDetails `detail` and the traceId, because an operator is expected to quote them. Each app
therefore has its own `FetchErrorAlert` with the same name and shape and a different message source.

**A bug the uniformisation exposed:** `error.vue` is handed a NuxtError that has crossed the SSR payload — a
*plain object*, so `instanceof Error` is false and `fetchErrorStatus` read no status off it. Every error
page, 404 included, printed the connectivity line ("Could not reach the server"). `describeHttpStatus` is
split out for callers that already hold the number; it is pinned by a test.

**What is deliberately *not* `FetchErrorAlert`:** the portal's domain-state alerts — "PUUID invalidated",
"Rejected", a seed request's stored error. Those report a *record's* state, not a failed request, and stay
plain `UAlert`s. `color="error"` is not by itself the mark of a fetch failure.

## Empty states go through `UEmpty`, themed like the cards (2026-09-22)

**Decided in #1669, the empty-state half of the vocabulary #1661 settled for errors.** The two are deliberately
separate: an empty state is **not** an error — "no ranked games on this champion yet" is a correct answer, not
a failure, and the codebase already relied on that distinction (`useChampion` swallows a 404 into
`notEnoughData` rather than raising it). #1661 therefore left these alone; this entry finishes the job.

There were twelve hand-rolled empty-state *cards* and no component behind any of them — three paddings
(`px-6 py-12`, `px-6 py-8`, `px-4 py-8`), three card shapes (`surface rounded-lg`, `surface rounded-md`, a
dashed `rounded-xl border-accented bg-muted`) and four title styles (`text-base font-semibold`,
`font-medium`, `text-sm font-medium`, none at all). Nuxt UI ships `UEmpty` — `icon` / `avatar` / `title` /
`description` / `actions` / `variant` / `size` / `loading` — which covers every one of them.

- **Themed once in `app.config.ts`, like `card`**, and for the same reason: the next empty state should
  inherit the shape rather than copy classes a thirteenth time.
- **`soft` is the default and had to be restated opaque.** Nuxt UI's stock `soft` is `bg-elevated/50`, and a
  plain utility out-cascades `@utility surface`'s background — the exact trap the `card` theme already
  documents. Left alone, every empty state would render at 50 % against the translucency #1060 removed.
  `description` also drops Nuxt UI's `text-toned` for the site's own muted/highlighted split.
- **`UEmpty`'s `title` renders an `<h2>`.** That is right where the empty state *is* the page's content
  (a not-found profile, an empty favorites list), and wrong inside a `SectionCard`, whose own title is an
  `<h3>` — the prop would invert the outline. Those call sites (`FallbackBuild`, the matchup recommendation
  card) put both lines in `#description` with the first one emphasised, which is what their markup did
  anyway. The matchup **draft placeholder** takes the same route for a different reason already on record:
  it is deliberately unlabelled by a heading so it reads as a placeholder and not as a third panel.
- **The icon is neutral everywhere now.** It was `text-primary` on favorites and `text-dimmed` on the draft
  stage. An empty-state icon is decorative, and the accent is scarce by rule — so it goes to the component's
  neutral avatar in both.

**"Player not found" stays an empty state, not the 404 page.** A Riot ID can name a real player we simply do
not track: that is "we don't hold this", not "this does not exist". The page keeps its breadcrumb and its
shareable URL, and the card offers a way onward (`actions`) where `error.vue` offers only a way back. The
argument for the 404 page — `champion-route.ts` 404s a URL that names nothing — does not transfer: the
profile fetch is client-only by rule (#862, per-viewer payloads never reach SSR HTML), so the server has
already answered **200** with skeletons and `showError` could not change the status anyway. It would have
bought a different presentation, not a real 404.

**The second pass finished the one-line states in #1681** — eighteen of them, half again as many as the
inventory in #1669 had found (it grepped for "No … yet" and missed `MainsComparison`'s four). They were a bare
`<p class="text-muted">` inside a card; they are `UEmpty` now, description-only and icon-bearing.

- **`size` had to be taught to change the box, not just the type.** Nuxt UI's sizes scale the avatar and the
  font and leave the root at `p-4 sm:p-6 lg:p-8`, so a "small" empty state was still a full-height block and
  converting a `py-3` line grew it threefold. `sm` and `xs` carry their own root padding in the theme, which
  is what makes one usable inside a card body (`sm`) or a compact list (`xs`, no icon — an icon chip in a
  favorite card is taller than the three match rows it replaces).
- **All description-only.** These sit inside a `SectionCard`, so `UEmpty`'s `<h2>` title is either an
  inversion (level 3) or a flat duplicate (level 2); the two that had a bolded pseudo-title keep it as an
  emphasised first line in `#description`, the same shape `FallbackBuild` uses.

**Three failures had been hiding among them, and #1661 missed all three** — `Couldn't load synergies` /
`matchups` / `the comparison`, each hand-written as the same muted line as the empty states around it, with
copy of its own that never saw the real status. The #1661 sweep keyed on `UAlert` and `describeFetchError`
call sites, and these were neither. They are `FetchErrorAlert`s now. The lesson for the next vocabulary sweep:
grep the *copy* as well as the components, because the drift that matters is the state rendered with no
component at all.

## Keycap surfaces and one translucent bar; the type stays Inter (2026-09-27)

**Decided by the product owner in #1709, amending one point of #1060.**
The components borrow the Raycast site's material language — not its look: the rose-gold accent, the `ink`
surfaces and the eclipse hero all stay, and Raycast's animated banded backdrop was mocked up and dropped.

- **Surfaces get a keycap edge** (`--shadow-key`: inset lit top, inset shaded bottom), on every `surface` and on
  filled buttons. It gives a card and a control a physical edge without an outer shadow muddying the four-step
  opaque ladder, which is untouched.
- **Translucency returns for the header bar only** (`glass-bar`). #1060 removed `glass` because translucency
  *everywhere* meant nothing was ever on top of anything. A floating header is the one surface content really
  scrolls behind, so the argument does not apply to it — and it applies to every other surface as much as
  before: `glass-bar` is not a panel material.
- **Typography stays Inter, set tight, and eyebrows stay rose gold.** The pass first moved eyebrows to Geist Mono
  uppercase in `text-dimmed` and set headings lighter (medium) with default tracking. The product owner withdrew
  both after seeing them: the mono label read as a foreign typeface and the loose headings lost the site's voice.
  Five alternatives (Geist, Space Grotesk, Sora, Bricolage Grotesque, an Instrument Serif accent word) were
  compared and rejected in favour of the previous setting. Mono stays where it *is* the meaning — the footer's
  build stamp, tier letters.
- **Primary buttons stay rose gold.** Raycast's CTAs are neutral light-grey; adopting that would remove the
  accent from the one place it means "act here". Only the keycap edge is borrowed.



## Nuxt UI only feeds Tailwind the themes of the components the site uses (2026-10-04)

**Decision:** `ui.experimental.componentDetection` is enabled in `web/nuxt.config.ts` — #1641.

- **Why.** By default Nuxt UI adds the theme file of every component it ships (`@source "./ui"`) to Tailwind's
  sources, so every utility mentioned by a component the site never renders (pricing tables, dashboards, color
  pickers…) ships in the entry stylesheet that every page downloads. Detection scans the layers' `app/`
  directories (the site's and `web/layers/common`'s) for `U*` names and keeps only those themes and their
  dependencies: 39 components today.
- **The saving, measured** (production `nuxt build`, entry stylesheet, zlib gzip -9 / brotli q11):
  301,332 → 216,513 B raw (−28 %), 38,944 → 29,514 B gzip (−9.4 KB, −24 %), 30,665 → 23,537 B brotli
  (−23 %). 763 of 2,563 class selectors disappear, none added. The prose share (#1624) is untouched: with
  `prose` on, the whole `ui/prose` directory stays a source, and the `Prose*` components the pages use only
  depend on `ULink`/`UIcon`, both detected.
- **No visual change, checked three ways** (dev server, `NUXT_DEV_MOCK_API=1`): (1) every class token in the SSR
  HTML of 13 pages (home, champions, tier list, champion, matchup, truemains, player, player-champion, about,
  download, privacy, terms, favorites) still has its rule — zero tokens lost to the removed selectors; (2) a
  computed-style fingerprint of every element after hydration, off vs on, on the main pages plus the open ⌘K
  palette — the only differences were reload noise of the same size as an off-vs-off run (sub-pixel grid
  tracks, a hover transition, inherited text properties on `<img>`, Chrome's serialisation of `calc(infinity * 1px)` on `rounded-full`, whose rule
  is byte-identical in both builds); (3) screenshots of the champion and player pages. A true pixel diff
  was not possible on this machine (no headless Chromium; Firefox headless hung), hence the style diff.
- **The one blind spot.** Detection is a text scan, so a Nuxt UI component picked at runtime from a string
  (`resolveComponent('UModal')`, `:is="'UModal'"`) is not seen and would render unstyled. None exists
  today; if one is added, pass an array instead of `true` (`componentDetection: ['Modal']`) to force it in.
  New components written in templates or scripts are picked up by the next build (and by HMR in dev).
