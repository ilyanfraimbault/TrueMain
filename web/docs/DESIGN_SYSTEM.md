# Design system conventions

Conventions that are enforced by nothing but habit — write them here before they drift.

## Semantic text hierarchy

Four semantic text color tokens from `@nuxt/ui` (`text-default`, `text-muted`,
`text-dimmed`, `text-highlighted`) cover every text color need. Prefer them over
raw `text-{color}-{shade}` utilities so text automatically tracks light/dark mode
and the rose-gold palette defined in [`main.css`](../app/assets/css/main.css).

| Token | Use for |
| --- | --- |
| `text-default` | Primary content — headings, body copy, the main read of a component. |
| `text-muted` | Secondary content — subtitles, supporting labels, inactive tab text. |
| `text-dimmed` | Tertiary / disabled content — placeholder text, disabled controls, the least important label on a dense row. |
| `text-highlighted` | Emphasis — active state, the value the user came to see (a stat, a champion name), page titles. |

## Component naming conventions

- **`App*`** (`AppHeader`, `AppLogo`, `AppFooter`, `AppSearch`, `AppBackdrop`) — layout/shell components. One instance each, mounted from `app.vue` or a layout, not meant to be reused inside a page.
- **`Champion/*`** — feature-scoped components for the champion detail area (`Champion/Header`, `Champion/Matchups`, `Champion/BuildPanel`, ...). Each owns a section of the champion page.
- **`Champion/Core/*`** — low-level data-viz primitives consumed by build panels (`Champion/Core/Runes`, `Champion/Core/BuildPath`, `Champion/Core/Spells`, ...). These render a single build artifact and stay presentational — no data fetching, no page-level state.

New components should sit at the narrowest scope that fits: a one-off page section goes under the feature folder (e.g. `Champion/*`); a primitive reused by multiple panels goes under `Champion/Core/*`; a component with no feature owner and used app-wide is a top-level component (see `SectionCard` below).

## Prop vs slot: `SectionCard`

[`SectionCard`](../app/components/SectionCard.vue) accepts both a `title` prop and a `#title` slot. Rule of thumb:

- **Prop** (`title`, `subtitle`) for plain text — the common case. It also drives the card's automatic `aria-labelledby` wiring, so plain-text titles get the accessible name for free.
- **Slot** (`#title`) only when the header needs markup the prop can't express — a badge next to the heading, a link, an icon. Using the slot opts out of the automatic `aria-labelledby`; name the region yourself if it needs one.

The same split applies to any component that mixes a data prop with an equivalent slot: default to the prop, reach for the slot only when the content is structural rather than textual.
