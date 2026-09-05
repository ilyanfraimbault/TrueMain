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
  was avoiding no longer exists. `dedication.ts` and `PlayerPerformance.vue` read `--color-tier-*` directly,
  so the dedication ranks and performance verdicts followed for free.
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

