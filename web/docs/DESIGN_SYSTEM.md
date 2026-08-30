# Design system conventions

Conventions that are enforced by nothing but habit — write them here before they drift.

The tokens themselves live in [`main.css`](../app/assets/css/main.css), with the reasoning behind each
choice in its comment. `.claude/docs/decisions.md` records why the system looks the way it does. Every colour
family, elevation step and material is rendered on one screen at **`/dev/design-system`** — check a change
there before touring the real pages, and add the section yourself when you add a family.

The app is **dark-only**. There is no colour-mode toggle and no `dark:` variant anywhere; the surface tokens
sit on `:root`. Don't add a light variant to a component — it will never be reviewed.

## Colour has four jobs, and the boundaries are the point

| Family | Owns | Allowed on |
| --- | --- | --- |
| `primary` (rosegold) | Brand, interaction **and the good end of a measurement** | Logo, active nav, focus rings, primary buttons, links, selected states — and `--color-data-good` |
| `--color-data-*` | Measurements | Anything — `text-*`, `bg-*`, `ring-*`, `border-*` |
| `--color-gold` | The accent-of-the-accent: metal, and the **standout** top of the data axis | Hairlines, the logo sheen, the MVP crown — and `text-*` on a standout measurement. Never a fill |
| `--color-stat-*`, `--color-rune-*` | Riot's in-game vocabulary (stats & damage types, the five rune trees) | **Prose only** — never `bg-*` / `ring-*` / `border-*`, never on a measurement |

**The data axis is one-sided**: `--color-data-good` is `rosegold-400`, `--color-data-mid` is `ink-400`
("measured, and it is average"), and everything below steps down the neutral ramp to `--color-data-bad`
(`ink-500`). A losing value is not flagged in a warning colour, it is simply not highlighted. `-dim` stops are
for large fills, where a full stop would shout.

**One-sided is a claim about the bottom of the axis.** The top has a second, rarer step: `--color-gold` above
`--color-data-good`, for a *standout* value — a Perfect KDA, a 75+ performance score
([`MatchRow.vue`](../app/components/match/MatchRow.vue)). It stays `--color-gold` rather than becoming a
`--color-data-standout` deliberately: it is the same token the MVP crown wears, so the number and the accolade
say the same thing, and a second name for one hex is how those two drift apart. It is the one member of the
axis that is **text and small marks only** — a gold fill would out-shout the accent it exists to cap. Use it
for a genuine outlier, not as a third band on every metric; a scale with a gold on every row has no standout
left.

The `--color-tier-*` ladder is the **medal** scale — rose gold, gold, silver, bronze, iron. Five ranks read as
five ranks without a legend.

> A cold→warm axis (teal good → amber bad) shipped in #1060 and was withdrawn in #1096. If you are tempted to
> reintroduce a second hue for "bad", read that decision entry first — the argument against green/red
> (`rosegold-500` is itself a desaturated red) is still live, and the argument that got the teal removed is
> not a technical one you can out-argue.

**The accent is no longer exclusive to interaction, and that is the price.** A rose-gold number is a *good*
number, not a clickable one. Affordance must come from shape and position — a border, a cursor, a control's
own chrome — and never from hue alone. Rose gold is still not a generic surface tint: an accent applied to
every panel is how the pre-#1060 system ended up reading as one flat warm mass.

`--color-stat-*` no longer collides with the data axis the way #1060's teal→amber made it — nothing in
`--color-data-*` is cyan, and no stat colour means "bad" any more — but those tokens stay confined to tooltip
prose regardless. They are a *vocabulary*, not a scale, and a reader must never have to work out whether cyan
means "magic resist" or "good". The rule outlives the collision that first motivated it.

`--color-rune-*` (the five keystone trees, added in #1143 for the champion page's build paragraph) is the same
kind of family and carries the same rule: text emphasis inside prose, never a fill, never on a measurement. A
player reads "Domination" off the red before the word, which is why the trees earn Riot's colours at all.
Sorcery is a plain blue rather than the client's blue-violet — the one deliberate departure, because
violet/indigo is a hue this app uses nowhere else.

The one deliberate exception is `TIER_COLORS` / `TIER_HEX` in [`utils/tiers.ts`](../app/utils/tiers.ts):
those are **Riot's** rank colours, reproduced because a player reads Iron/Bronze/Gold from the colour before
the word.

## Elevation is four opaque steps

`bg-default` (page) → `bg-muted` (recessed) → `bg-elevated` (raised) → `bg-accented` (interactive).

Reach for the material utilities rather than composing a surface by hand:

- **`surface`** — the app-wide panel: opaque fill, neutral hairline, one soft shadow. Material only; pick
  your own radius and padding. Every `UCard` gets it globally from `app.config.ts`.
- **`surface-hover`** — the interactive counterpart. It moves the element up one step on the ladder rather
  than washing it with a tint, so hover reads as "raised" like every other level.
- **`deselected`** — `scale-90 opacity-30 grayscale`, the shared "not the picked option" treatment for
  rune/perk artwork.
- **`selected-perk`** — its counterpart, a faint `ring-primary/40` on the option that *was* picked. The
  artwork alone is not a reliable signal (a selected grey rune reads exactly like its unpicked neighbours), so
  the ring carries the state independently of the icon. Low-contrast on purpose: a highlight, not a border.

There is no translucent material. The former `glass` was removed once the last call site went opaque —
including the home hero's search field, which reads better solid against the eclipse.

**`surface` owns `background-color` and `border` — don't restate them.** Writing `class="surface border
border-default/60 bg-elevated/60"` is not merely redundant: a plain utility out-cascades a `@utility`
declaration, so the literal pair wins and the element is translucent again. It looks like it was converted
and isn't. Opacity is what collapsed the ladder in the first place — a panel, a row inside it and the page
behind all landed within a few percent of each other and the eye had no depth to read.

## A skeleton wears the material it replaces

Two rules, both learned by breaking them:

**The shell is the real component's material, opaque.** A placeholder row uses `surface`, exactly like the
`ListRowSurface` it stands in for — never a translucent stand-in. A dimmer shell means the whole list brightens
when the data lands, which reads as the page changing rather than filling in.

**The blocks are `bg-ink-700`, not a ladder step.** `animate-pulse` is `50% { opacity: .5 }`, so half of every
cycle the fill is blended halfway back into whatever is behind it. Judge a skeleton colour at *half* opacity:
`bg-accented` over a card lands on `#1f1f25` and vanishes, `ink-700` lands near `#2a2a31` and reads. The fill
lives in `ui.skeleton.base` (`app.config.ts`) and is duplicated once, deliberately, in `SkeletonImage.vue`,
which hand-rolls the same look to avoid a component instance per icon — **keep the two in sync**.

Nuxt UI's own default is `bg-elevated`, which since the ink rebuild is the exact fill of every card. Left alone,
every skeleton inside a card is invisible.

## Named values and global component defaults

Not every token is a colour. `main.css` also owns:

- **`--width-build-path` (336 px) / `--width-starter-items` (116 px)** — the fixed row widths of the
  build-path and starter-item strips, used as `sm:w-build-path` / `sm:w-starter-items`. Named so the
  item-count math lives in one place instead of an arbitrary `w-[…]` at each call site.

`app.config.ts` sets three app-wide component defaults, each of which is a design decision, not a
preference:

- **`card`** — every `UCard` is `surface rounded-xl` with trimmed padding, and the `soft` variant's stock
  `bg-elevated/50` is restated as the opaque `bg-elevated` (see the cascade trap below).
- **`skeleton.base`** — `bg-ink-700`, per the section above.
- **`badge.variant`** — `subtle` rather than Nuxt UI's `solid`. `subtle` is a translucent `bg-{color}/10`
  plus a matching `ring-inset ring-{color}/25`: it tints without becoming a surface of its own, and keeps the
  per-colour (pick vs. win) distinction those badges carry. This is the one place translucency survived the
  removal of `glass` — a badge is a tint on a known fill, not a pane with something behind it.

## A stat is a pair, and the pair has a house style

**Inter carries everything the reader reads**, measurements included. Geist Mono is kept only where monospace
is the meaning rather than a flourish — tier letters, the empty-slot glyph, hex codes on `/dev/design-system`.
#1060 had put the two utilities below on Geist Mono; withdrawn in #1111, because across a dense page it read as
a second unrelated typeface rather than as a register.

- **`stat-value`** — family, weight and figure style, but deliberately *not* size: a headline KPI and a table
  cell are the same material at different scales, so the call site picks the step.
- **`stat-label`** — 10 px, uppercase, wide tracking, dimmed.

The gap between the two is the point. The old scale put a value and its label one step apart (`text-sm` over
`text-xs`, same family, same weight), which made a dense row read as undifferentiated noise.

## Semantic text hierarchy

Four semantic text color tokens from `@nuxt/ui` (`text-default`, `text-muted`, `text-dimmed`,
`text-highlighted`) cover every text color need. Prefer them over raw `text-{color}-{shade}` utilities.

| Token | Use for |
| --- | --- |
| `text-default` | Primary content — headings, body copy, the main read of a component. |
| `text-muted` | Secondary content — subtitles, supporting labels, inactive tab text. |
| `text-dimmed` | Tertiary / disabled content — placeholder text, disabled controls, the least important label on a dense row. |
| `text-highlighted` | Emphasis — active state, the value the user came to see (a stat, a champion name), page titles. |

## Two traps worth knowing

**Tailwind only emits utilities it can see statically.** A computed `text-tier-${x}` or `bg-ink-${shade}`
renders as an unstyled element — build a literal map (see `TierBadge.vue`) rather than interpolating a class
name.

**A plain utility overrides a `@utility`'s own property — rely on it deliberately, not by accident.**
`class="stat-label text-primary"` renders rose gold, because Tailwind sorts custom `@utility` rules *before*
generated ones inside the utilities layer, so `.text-primary` wins on source order at equal specificity. That is
the intended way to recolour a `stat-label` or resize a `stat-value`. It is also the trap below, seen from the
other side: the same rule means a stray `bg-elevated/60` beats `surface`'s fill. Overriding one declared
property is fine; restating the material's own is what breaks it.

**Nuxt UI appends per-variant `root` classes rather than replacing them, and a plain utility out-cascades a
`@utility` declaration.** That is why the `card` theme in `app.config.ts` neutralises `soft`'s stock
`bg-elevated/50` to the opaque `bg-elevated`: left alone it wins over `surface`'s background and the material never
reaches the card. Expect the same trap on any component theme you extend with a material utility.

## Component naming conventions

- **`App*`** (`AppHeader`, `AppLogo`, `AppFooter`, `AppSearch`, `AppBackdrop`) — layout/shell components. One instance each, mounted from `app.vue` or a page, not meant to be reused inside a page.
- **`Champion/*`** — feature-scoped components for the champion detail area (`Champion/Header`, `Champion/Matchups`, `Champion/BuildPanel`, ...). Each owns a section of the champion page.
- **`Champion/Core/*`** — low-level data-viz primitives consumed by build panels (`Champion/Core/Runes`, `Champion/Core/BuildPath`, `Champion/Core/Spells`, ...). These render a single build artifact and stay presentational — no data fetching, no page-level state.

New components should sit at the narrowest scope that fits: a one-off page section goes under the feature folder (e.g. `Champion/*`); a primitive reused by multiple panels goes under `Champion/Core/*`; a component with no feature owner and used app-wide is a top-level component (see `SectionCard` below).

## Prop vs slot: `SectionCard`

[`SectionCard`](../app/components/SectionCard.vue) accepts both a `title` prop and a `#title` slot. Rule of thumb:

- **Prop** (`title`, `subtitle`) for plain text — the common case. It also drives the card's automatic `aria-labelledby` wiring, so plain-text titles get the accessible name for free.
- **Slot** (`#title`) only when the header needs markup the prop can't express — a badge next to the heading, a link, an icon. Using the slot opts out of the automatic `aria-labelledby`; name the region yourself if it needs one.

The same split applies to any component that mixes a data prop with an equivalent slot: default to the prop, reach for the slot only when the content is structural rather than textual.
