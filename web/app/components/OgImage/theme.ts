/**
 * Shared visual language for the Satori share cards (#926).
 *
 * Satori resolves neither CSS custom properties nor Tailwind utilities, so the
 * two things the cards cannot borrow from the app are the `@theme` colours in
 * `layers/common/app/assets/css/theme.css` and anything expressed as a class. Those are
 * restated below as literal hex; when a brand colour moves in `theme.css`, it
 * moves here too.
 *
 * Everything that is *already* a plain value or a pure function — `TIER_HEX`,
 * `formatRank`, `eloBracketLabel`, the position labels — is imported from where
 * the pages get it, not copied, so a rank can never read one colour on the page
 * and another on the card.
 *
 * The templates style exclusively through inline `style` objects. That is
 * deliberate: Satori supports a subset of CSS, and going through Tailwind would
 * make the card's output depend on how the module transpiles utilities rather
 * than on what we wrote.
 */

export { formatRank } from '#shared/utils/ranks'
export { TIER_HEX as RANK_COLOR } from '#common/utils/tiers'
export { eloBracketLabel as formatEloBracket } from '#shared/utils/elo-brackets'
export { formatCount } from '~~/shared/utils/counts'

import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { THEME_HEX } from '#shared/utils/theme-tokens'

/** `--color-ink-950`, the app's darkest surface — the card ground. */
export const INK = THEME_HEX['ink-950']
/** The `--ui-bg-elevated` step: panel fills inside the card. */
export const PANEL = '#1b1b20'
/** `--color-ink-800` — the quiet border around non-accented panels. */
export const PANEL_BORDER = THEME_HEX['ink-800']
/** `--color-ink-200` — primary text on the dark ground. */
export const TEXT = THEME_HEX['ink-200']
/** `--color-ink-400` — labels and secondary text. */
export const TEXT_MUTED = THEME_HEX['ink-400']
/** `--color-ink-500` — the dimmest legible step (footer, units). */
export const TEXT_DIM = THEME_HEX['ink-500']
/** `--color-rosegold-400`, the brand accent as it reads on dark. */
export const ROSEGOLD = THEME_HEX['rosegold-400']
/** `--color-rosegold-300`, the lighter end of the wordmark ramp. */
export const ROSEGOLD_LIGHT = THEME_HEX['rosegold-300']
/**
 * `--color-gold` at its on-dark hairline opacity. Spelled `rgba()` rather than
 * an 8-digit hex because Satori's colour parsing is only reliable on the former.
 */
export const GOLD_HAIRLINE = 'rgba(217, 182, 118, 0.22)'

/**
 * The `--color-tier-*` performance ladder — the medal scale, rose gold at S down
 * to iron at D. An unknown or missing tier is absent from the map: the card drops
 * the badge, mirroring the badge's own dash-instead-of-a-guess behaviour.
 */
export const TIER_COLOR: Record<string, string> = {
  S: THEME_HEX['tier-s'],
  A: THEME_HEX['tier-a'],
  B: THEME_HEX['tier-b'],
  C: THEME_HEX['tier-c'],
  D: THEME_HEX['tier-d'],
}

/**
 * The share cards' warm off-centre wash: two offset radial gradients, as far
 * as Satori's gradient support goes. The site itself no longer paints a glow
 * (the home hero's WebGL eclipse was retired for game art); the cards keep
 * theirs until they get the same treatment.
 */
export const BACKDROP = [
  'radial-gradient(900px 620px at 78% -12%, rgba(229,143,131,0.30), rgba(229,143,131,0) 70%)',
  'radial-gradient(700px 520px at 8% 108%, rgba(217,182,118,0.14), rgba(217,182,118,0) 70%)',
].join(', ')

/** Riot position value → the label the app's role pickers show. */
export function formatPosition(position: string): string {
  return POSITION_BY_VALUE.get(position.toUpperCase())?.label ?? position
}
