/**
 * Shared visual language for the Satori share cards (#926).
 *
 * Satori resolves neither CSS custom properties nor Tailwind utilities, so the
 * two things the cards cannot borrow from the app are the `@theme` colours in
 * `app/assets/css/main.css` and anything expressed as a class. Those are
 * restated below as literal hex; when a brand colour moves in `main.css`, it
 * moves here too.
 *
 * Everything that is *already* a plain value or a pure function — `TIER_HEX`,
 * `formatTier`, `eloBracketLabel`, the position labels — is imported from where
 * the pages get it, not copied, so a rank can never read one colour on the page
 * and another on the card.
 *
 * The templates style exclusively through inline `style` objects. That is
 * deliberate: Satori supports a subset of CSS, and going through Tailwind would
 * make the card's output depend on how the module transpiles utilities rather
 * than on what we wrote.
 */

export { formatTier as formatRank, TIER_HEX as RANK_COLOR } from '~/utils/tiers'
export { eloBracketLabel as formatEloBracket } from '~/utils/elo-brackets'

import { POSITION_BY_VALUE } from '~/utils/positions'

/** `--color-mauve-950`, the app's darkest surface — the card ground. */
export const INK = '#161213'
/** `--color-mauve-900`, one step up: panel fills inside the card. */
export const PANEL = '#211d1e'
/** `--color-mauve-800` — the quiet border around non-accented panels. */
export const PANEL_BORDER = '#362f31'
/** `--color-mauve-200` — primary text on the dark ground. */
export const TEXT = '#e6dee0'
/** `--color-mauve-400` — labels and secondary text. */
export const TEXT_MUTED = '#a99ba0'
/** `--color-mauve-500` — the dimmest legible step (footer, units). */
export const TEXT_DIM = '#7f7276'
/** `--color-rosegold-400`, the brand accent as it reads on dark. */
export const ROSEGOLD = '#e58f83'
/** `--color-rosegold-300`, the lighter end of the wordmark ramp. */
export const ROSEGOLD_LIGHT = '#eeaea3'
/**
 * `--color-gold` at its on-dark hairline opacity. Spelled `rgba()` rather than
 * an 8-digit hex because Satori's colour parsing is only reliable on the former.
 */
export const GOLD_HAIRLINE = 'rgba(217, 182, 118, 0.22)'

/**
 * The `--color-tier-*` performance ladder (S rose-gold → D iron). This one has
 * no hex counterpart in `app/utils` — `TierBadge.vue` reaches for it through
 * `text-tier-*` classes — so it is restated here rather than imported. An
 * unknown or missing tier is deliberately absent from the map: the card drops
 * the badge, mirroring the badge's own dash-instead-of-a-guess behaviour.
 */
export const TIER_COLOR: Record<string, string> = {
  S: '#e58f83',
  A: '#e2b95c',
  B: '#b9c2cd',
  C: '#b3763f',
  D: '#837a7c',
}

/**
 * The rose-gold "eclipse" the site paints behind every page (`AppBackdrop.vue`
 * draws it in WebGL). Two offset radial washes are as close as Satori's
 * gradient support gets, and they carry the same warm off-centre glow.
 */
export const BACKDROP = [
  'radial-gradient(900px 620px at 78% -12%, rgba(229,143,131,0.30), rgba(229,143,131,0) 70%)',
  'radial-gradient(700px 520px at 8% 108%, rgba(217,182,118,0.14), rgba(217,182,118,0) 70%)',
].join(', ')

/** Riot position value → the label the app's role pickers show. */
export function formatPosition(position: string): string {
  return POSITION_BY_VALUE.get(position.toUpperCase())?.label ?? position
}

/**
 * Thousands-separated integer. Pinned to `en-US` for the same reason the rest
 * of the app is: the card is rendered on the server, and a locale-dependent
 * separator would make the output depend on the container's environment.
 */
export function formatCount(value: number): string {
  return value.toLocaleString('en-US')
}
