// XML-escaping for chart axis tick labels (issue #842).
//
// `@unovis/ts` — the engine behind `nuxt-charts`/`vue-chrts` — renders axis
// tick text by *string-building* an SVG fragment and then parsing it as strict
// XML (`utils/text.js`, `renderTextToTspanStrings` + `renderTextToSvgTextElement`):
//
//   `<tspan x="…" dy="…em" …>${line}</tspan>`
//   parser.parseFromString(svgCodeSanitized, 'image/svg+xml')
//
// The tick text is interpolated raw — the library's own `escapeStringKeepHash`
// only escapes quotes and control characters, never `&`, `<` or `>`. A tick
// label containing one of those makes the fragment invalid XML, so `DOMParser`
// hands back a `<parsererror>` document instead of the tspan. It never throws,
// so the chart keeps working, but Firefox logs an `XML Parsing Error` for every
// failed parse.
//
// The site hits this on every champion page (the scaling buckets are labelled
// `<20m`, `20-25m`, …) and the admin on every bar chart labelled from data
// ("Nunu & Willump"). Both read this one copy.
//
// Verified still present in every published `@unovis/ts` — 1.6.5 (ours), 1.6.7
// (latest) and the 1.7.0 pre-releases — and `vue-chrts@2.2.0` still depends on
// that same `^1.6.2` range, so no dependency bump fixes it. Escaping on our
// side does: the entities are decoded by the XML parse, so the tick still
// *renders* as `<20m` while the fragment stays well-formed.
//
// Escaping is safe against the library's word wrapping: it splits lines on
// `[' ', '-', '.', ',']` only, none of which occur inside `&amp;`/`&lt;`/`&gt;`,
// and character-level breaking (`tickTextForceWordBreak`) is off by default, so
// an entity can never be split across two tspans.
//
// Only tick text goes through that XML path. The axis *label* (`xLabel` /
// `yLabel`) is set with d3's `.text()` (plain DOM `textContent`), where an
// entity would show up literally — so labels must NOT be escaped.

/**
 * Escapes the XML metacharacters that break `@unovis/ts`'s tick rendering.
 * `&` is replaced first so already-escaped output is not double-escaped.
 */
export function escapeChartTickText(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

/**
 * Wraps an axis tick formatter so its output is XML-safe. An absent formatter
 * stays absent — unovis then stringifies the raw tick value, which is numeric
 * or a `Date` and never needs escaping.
 */
export function escapeTickFormatter<F extends (tick: never, i?: number, ticks?: never[]) => string>(
  formatter: F | undefined,
): F | undefined {
  if (!formatter) return undefined
  // A chart's tick type (number or Date) passes through untouched; only the
  // returned string is escaped. Generic over the formatter rather than naming
  // the chart library's type, so the admin can read this file without it.
  return ((tick: never, i?: number, ticks?: never[]) =>
    escapeChartTickText(formatter(tick, i, ticks))) as F
}
